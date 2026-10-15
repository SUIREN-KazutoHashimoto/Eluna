using System.IO;
using System.IO.Compression;
using System.Text;
using Eluna.App.Services;

namespace Eluna.Checks;

internal static class LicenseChecks
{
    internal static async Task RunAsync()
    {
        IReadOnlyList<LicenseDocument> basic = await LicenseCatalog.ReadAsync(null, default);
        Require(basic[0].Title == "Eluna" && basic[1].Title == "llama.cpp" && basic[2].Title == "stable-diffusion.cpp", "The primary software licenses are missing or difficult to find.");
        Require(basic[0].Text.Contains("Version 2.0", StringComparison.Ordinal) && basic[1].Text.Contains("The ggml authors", StringComparison.Ordinal) &&
            basic[2].Text.Contains("Copyright (c) 2023 leejet", StringComparison.Ordinal) && basic[2].Text.Contains("THE SOFTWARE IS PROVIDED", StringComparison.Ordinal), "The application or native-engine original notices were lost.");
        using var bundle = new MemoryStream();
        const string original = "Copyright © 2026\r\n\r\n全文は加工せず保持する。\n";
        using (var archive = new ZipArchive(bundle, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach ((string name, string text) in new[]
            {
                ("Licenses/vendor/example/LICENSE", original),
                ("Licenses/stable-diffusion-thirdparty-example.h", original + "// source-contained license"),
                ("Licenses/llama.cpp-MIT.txt", basic[1].Text),
                ("cpu/llama-server.exe", "This is an engine, not a notice"),
                ("Other/Licenses/ignored.txt", "This is outside the notice directory")
            })
            { using var writer = new StreamWriter(archive.CreateEntry(name).Open(), new UTF8Encoding(false)); await writer.WriteAsync(text); }
            archive.CreateEntry("Licenses/empty-directory/");
        }
        bundle.Position = 0;
        IReadOnlyList<LicenseDocument> combined = await LicenseCatalog.ReadAsync(bundle, default);
        Require(combined.Count == basic.Count + 2 && combined.Count(d => d.FileName == "llama.cpp-MIT.txt") == 1, "The license viewer duplicated notices or included native binaries.");
        Require(combined.Single(d => d.FileName == "vendor/example/LICENSE").Text == original &&
            combined.Single(d => d.FileName.EndsWith("example.h", StringComparison.Ordinal)).Text == original + "// source-contained license", "The viewer changed or truncated copyright text, line endings, or source-contained notices.");
        Require(bundle.CanRead, "The license reader closed its caller's stream.");
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        bool stopped = false; try { await LicenseCatalog.ReadAsync(null, canceled.Token); } catch (OperationCanceledException) { stopped = true; }
        Require(stopped, "Closing settings could not cancel notice loading.");
        using var invalid = new MemoryStream(Encoding.UTF8.GetBytes("broken archive"));
        bool failed = false; try { await LicenseCatalog.ReadAsync(invalid, default); } catch (InvalidDataException) { failed = true; }
        Require(failed, "A broken notice bundle silently appeared complete.");
        Console.WriteLine("Licenses: embedded primary notices, nested/source-contained notices, exact text, deduplication, binary exclusion, cancellation and corrupt archive: OK");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) { throw new InvalidOperationException(message); }
    }
}
