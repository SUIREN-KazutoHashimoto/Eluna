using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Eluna.App.Models;

namespace Eluna.App.Services;

public sealed partial class UpdateService
{
    private static IReadOnlyList<ReleaseAsset> ParseAssets(JsonElement release, string tag)
    {
        if (!release.TryGetProperty("assets", out JsonElement assets) || assets.ValueKind != JsonValueKind.Array) { return []; }
        var result = new List<ReleaseAsset>();
        foreach (JsonElement item in assets.EnumerateArray())
        {
            string? name = item.TryGetProperty("name", out JsonElement n) ? n.GetString() : null;
            if (name is not ("Eluna.exe" or "Eluna-Setup.exe" or "update-manifest.json")) { continue; }
            if (!item.TryGetProperty("browser_download_url", out JsonElement url) || !Uri.TryCreate(url.GetString(), UriKind.Absolute, out Uri? uri) || uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0 || !uri.AbsoluteUri.StartsWith(ReleasePrefix + "download/" + Uri.EscapeDataString(tag) + "/", StringComparison.Ordinal)) { throw new JsonException("配布ファイルのURLを確認できませんでした。"); }
            if (!item.TryGetProperty("size", out JsonElement bytes) || !bytes.TryGetInt64(out long size) || size <= 0 || size > 1536L * 1024 * 1024) { throw new JsonException("配布ファイルの容量を確認できませんでした。"); }
            string? digest = item.TryGetProperty("digest", out JsonElement hash) && hash.ValueKind == JsonValueKind.String ? hash.GetString() : null;
            result.Add(new(name, uri, size, digest));
        }
        if (result.Select(a => a.Name).Distinct(StringComparer.Ordinal).Count() != result.Count) { throw new JsonException("配布ファイルが重複しています。"); }
        return result;
    }
    internal static ReleaseInfo? SelectDevelopment(JsonElement list, string current)
    {
        if (list.ValueKind != JsonValueKind.Array) { throw new JsonException("開発リリースの一覧を確認できませんでした。"); }
        ReleaseInfo? best = null;
        foreach (JsonElement row in list.EnumerateArray())
        {
            if (row.TryGetProperty("draft", out JsonElement draft) && draft.ValueKind == JsonValueKind.True || !row.TryGetProperty("prerelease", out JsonElement pre) || pre.ValueKind != JsonValueKind.True) { continue; }
            ReleaseInfo release = Parse(row, current);
            if (!Regex.IsMatch(release.Tag, @"^v?\d+\.\d+\.\d+-develop\.\d+(?:\.\d+)?$", RegexOptions.CultureInvariant)) { continue; }
            if (best is null || Compare(release.Tag, best.Tag) == true) { best = release; }
        }
        return best;
    }
    private static string AssetHash(ReleaseAsset asset)
    {
        if (asset.Digest is null || !Regex.IsMatch(asset.Digest, "^sha256:[a-fA-F0-9]{64}$", RegexOptions.CultureInvariant)) { throw new InvalidDataException("GitHubのSHA-256情報がないため、この版は自動更新できません。"); }
        return asset.Digest[7..];
    }
    internal static async Task ValidateFileAsync(string file, string hash, long size, CancellationToken token)
    {
        if (!Regex.IsMatch(hash, "^[a-fA-F0-9]{64}$", RegexOptions.CultureInvariant) || !File.Exists(file) || new FileInfo(file).Length != size) { throw new InvalidDataException("配布ファイルの容量またはハッシュ形式が一致しません。"); }
        await using var input = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true);
        string actual = Convert.ToHexString(await SHA256.HashDataAsync(input, token));
        if (!actual.Equals(hash, StringComparison.OrdinalIgnoreCase)) { throw new InvalidDataException("配布ファイルのSHA-256が一致しません。再取得してください。"); }
    }
    private async Task DownloadAsync(ReleaseAsset asset, string destination, Action<double>? progress, CancellationToken token)
    {
        string hash = AssetHash(asset);
        using HttpResponseMessage response = await _http.GetAsync(asset.Url, HttpCompletionOption.ResponseHeadersRead, token); response.EnsureSuccessStatusCode();
        if (response.RequestMessage?.RequestUri is { } final && (final.Scheme != "https" || final.Host is not ("github.com" or "release-assets.githubusercontent.com" or "objects.githubusercontent.com"))) { throw new HttpRequestException("配布ファイルの取得先を確認できませんでした。"); }
        if (response.Content.Headers.ContentLength is long announced && announced != asset.Size) { throw new InvalidDataException("配布ファイルの容量が変更されています。"); }
        await using (Stream input = await response.Content.ReadAsStreamAsync(token))
        await using (var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true))
        {
            byte[] buffer = new byte[65536]; long received = 0;
            for (int count; (count = await input.ReadAsync(buffer, token)) > 0;)
            { received += count; if (received > asset.Size) { throw new InvalidDataException("配布ファイルが予定容量を超えています。"); } await output.WriteAsync(buffer.AsMemory(0, count), token); progress?.Invoke(received * 100d / asset.Size); }
        }
        await ValidateFileAsync(destination, hash, asset.Size, token);
    }
    internal static void ValidateManifest(ReleaseManifest manifest, ReleaseInfo release, string train, ReleaseAsset binary)
    {
        if (manifest.Schema != 1 || manifest.Version != release.Tag.TrimStart('v') || manifest.Train != train || manifest.File != "Eluna.exe" || manifest.DataSchema < 5 || manifest.Size != binary.Size || string.IsNullOrEmpty(manifest.Sha256) || !manifest.Sha256.Equals(AssetHash(binary), StringComparison.OrdinalIgnoreCase) || release.Prerelease != (train == "develop"))
        { throw new InvalidDataException("更新情報の版・トレイン・データ互換性を確認できませんでした。"); }
    }
    internal async Task<StagedUpdate> StageAsync(AppStore store, ReleaseInfo release, string train, Action<double>? progress, CancellationToken token)
    {
        if (release.IsNewer != true) { throw new InvalidOperationException("現在より新しい互換版だけを自動適用できます。トレイン変更でも自動でダウングレードしません。"); }
        ReleaseAsset binary = release.Assets?.SingleOrDefault(a => a.Name == "Eluna.exe") ?? throw new InvalidDataException("更新用のexeがありません。");
        ReleaseAsset manifestAsset = release.Assets?.SingleOrDefault(a => a.Name == "update-manifest.json") ?? throw new InvalidDataException("更新マニフェストがありません。");
        if (manifestAsset.Size > 16384) { throw new InvalidDataException("更新マニフェストが大きすぎます。"); }
        string directory = Path.Combine(store.Root, "Updates", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        try
        {
            string manifestPath = Path.Combine(directory, "manifest.json"); await DownloadAsync(manifestAsset, manifestPath, null, token);
            ReleaseManifest manifest = JsonSerializer.Deserialize<ReleaseManifest>(await File.ReadAllTextAsync(manifestPath, token), AppStore.JsonOptions) ?? throw new JsonException("更新マニフェストが空です。");
            ValidateManifest(manifest, release, train, binary);
            string exe = Path.Combine(directory, "Eluna.exe"); await DownloadAsync(binary, exe, progress, token);
            await using (var pe = File.OpenRead(exe)) { if (pe.Length < 65536 || pe.ReadByte() != 'M' || pe.ReadByte() != 'Z') { throw new InvalidDataException("Windows実行ファイルを確認できませんでした。"); } }
            var staged = new StagedUpdate(manifest.Version, train, exe, manifest.Sha256, manifest.Size, manifest.DataSchema);
            string ready = Path.Combine(store.Root, "Updates", "ready.json"); string temp = ready + ".tmp";
            await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(staged, AppStore.JsonOptions), token); token.ThrowIfCancellationRequested(); File.Move(temp, ready, true);
            store.Trace.Write("info", "update", "download.completed", "version=" + staged.Version + "; train=" + train); return staged;
        }
        catch { foreach (string name in new[] { "manifest.json", "Eluna.exe" }) { string path = Path.Combine(directory, name); if (File.Exists(path)) { File.Delete(path); } } if (!Directory.EnumerateFileSystemEntries(directory).Any()) { Directory.Delete(directory); } throw; }
    }
}
