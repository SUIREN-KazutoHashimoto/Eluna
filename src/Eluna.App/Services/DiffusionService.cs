using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace Eluna.App.Services;

public sealed record ImageGenerationOptions(string ModelPath, string Prompt, string Negative = "", int Width = 512, int Height = 512, int Steps = 20, long Seed = -1, double Guidance = 7, string Compute = "auto");

public static class DiffusionService
{
    public static async Task GenerateAsync(AppStore store, ImageGenerationOptions options, string output, Action<string> progress, CancellationToken token)
    {
        if (!File.Exists(options.ModelPath) || options.Prompt.Length is 0 or > 4000 || options.Negative.Length > 2000 || options.Width is < 256 or > 1024 || options.Height is < 256 or > 1024 || options.Width % 64 != 0 || options.Height % 64 != 0 || options.Steps is < 1 or > 50 || !double.IsFinite(options.Guidance) || options.Guidance is < 0 or > 20)
        { throw new InvalidDataException("モデル、プロンプト、解像度（256〜1024・64単位）、ステップ（1〜50）を確認してください。"); }
        string root = await NativeRuntime.PrepareAsync(store);
        bool gpu = options.Compute != "cpu" && File.Exists(Path.Combine(Environment.SystemDirectory, "vulkan-1.dll"));
        string backend = gpu ? "vulkan" : "cpu";
        string binary = Path.Combine(root, backend, "sd-cli.exe");
        if (!File.Exists(binary)) { binary = Path.Combine(root, "diffusion-" + backend + "-static", "bin", "Release", "sd-cli.exe"); }
        if (!File.Exists(binary)) { throw new FileNotFoundException("画像生成エンジンがありません。画像機能対応の配布版を使用してください。"); }
        var start = new ProcessStartInfo(binary) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
        string[] arguments = ["--model", options.ModelPath, "--prompt", options.Prompt, "--negative-prompt", options.Negative,
            "--width", options.Width.ToString(CultureInfo.InvariantCulture), "--height", options.Height.ToString(CultureInfo.InvariantCulture),
            "--steps", options.Steps.ToString(CultureInfo.InvariantCulture), "--seed", options.Seed.ToString(CultureInfo.InvariantCulture),
            "--cfg-scale", options.Guidance.ToString(CultureInfo.InvariantCulture), "--sampling-method", "euler_a", "--output", output];
        foreach (string argument in arguments) { start.ArgumentList.Add(argument); }
        if (options.Compute.StartsWith("Vulkan", StringComparison.Ordinal)) { start.Environment["GGML_VK_VISIBLE_DEVICES"] = options.Compute[6..]; }
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        using var process = Process.Start(start) ?? throw new IOException("画像生成エンジンを起動できませんでした。");
        using var job = new WindowsJob(process);
        var tail = new System.Collections.Concurrent.ConcurrentQueue<string>();
        async Task Read(StreamReader reader)
        {
            while (await reader.ReadLineAsync(token) is string line)
            {
                tail.Enqueue(line); while (tail.Count > 8) { tail.TryDequeue(out _); }
                Match step = Regex.Match(line, @"(\d+)\s*/\s*(\d+)");
                string message = step.Success ? "画像を生成しています… " + step.Value : "画像モデルの読み込み・生成を実行しています…";
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() => progress(message));
            }
        }
        Task logs = Task.WhenAll(Read(process.StandardOutput), Read(process.StandardError));
        try { await process.WaitForExitAsync(token); await logs; }
        catch { if (!process.HasExited) { process.Kill(true); } try { await logs; } catch (OperationCanceledException) { } if (File.Exists(output)) { File.Delete(output); } throw; }
        if (process.ExitCode != 0 || !File.Exists(output)) { throw new InvalidOperationException("画像生成に失敗しました。モデルの対応形式・メモリを確認し、CPUも試してください。\n" + string.Join('\n', tail.TakeLast(3))); }
        progress("画像を生成しました");
    }
}
