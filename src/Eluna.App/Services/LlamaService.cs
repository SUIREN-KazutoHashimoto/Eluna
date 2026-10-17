using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Eluna.App.Models;

namespace Eluna.App.Services;

public sealed class LlamaService : IDisposable
{
    private readonly AppStore _store;
    private readonly ConcurrentQueue<string> _diagnostics = new();
    private Process? _process;
    private WindowsJob? _job;
    private HttpClient? _http;
    private string? _activeModel;
    private string? _activeCompute;
    private int _activeContext;
    private string? _keyPath;
    private ModelPreferences _preferences = new();
    private bool? _supportsThinking;
    private string? _activeProjector;
    public string? BinaryDirectory { get; private set; }
    public string ComputeLabel { get; private set; } = "未読み込み";
    public IReadOnlyList<ComputeDevice> Devices { get; private set; } = [];
    public int LastInputTokens { get; private set; }
    private GenerationAllowance? _allowance;
    internal IDisposable WithAllowance(GenerationAllowance allowance)
    {
        if (_allowance is not null) { throw new InvalidOperationException("生成予算は同じエンジンへ重ねて設定できません。"); }
        _allowance = allowance; return new AllowanceLease(this);
    }
    private sealed class AllowanceLease(LlamaService engine) : IDisposable
    { public void Dispose() { engine._allowance = null; } }
    public int LastOutputTokens { get; private set; }
    public double LastTokensPerSecond { get; private set; }
    public long MemoryUsageBytes
    {
        get { try { return _process is { HasExited: false } ? _process.WorkingSet64 : 0; } catch (Exception ex) when (ex is InvalidOperationException or Win32Exception) { return 0; } }
    }

    public LlamaService(AppStore store) => _store = store;

    internal LlamaService CreateWorker() => new(_store) { BinaryDirectory = BinaryDirectory, Devices = Devices };

    public async Task InitializeAsync()
    {
        BinaryDirectory = await NativeRuntime.PrepareAsync(_store);
        if (!File.Exists(Path.Combine(Environment.SystemDirectory, "vulkan-1.dll")))
        {
            return;
        }
        var start = CreateStartInfo("vulkan");
        start.ArgumentList.Add("--list-devices");
        using var process = new Process { StartInfo = start };
        try { process.Start(); }
        catch (Win32Exception) { return; }
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
            string text = await output;
            await error;
            if (process.ExitCode == 0)
            {
                Devices = Regex.Matches(text, @"(?m)^\s*(Vulkan\d+):\s*(.+)$")
                    .Select(m => new ComputeDevice(m.Groups[1].Value, m.Groups[2].Value.Trim())).DistinctBy(d => d.Id).ToList();
            }
        }
        catch (OperationCanceledException)
        {
            Devices = [];
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(true);
            }
        }
    }

    public async Task EnsureModelAsync(LocalModel model, AppSettings settings, Action<string> status, CancellationToken token)
    {
        if (_process is { HasExited: false } && _activeModel == model.Id &&
            _activeCompute == settings.Compute && _activeContext == settings.ContextSize && _supportsThinking == model.Profile?.SupportsThinking && _activeProjector == model.Projector?.FilePath)
        {
            ModelPreferences.Validate(model.Options); _preferences = model.Options;
            return;
        }
        Stop(); _activeProjector = model.Projector?.FilePath;
        ModelPreferences.Validate(model.Options); _preferences = model.Options; _supportsThinking = model.Profile?.SupportsThinking;
        string compute = settings.Compute == "auto" ? Devices.FirstOrDefault()?.Id ?? "cpu" : settings.Compute;
        if (compute != "cpu" && !Devices.Any(d => d.Id == compute))
        {
            throw new InvalidOperationException("選択したGPUが見つかりません。設定から「自動」かCPUを選んでください。");
        }
        try
        {
            await StartModelAsync(model, settings.ContextSize, compute, status, token);
        }
        catch (OperationCanceledException)
        {
            Stop();
            throw;
        }
        catch (Exception ex) when (settings.Compute == "auto" && compute != "cpu" && ex is not OperationCanceledException)
        {
            Stop();
            status("GPUで読み込めなかったため、CPUで再試行しています…");
            await StartModelAsync(model, settings.ContextSize, "cpu", status, token);
        }
        _activeModel = model.Id;
        _activeCompute = settings.Compute;
        _activeContext = settings.ContextSize;
    }

    private async Task StartModelAsync(LocalModel model, int context, string compute, Action<string> status, CancellationToken token)
    {
        _diagnostics.Clear();
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        string key = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        string session = Path.Combine(_store.Root, "Sessions");
        Directory.CreateDirectory(session);
        _keyPath = Path.Combine(session, $"{Guid.NewGuid():N}.key");
        await File.WriteAllTextAsync(_keyPath, key, token);
        var start = CreateStartInfo(compute == "cpu" ? "cpu" : "vulkan");
        string[] args = ["--model", model.FilePath, "--host", "127.0.0.1", "--port", port.ToString(),
            "--ctx-size", context.ToString(), "--parallel", "1", "--api-key-file", _keyPath, "--no-webui",
            "--log-verbosity", "0", "--device", compute == "cpu" ? "none" : compute,
            "--gpu-layers", compute == "cpu" ? "0" : "auto"];
        foreach (string argument in args)
        {
            start.ArgumentList.Add(argument);
        }
        if (model.Projector is not null)
        {
            if (!File.Exists(model.Projector.FilePath)) { throw new FileNotFoundException("画像認識用のprojectorがありません。画像認識設定から取得してください。"); }
            start.ArgumentList.Add("--mmproj"); start.ArgumentList.Add(model.Projector.FilePath);
            if (compute == "cpu") { start.ArgumentList.Add("--no-mmproj-offload"); }
        }
        if (compute == "cpu")
        {
            start.ArgumentList.Add("--no-kv-offload");
        }
        _process = new Process { StartInfo = start };
        _process.OutputDataReceived += CaptureDiagnostic;
        _process.ErrorDataReceived += CaptureDiagnostic;
        try
        {
            status($"モデルを{(compute == "cpu" ? "CPU" : "GPU")}に読み込んでいます…");
            _process.Start();
            _job = new WindowsJob(_process);
            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();
            _http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}/"), Timeout = Timeout.InfiniteTimeSpan };
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key);
            var elapsed = Stopwatch.StartNew();
            while (elapsed.Elapsed < TimeSpan.FromMinutes(3))
            {
                token.ThrowIfCancellationRequested();
                if (_process.HasExited)
                {
                    _process.WaitForExit();
                    throw new InvalidOperationException($"モデルを読み込めませんでした (終了コード {_process.ExitCode})。別のモデルかCPUを試してください。\n" + string.Join('\n', _diagnostics.TakeLast(4)));
                }
                try
                {
                    using HttpResponseMessage health = await _http.GetAsync("health", token);
                    if (health.IsSuccessStatusCode)
                    {
                        using HttpResponseMessage authenticated = await _http.GetAsync("v1/models", token);
                        if (authenticated.IsSuccessStatusCode && !_process.HasExited)
                        {
                            ComputeLabel = compute == "cpu" ? "CPU" : $"GPU · {Devices.First(d => d.Id == compute).Name.Split('(')[0].Trim()}";
                            return;
                        }
                    }
                }
                catch (HttpRequestException)
                {
                    // The server begins listening after loading the model.
                }
                await Task.Delay(300, token);
            }
            throw new TimeoutException("モデルの読み込みに時間がかかっています。小さいモデルかCPUを試してください。");
        }
        catch
        {
            Stop();
            throw;
        }
    }

    public async IAsyncEnumerable<GenerationUpdate> GenerateAsync(IEnumerable<SavedMessage> messages, InferenceMode mode,
        [EnumeratorCancellation] CancellationToken token, string? instruction = null, int? outputLimit = null, object? jsonSchema = null, bool preserveSampling = false)
    {
        if (_http is null || _process is null || _process.HasExited)
        {
            throw new InvalidOperationException("モデルが準備できていません。もう一度送信してください。");
        }
        SavedMessage[] history = messages.ToArray();
        if (_supportsThinking == false) { mode = InferenceModes.Find("fast"); }
        object payload = CreatePayload(history, mode, new(mode.ThinkingTokens, _preferences.MaxOutputTokens + mode.ThinkingTokens), instruction, _preferences);
        using HttpResponseMessage counted = await _http.PostAsJsonAsync("v1/chat/completions/input_tokens", payload, token);
        await EnsureResponseAsync(counted, token);
        using JsonDocument input = JsonDocument.Parse(await counted.Content.ReadAsStringAsync(token));
        int inputTokens = input.RootElement.GetProperty("input_tokens").GetInt32();
        LastInputTokens = inputTokens;
        LastOutputTokens = 0;
        LastTokensPerSecond = 0;
        GenerationLimits limits = InferenceModes.GetLimits(mode, _activeContext, inputTokens, _preferences.MaxOutputTokens);
        if (outputLimit is > 0) { limits = InferenceModes.LimitOutput(limits, outputLimit.Value); }
        GenerationAllowance? allowance = _allowance;
        if (allowance is not null) { limits = InferenceModes.LimitOutput(limits, allowance.Limit(limits.MaxTokens)); }
        payload = CreatePayload(history, mode, limits, instruction, _preferences);
        if (jsonSchema is not null)
        {
            System.Text.Json.Nodes.JsonObject structured = JsonSerializer.SerializeToNode(payload)!.AsObject();
            structured["response_format"] = JsonSerializer.SerializeToNode(new { type = "json_object", schema = jsonSchema });
            if (!preserveSampling) { structured["temperature"] = 0.2; }
            payload = structured;
        }
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "v1/chat/completions")
            {
                Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
            };
            using HttpResponseMessage response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
            await EnsureResponseAsync(response, token);
            await using Stream stream = await response.Content.ReadAsStreamAsync(token);
            await foreach (GenerationUpdate piece in ReadEventsAsync(stream, token))
            {
                if (piece.OutputTokens is > 0) { LastOutputTokens = piece.OutputTokens.Value; }
                if (piece.OutputMilliseconds is > 0) { LastTokensPerSecond = LastOutputTokens * 1000.0 / piece.OutputMilliseconds.Value; }
                yield return piece;
            }
        }
        finally { allowance?.Spend(LastOutputTokens, limits.MaxTokens); }
    }

    public async Task<int> CountInputTokensAsync(IEnumerable<SavedMessage> messages, string instruction, CancellationToken token)
    {
        if (_http is null) { throw new InvalidOperationException("モデルが準備できていません。"); }
        using HttpResponseMessage response = await _http.PostAsJsonAsync("v1/chat/completions/input_tokens",
            CreatePayload(messages, InferenceModes.Find("fast"), new(0, 512), instruction), token);
        await EnsureResponseAsync(response, token);
        using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
        return document.RootElement.GetProperty("input_tokens").GetInt32();
    }

    internal static object CreatePayload(IEnumerable<SavedMessage> messages, InferenceMode mode, GenerationLimits limits, string? instruction = null, ModelPreferences? preferences = null) => new
    {
        messages = new[] { new SavedMessage("system", instruction ?? new AppSettings().SystemInstruction) }
            .Concat(messages).Select(m => new { role = m.Role, content = MessageContent(m) }),
        stream = true,
        max_tokens = limits.MaxTokens,
        temperature = (preferences ?? new()).Temperature,
        top_p = (preferences ?? new()).TopP,
        top_k = (preferences ?? new()).TopK,
        repeat_penalty = (preferences ?? new()).RepeatPenalty,
        reasoning_format = "deepseek",
        reasoning_effort = limits.ThinkingTokens > 0 ? mode.Effort : "none",
        reasoning_budget_tokens = limits.ThinkingTokens,
        chat_template_kwargs = new { enable_thinking = limits.ThinkingTokens > 0 }
    };

    private static object MessageContent(SavedMessage message)
    {
        ChatAttachment[] images = message.Attachments?.Where(a => a.ImageBase64 is not null).ToArray() ?? [];
        if (images.Length == 0) { return message.PromptText; }
        return new object[] { new { type = "text", text = message.PromptText } }.Concat(images.Select(a => (object)new { type = "image_url", image_url = new { url = "data:image/jpeg;base64," + a.ImageBase64 } })).ToArray();
    }

    private static async Task EnsureResponseAsync(HttpResponseMessage response, CancellationToken token)
    {
        if (!response.IsSuccessStatusCode)
        {
            string error = await response.Content.ReadAsStringAsync(token);
            throw new HttpRequestException($"応答を生成できませんでした ({(int)response.StatusCode})。新しい会話か、短い入力を試してください。\n{error[..Math.Min(error.Length, 500)]}");
        }
    }

    internal static async IAsyncEnumerable<GenerationUpdate> ReadEventsAsync(Stream stream, [EnumeratorCancellation] CancellationToken token)
    {
        using var reader = new StreamReader(stream);
        var data = new StringBuilder();
        while (await reader.ReadLineAsync(token) is string line)
        {
            if (line.Length == 0)
            {
                if (data.Length == 0)
                {
                    continue;
                }
                string json = data.ToString();
                data.Clear();
                if (json == "[DONE]")
                {
                    yield break;
                }
                using JsonDocument document = JsonDocument.Parse(json);
                if (document.RootElement.TryGetProperty("error", out _))
                {
                    throw new InvalidOperationException("生成中にエラーが発生しました。もう一度送信してください。");
                }
                if (document.RootElement.TryGetProperty("choices", out JsonElement choices) && choices.GetArrayLength() > 0)
                {
                    JsonElement choice = choices[0];
                    string content = "", reasoning = "";
                    if (choice.TryGetProperty("delta", out JsonElement delta))
                    {
                        if (delta.TryGetProperty("content", out JsonElement text) && text.ValueKind == JsonValueKind.String)
                        {
                            content = text.GetString() ?? "";
                        }
                        if (delta.TryGetProperty("reasoning_content", out JsonElement thought) && thought.ValueKind == JsonValueKind.String)
                        {
                            reasoning = thought.GetString() ?? "";
                        }
                    }
                    string? finish = choice.TryGetProperty("finish_reason", out JsonElement reason) && reason.ValueKind == JsonValueKind.String
                        ? reason.GetString() : null;
                    if (content.Length > 0 || reasoning.Length > 0 || finish is not null)
                    {
                        int? outputTokens = null;
                        double? outputMilliseconds = null;
                        if (document.RootElement.TryGetProperty("timings", out JsonElement timings))
                        {
                            if (timings.TryGetProperty("predicted_n", out JsonElement count) && count.TryGetInt32(out int n)) { outputTokens = n; }
                            if (timings.TryGetProperty("predicted_ms", out JsonElement milliseconds) && milliseconds.TryGetDouble(out double ms)) { outputMilliseconds = ms; }
                        }
                        yield return new(content, reasoning, finish, outputTokens, outputMilliseconds);
                    }
                }
            }
            else if (line.StartsWith("data:", StringComparison.Ordinal))
            {
                if (data.Length > 0)
                {
                    data.Append('\n');
                }
                data.Append(line.AsSpan(5).TrimStart());
            }
        }
        token.ThrowIfCancellationRequested();
        throw new IOException("推論エンジンとの接続が途切れました。もう一度送信してください。");
    }

    private ProcessStartInfo CreateStartInfo(string backend)
    {
        var start = new ProcessStartInfo
        {
            FileName = Path.Combine(FindBackendDirectory(backend), "llama-server.exe"),
            WorkingDirectory = FindBackendDirectory(backend),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (string key in start.Environment.Keys.Where(k => k.StartsWith("LLAMA_", StringComparison.OrdinalIgnoreCase)).ToList())
        {
            start.Environment.Remove(key);
        }
        return start;
    }

    private string FindBackendDirectory(string backend)
    {
        string root = BinaryDirectory ?? throw new InvalidOperationException("推論エンジンを準備中です。");
        foreach (string folder in new[] { backend, $"llama-{backend}-static/bin/Release", $"llama-{backend}/bin/Release" })
        {
            string directory = Path.Combine(root, folder);
            if (File.Exists(Path.Combine(directory, "llama-server.exe")))
            {
                return directory;
            }
        }
        throw new FileNotFoundException("同梱の推論エンジンが見つかりません。Eluna.exeをもう一度配置してください。");
    }

    private void CaptureDiagnostic(object sender, DataReceivedEventArgs args)
    {
        if (args.Data is not null)
        {
            _diagnostics.Enqueue(args.Data);
            while (_diagnostics.Count > 30)
            {
                _diagnostics.TryDequeue(out _);
            }
        }
    }

    public void Stop()
    {
        _http?.Dispose();
        _http = null;
        _job?.Dispose();
        _job = null;
        if (_process is not null)
        {
            try
            {
                if (!_process.HasExited)
                {
                    _process.Kill(true);
                }
            }
            catch (InvalidOperationException)
            {
            }
            _process.Dispose();
            _process = null;
        }
        if (_keyPath is not null)
        {
            try { File.Delete(_keyPath); } catch (IOException) { }
            _keyPath = null;
        }
        _activeModel = null;
    }

    public void Dispose() => Stop();
}
