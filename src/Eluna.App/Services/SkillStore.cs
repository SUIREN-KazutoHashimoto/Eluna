using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Eluna.App.Models;

namespace Eluna.App.Services;

public sealed class SkillStore(AppStore store)
{
    public const int MaxFiles = 256;
    public const long MaxBytes = 32 * 1024 * 1024;
    private string Root => Path.Combine(store.Root, "Skills");
    public static string SafePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) { throw new InvalidDataException("スキル内のファイル名が空です。"); }
        path = path.Replace('\\', '/');
        string[] parts = path.Split('/');
        if (path.Equals(".eluna-manifest.json", StringComparison.OrdinalIgnoreCase) || path.Length > 240 || parts.Any(p => p.Length == 0 || p is "." or ".." || p.EndsWith('.') || p.EndsWith(' ') ||
            p.Any(c => c < 32 || "<>:\"|?*".Contains(c)) || System.Text.RegularExpressions.Regex.IsMatch(p, "^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\\.|$)", System.Text.RegularExpressions.RegexOptions.IgnoreCase)))
        { throw new InvalidDataException("スキル内のファイル名に対応していません: " + path); }
        return string.Join('/', parts);
    }
    internal static void RejectLinks(string path, string boundary)
    {
        string stop = Path.GetFullPath(boundary).TrimEnd(Path.DirectorySeparatorChar);
        for (string? current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
        {
            if (File.Exists(current) || Directory.Exists(current))
            { if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) { throw new InvalidDataException("リンク・ジャンクションを含むスキルは使えません。"); } }
            if (string.Equals(current.TrimEnd(Path.DirectorySeparatorChar), stop, StringComparison.OrdinalIgnoreCase)) { return; }
        }
        throw new InvalidDataException("スキルのファイルが保存範囲の外です。");
    }
    public Task<SkillPackage> ReadPackageAsync(string path, CancellationToken token) => Task.Run(async () =>
    {
        var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase); long total = 0;
        async Task Add(string name, Stream input, long size)
        {
            token.ThrowIfCancellationRequested(); name = SafePath(name); if (name.Equals("SKILL.md", StringComparison.OrdinalIgnoreCase)) { name = "SKILL.md"; }
            if (files.Count >= MaxFiles || size < 0 || size > 16 * 1024 * 1024 || total + size > MaxBytes || files.ContainsKey(name))
            { throw new InvalidDataException("スキルは256ファイル・合計32 MB・各16 MBまでです。ファイル名の重複も確認してください。"); }
            using var memory = new MemoryStream(); byte[] buffer = new byte[65536]; int count;
            while ((count = await input.ReadAsync(buffer, token)) > 0)
            {
                if (memory.Length + count > size) { throw new InvalidDataException("展開後のファイルサイズが不正です。"); }
                await memory.WriteAsync(buffer.AsMemory(0, count), token);
            }
            if (memory.Length != size) { throw new InvalidDataException("スキルのファイルが途中で終了しました。"); }
            total += size; files.Add(name, memory.ToArray());
        }
        string? directoryName = null;
        if (Directory.Exists(path))
        {
            string source = Path.GetFullPath(path); directoryName = Path.GetFileName(source.TrimEnd(Path.DirectorySeparatorChar));
            var folders = new Stack<string>(); folders.Push(source); int directories = 0;
            while (folders.TryPop(out string? folder))
            {
                token.ThrowIfCancellationRequested(); RejectLinks(folder, source);
                if (++directories > 256) { throw new InvalidDataException("フォルダーが多すぎます。"); }
                foreach (string child in Directory.EnumerateFileSystemEntries(folder))
                {
                    RejectLinks(child, source);
                    if (Directory.Exists(child)) { folders.Push(child); }
                    else { await using var input = File.OpenRead(child); await Add(Path.GetRelativePath(source, child), input, input.Length); }
                }
            }
        }
        else if (Path.GetFileName(path).Equals("SKILL.md", StringComparison.OrdinalIgnoreCase))
        {
            return await ReadPackageAsync(Path.GetDirectoryName(Path.GetFullPath(path))!, token);
        }
        else
        {
            if (new FileInfo(path).Length > MaxBytes) { throw new InvalidDataException("スキルのZIPは32 MBまでです。"); }
            using var zip = ZipFile.OpenRead(path);
            if (zip.Entries.Count > 512) { throw new InvalidDataException("ZIP内の項目が多すぎます。"); }
            var entries = zip.Entries.Where(e => !string.IsNullOrEmpty(e.Name)).ToList();
            foreach (ZipArchiveEntry entry in zip.Entries)
            {
                SafePath(entry.FullName.TrimEnd('/'));
                if (((entry.ExternalAttributes >> 16) & 0xf000) == 0xa000 || (entry.ExternalAttributes & (int)FileAttributes.ReparsePoint) != 0)
                { throw new InvalidDataException("ZIP内のリンクは取り込めません。"); }
            }
            string[] roots = entries.Where(e => e.FullName.Equals("SKILL.md", StringComparison.OrdinalIgnoreCase) || e.FullName.EndsWith("/SKILL.md", StringComparison.OrdinalIgnoreCase))
                .Select(e => e.FullName[..^8]).ToArray();
            if (roots.Length != 1) { throw new InvalidDataException("ZIPには1つのスキルとSKILL.mdを含めてください。"); }
            string prefix = roots[0]; directoryName = prefix.Length == 0 ? null : prefix.TrimEnd('/').Split('/').Last();
            foreach (ZipArchiveEntry entry in entries)
            {
                if (!entry.FullName.StartsWith(prefix, StringComparison.Ordinal) || entry.FullName.Length == prefix.Length)
                { throw new InvalidDataException("スキルのフォルダー外にファイルがあります。"); }
                using Stream input = entry.Open(); await Add(entry.FullName[prefix.Length..], input, entry.Length);
            }
        }
        SkillPackage package = SkillFormat.Parse(files);
        if (directoryName is not null && directoryName != package.Name) { throw new InvalidDataException("スキルのフォルダー名をnameと一致させてください: " + package.Name); }
        return package;
    }, token);

    private static string Hash(List<SkillFile> files) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', files.OrderBy(f => f.Path, StringComparer.Ordinal).Select(f => f.Path + "\t" + f.Size + "\t" + f.Sha256))))).ToLowerInvariant();
    public Task<SkillRegistration> SaveAsync(SkillPackage package, Guid? projectId, SkillRegistration? previous, CancellationToken token) => Task.Run(async () =>
    {
        package = SkillFormat.Parse(package.Files);
        if (package.Files.Count > MaxFiles || package.Files.Values.Any(b => b.Length > 16 * 1024 * 1024) || package.Files.Values.Sum(b => (long)b.Length) > MaxBytes) { throw new InvalidDataException("スキルのファイル上限を超えています。"); }
        var files = package.Files.Select(f => new SkillFile(SafePath(f.Key), f.Value.Length, Convert.ToHexString(SHA256.HashData(f.Value)).ToLowerInvariant())).ToList();
        string revision = Hash(files); Guid id = previous?.Id ?? Guid.NewGuid();
        var registration = new SkillRegistration(id, projectId, package.Name, package.Description, revision, previous?.Enabled ?? true, previous?.ExecutionPolicy ?? "ask", package.License, package.Compatibility);
        string target = RevisionDirectory(registration); RejectLinks(target, store.Root); Directory.CreateDirectory(Root);
        if (!Directory.Exists(target))
        {
            string temporary = Path.Combine(Root, ".staging-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(temporary);
            try
            {
                foreach ((string path, byte[] data) in package.Files)
                { string file = Path.Combine(temporary, SafePath(path)); Directory.CreateDirectory(Path.GetDirectoryName(file)!); await File.WriteAllBytesAsync(file, data, token); }
                await File.WriteAllTextAsync(Path.Combine(temporary, ".eluna-manifest.json"), JsonSerializer.Serialize(new SkillManifest(package.Name, package.Description, package.License, package.Compatibility, files), AppStore.JsonOptions), token);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                for (int attempt = 0; ; attempt++)
                {
                    try { Directory.Move(temporary, target); break; }
                    catch (IOException) when (Directory.Exists(target)) { break; }
                    catch (Exception ex) when (attempt < 7 && ex is IOException or UnauthorizedAccessException && (ex.HResult & 0xffff) is 5 or 32 or 33 or 1175 or 1176 or 1224)
                    { await Task.Delay(25 << attempt, token); }
                }
            }
            finally
            {
                if (Directory.Exists(temporary))
                {
                    RejectLinks(temporary, Root);
                    try { Directory.Delete(temporary, true); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
                }
            }
        }
        return registration;
    }, token);
    private string RevisionDirectory(SkillRegistration registration)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(registration.Revision, "^[a-f0-9]{64}$")) { throw new InvalidDataException("スキルの版情報が不正です。"); }
        return Path.Combine(Root, registration.Id.ToString("N"), registration.Revision);
    }
    public async Task<SkillManifest> ManifestAsync(SkillRegistration registration, CancellationToken token)
    {
        string directory = RevisionDirectory(registration); RejectLinks(directory, store.Root);
        string path = Path.Combine(directory, ".eluna-manifest.json"); RejectLinks(path, store.Root);
        if (!File.Exists(path)) { throw new FileNotFoundException("スキルの保存ファイルがありません。スキル管理から取り込み直してください。", path); }
        if (new FileInfo(path).Length > 256 * 1024) { throw new InvalidDataException("スキルの一覧が大きすぎます。"); }
        SkillManifest manifest = JsonSerializer.Deserialize<SkillManifest>(await File.ReadAllTextAsync(path, token), AppStore.JsonOptions) ?? throw new InvalidDataException("スキルの一覧が空です。");
        if (manifest.Name != registration.Name || manifest.Description != registration.Description || manifest.Files is null || manifest.Files.Count > MaxFiles ||
            manifest.Files.Select(f => SafePath(f.Path)).Distinct(StringComparer.OrdinalIgnoreCase).Count() != manifest.Files.Count ||
            manifest.Files.Any(f => f.Size < 0 || f.Size > 16 * 1024 * 1024) || manifest.Files.Sum(f => f.Size) > MaxBytes || Hash(manifest.Files) != registration.Revision)
        { throw new InvalidDataException("スキルの保存内容が変更されています。取り込み直してください。"); }
        return manifest;
    }
    public async Task<byte[]> ReadFileAsync(SkillRegistration registration, string relative, CancellationToken token)
    {
        relative = SafePath(relative); SkillManifest manifest = await ManifestAsync(registration, token);
        SkillFile expected = manifest.Files.FirstOrDefault(f => f.Path.Equals(relative, StringComparison.OrdinalIgnoreCase)) ?? throw new InvalidDataException("このスキルに含まれないファイルは読めません: " + relative);
        string path = Path.Combine(RevisionDirectory(registration), expected.Path); RejectLinks(path, store.Root);
        if (new FileInfo(path).Length != expected.Size) { throw new InvalidDataException("スキルのファイルサイズが変わっています。"); }
        byte[] bytes = await File.ReadAllBytesAsync(path, token);
        if (Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant() != expected.Sha256) { throw new InvalidDataException("スキルのファイルが変更されています。取り込み直してください。"); }
        return bytes;
    }
    public async Task<SkillPackage> PackageAsync(SkillRegistration registration, CancellationToken token)
    {
        SkillManifest manifest = await ManifestAsync(registration, token); var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        foreach (SkillFile file in manifest.Files) { files.Add(file.Path, await ReadFileAsync(registration, file.Path, token)); }
        return SkillFormat.Parse(files);
    }
    public async Task ExportAsync(SkillRegistration registration, string destination, CancellationToken token)
    {
        SkillPackage package = await PackageAsync(registration, token);
        string temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var zip = ZipFile.Open(temporary, ZipArchiveMode.Create))
            {
                foreach ((string path, byte[] data) in package.Files)
                { await using Stream output = zip.CreateEntry(package.Name + "/" + path, CompressionLevel.Optimal).Open(); await output.WriteAsync(data, token); }
            }
            token.ThrowIfCancellationRequested(); File.Move(temporary, destination, true);
        }
        finally { if (File.Exists(temporary)) { File.Delete(temporary); } }
    }
}
