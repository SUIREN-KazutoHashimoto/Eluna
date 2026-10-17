using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Eluna.App.Services;

public sealed record ReleaseAsset(string Name, Uri Url, long Size, string? Digest);
public sealed record ReleaseInfo(string Tag, string Name, string Notes, Uri Page, DateTimeOffset? Published, bool? IsNewer, bool Prerelease = false, IReadOnlyList<ReleaseAsset>? Assets = null);
public sealed partial class UpdateService : IDisposable
{
    private const string ReleasePrefix = "https://github.com/SUIREN-KazutoHashimoto/Eluna/releases/";
    private readonly HttpClient _http;
    private readonly bool _ownsHttp;
    public static string CurrentVersion => typeof(UpdateService).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "0.1.0";
    public UpdateService(HttpClient? http = null)
    {
        _ownsHttp = http is null; _http = http ?? new() { Timeout = TimeSpan.FromSeconds(25) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("Eluna/" + CurrentVersion);
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
    }
    public async Task<ReleaseInfo?> CheckAsync(CancellationToken token, string train = "stable")
    {
        if (train is not ("stable" or "develop")) { throw new ArgumentException("更新トレインを確認してください。", nameof(train)); }
        using HttpResponseMessage response = await _http.GetAsync("https://api.github.com/repos/SUIREN-KazutoHashimoto/Eluna/releases" + (train == "stable" ? "/latest" : "?per_page=100"), token);
        if (response.StatusCode == HttpStatusCode.NotFound) { return null; }
        if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests) { throw new HttpRequestException("GitHubの取得制限に達しました。時間を置いて再試行してください。"); }
        response.EnsureSuccessStatusCode();
        using JsonDocument json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
        return train == "stable" ? Parse(json.RootElement, CurrentVersion) : SelectDevelopment(json.RootElement, CurrentVersion);
    }
    internal static ReleaseInfo Parse(JsonElement release, string current)
    {
        if (release.ValueKind != JsonValueKind.Object || !release.TryGetProperty("tag_name", out JsonElement tagValue) || tagValue.ValueKind != JsonValueKind.String ||
            !release.TryGetProperty("html_url", out JsonElement urlValue) || urlValue.ValueKind != JsonValueKind.String) { throw new JsonException("リリース情報の形式を確認できませんでした。"); }
        string tag = tagValue.GetString()!;
        string url = urlValue.GetString()!;
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? page) || !page.AbsoluteUri.StartsWith(ReleasePrefix, StringComparison.Ordinal) || page.UserInfo.Length > 0)
        { throw new JsonException("リリースのリンクを確認できませんでした。"); }
        DateTimeOffset? published = release.TryGetProperty("published_at", out JsonElement date) && date.ValueKind == JsonValueKind.String && date.TryGetDateTimeOffset(out DateTimeOffset value) ? value : null;
        return new(tag, release.TryGetProperty("name", out JsonElement name) && name.ValueKind == JsonValueKind.String ? name.GetString()! : tag,
            release.TryGetProperty("body", out JsonElement body) && body.ValueKind == JsonValueKind.String ? body.GetString()! : "更新内容がありません。", page, published, Compare(tag, current), release.TryGetProperty("prerelease", out JsonElement pre) && pre.ValueKind == JsonValueKind.True, ParseAssets(release, tag));
    }
    internal static bool? Compare(string available, string installed)
    {
        static (Version Base, string[]? Pre)? ParseVersion(string value)
        {
            Match parsed = Regex.Match(value, @"^v?(?<version>\d+\.\d+\.\d+(?:\.\d+)?)(?:-(?<pre>[A-Za-z0-9.-]+))?(?:\+[A-Za-z0-9.-]+)?$", RegexOptions.IgnoreCase);
            if (!parsed.Success || !Version.TryParse(parsed.Groups["version"].Value, out Version? version)) { return null; }
            string[]? pre = parsed.Groups["pre"].Success ? parsed.Groups["pre"].Value.Split('.') : null;
            if (pre?.Any(p => p.Length == 0) == true) { return null; }
            return (new Version(version.Major, version.Minor, Math.Max(0, version.Build), Math.Max(0, version.Revision)), pre);
        }
        var next = ParseVersion(available); var current = ParseVersion(installed);
        if (next is null || current is null) { return null; }
        int compare = next.Value.Base.CompareTo(current.Value.Base);
        if (compare != 0) { return compare > 0; }
        if (next.Value.Pre is null) { return current.Value.Pre is not null; }
        if (current.Value.Pre is null) { return false; }
        for (int i = 0; i < Math.Min(next.Value.Pre.Length, current.Value.Pre.Length); i++)
        {
            string a = next.Value.Pre[i], b = current.Value.Pre[i];
            bool aNumber = a.All(char.IsAsciiDigit), bNumber = b.All(char.IsAsciiDigit);
            if (aNumber && bNumber) { compare = a.TrimStart('0').Length.CompareTo(b.TrimStart('0').Length); if (compare == 0) { compare = string.CompareOrdinal(a.TrimStart('0'), b.TrimStart('0')); } }
            else if (aNumber != bNumber) { compare = aNumber ? -1 : 1; }
            else { compare = string.CompareOrdinal(a, b); }
            if (compare != 0) { return compare > 0; }
        }
        return next.Value.Pre.Length > current.Value.Pre.Length;
    }
    public void Dispose() { if (_ownsHttp) { _http.Dispose(); } }
}
