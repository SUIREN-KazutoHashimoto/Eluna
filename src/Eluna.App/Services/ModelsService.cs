using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Eluna.App.Models;

namespace Eluna.App.Services;

public sealed class ModelsService(AppStore store)
{
    public Task<long> UsageAsync(CancellationToken token) => Task.Run(() => Directory.Exists(store.ModelsDirectory) ? ManagedDirectories(store.ModelsDirectory)
        .SelectMany(d => Directory.EnumerateFiles(d)).Where(p => (File.GetAttributes(p) & FileAttributes.ReparsePoint) == 0).Sum(p => new FileInfo(p).Length) : 0, token);
    public async Task<LocalModel> InspectAsync(LocalModel model, CancellationToken token)
    {
        ModelProfile profile = await GgufInspector.ReadAsync(model, token);
        LocalModel updated = model with { Profile = profile, License = profile.License.Length > 0 ? profile.License : model.License };
        await UpdateAsync(updated, token); return updated;
    }
    public async Task UpdateAsync(LocalModel model, CancellationToken token = default)
    {
        ModelPreferences.Validate(model.Options);
        string metadata = await FindMetadataAsync(model.Id, token);
        await SaveMetadataAsync(store, metadata, model, token);
    }
    public async Task InstallProjectorAsync(LocalModel model, ModelFile file, IProgress<DownloadProgress>? progress, CancellationToken token)
    {
        string metadata = await FindMetadataAsync(model.Id, token);
        using var catalog = new ModelCatalog(store);
        string path = await catalog.DownloadFileAsync(file, Path.GetDirectoryName(metadata)!, progress, token);
        await UpdateAsync(model with { Projector = new(path, file.Sha256, file.Size) }, token);
    }
    public async Task ImportProjectorAsync(LocalModel model, string path, CancellationToken token)
    {
        string metadata = await FindMetadataAsync(model.Id, token);
        await using var input = File.OpenRead(path); byte[] magic = new byte[4];
        await input.ReadExactlyAsync(magic, token);
        if (!magic.AsSpan().SequenceEqual("GGUF"u8)) { throw new InvalidDataException("GGUF形式ではありません。"); }
        input.Position = 0; string hash = Convert.ToHexString(await SHA256.HashDataAsync(input, token)).ToLowerInvariant();
        string target = Path.Combine(Path.GetDirectoryName(metadata)!, "mmproj-" + hash[..16] + ".gguf");
        if (!Path.GetFullPath(path).Equals(target, StringComparison.OrdinalIgnoreCase))
        {
            input.Position = 0;
            await using (var output = new FileStream(target + ".part", FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 1024, true)) { await input.CopyToAsync(output, token); }
            token.ThrowIfCancellationRequested(); File.Move(target + ".part", target, true);
        }
        await UpdateAsync(model with { Projector = new(target, hash, input.Length) }, token);
    }
    internal static async Task SaveMetadataAsync(AppStore store, string metadata, LocalModel model, CancellationToken token, bool preserveCustomization = false)
    {
        await store.ModelsGate.WaitAsync(token);
        try
        {
            if (preserveCustomization && File.Exists(metadata))
            {
                LocalModel? previous = JsonSerializer.Deserialize<LocalModel>(await File.ReadAllTextAsync(metadata, token), AppStore.JsonOptions);
                if (previous?.Id == model.Id) { model = model with { DisplayName = previous.DisplayName, IsFavorite = previous.IsFavorite, Preferences = previous.Preferences, Projector = previous.Projector }; }
            }
            await File.WriteAllTextAsync(metadata + ".tmp", JsonSerializer.Serialize(model, AppStore.JsonOptions), token); File.Move(metadata + ".tmp", metadata, true);
        }
        finally { store.ModelsGate.Release(); }
    }
    private async Task<string> FindMetadataAsync(string id, CancellationToken token)
    {
        foreach (string directory in ManagedDirectories(store.ModelsDirectory))
        {
            string path = Path.Combine(directory, "model.json"); if (!File.Exists(path)) { continue; }
            try { LocalModel? model = JsonSerializer.Deserialize<LocalModel>(await File.ReadAllTextAsync(path, token), AppStore.JsonOptions); if (model?.Id == id) { return path; } }
            catch (JsonException) { }
        }
        throw new InvalidDataException("モデルの管理情報が見つかりません。");
    }
    internal static IEnumerable<string> ManagedDirectories(string root) => Directory.EnumerateDirectories(root).Where(d =>
        Regex.IsMatch(Path.GetFileName(d), @"^(?:[a-fA-F0-9]{24,64}|local-[a-fA-F0-9]{20,64})$") && (File.GetAttributes(d) & FileAttributes.ReparsePoint) == 0);
    private static string Owned(string path, string root)
    {
        string absolute = Path.GetFullPath(path), prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!absolute.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || (File.Exists(absolute) || Directory.Exists(absolute)) && (File.GetAttributes(absolute) & FileAttributes.ReparsePoint) != 0)
        { throw new InvalidDataException("モデルの保存先の外やリンク先のファイルは操作できません。"); }
        return absolute;
    }
    public async Task DeleteAsync(LocalModel model, CancellationToken token)
    {
        string metadata = await FindMetadataAsync(model.Id, token), directory = Path.GetDirectoryName(metadata)!;
        string[] files = model.AllFiles.Select(p => Owned(p.FilePath, directory)).ToArray();
        await store.ModelsGate.WaitAsync(token);
        try
        {
            token.ThrowIfCancellationRequested();
            foreach (string file in files) { File.Delete(file); if (File.Exists(file + ".part")) { File.Delete(Owned(file + ".part", directory)); } }
            File.Delete(Owned(metadata, store.ModelsDirectory));
            if (!Directory.EnumerateFileSystemEntries(directory).Any()) { Directory.Delete(directory, false); }
        }
        finally { store.ModelsGate.Release(); }
    }
    public Task MoveDirectoryAsync(string destination, IProgress<DownloadProgress>? progress, CancellationToken token) => Task.Run(async () =>
    {
        string old = Path.GetFullPath(store.ModelsDirectory).TrimEnd(Path.DirectorySeparatorChar), target = Path.GetFullPath(destination).TrimEnd(Path.DirectorySeparatorChar);
        if (old.Equals(target, StringComparison.OrdinalIgnoreCase)) { return; }
        if (target == Path.GetPathRoot(target)?.TrimEnd(Path.DirectorySeparatorChar) || target.StartsWith(old + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || old.StartsWith(target + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        { throw new InvalidDataException("別の専用フォルダを選んでください。保存先同士を入れ子にはできません。"); }
        Directory.CreateDirectory(target);
        if ((File.GetAttributes(target) & FileAttributes.ReparsePoint) != 0) { throw new InvalidDataException("リンクではなく、保存先のフォルダを直接選んでください。"); }
        if (!Directory.Exists(old)) { await store.SetModelLocationAsync(target); progress?.Report(new(0, 0, "新しい保存先を設定しました。利用できない旧保存先のモデルは保管されています。")); return; }
        await store.ModelsGate.WaitAsync(token);
        var copied = new List<string>(); long received = 0;
        try
        {
            string[] directories = ManagedDirectories(old).ToArray();
            var dataFiles = directories.SelectMany(d => Directory.EnumerateFiles(d).Where(f => f.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".gguf.part", StringComparison.OrdinalIgnoreCase))).Select(f => Owned(f, old)).ToArray();
            long total = dataFiles.Sum(f => new FileInfo(f).Length);
            long needed = dataFiles.Where(f => !File.Exists(Path.Combine(target, Path.GetRelativePath(old, f)))).Sum(f => new FileInfo(f).Length);
            if (new DriveInfo(Path.GetPathRoot(target)!).AvailableFreeSpace < needed + 32 * 1024 * 1024) { throw new IOException("移動先の空き容量が不足しています。"); }
            foreach (string source in dataFiles)
            {
                token.ThrowIfCancellationRequested(); string output = Path.Combine(target, Path.GetRelativePath(old, source)); Directory.CreateDirectory(Path.GetDirectoryName(output)!); Owned(output, target);
                if (!File.Exists(output))
                {
                    string temporary = output + ".eluna-moving-" + Guid.NewGuid().ToString("N");
                    try
                    {
                        await using (var input = File.OpenRead(source))
                        await using (var writer = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1024 * 1024, true))
                        {
                            byte[] buffer = new byte[1024 * 1024]; int count;
                            while ((count = await input.ReadAsync(buffer, token)) > 0) { await writer.WriteAsync(buffer.AsMemory(0, count), token); received += count; progress?.Report(new(received, total, "モデルを移動中")); }
                        }
                        if (!await SameFileAsync(source, temporary, token)) { throw new InvalidDataException("移動したファイルの検証に失敗しました。"); }
                        File.Move(temporary, output);
                    }
                    finally { if (File.Exists(temporary)) { File.Delete(Owned(temporary, target)); } }
                }
                else
                {
                    if (!await SameFileAsync(source, output, token)) { throw new IOException("移動先に別のファイルがあります。別の保存先を選んでください。"); }
                    received += new FileInfo(source).Length;
                }
                copied.Add(source);
            }
            foreach (string directory in directories)
            {
                string metadata = Path.Combine(directory, "model.json"); if (!File.Exists(metadata)) { continue; }
                LocalModel? model = JsonSerializer.Deserialize<LocalModel>(await File.ReadAllTextAsync(metadata, token), AppStore.JsonOptions); if (model is null) { throw new InvalidDataException("モデルの管理情報を読み取れません。"); }
                string Rewrite(string path) => Path.GetFullPath(path).StartsWith(old + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ? Path.Combine(target, Path.GetRelativePath(old, path)) : path;
                LocalModel relocated = model with { FilePath = Rewrite(model.FilePath), Parts = model.Parts?.Select(p => p with { FilePath = Rewrite(p.FilePath) }).ToList(), Projector = model.Projector is null ? null : model.Projector with { FilePath = Rewrite(model.Projector.FilePath) } };
                string folder = Path.Combine(target, Path.GetFileName(directory)); Directory.CreateDirectory(folder); Owned(folder, target);
                string targetMetadata = Path.Combine(folder, "model.json");
                if (File.Exists(targetMetadata)) { LocalModel? prior = JsonSerializer.Deserialize<LocalModel>(await File.ReadAllTextAsync(targetMetadata, token), AppStore.JsonOptions); if (prior?.Id != model.Id) { throw new IOException("移動先の管理情報と競合しています。"); } }
                await File.WriteAllTextAsync(targetMetadata + ".tmp", JsonSerializer.Serialize(relocated, AppStore.JsonOptions), token); File.Move(targetMetadata + ".tmp", targetMetadata, true); copied.Add(metadata);
            }
            token.ThrowIfCancellationRequested(); await store.SetModelLocationAsync(target);
            // The new location is valid before any original is removed. Do not follow directory links.
            foreach (string source in copied) { try { File.Delete(Owned(source, old)); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* A retained original can be cleaned up later. */ } }
            foreach (string directory in directories) { if (!Directory.EnumerateFileSystemEntries(directory).Any()) { Directory.Delete(directory, false); } }
            progress?.Report(new(total, total, "移動とファイル検証が完了しました"));
        }
        finally { store.ModelsGate.Release(); }
    }, token);
    private static async Task<bool> SameFileAsync(string first, string second, CancellationToken token)
    {
        if (new FileInfo(first).Length != new FileInfo(second).Length) { return false; }
        await using var a = File.OpenRead(first); await using var b = File.OpenRead(second);
        byte[] x = await SHA256.HashDataAsync(a, token), y = await SHA256.HashDataAsync(b, token); return x.AsSpan().SequenceEqual(y);
    }
}
