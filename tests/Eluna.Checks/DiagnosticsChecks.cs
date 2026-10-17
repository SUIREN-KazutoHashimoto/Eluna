using System.IO;
using System.IO.Compression;
using Eluna.App.Models;
using Eluna.App.Services;

namespace Eluna.Checks;

internal static class DiagnosticsChecks
{
    private static void Check(bool condition, string message) { if (!condition) { throw new InvalidOperationException(message); } }
    internal static async Task RunAsync(string root)
    {
        Directory.CreateDirectory(root); var trace = new AppTrace(root); Guid first = Guid.NewGuid(), second = Guid.NewGuid();
        trace.Configure(new("off")); trace.Write("error", "test", "off", "must not be stored"); await trace.FlushAsync();
        Check(!Directory.Exists(Path.Combine(root, "Logs")), "Disabled collection created log files.");
        trace.Configure(new()); trace.Write("debug", "test", "suppressed"); trace.Content("test", "private", "must not be stored"); trace.Write("info", "test", "normal", correlation: first); await trace.FlushAsync();
        Check((await trace.ReadAsync()).Count == 1, "Normal collection captured debug events or body content.");
        trace.Configure(new("detailed")); trace.Write("debug", "test", "metadata", correlation: first); trace.Content("test", "private", "must not be stored"); await trace.FlushAsync();
        Check((await trace.ReadAsync()).Count == 2, "Detailed metadata mode stored content without explicit capture.");
        trace.Configure(new("detailed", true)); string secret = "hf_0123456789abcdefghijklmnopqrstuvwxyz";
        trace.Content("model", "request", "password=not-for-log Authorization: Bearer abcdef12345 " + secret, first);
        await Task.WhenAll(Enumerable.Range(0, 120).Select(i => Task.Run(() => trace.Write("debug", "queue", "event", "index=" + i, i % 2 == 0 ? first : second))));
        await trace.FlushAsync(); IReadOnlyList<DiagnosticEvent> rows = await trace.ReadAsync();
        Check(rows.Count == 123 && rows.Select(r => r.Sequence).Distinct().Count() == rows.Count && rows.All(r => !r.Detail.Contains(secret, StringComparison.Ordinal) && !r.Detail.Contains("not-for-log", StringComparison.Ordinal) && !r.Detail.Contains("abcdef12345", StringComparison.Ordinal)), "Concurrent records were lost or credentials entered the log.");
        Check((await trace.ReadAsync(first)).All(r => r.Correlation == first) && (await trace.ReadAsync(first)).Count == 63, "Message correlation crossed unrelated operations.");
        using (DiagnosticScope.Start(trace, first))
        {
            await Task.WhenAll(Task.Run(async () => { using var scope = DiagnosticScope.Start(trace, second); await Task.Yield(); DiagnosticScope.Current!.Write("debug", "scope", "second"); }), Task.Run(async () => { await Task.Yield(); DiagnosticScope.Current!.Write("debug", "scope", "first"); }));
            Check(DiagnosticScope.Current?.Correlation == first, "Concurrent child scope changed its parent's operation.");
        }
        Check(DiagnosticScope.Current is null, "Operation context leaked after disposal.");
        string longText = new string('文', 15999) + "😀" + new string('字', 16000);
        trace.Content("model", "long", longText, first); await trace.FlushAsync(); DiagnosticEvent[] chunks = (await trace.ReadAsync(first)).Where(r => r.Event == "long").OrderBy(r => r.Part).ToArray();
        Check(chunks.Length == chunks[0].Parts && string.Concat(chunks.Select(r => r.Detail)) == longText, "Large Unicode I/O lost data or split its reconstruction metadata.");
        string zipPath = Path.Combine(root, "trace.zip"); await trace.ExportAsync(zipPath);
        using (ZipArchive archive = ZipFile.OpenRead(zipPath)) { Check(archive.Entries.Any(e => e.Name == "collection.json") && archive.Entries.Any(e => e.Name.EndsWith(".jsonl", StringComparison.Ordinal)), "Log export omitted configuration or events."); foreach (ZipArchiveEntry entry in archive.Entries) { using var reader = new StreamReader(entry.Open()); Check(!(await reader.ReadToEndAsync()).Contains(secret, StringComparison.Ordinal), "Export reintroduced a credential."); } }
        string retentionRoot = Path.Combine(root, "retention"); var retained = new AppTrace(retentionRoot); retained.Configure(new("detailed", true, 1, 8));
        Directory.CreateDirectory(Path.Combine(retentionRoot, "Logs")); string old = Path.Combine(retentionRoot, "Logs", "trace-old.jsonl"); await File.WriteAllTextAsync(old, "old"); File.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddDays(-3));
        for (int i = 0; i < 10; i++) { retained.Content("test", "capacity", i + new string('x', 1024 * 1024)); }
        await retained.FlushAsync(); long size = new DirectoryInfo(Path.Combine(retentionRoot, "Logs")).GetFiles("trace-*.jsonl").Sum(f => f.Length);
        Check(!File.Exists(old) && size <= 8 * 1024 * 1024 && size > 0 && (await retained.ReadAsync()).Any(r => r.Detail.StartsWith("9", StringComparison.Ordinal)), "Age/size retention removed the newest trace or exceeded its limit.");
        var settings = new AppSettings(DeveloperMode: false, Logs: new("detailed", true)); var store = new AppStore(Path.Combine(root, "state")); await store.SaveAsync(new(1, settings, []));
        Check((await store.LoadAsync()).Settings == settings, "Developer visibility and collection preferences did not persist independently.");
        string extraSecrets = "-----BEGIN PRIVATE KEY-----\nFAKE_PRIVATE_MATERIAL\n-----END PRIVATE KEY-----\nCookie: session=FAKE_COOKIE; second=FAKE_SECOND\nhttps://account:FAKE_PASSWORD@example.test/\nclient_secret=FAKE_CLIENT_SECRET"; string protectedText = AppTrace.Redact(extraSecrets); Check(!new[] { "FAKE_PRIVATE_MATERIAL", "FAKE_COOKIE", "FAKE_SECOND", "FAKE_PASSWORD", "FAKE_CLIENT_SECRET" }.Any(protectedText.Contains), "Multiline private keys, cookies, URL credentials or client secrets leaked into diagnostics.");
        Console.WriteLine("Diagnostics: off/normal/detailed metadata and explicit I/O, redaction, concurrent context isolation and ordering, message correlation, Unicode chunks, ZIP, retention and settings: OK");
    }
}
