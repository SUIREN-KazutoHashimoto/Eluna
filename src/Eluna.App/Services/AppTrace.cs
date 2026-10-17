using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Eluna.App.Models;

namespace Eluna.App.Services;

internal sealed class AppTrace(string root)
{
    private sealed record Pending(long Sequence, DateTimeOffset Time, string Level, string Area, string Event, string Detail, Guid? Correlation, Guid? Room, Guid? Agent);
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true };
    private readonly object _gate = new();
    private readonly Queue<Pending> _pending = [];
    private readonly Guid _session = Guid.NewGuid();
    private readonly string _directory = Path.Combine(root, "Logs");
    private volatile LogOptions _options = new();
    private Task _drain = Task.CompletedTask;
    private bool _draining;
    private long _sequence, _dropped;
    private int _part;
    internal string? LastFailure { get; private set; }
    internal long Dropped => Interlocked.Read(ref _dropped);
    internal bool Detailed => _options.Level == "detailed";
    internal bool CapturesContents => Detailed && _options.CaptureContents;
    internal static void Validate(LogOptions options)
    {
        if (options.Level is not ("off" or "normal" or "detailed") || options.RetentionDays is < 1 or > 90 || options.MaxMegabytes is < 8 or > 2048)
        { throw new InvalidDataException("ログの収集レベル・保持期間・容量を確認してください。"); }
    }
    internal void Configure(LogOptions options) { Validate(options); _options = options; }
    internal void Write(string level, string area, string name, string detail = "", Guid? correlation = null, Guid? room = null, Guid? agent = null)
    {
        if (_options.Level == "off" || level == "debug" && !Detailed) { return; }
        lock (_gate)
        {
            if (_pending.Count >= 10000) { Interlocked.Increment(ref _dropped); return; }
            _pending.Enqueue(new(++_sequence, DateTimeOffset.UtcNow, level, area, name, detail, correlation, room, agent));
            if (!_draining) { _draining = true; _drain = Task.Run(DrainAsync); }
        }
    }
    internal void Content(string area, string name, string content, Guid? correlation = null, Guid? room = null, Guid? agent = null)
    { if (CapturesContents) { Write("debug", area, name, content, correlation, room, agent); } }
    internal void Failure(string area, string name, Exception error, Guid? correlation = null, Guid? room = null, Guid? agent = null) => Write("error", area, name, error.ToString(), correlation, room, agent);
    internal static string Redact(string text)
    {
        try
        {
            text = Regex.Replace(text, @"-----BEGIN (?:[A-Z ]+ )?PRIVATE KEY-----[\s\S]*?(?:-----END (?:[A-Z ]+ )?PRIVATE KEY-----|\z)|\b[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}", "[secret omitted]", RegexOptions.NonBacktracking, TimeSpan.FromMilliseconds(250));
            text = Regex.Replace(text, @"(?<scheme>https?://)[^/\s:@]+:[^/\s@]+@", "${scheme}[credentials omitted]@", RegexOptions.IgnoreCase | RegexOptions.NonBacktracking, TimeSpan.FromMilliseconds(250));
            text = Regex.Replace(text, @"\b(?:Set-Cookie|Cookie)\s*:\s*[^\r\n]+", "Cookie: [secret omitted]", RegexOptions.IgnoreCase | RegexOptions.NonBacktracking, TimeSpan.FromMilliseconds(250));
            text = Regex.Replace(text, @"\b(?:Bearer|Basic)\s+[A-Za-z0-9+/_.=\-]+|\b(?:sk-[A-Za-z0-9_-]{12,}|hf_[A-Za-z0-9]{12,}|gh[pousr]_[A-Za-z0-9_]{12,}|github_pat_[A-Za-z0-9_]{12,})", "[secret omitted]", RegexOptions.IgnoreCase | RegexOptions.NonBacktracking, TimeSpan.FromMilliseconds(250));
            return Regex.Replace(text, @"(?<key>[""']?(?:password|passwd|secret|api[_-]?key|access[_-]?token|refresh[_-]?token|id[_-]?token|client[_-]?secret|sessionid|authorization|cookie|パスワード)[""']?\s*[:=]\s*)(?:""(?:\\.|[^""\\])*""|'[^']*'|[^\s,;&}\r\n]+)", "${key}[secret omitted]", RegexOptions.IgnoreCase | RegexOptions.NonBacktracking, TimeSpan.FromMilliseconds(250));
        }
        catch (RegexMatchTimeoutException) { return "[秘密情報の検査が完了しなかったため本文を保存しませんでした]"; }
    }
    private async Task DrainAsync()
    {
        int inFlight = 0;
        try
        {
            Directory.CreateDirectory(_directory);
            while (true)
            {
                Pending[] batch;
                lock (_gate)
                {
                    if (_pending.Count == 0) { _draining = false; return; }
                    batch = Enumerable.Range(0, Math.Min(128, _pending.Count)).Select(_ => _pending.Dequeue()).ToArray(); inFlight = batch.Length;
                }
                FileStream? output = null;
                try
                {
                    foreach (Pending entry in batch)
                    {
                        string safe = Redact(entry.Detail); var chunks = new List<string>();
                        for (int offset = 0; offset < safe.Length;)
                        { int length = Math.Min(16000, safe.Length - offset); if (offset + length < safe.Length && char.IsHighSurrogate(safe[offset + length - 1])) { length--; } chunks.Add(safe.Substring(offset, length)); offset += length; }
                        if (chunks.Count == 0) { chunks.Add(""); }
                        for (int part = 0; part < chunks.Count; part++)
                        {
                            var row = new DiagnosticEvent(_session, entry.Sequence, entry.Time, entry.Level, entry.Area, entry.Event, chunks[part], entry.Correlation, entry.Room, entry.Agent, part, chunks.Count);
                            byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(row, Json);
                            output ??= new FileStream(Path.Combine(_directory, $"trace-{_session:N}-{_part:D4}.jsonl"), FileMode.Append, FileAccess.Write, FileShare.ReadWrite, 16384, true);
                            if (output.Length > 0 && output.Length + bytes.Length > 2 * 1024 * 1024)
                            { await output.DisposeAsync(); _part++; output = new(Path.Combine(_directory, $"trace-{_session:N}-{_part:D4}.jsonl"), FileMode.Append, FileAccess.Write, FileShare.ReadWrite, 16384, true); }
                            await output.WriteAsync(bytes); await output.WriteAsync("\n"u8.ToArray());
                        }
                        inFlight--;
                    }
                }
                finally { if (output is not null) { await output.DisposeAsync(); } }
                Trim(); LastFailure = null;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        { LastFailure = ex.Message; lock (_gate) { Interlocked.Add(ref _dropped, _pending.Count + inFlight); _pending.Clear(); _draining = false; } }
    }
    private void Trim()
    {
        FileInfo[] files = new DirectoryInfo(_directory).GetFiles("trace-*.jsonl").OrderByDescending(f => f.LastWriteTimeUtc).ToArray();
        long kept = 0; DateTime cutoff = DateTime.UtcNow.AddDays(-_options.RetentionDays);
        foreach (FileInfo file in files)
        { kept += file.Length; if (file.LastWriteTimeUtc < cutoff || kept > (long)_options.MaxMegabytes * 1024 * 1024) { file.Delete(); } }
    }
    internal async Task FlushAsync()
    { while (true) { Task drain; lock (_gate) { if (!_draining && _pending.Count == 0) { return; } drain = _drain; } await drain; } }
    internal async Task<IReadOnlyList<DiagnosticEvent>> ReadAsync(Guid? correlation = null, int limit = 5000, CancellationToken token = default, IReadOnlySet<Guid>? correlations = null)
    {
        await FlushAsync();
        return await Task.Run(async () =>
        {
            var result = new Queue<DiagnosticEvent>();
            foreach (string path in Directory.Exists(_directory) ? Directory.EnumerateFiles(_directory, "trace-*.jsonl").OrderBy(File.GetLastWriteTimeUtc).ToArray() : Array.Empty<string>())
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    using var reader = new StreamReader(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete));
                    while (await reader.ReadLineAsync(token) is { } line)
                    {
                        try
                        { DiagnosticEvent? row = JsonSerializer.Deserialize<DiagnosticEvent>(line, Json); if (row is null || correlation is not null && row.Correlation != correlation || correlations is not null && (row.Correlation is not Guid related || !correlations.Contains(related))) { continue; } result.Enqueue(row); if (result.Count > limit) { result.Dequeue(); } }
                        catch (JsonException) { }
                    }
                }
                catch (FileNotFoundException) { }
            }
            return (IReadOnlyList<DiagnosticEvent>)result.ToArray();
        }, token);
    }
    internal async Task ExportAsync(string destination, CancellationToken token = default)
    {
        await FlushAsync();
        await Task.Run(async () =>
        {
            string temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 16384, true))
                {
                    using var zip = new ZipArchive(output, ZipArchiveMode.Create, true, Encoding.UTF8);
                    foreach (string path in Directory.Exists(_directory) ? Directory.EnumerateFiles(_directory, "trace-*.jsonl").OrderBy(File.GetLastWriteTimeUtc).ToArray() : Array.Empty<string>())
                    {
                        token.ThrowIfCancellationRequested();
                        try
                        {
                            await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 16384, true);
                            await using Stream entry = zip.CreateEntry(Path.GetFileName(path), CompressionLevel.Fastest).Open(); await input.CopyToAsync(entry, token);
                        }
                        catch (FileNotFoundException) { }
                    }
                    await using Stream summary = zip.CreateEntry("collection.json").Open();
                    await JsonSerializer.SerializeAsync(summary, new { version = UpdateService.CurrentVersion, created = DateTimeOffset.UtcNow, _session, options = _options, dropped = Dropped, lastFailure = LastFailure }, Json, token);
                }
                token.ThrowIfCancellationRequested(); File.Move(temporary, destination, true);
            }
            finally { if (File.Exists(temporary)) { File.Delete(temporary); } }
        }, token);
    }
}

internal sealed record TraceOperation(AppTrace Trace, Guid Correlation, Guid? Room = null, Guid? Agent = null)
{
    internal void Write(string level, string area, string name, string detail = "") => Trace.Write(level, area, name, detail, Correlation, Room, Agent);
    internal void Content(string area, string name, string content) => Trace.Content(area, name, content, Correlation, Room, Agent);
    internal void Failure(string area, string name, Exception error) => Trace.Failure(area, name, error, Correlation, Room, Agent);
}
internal static class DiagnosticScope
{
    private static readonly AsyncLocal<TraceOperation?> Active = new();
    internal static TraceOperation? Current => Active.Value;
    internal static IDisposable Start(AppTrace trace, Guid correlation, Guid? room = null, Guid? agent = null)
    { TraceOperation? previous = Active.Value; Active.Value = new(trace, correlation, room, agent); return new Lease(previous); }
    private sealed class Lease(TraceOperation? previous) : IDisposable
    { public void Dispose() { Active.Value = previous; } }
}
