using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Eluna.App.Models;

namespace Eluna.App.Services;

internal interface IComputerDesktop
{
    Task<ComputerObservation> ObserveAsync(ComputerTarget target, CancellationToken token);
    Task ExecuteAsync(ComputerObservation observation, ComputerAction action, CancellationToken token);
}

internal sealed class ComputerDesktop : IComputerDesktop
{
    internal async Task<List<ComputerTarget>> WindowsAsync(CancellationToken token) =>
        (await CallAsync(new("windows", Environment.ProcessId), token)).Targets ?? [];
    public async Task<ComputerObservation> ObserveAsync(ComputerTarget target, CancellationToken token) =>
        (await CallAsync(new("observe", Environment.ProcessId, target), token)).Observation ?? throw new IOException("画面を取得できませんでした。");
    internal async Task<ComputerObservation> ObserveScreenAsync(ComputerTarget target, CancellationToken token) =>
        (await CallAsync(new("observe-screen", Environment.ProcessId, target), token)).Observation ?? throw new IOException("画面を取得できませんでした。");
    public async Task ExecuteAsync(ComputerObservation observation, ComputerAction action, CancellationToken token)
    {
        try { await CallAsync(new("execute", Environment.ProcessId, Observation: observation, Action: action), token); }
        finally { if (action.Type == "drag") { ComputerDesktopNative.ReleaseDrag(); } }
    }

    private static async Task<ComputerWorkerResponse> CallAsync(ComputerWorkerRequest request, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromSeconds(12));
        using var process = new Process { StartInfo = StartInfo("--computer-worker") };
        WindowsJob? job = null;
        try
        {
            process.Start(); job = new WindowsJob(process);
            if (request.Operation == "execute") { _ = AllowSetForegroundWindow((uint)process.Id); }
            Task<string> response = process.StandardOutput.ReadToEndAsync(deadline.Token);
            Task<string> errors = process.StandardError.ReadToEndAsync(deadline.Token);
            await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(request, ComputerJson.Options).AsMemory(), deadline.Token);
            process.StandardInput.Close();
            await process.WaitForExitAsync(deadline.Token); string json = await response; await errors;
            if (json.Length > 8000000 || process.ExitCode != 0) { throw new IOException("画面操作の補助プロセスを完了できませんでした。"); }
            ComputerWorkerResponse result = JsonSerializer.Deserialize<ComputerWorkerResponse>(json, ComputerJson.Options) ?? throw new IOException("画面操作の応答がありません。");
            if (result.Changed) { throw new ComputerScreenChangedException(result.Error ?? "画面が変化"); }
            if (result.Error is not null) { throw new InvalidOperationException(result.Error); }
            return result;
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        { throw new TimeoutException("対象アプリから応答がありません。操作を停止しました。アプリの状態を確認して再実行してください。"); }
        finally
        {
            job?.Dispose();
            try { if (!process.HasExited) { process.Kill(true); await process.WaitForExitAsync(); } } catch (InvalidOperationException) { }
        }
    }

    internal static ProcessStartInfo StartInfo(string argument)
    {
        string app = Path.Combine(AppContext.BaseDirectory, "Eluna.dll");
        string appHost = Path.Combine(AppContext.BaseDirectory, "Eluna.exe");
        string executable = File.Exists(appHost) ? appHost : Environment.ProcessPath!;
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = Encoding.UTF8
        };
        if (Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase)) { start.ArgumentList.Add(app); }
        start.ArgumentList.Add(argument); return start;
    }

    internal static async Task<int> RunWorkerAsync()
    {
        ComputerWorkerResponse result;
        try
        {
            string? line = await Console.In.ReadLineAsync();
            if (line is null || line.Length > 8000000) { throw new InvalidDataException("操作要求がありません。"); }
            ComputerWorkerRequest request = JsonSerializer.Deserialize<ComputerWorkerRequest>(line, ComputerJson.Options) ?? throw new InvalidDataException("操作要求が空です。");
            result = request.Operation switch
            {
                "windows" => new(Targets: ComputerDesktopNative.Windows(request.Parent)),
                "observe" when request.Target is not null => new(Observation: ComputerDesktopNative.Observe(request.Target, request.Parent)),
                "observe-screen" when request.Target is not null => new(Observation: ComputerDesktopNative.Observe(request.Target, request.Parent, true)),
                "execute" => Execute(request),
                _ => throw new InvalidDataException("対応していない操作です。")
            };
        }
        catch (ComputerScreenChangedException ex) { result = new(Error: ex.Category, Changed: true); }
        catch (Exception ex) { result = new(Error: ex.Message); }
        await Console.Out.WriteLineAsync(JsonSerializer.Serialize(result, ComputerJson.Options)); return 0;
    }
    private static ComputerWorkerResponse Execute(ComputerWorkerRequest request) { ComputerDesktopNative.Execute(request); return new(); }
    [DllImport("user32.dll")] private static extern bool AllowSetForegroundWindow(uint processId);
}
