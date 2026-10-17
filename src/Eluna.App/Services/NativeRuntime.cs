using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;

namespace Eluna.App.Services;

public static class NativeRuntime
{
    public static async Task<string> PrepareAsync(AppStore store)
    {
        using Stream? bundle = Assembly.GetExecutingAssembly().GetManifestResourceStream("Eluna.NativeRuntime.zip");
        if (bundle is null)
        {
            string? directory = FindDevelopmentRuntime();
            return directory ?? throw new FileNotFoundException("推論エンジンが見つかりません。配布用のEluna.exeを使用してください。");
        }
        string hash = Convert.ToHexString(await SHA256.HashDataAsync(bundle)).ToLowerInvariant();
        string target = Path.Combine(store.Root, "Runtime", hash[..20]);
        string marker = Path.Combine(target, ".ready");
        if (File.Exists(marker) && File.Exists(Path.Combine(target, "cpu", "llama-server.exe")) && File.Exists(Path.Combine(target, "vulkan", "llama-server.exe")) && File.Exists(Path.Combine(target, "cpu", "sd-cli.exe")) && File.Exists(Path.Combine(target, "vulkan", "sd-cli.exe")) && File.Exists(Path.Combine(target, "ui", "Eluna.Text.dll")))
        {
            return target;
        }
        bundle.Position = 0;
        Directory.CreateDirectory(target);
        using var archive = new ZipArchive(bundle, ZipArchiveMode.Read);
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            string destination = Path.GetFullPath(Path.Combine(target, entry.FullName));
            if (!destination.StartsWith(target + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("推論エンジンのアーカイブ形式が不正です。");
            }
            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(destination);
                continue;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            await using Stream source = entry.Open();
            await using var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None, 65536, true);
            await source.CopyToAsync(output);
        }
        await File.WriteAllTextAsync(marker, hash);
        return target;
    }

    private static string? FindDevelopmentRuntime()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            foreach (string relative in new[] { "build/llama-cpu-static/bin/Release", "build/llama-cpu/bin/Release" })
            {
                string candidate = Path.Combine(directory.FullName, relative);
                if (File.Exists(Path.Combine(candidate, "llama-server.exe")))
                {
                    return Path.Combine(directory.FullName, "build");
                }
            }
        }
        return null;
    }
}
