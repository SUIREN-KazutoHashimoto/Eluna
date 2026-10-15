using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text;

namespace Eluna.App.Services;

internal sealed record LicenseDocument(string FileName, string Title, string Description, string Text);

internal static class LicenseCatalog
{
    private const string ResourcePrefix = "Eluna.Licenses.";

    internal static async Task<IReadOnlyList<LicenseDocument>> LoadAsync(CancellationToken cancellationToken)
    {
        using Stream? bundle = typeof(LicenseCatalog).Assembly.GetManifestResourceStream("Eluna.NativeRuntime.zip");
        return await ReadAsync(bundle, cancellationToken);
    }

    // Read the embedded notices directly, even before the native engines have been extracted.
    internal static async Task<IReadOnlyList<LicenseDocument>> ReadAsync(Stream? nativeBundle, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var documents = new Dictionary<string, LicenseDocument>(StringComparer.OrdinalIgnoreCase);
        Assembly assembly = typeof(LicenseCatalog).Assembly;
        foreach (string resource in assembly.GetManifestResourceNames().Where(n => n.StartsWith(ResourcePrefix, StringComparison.Ordinal)))
        {
            using Stream source = assembly.GetManifestResourceStream(resource)!;
            using var reader = new StreamReader(source, Encoding.UTF8, true);
            string fileName = resource[ResourcePrefix.Length..];
            documents[fileName] = Describe(fileName, await reader.ReadToEndAsync(cancellationToken));
        }
        if (nativeBundle is not null)
        {
            using var archive = new ZipArchive(nativeBundle, ZipArchiveMode.Read, leaveOpen: true);
            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!entry.FullName.StartsWith("Licenses/", StringComparison.Ordinal) || entry.Name.Length == 0) { continue; }
                string fileName = entry.FullName["Licenses/".Length..];
                using Stream source = entry.Open();
                using var reader = new StreamReader(source, Encoding.UTF8, true);
                documents[fileName] = Describe(fileName, await reader.ReadToEndAsync(cancellationToken));
            }
        }
        return documents.Values.OrderBy(d => Order(d.FileName)).ThenBy(d => d.Title, StringComparer.OrdinalIgnoreCase).ThenBy(d => d.FileName, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static int Order(string name) => name switch
    {
        "Eluna-Apache-2.0.txt" => 0,
        "llama.cpp-MIT.txt" => 1,
        "stable-diffusion-LICENSE" => 2,
        "THIRD_PARTY_NOTICES.md" => 3,
        _ => 4
    };

    private static LicenseDocument Describe(string name, string text)
    {
        (string title, string description) = name switch
        {
            "Eluna-Apache-2.0.txt" => ("Eluna", "アプリ本体 · Apache-2.0"),
            "llama.cpp-MIT.txt" => ("llama.cpp", "会話・画像認識エンジン · MIT"),
            "stable-diffusion-LICENSE" => ("stable-diffusion.cpp", "画像生成エンジン · MIT"),
            "THIRD_PARTY_NOTICES.md" => ("第三者コードについて", "同梱ソフトウェアとモデルの扱い"),
            "Vulkan-SDK-notices.txt" => ("Vulkan", "GPU処理に関連する通知"),
            _ when name.StartsWith("Microsoft.NETCore.", StringComparison.Ordinal) => (".NET ランタイム", name.Contains("THIRD-PARTY", StringComparison.Ordinal) ? "第三者コードの通知" : "ランタイムの利用条件"),
            _ when name.StartsWith("Microsoft.WindowsDesktop.", StringComparison.Ordinal) => ("WPF ランタイム", "Windows用UIランタイムの利用条件"),
            _ when name.StartsWith("vendor/", StringComparison.Ordinal) => ("llama.cpp / " + name.Split('/')[^2], "同梱ライブラリの通知"),
            _ when name.StartsWith("stable-diffusion-", StringComparison.Ordinal) => (name["stable-diffusion-".Length..].Replace("thirdparty-", "", StringComparison.Ordinal), "stable-diffusion.cpp の関連通知"),
            _ => (name, "同梱ソフトウェアの通知")
        };
        return new(name, title, description, text);
    }
}
