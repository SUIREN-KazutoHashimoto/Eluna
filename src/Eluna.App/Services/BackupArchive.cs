using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Eluna.App.Models;

namespace Eluna.App.Services;

public sealed record BackupProgress(string Stage, long Completed, long Total)
{
    public double Percent => Total > 0 ? Math.Min(100, Completed * 100d / Total) : 0;
}
public sealed record BackupEntry(string Path, long Length, string Sha256);
public sealed record BackupAsset(string Source, string Path);
public sealed record BackupManifest(int Version, DateTimeOffset CreatedAt, string OriginalRoot, string OriginalModels,
    bool IncludesModels, List<BackupEntry> Files, List<BackupAsset> Assets);
public sealed record BackupPreview(int Files, long Bytes, bool IncludesModels);
public sealed record PreparedBackup(string Directory, BackupManifest Manifest);
internal sealed record BackupSource(string Source, string Path, long Length);
internal sealed record RestoreEntry(string Path, bool HadOriginal, bool HasNew);
internal sealed record RestoreJournal(string Previous, string Stage, List<RestoreEntry> Entries);

public static class BackupArchive
{
    private static readonly string[] StateFiles = ["state.json", "state-knowledge.json", "state-skills.json"];
    private static readonly string[] RootFiles = [.. StateFiles, "media-jobs.json", "downloads.json", "downloads-v2.json", "window.json", "reading-positions.json", "image-model.json", "model-location.json"];
    private static readonly string[] Folders = ["Skills", "Generated", "Models", "ImageModels", "ModelAssets"];
    private const long MaximumBytes = 1L << 40;
    private static readonly HashSet<string> PathProperties = new(StringComparer.OrdinalIgnoreCase) { "FilePath", "ModelPath", "OutputPath", "ResultPath", "ImportPath" };
    private sealed class Reporter(IProgress<BackupProgress>? progress)
    {
        private long _last;
        internal void Report(string stage, long completed, long total, bool force = false)
        { if (force || Environment.TickCount64 - _last >= 100) { _last = Environment.TickCount64; progress?.Report(new(stage, completed, total)); } }
    }

    private static bool Under(string path, string root) => Path.GetFullPath(path).StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    private static void RejectLinks(string path, string root)
    {
        string? current = Path.GetFullPath(path), boundary = Path.GetFullPath(root);
        while (current is not null && (current.Equals(boundary, StringComparison.OrdinalIgnoreCase) || Under(current, boundary)))
        {
            if ((File.Exists(current) || System.IO.Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) { throw new InvalidDataException("リンクを含む保存先はバックアップできません。"); }
            current = Path.GetDirectoryName(current);
        }
    }
    private static IEnumerable<string> Walk(string folder)
    {
        if (!System.IO.Directory.Exists(folder)) { yield break; }
        RejectLinks(folder, folder);
        foreach (string path in System.IO.Directory.EnumerateFileSystemEntries(folder).Order(StringComparer.OrdinalIgnoreCase))
        {
            RejectLinks(path, folder);
            if (System.IO.Directory.Exists(path)) { if (!Path.GetFileName(path).StartsWith(".staging-", StringComparison.Ordinal)) { foreach (string file in Walk(path)) { yield return file; } } }
            else if (!path.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase) && !path.EndsWith(".writing", StringComparison.OrdinalIgnoreCase) && !path.EndsWith(".part", StringComparison.OrdinalIgnoreCase)) { yield return path; }
        }
    }
    private static bool Weight(string path) => Path.GetExtension(path).ToLowerInvariant() is ".gguf" or ".ggml" or ".safetensors" or ".bin";
    private static async Task<(List<BackupSource> Files, List<BackupAsset> Assets)> SourcesAsync(AppStore store, bool models, CancellationToken token)
    {
        var files = new List<BackupSource>(); var assets = new List<BackupAsset>();
        void Add(string source, string relative) { token.ThrowIfCancellationRequested(); if (File.Exists(source)) { CheckPath(relative); RejectLinks(source, Path.GetDirectoryName(source)!); files.Add(new(source, relative, new FileInfo(source).Length)); } }
        foreach (string name in RootFiles) { Add(Path.Combine(store.Root, name), name); }
        foreach (string name in new[] { "Skills", "Generated", "ImageModels", "ModelAssets" })
        { foreach (string path in Walk(Path.Combine(store.Root, name))) { if (name is "Skills" or "Generated" || models || !Weight(path)) { Add(path, Path.GetRelativePath(store.Root, path).Replace('\\', '/')); } } }
        foreach (string path in Walk(store.ModelsDirectory))
        { if (models || Path.GetFileName(path).Equals("model.json", StringComparison.OrdinalIgnoreCase)) { Add(path, "Models/" + Path.GetRelativePath(store.ModelsDirectory, path).Replace('\\', '/')); } }
        if (models)
        {
            List<string> referenced = (await store.ListModelsAsync()).SelectMany(m => m.AllFiles).Select(f => f.FilePath).ToList();
            string image = Path.Combine(store.Root, "image-model.json");
            if (File.Exists(image)) { string? selected = JsonSerializer.Deserialize<string>(await File.ReadAllTextAsync(image, token)); if (selected is not null) { referenced.Add(selected); } }
            foreach (string source in referenced.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!File.Exists(source) || Under(source, store.Root) || Under(source, store.ModelsDirectory)) { continue; }
                if (!Weight(source)) { throw new InvalidDataException("モデルのファイル形式を確認してください。"); }
                string relative = "ModelAssets/" + Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Path.GetFullPath(source))))[..24] + "/" + Path.GetFileName(source);
                Add(source, relative); assets.Add(new(Path.GetFullPath(source), relative));
            }
        }
        if (files.Count > 100_000 || files.Sum(f => f.Length) > MaximumBytes) { throw new InvalidDataException("バックアップの上限（100,000ファイル・1 TB）を超えています。"); }
        if (files.Select(f => f.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() != files.Count) { throw new InvalidDataException("バックアップ内の保存先が重複しています。"); }
        return (files, assets);
    }
    public static Task<BackupPreview> PreviewAsync(AppStore store, bool models, CancellationToken token = default) => Task.Run(async () =>
    { var source = await SourcesAsync(store, models, token); return new BackupPreview(source.Files.Count, source.Files.Sum(f => f.Length), models); }, token);

    public static Task CreateAsync(AppStore store, string destination, bool models, IProgress<BackupProgress>? progress = null, CancellationToken token = default) => Task.Run(async () =>
    {
        string full = Path.GetFullPath(destination);
        if ((Under(full, store.Root) && !Under(full, store.BackupsDirectory)) || Under(full, store.ModelsDirectory)) { throw new InvalidOperationException("保存先はデータフォルダー外、またはBackupsフォルダーを選んでください。"); }
        var source = await SourcesAsync(store, models, token); long total = source.Files.Sum(f => f.Length), completed = 0;
        var reporter = new Reporter(progress); byte[] buffer = new byte[81920];
        string temporary = full + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
            {
                using (var zip = new ZipArchive(output, ZipArchiveMode.Create, true))
                {
                    var entries = new List<BackupEntry>();
                    foreach (BackupSource file in source.Files)
                    {
                        token.ThrowIfCancellationRequested(); RejectLinks(file.Source, Path.GetDirectoryName(file.Source)!);
                        await using var input = new FileStream(file.Source, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
                        await using Stream entry = zip.CreateEntry("files/" + file.Path, CompressionLevel.Fastest).Open();
                        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256); long count = 0;
                        for (int read; (read = await input.ReadAsync(buffer, token)) > 0;)
                        {
                            count += read; if (count > file.Length) { throw new IOException("保存中にファイルが変更されました。もう一度実行してください。"); }
                            await entry.WriteAsync(buffer.AsMemory(0, read), token); hash.AppendData(buffer, 0, read); completed += read;
                            reporter.Report("保存中 · " + file.Path, completed, total);
                        }
                        if (count != file.Length) { throw new IOException("保存中にファイルが変更されました。"); }
                        entries.Add(new(file.Path, count, Convert.ToHexString(hash.GetHashAndReset())));
                    }
                    byte[] metadata = JsonSerializer.SerializeToUtf8Bytes(new BackupManifest(1, DateTimeOffset.UtcNow, Path.GetFullPath(store.Root), Path.GetFullPath(store.ModelsDirectory), models, entries, source.Assets), AppStore.JsonOptions);
                    if (metadata.Length > 16 * 1024 * 1024 || entries.Any(f => RootFiles.Contains(f.Path) && f.Length > (256L << 20) || f.Path.StartsWith("Models/", StringComparison.Ordinal) && Path.GetFileName(f.Path) == "model.json" && f.Length > (2L << 20))) { throw new InvalidDataException("バックアップの管理ファイルが大きすぎます。"); }
                    await using Stream manifest = zip.CreateEntry("manifest.json").Open(); await manifest.WriteAsync(metadata, token);
                }
                await output.FlushAsync(token); output.Flush(true);
            }
            token.ThrowIfCancellationRequested(); File.Move(temporary, full, true); reporter.Report("保存しました", total, total, true);
        }
        finally { if (File.Exists(temporary)) { File.Delete(temporary); } }
    }, token);

    internal static void CheckPath(string relative)
    {
        if (relative is null || relative.Length is 0 or > 1024 || relative.Contains('\\') || relative.Contains(':') || relative.Split('/').Any(p => p is "" or "." or ".." || p.EndsWith('.') || p.EndsWith(' ') || p.Any(c => c < 32 || "<>\"|?*".Contains(c)) ||
            new[] { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" }.Contains(p.Split('.')[0], StringComparer.OrdinalIgnoreCase)))
        { throw new InvalidDataException("バックアップに不正なファイル名があります。"); }
        if (!RootFiles.Contains(relative, StringComparer.Ordinal) && (!relative.Contains('/') || !Folders.Contains(relative.Split('/')[0], StringComparer.Ordinal))) { throw new InvalidDataException("対応していないバックアップの保存先です。"); }
    }
    private static async Task<BackupManifest> ManifestAsync(ZipArchive zip, CancellationToken token)
    {
        ZipArchiveEntry entry = zip.GetEntry("manifest.json") ?? throw new InvalidDataException("Elunaの全体バックアップではありません。");
        if (entry.Length > 16 * 1024 * 1024) { throw new InvalidDataException("バックアップの一覧が大きすぎます。"); }
        await using Stream content = entry.Open();
        BackupManifest manifest = await JsonSerializer.DeserializeAsync<BackupManifest>(content, AppStore.JsonOptions, token) ?? throw new InvalidDataException("バックアップの一覧が空です。");
        if (manifest.Version != 1 || manifest.Files is null || manifest.Assets is null || manifest.Files.Count > 100_000 || !Path.IsPathFullyQualified(manifest.OriginalRoot) || !Path.IsPathFullyQualified(manifest.OriginalModels) || manifest.Files.Any(f => f is null || f.Length < 0 || f.Length > MaximumBytes || f.Sha256 is null || f.Sha256.Length != 64 || f.Sha256.Any(c => !Uri.IsHexDigit(c))) || manifest.Files.Sum(f => (decimal)f.Length) > MaximumBytes)
        { throw new InvalidDataException("バックアップの形式に対応していません。"); }
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var zipPaths = manifest.Files.Select(f => "files/" + f.Path).ToHashSet(StringComparer.Ordinal); zipPaths.Add("manifest.json");
        foreach (BackupEntry file in manifest.Files)
        {
            CheckPath(file.Path);
            if (!paths.Add(file.Path) || RootFiles.Contains(file.Path) && file.Length > (256L << 20) || file.Path.StartsWith("Models/", StringComparison.Ordinal) && Path.GetFileName(file.Path) == "model.json" && file.Length > (2L << 20)) { throw new InvalidDataException("バックアップの管理ファイルが重複しているか、大きすぎます。"); }
        }
        if (manifest.Assets.Count > manifest.Files.Count || manifest.Assets.Select(a => a?.Source).Distinct(StringComparer.OrdinalIgnoreCase).Count() != manifest.Assets.Count) { throw new InvalidDataException("モデル参照が重複しています。"); }
        foreach (BackupAsset asset in manifest.Assets) { if (asset is null || !Path.IsPathFullyQualified(asset.Source) || !paths.Contains(asset.Path) || !asset.Path.StartsWith("ModelAssets/", StringComparison.Ordinal)) { throw new InvalidDataException("モデル参照が不正です。"); } }
        if (zip.Entries.Count != manifest.Files.Count + 1 || zip.Entries.Select(e => e.FullName).Distinct(StringComparer.OrdinalIgnoreCase).Count() != zip.Entries.Count ||
            zip.Entries.Any(e => !zipPaths.Contains(e.FullName)) || !manifest.IncludesModels && manifest.Files.Any(f => (f.Path.StartsWith("Models/", StringComparison.OrdinalIgnoreCase) || f.Path.StartsWith("ImageModels/", StringComparison.OrdinalIgnoreCase) || f.Path.StartsWith("ModelAssets/", StringComparison.OrdinalIgnoreCase)) && Weight(f.Path)))
        { throw new InvalidDataException("バックアップのファイル一覧が一致しません。"); }
        return manifest;
    }
    public static Task<BackupManifest> InspectAsync(string path, CancellationToken token = default) => Task.Run(async () =>
    { using var zip = ZipFile.OpenRead(path); return await ManifestAsync(zip, token); }, token);

    public static Task<PreparedBackup> PrepareAsync(AppStore store, string archive, IProgress<BackupProgress>? progress = null, CancellationToken token = default) => Task.Run(async () =>
    {
        using var zip = ZipFile.OpenRead(archive); BackupManifest manifest = await ManifestAsync(zip, token);
        string stage = Path.Combine(store.BackupsDirectory, ".restore-stage-" + Guid.NewGuid().ToString("N")); System.IO.Directory.CreateDirectory(stage);
        try
        {
            long total = manifest.Files.Sum(f => f.Length), completed = 0;
            var reporter = new Reporter(progress); byte[] buffer = new byte[81920];
            if (new DriveInfo(Path.GetPathRoot(stage)!).AvailableFreeSpace < total + (16L << 20)) { throw new IOException("バックアップを展開する空き容量がありません。"); }
            foreach (BackupEntry file in manifest.Files)
            {
                ZipArchiveEntry entry = zip.GetEntry("files/" + file.Path)!;
                if (entry.Length != file.Length || ((entry.ExternalAttributes >> 16) & 0xf000) == 0xa000) { throw new InvalidDataException("バックアップの容量またはファイルの種類が一致しません。"); }
                string target = Path.Combine(stage, file.Path); System.IO.Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                await using Stream input = entry.Open(); await using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256); long count = 0;
                for (int read; (read = await input.ReadAsync(buffer, token)) > 0;)
                {
                    count += read; if (count > file.Length) { throw new InvalidDataException("バックアップの展開容量が一致しません。"); }
                    await output.WriteAsync(buffer.AsMemory(0, read), token); hash.AppendData(buffer, 0, read); completed += read;
                    reporter.Report("確認中 · " + file.Path, completed, total);
                }
                if (count != file.Length || !Convert.ToHexString(hash.GetHashAndReset()).Equals(file.Sha256, StringComparison.OrdinalIgnoreCase)) { throw new InvalidDataException("バックアップが破損しています。元のデータは変更していません。"); }
            }
            await RebaseAsync(stage, store.Root, manifest, token); await ValidateAsync(stage, store.Root, token);
            token.ThrowIfCancellationRequested(); reporter.Report("復元の準備ができました", total, total, true);
            return new PreparedBackup(stage, manifest);
        }
        catch { DeleteStage(stage, store.BackupsDirectory); throw; }
    }, token);

    private static async Task RebaseAsync(string stage, string targetRoot, BackupManifest manifest, CancellationToken token)
    {
        string Map(string path)
        {
            if (!Path.IsPathFullyQualified(path)) { return path; }
            BackupAsset? asset = manifest.Assets.FirstOrDefault(a => a.Source.Equals(path, StringComparison.OrdinalIgnoreCase));
            if (asset is not null) { return Path.Combine(targetRoot, asset.Path.Replace('/', Path.DirectorySeparatorChar)); }
            bool network = path.StartsWith("\\\\", StringComparison.Ordinal);
            if (Under(path, manifest.OriginalModels) && (manifest.IncludesModels || Under(path, manifest.OriginalRoot) || network || !File.Exists(path))) { return Path.Combine(targetRoot, "Models", Path.GetRelativePath(manifest.OriginalModels, path)); }
            if (Under(path, manifest.OriginalRoot)) { return Path.Combine(targetRoot, Path.GetRelativePath(manifest.OriginalRoot, path)); }
            // Restoring an archive must not probe an arbitrary UNC server with Windows credentials.
            return network ? Path.Combine(targetRoot, "ModelAssets", "unavailable-" + Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(path)))[..24], Path.GetFileName(path)) : path;
        }
        void Rewrite(JsonNode? node)
        {
            if (node is JsonObject obj)
            {
                foreach (var pair in obj.ToArray())
                {
                    if (PathProperties.Contains(pair.Key) && pair.Value is JsonValue value && value.TryGetValue(out string? path) && path is not null) { obj[pair.Key] = Map(path); }
                    else if (pair.Key.Equals("Artifacts", StringComparison.OrdinalIgnoreCase) && pair.Value is JsonArray artifacts)
                    { for (int i = 0; i < artifacts.Count; i++) { if (artifacts[i] is JsonValue artifact && artifact.TryGetValue(out string? file) && file is not null) { artifacts[i] = Map(file); } } }
                    else { Rewrite(pair.Value); }
                }
            }
            else if (node is JsonArray array) { foreach (JsonNode? child in array) { Rewrite(child); } }
        }
        foreach (string path in Walk(stage).Where(p => RootFiles.Contains(Path.GetRelativePath(stage, p), StringComparer.OrdinalIgnoreCase) || Under(p, Path.Combine(stage, "Models")) && Path.GetFileName(p) == "model.json"))
        {
            JsonNode? node = JsonNode.Parse(await File.ReadAllTextAsync(path, token));
            if (Path.GetFileName(path) == "model-location.json") { node = null; }
            else if (Path.GetFileName(path) == "image-model.json") { if (node is JsonValue v && v.TryGetValue(out string? image) && image is not null) { node = JsonValue.Create(Map(image)); } }
            else { Rewrite(node); }
            if (Path.GetFileName(path) is "downloads.json" or "downloads-v2.json" && node is JsonArray downloads)
            { foreach (JsonObject item in downloads.OfType<JsonObject>()) { if (item["Status"]?.GetValue<string>() != "complete") { item["Status"] = "paused"; item["Received"] = 0; } } }
            await File.WriteAllTextAsync(path, node?.ToJsonString(AppStore.JsonOptions) ?? "null", token);
        }
    }
    private static async Task ValidateAsync(string stage, string targetRoot, CancellationToken token)
    {
        foreach (string name in StateFiles.Where(n => File.Exists(Path.Combine(stage, n))))
        { AppState state = JsonSerializer.Deserialize<AppState>(await File.ReadAllTextAsync(Path.Combine(stage, name), token), AppStore.JsonOptions) ?? throw new InvalidDataException("履歴が空です。"); AppStore.Validate(state); }
        string media = Path.Combine(stage, "media-jobs.json");
        if (File.Exists(media))
        {
            List<SavedMediaJob> jobs = JsonSerializer.Deserialize<List<SavedMediaJob>>(await File.ReadAllTextAsync(media, token), AppStore.JsonOptions) ?? throw new InvalidDataException("作成予約が空です。");
            if (jobs.Count > 100 || jobs.Any(j => j is null || j.Request is null || j.Title is null || j.Text is null) || jobs.Select(j => j.Id).Distinct().Count() != jobs.Count) { throw new InvalidDataException("作成予約の形式が不正です。"); }
            foreach (SavedMediaJob job in jobs)
            {
                if (job.Request.OutputPath is null || !Under(job.Request.OutputPath, Path.Combine(targetRoot, "Generated")) || job.Request.Kind is not ("image" or "document" or "translation") || job.Request.Kind == "image" && job.Request.Image is null || job.Request.Kind != "image" && job.Request.Context is null) { throw new InvalidDataException("作成予約の保存先または種類が不正です。"); }
                if (job.Request.Context is { } context) { AppStore.Validate(context.State); }
            }
        }
    }

    internal static void DeleteStage(string stage, string backups)
    {
        string full = Path.GetFullPath(stage);
        if (!Under(full, backups) || !Path.GetFileName(full).StartsWith(".restore-stage-", StringComparison.Ordinal)) { throw new InvalidOperationException("展開先の範囲を確認できません。"); }
        RejectLinks(full, backups); if (System.IO.Directory.Exists(full)) { System.IO.Directory.Delete(full, true); }
    }
    public static async Task<string> ApplyAsync(AppStore store, PreparedBackup prepared, IProgress<BackupProgress>? progress = null)
    {
        await using var restoreLock = new FileStream(Path.Combine(store.Root, "restore.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        if (!Under(prepared.Directory, store.BackupsDirectory) || !Path.GetFileName(prepared.Directory).StartsWith(".restore-stage-", StringComparison.Ordinal)) { throw new InvalidDataException("復元の準備先が不正です。"); }
        await Task.Run(() => ValidateAsync(prepared.Directory, store.Root, CancellationToken.None));
        string safety = Path.Combine(store.BackupsDirectory, "before-restore-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N") + ".zip");
        await CreateAsync(store, safety, prepared.Manifest.IncludesModels, progress);
        return await Task.Run(() =>
        {
            var incoming = Walk(prepared.Directory).Select(p => Path.GetRelativePath(prepared.Directory, p).Replace('\\', '/')).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var existing = RootFiles.Where(n => File.Exists(Path.Combine(store.Root, n))).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (string folder in Folders)
            {
                foreach (string file in Walk(Path.Combine(store.Root, folder)))
                {
                    if (folder is "Skills" or "Generated" || prepared.Manifest.IncludesModels || folder == "Models" && Path.GetFileName(file) == "model.json" || folder is "ImageModels" or "ModelAssets" && !Weight(file))
                    { existing.Add(Path.GetRelativePath(store.Root, file).Replace('\\', '/')); }
                }
            }
            var entries = existing.Union(incoming, StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).Select(p => new RestoreEntry(p, File.Exists(Path.Combine(store.Root, p)), incoming.Contains(p))).ToList();
            string previous = Path.Combine(store.BackupsDirectory, "restore-previous-" + Guid.NewGuid().ToString("N")); System.IO.Directory.CreateDirectory(previous);
            var journal = new RestoreJournal(previous, prepared.Directory, entries);
            string journalPath = Path.Combine(store.Root, "restore-current.json");
            foreach (RestoreEntry entry in entries) { CheckPath(entry.Path); RejectLinks(Path.Combine(store.Root, entry.Path), store.Root); }
            string json = JsonSerializer.Serialize(journal, AppStore.JsonOptions); File.WriteAllText(journalPath + ".tmp", json);
            using (var durable = new FileStream(journalPath + ".tmp", FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { durable.Flush(true); }
            File.Move(journalPath + ".tmp", journalPath);
            try
            {
                foreach (RestoreEntry entry in entries)
                {
                    string target = Path.Combine(store.Root, entry.Path), old = Path.Combine(previous, entry.Path);
                    if (entry.HadOriginal) { System.IO.Directory.CreateDirectory(Path.GetDirectoryName(old)!); File.Move(target, old); }
                    if (entry.HasNew) { System.IO.Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Move(Path.Combine(prepared.Directory, entry.Path), target); }
                }
                // Deleting the durable journal commits the transaction. Previous files remain recoverable.
                File.Delete(journalPath);
            }
            catch { RecoverCore(store.Root); throw; }
            try { DeleteStage(prepared.Directory, store.BackupsDirectory); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            return safety;
        });
    }
    public static void Recover(string root)
    {
        string path = Path.Combine(root, "restore-current.json"); if (!File.Exists(path)) { return; }
        using var restoreLock = new FileStream(Path.Combine(root, "restore.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        RecoverCore(root);
    }
    private static void RecoverCore(string root)
    {
        string path = Path.Combine(root, "restore-current.json");
        if (new FileInfo(path).Length > 32 * 1024 * 1024) { throw new InvalidDataException("復元記録が大きすぎます。データを保持して起動を停止しました。"); }
        RestoreJournal journal = JsonSerializer.Deserialize<RestoreJournal>(File.ReadAllText(path), AppStore.JsonOptions) ?? throw new InvalidDataException("復元記録を読み取れません。");
        string backups = Path.Combine(root, "Backups");
        if (!Under(journal.Previous, backups) || !Path.GetFileName(journal.Previous).StartsWith("restore-previous-", StringComparison.Ordinal) || !Under(journal.Stage, backups) || !Path.GetFileName(journal.Stage).StartsWith(".restore-stage-", StringComparison.Ordinal) || journal.Entries is null || journal.Entries.Count > 200_000) { throw new InvalidDataException("復元記録の保存先が不正です。"); }
        foreach (RestoreEntry entry in journal.Entries) { CheckPath(entry.Path); RejectLinks(Path.Combine(root, entry.Path), root); RejectLinks(Path.Combine(journal.Previous, entry.Path), backups); }
        foreach (RestoreEntry entry in journal.Entries.AsEnumerable().Reverse())
        {
            string target = Path.Combine(root, entry.Path), old = Path.Combine(journal.Previous, entry.Path), staged = Path.Combine(journal.Stage, entry.Path);
            if (File.Exists(old)) { if (File.Exists(target)) { File.Delete(target); } System.IO.Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Move(old, target); }
            else if (!entry.HadOriginal && entry.HasNew && !File.Exists(staged) && File.Exists(target)) { File.Delete(target); }
        }
        File.Delete(path);
    }
}
