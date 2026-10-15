using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Eluna.App.Models;
using Eluna.App.Services;

namespace Eluna.Checks;

internal static class ModelManagementChecks
{
    private static void Assert(bool condition, string message) { if (!condition) { throw new InvalidOperationException(message); } }
    public static byte[] GgufFixture()
    {
        using var memory = new MemoryStream(); using var writer = new BinaryWriter(memory, Encoding.UTF8, true);
        void Text(string text) { byte[] bytes = Encoding.UTF8.GetBytes(text); writer.Write((ulong)bytes.Length); writer.Write(bytes); }
        writer.Write(0x46554747u); writer.Write(3u); writer.Write(2ul); writer.Write(11ul);
        void String(string key, string value) { Text(key); writer.Write(8u); Text(value); }
        void Number(string key, uint value) { Text(key); writer.Write(4u); writer.Write(value); }
        String("general.architecture", "test"); String("general.name", "確認用モデル"); Number("general.file_type", 15);
        String("tokenizer.chat_template", "{% if enable_thinking %}<think>{% endif %}"); Number("test.context_length", 16384);
        Number("test.block_count", 4); Number("test.embedding_length", 1024); Number("test.attention.head_count", 16);
        Number("test.attention.head_count_kv", 2); String("general.license", "test-license");
        Text("general.languages"); writer.Write(9u); writer.Write(8u); writer.Write(2ul); Text("ja"); Text("en");
        void Tensor(string name, ulong x, ulong y) { Text(name); writer.Write(2u); writer.Write(x); writer.Write(y); writer.Write(2u); writer.Write(0ul); }
        Tensor("embedding.weight", 1024, 512); Tensor("output.weight", 512, 128); writer.Flush(); return memory.ToArray();
    }
    public static async Task RunAsync(string root)
    {
        Directory.CreateDirectory(root); string input = Path.Combine(root, "fixture.gguf"); await File.WriteAllBytesAsync(input, GgufFixture());
        var store = new AppStore(Path.Combine(root, "store")); using var catalog = new ModelCatalog(store); var service = new ModelsService(store);
        LocalModel model = await catalog.ImportAsync(input, null, CancellationToken.None); ModelProfile profile = model.Profile!;
        Assert(profile.Architecture == "test" && profile.Parameters == 589824 && profile.TrainingContext == 16384 && profile.Quantization == "Q4_K_M" && profile.SupportsThinking == true && profile.Languages.Contains("ja"), "GGUF capability or tensor metadata was incorrect.");
        Assert(profile.EstimatedKvBytes(4096) == 8 * 1024 * 1024, "KV capacity estimate did not use the model's head dimensions.");
        var capacity = HardwareAdvisor.Capture([]); Assert(capacity.TotalRam > 0 && capacity.AvailableRam <= capacity.TotalRam, "Windows physical memory could not be detected.");
        string insufficient = HardwareAdvisor.Describe(8L * 1024 * 1024 * 1024, profile, 4096, new(16L << 30, 2L << 30, 4L << 30, "test GPU"));
        Assert(insufficient.Contains("空きRAMを超える", StringComparison.Ordinal), "A model too large for free RAM was recommended as fitting.");
        var preferences = new ModelPreferences(8192, 0.25, 0.85, 7, 1.15, 1024, "fast");
        await service.UpdateAsync(model with { DisplayName = "名前変更", IsFavorite = true, Preferences = preferences });
        LocalModel restored = (await store.ListModelsAsync()).Single(); Assert(restored.IsFavorite && restored.DisplayName == "名前変更" && restored.Options == preferences, "Model name, favorite or preferences did not persist.");
        LocalModel reimported = await catalog.ImportAsync(input, null, CancellationToken.None); Assert(reimported.DisplayName == restored.DisplayName && reimported.Options == preferences && reimported.IsFavorite, "Reimport erased model preferences or favorites.");
        using JsonDocument payload = JsonDocument.Parse(JsonSerializer.Serialize(LlamaService.CreatePayload([new("user", "確認")], InferenceModes.Find("fast"), new(0, 1024), preferences: preferences)));
        Assert(payload.RootElement.GetProperty("temperature").GetDouble() == 0.25 && payload.RootElement.GetProperty("top_k").GetInt32() == 7 && payload.RootElement.GetProperty("repeat_penalty").GetDouble() == 1.15, "The engine request ignored model sampling preferences.");
        bool invalid = false; try { ModelPreferences.Validate(preferences with { Temperature = double.NaN }); } catch (InvalidDataException) { invalid = true; }
        Assert(invalid, "Nonfinite sampling settings were accepted.");
        using var cancel = new CancellationTokenSource(); string original = restored.FilePath;
        bool cancelled = false;
        try { await service.MoveDirectoryAsync(Path.Combine(root, "cancelled-move"), new InlineProgress(_ => cancel.Cancel()), cancel.Token); } catch (OperationCanceledException) { cancelled = true; }
        Assert(cancelled && File.Exists(original) && store.ModelsDirectory.EndsWith("Models", StringComparison.Ordinal) && (await store.ListModelsAsync()).Single().Options == preferences, "Cancelled relocation changed the active registry or deleted original weights.");
        string destination = Path.Combine(root, "new-models"); await service.MoveDirectoryAsync(destination, null, CancellationToken.None);
        LocalModel moved = (await store.ListModelsAsync()).Single();
        Assert(moved.FilePath.StartsWith(destination, StringComparison.OrdinalIgnoreCase) && !File.Exists(original) && File.Exists(moved.FilePath) && moved.Options == preferences && new AppStore(store.Root).ModelsDirectory == destination, "Model relocation was not verified, persisted or complete.");
        bool nested = false; try { await service.MoveDirectoryAsync(Path.Combine(destination, "nested"), null, CancellationToken.None); } catch (InvalidDataException) { nested = true; }
        Assert(nested && File.Exists(moved.FilePath), "A nested destination was accepted or damaged a model.");
        string external = Path.Combine(root, "external.gguf"); await File.WriteAllBytesAsync(external, GgufFixture());
        await service.UpdateAsync(moved with { FilePath = external }); bool protectedExternal = false;
        try { await service.DeleteAsync(moved with { FilePath = external }, CancellationToken.None); } catch (InvalidDataException) { protectedExternal = true; }
        Assert(protectedExternal && File.Exists(external), "Model deletion removed a file outside the model registry.");
        await service.UpdateAsync(moved); await service.DeleteAsync(moved, CancellationToken.None);
        Assert((await store.ListModelsAsync()).Count == 0 && !File.Exists(moved.FilePath), "Registered model deletion was incomplete.");
        string unavailableRoot = Path.Combine(root, "unavailable-store"); Directory.CreateDirectory(unavailableRoot);
        string unavailablePath = Path.Combine(root, "unavailable-file"); await File.WriteAllTextAsync(unavailablePath, "preserved");
        await File.WriteAllTextAsync(Path.Combine(unavailableRoot, "model-location.json"), JsonSerializer.Serialize(unavailablePath));
        var unavailable = new AppStore(unavailableRoot); Assert(unavailable.RecoveryNotice is not null && (await unavailable.ListModelsAsync()).Count == 0, "An unavailable model drive prevented the app from opening.");
        await new ModelsService(unavailable).MoveDirectoryAsync(Path.Combine(root, "available-again"), null, CancellationToken.None);
        Assert(File.Exists(unavailablePath) && Directory.Exists(unavailable.ModelsDirectory), "Recovering the model location removed the inaccessible original.");
        await QueueAsync(Path.Combine(root, "queue"));
        await TimeoutAsync(Path.Combine(root, "timeout"));
        Console.WriteLine("GGUF metadata, Windows capacity, model presets, favorite/name persistence, verified relocation and protected deletion: OK");
    }
    private static async Task QueueAsync(string root)
    {
        byte[] data = new byte[3 * 1024 * 1024]; RandomNumberGenerator.Fill(data); "GGUF"u8.CopyTo(data); string hash = Convert.ToHexString(SHA256.HashData(data));
        using var handler = new SlowDownloadHandler(data); using var http = new HttpClient(handler);
        var store = new AppStore(root); using var queue = new DownloadQueue(store, new ModelCatalog(store, http));
        ModelFile first = new("test/queue", "revision", "first.gguf", hash, data.Length, "test"), second = first with { Filename = "second.gguf" };
        DownloadItem one = queue.Enqueue(first), two = queue.Enqueue(second);
        one.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(DownloadItem.Received) && one.Received > 0 && one.Status == "active") { queue.Pause(one); } };
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        while (two.Status is not ("complete" or "failed")) { await Task.Delay(10, timeout.Token); }
        Assert(one.Status == "paused" && two.Status == "complete" && handler.MaximumRequests == 1 && (await store.ListModelsAsync()).Count == 1, "The background queue did not pause one task, continue the next or preserve sequential downloads.");
        await queue.ShutdownAsync();
        using var resumed = new DownloadQueue(store, new ModelCatalog(store, http)); await resumed.LoadAsync();
        Assert(resumed.Items.Count == 2 && resumed.Items[0].CanResume && resumed.Items[1].CanUse, "The queue did not survive app restart.");
        resumed.Resume(resumed.Items[0]);
        while (resumed.Items[0].Status is not ("complete" or "failed")) { await Task.Delay(10, timeout.Token); }
        Assert(resumed.Items[0].Status == "complete" && handler.RangeRequests > 0 && (await store.ListModelsAsync()).Count == 2, "A restored download did not continue from its existing partial data.");
        await resumed.ShutdownAsync();
        Console.WriteLine("Background sequential queue, pause/next task, saved restart, Range resume and completion: OK");
    }
    private sealed class InlineProgress(Action<DownloadProgress> callback) : IProgress<DownloadProgress> { public void Report(DownloadProgress progress) => callback(progress); }
    private static async Task TimeoutAsync(string root)
    {
        byte[] bytes = GgufFixture(); string hash = Convert.ToHexString(SHA256.HashData(bytes));
        var file = new ModelFile("test/timeout", "revision", "model.gguf", hash, bytes.Length, "test");
        using var handler = new StalledHandler(bytes); using var http = new HttpClient(handler);
        var store = new AppStore(root); using var queue = new DownloadQueue(store, new ModelCatalog(store, http, TimeSpan.FromMilliseconds(100)));
        DownloadItem item = queue.Enqueue(file); using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (item.Status is not ("failed" or "complete")) { await Task.Delay(10, timeout.Token); }
        Assert(item.Status == "failed" && item.CanResume && item.Error.Contains("途中から再開", StringComparison.Ordinal), "A stalled transfer remained active indefinitely or lost retry guidance.");
        handler.Stall = false; queue.Resume(item);
        while (item.Status is not ("failed" or "complete")) { await Task.Delay(10, timeout.Token); }
        Assert(item.Status == "complete" && (await store.ListModelsAsync()).Count == 1, "A timed-out transfer could not be retried.");
        await queue.ShutdownAsync(); Console.WriteLine("Stalled transfer timeout, retry guidance and successful retry: OK");
    }
    private sealed class StalledHandler(byte[] bytes) : HttpMessageHandler
    {
        public bool Stall { get; set; } = true;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            if (Stall) { await Task.Delay(Timeout.Infinite, token); }
            return new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
        }
    }
    private sealed class SlowDownloadHandler(byte[] data) : HttpMessageHandler
    {
        private int _requests;
        public int MaximumRequests { get; private set; }
        public int RangeRequests { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            int active = Interlocked.Increment(ref _requests); MaximumRequests = Math.Max(MaximumRequests, active);
            try
            {
                await Task.Delay(150, token); int offset = (int)(request.Headers.Range?.Ranges.Single().From ?? 0); if (offset > 0) { RangeRequests++; }
                var response = new HttpResponseMessage(offset > 0 ? HttpStatusCode.PartialContent : HttpStatusCode.OK) { Content = new ByteArrayContent(data, offset, data.Length - offset) };
                if (offset > 0) { response.Content.Headers.ContentRange = new ContentRangeHeaderValue(offset, data.Length - 1, data.Length); }
                return response;
            }
            finally { Interlocked.Decrement(ref _requests); }
        }
    }
}
