using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows.Threading;
using Eluna.App.Models;
using Eluna.App.Services;

namespace Eluna.Checks;

internal static class RemainingFeatureChecks
{
    private static void Check(bool value, string message) { if (!value) { throw new InvalidOperationException(message); } }
    internal static async Task RunAsync(string root)
    {
        Directory.CreateDirectory(root);
        var random = new Random(743);
        for (int i = 0; i < 200; i++)
        {
            string a = string.Join('\n', Enumerable.Range(0, random.Next(1, 40)).Select(_ => "行" + random.Next(12)));
            string b = string.Join('\n', Enumerable.Range(0, random.Next(1, 40)).Select(_ => "行" + random.Next(12)));
            var diff = TextDiff.Compare(a, b);
            Check(string.Join('\n', diff.Where(l => l.Kind != '+').Select(l => l.Text)) == a && string.Join('\n', diff.Where(l => l.Kind != '-').Select(l => l.Text)) == b, "Diff reconstruction lost or invented content.");
        }
        string key = Guid.NewGuid() + "/" + Guid.NewGuid(); var positions = new ReadingPositions(root); positions.Set(key, 158.5, false); positions.Set("bad", double.NaN, true); await positions.SaveAsync();
        var restored = new ReadingPositions(root); await restored.LoadAsync(); Check(restored.Find(key) is { Offset: 158.5, Follow: false }, "Reading position did not survive restart.");
        foreach (string source in new[] { "| A | B |\n| --- | --- |\n| c | d |\n\n```cs\nvar a = 3;\n```\n\n# 見出し\n\n段落\n次の行", "a\r\nb\r\n\r\n~~~py\r\nprint('x')\r\n~~~\r\n\r\n> 引用\r\n> 続き\r\n", string.Join("\n\n", Enumerable.Range(0, 100).Select(i => "段落 " + i)) })
        {
            var parser = new StreamingMarkdownParser();
            for (int i = 1; i <= source.Length; i++)
            {
                string current = source[..i]; Check(JsonSerializer.Serialize(parser.Update(current)) == JsonSerializer.Serialize(MarkdownParser.Parse(current)), "Incremental Markdown differs from full parsing at " + i);
            }
            Check(JsonSerializer.Serialize(parser.Update("編集後\n\n# 別の回答")) == JsonSerializer.Serialize(MarkdownParser.Parse("編集後\n\n# 別の回答")), "Replacing a streamed answer retained stale blocks.");
        }
        var stream = new StreamingMarkdownParser(); string head = string.Join("\n\n", Enumerable.Range(0, 3000).Select(i => "段落 " + i + " " + new string('文', 50))); stream.Update(head);
        for (int i = 0; i < 100; i++) { head += "\n\n追記 " + i; stream.Update(head); }
        Check(stream.ParsedCharacters < head.Length * 3, "Streaming parsing repeatedly parsed the stable document prefix.");
        using var json = JsonDocument.Parse("""{"id":"fixture/model","tags":["reasoning"],"pipeline_tag":"image-text-to-text","downloads":1} """);
        var repository = ModelCatalog.RepositoryInfo(json.RootElement);
        Check(repository.SupportsThinking == true && repository.SupportsVision == true && repository.MatchesCapability(1, false), "Capability metadata was lost.");
        var unknown = new ModelRepository("fixture/unknown", "MIT", 0); Check(!unknown.MatchesCapability(1, false) && unknown.MatchesCapability(1, true), "Unknown capabilities were guessed.");
        Check(HardwareAdvisor.Describe(1L << 30, null, 4096, new(16L << 30, 12L << 30, 8L << 30, "fixture", 1L << 30)).Contains("空きVRAMを超える", StringComparison.Ordinal), "GPU fit ignored current free memory.");
        await DispatcherCheckAsync(() => DownloadsAsync(Path.Combine(root, "parallel-downloads")));
        Console.WriteLine("Remaining features: exact diffs, persisted reading positions, streamed Markdown equivalence/work bounds, capability unknowns, VRAM estimates and parallel download isolation: OK");
    }
    private static Task DispatcherCheckAsync(Func<Task> check)
    {
        var result = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher; SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            dispatcher.BeginInvoke(async () => { try { await check(); result.TrySetResult(); } catch (Exception ex) { result.TrySetException(ex); } finally { dispatcher.BeginInvokeShutdown(DispatcherPriority.Background); } }); Dispatcher.Run();
        }); thread.SetApartmentState(ApartmentState.STA); thread.Start(); return result.Task;
    }
    private static async Task DownloadsAsync(string root)
    {
        using var handler = new ControlledDownloads(); using var http = new HttpClient(handler); using var catalog = new ModelCatalog(new(root), http); using var queue = new DownloadQueue(new(root), catalog) { Limit = 2 };
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        async Task Until(Func<bool> ready) { while (!ready()) { await Task.Delay(10, timeout.Token); } }
        ModelFile File(string name) => new("fixture/images", new string('a', 40), name, Convert.ToHexString(SHA256.HashData(handler.Bytes)), handler.Bytes.Length, "MIT");
        DownloadItem a = queue.EnqueueImage(File("a.gguf")), b = queue.EnqueueImage(File("b.gguf")), c = queue.EnqueueImage(File("c.gguf"));
        await Until(() => handler.Entered == 2); Check(a.Status == "active" && b.Status == "active" && c.Status == "queued", "Download concurrency limit was not respected.");
        queue.Limit = 1; queue.Pause(a); await Until(() => a.Status == "paused"); Check(b.Status == "active" && c.Status == "queued", "Stopping one download affected its peer or ignored the reduced limit.");
        handler.Release.TrySetResult(); await Until(() => b.Status == "complete" && c.Status == "complete");
        Check(b.ResultPath != c.ResultPath && System.IO.File.Exists(b.ResultPath) && System.IO.File.Exists(c.ResultPath), "Parallel downloads shared or lost output files.");
        DownloadItem broken = queue.EnqueueImage(File("bad.gguf")), healthy = queue.EnqueueImage(File("good.gguf")); queue.Limit = 2;
        await Until(() => broken.Status == "failed" && healthy.Status == "complete");
        await queue.ShutdownAsync(); using var restored = new DownloadQueue(new(root), new ModelCatalog(new(root), http)); await restored.LoadAsync();
        Check(restored.Items.Single(i => i.Id == a.Id).Status == "paused" && restored.Items.Count(i => i.Status == "complete") == 3, "Parallel download state did not survive restart.");
    }
    private sealed class ControlledDownloads : HttpMessageHandler
    {
        public byte[] Bytes { get; } = [.. "GGUF"u8.ToArray(), .. new byte[8192]];
        public int Entered;
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            byte[] payload = request.RequestUri!.AbsolutePath.EndsWith("bad.gguf", StringComparison.Ordinal) ? new byte[Bytes.Length] : Bytes;
            Interlocked.Increment(ref Entered); return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new DelayedStream(payload, Release.Task)) });
        }
    }
    private sealed class DelayedStream(byte[] bytes, Task release) : MemoryStream(bytes)
    {
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default) { await release.WaitAsync(token); return await base.ReadAsync(buffer, token); }
    }
}
