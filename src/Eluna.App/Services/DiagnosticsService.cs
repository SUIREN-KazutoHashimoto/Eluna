using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using Eluna.App.Models;

namespace Eluna.App.Services;

public static class DiagnosticsService
{
    public static Task ExportAsync(string path, AppSettings settings, string compute, Exception? error = null) => File.WriteAllTextAsync(path,
        JsonSerializer.Serialize(new
        {
            version = typeof(DiagnosticsService).Assembly.GetName().Version?.ToString(),
            windows = RuntimeInformation.OSDescription,
            framework = RuntimeInformation.FrameworkDescription,
            architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            compute,
            contextSize = settings.ContextSize,
            inferenceMode = settings.InferenceMode,
            processMemoryBytes = System.Diagnostics.Process.GetCurrentProcess().WorkingSet64,
            errorType = error?.GetType().Name,
            errorCode = error?.HResult,
            createdUtc = DateTimeOffset.UtcNow
        }, AppStore.JsonOptions));

    public static string Explain(Exception error)
    {
        string text = error.Message;
        if (text.Contains("memory", StringComparison.OrdinalIgnoreCase) || text.Contains("メモリ", StringComparison.OrdinalIgnoreCase))
        {
            return "メモリが不足した可能性があります。小さいモデル、短い会話設定、CPUへの切り替えを試してください。\n" + text;
        }
        return error is System.Net.Http.HttpRequestException or IOException
            ? "推論エンジンへの接続または保存に失敗しました。再試行できます。\n" + text : text;
    }
}
