using System.Diagnostics;
using System.IO;
using System.Text;
using Eluna.App.Models;

namespace Eluna.App.Services;

internal sealed class SkillScriptRunner(AppStore store, SkillStore skills)
{
    internal async Task<SkillExecution> RunAsync(SkillRegistration skill, string path, string input,
        Func<SkillExecutionRequest, CancellationToken, Task<bool>>? review, bool forceReview, CancellationToken token)
    {
        path = SkillStore.SafePath(path);
        if (!path.StartsWith("scripts/", StringComparison.OrdinalIgnoreCase) || !path.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase))
        { return new(path, input, "非対応", "実行できるコードはscripts内のPowerShell .ps1です。Python・Bash・外部パッケージは実行しません。", []); }
        if (skill.ExecutionPolicy == "never") { return new(path, input, "拒否", "このスキルのコード実行は許可されていません。", []); }
        byte[] code = await skills.ReadFileAsync(skill, path, token);
        string script = SkillFormat.Utf8.GetString(code);
        string work = Path.Combine(store.Root, "Generated", "Skills", Guid.NewGuid().ToString("N"));
        string output = Path.Combine(work, "output");
        var request = new SkillExecutionRequest(skill.Name, path, script, input, output);
        if ((skill.ExecutionPolicy != "trusted" || forceReview) && (review is null || !await review(request, token)))
        { return new(path, input, "拒否", "利用者が実行を承認していません。計算やファイル作成を実行したと説明しないでください。", []); }
        token.ThrowIfCancellationRequested();
        await Task.Run(async () =>
        {
            SkillManifest manifest = await skills.ManifestAsync(skill, token);
            foreach (SkillFile file in manifest.Files)
            {
                string destination = Path.Combine(work, "skill", file.Path); Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                byte[] bytes = await skills.ReadFileAsync(skill, file.Path, token);
                if (Path.GetExtension(destination).ToLowerInvariant() is ".ps1" or ".psm1" or ".psd1")
                { await File.WriteAllTextAsync(destination, SkillFormat.Utf8.GetString(bytes).TrimStart('\uFEFF'), new UTF8Encoding(true), token); }
                else { await File.WriteAllBytesAsync(destination, bytes, token); }
            }
        }, token);
        Directory.CreateDirectory(output);
        string inputPath = Path.Combine(work, "input.json"), wrapper = Path.Combine(work, "run.ps1");
        await File.WriteAllTextAsync(inputPath, input, new UTF8Encoding(true), token);
        await File.WriteAllTextAsync(wrapper, "param([string]$ScriptPath,[string]$InputJson,[string]$OutputDirectory)\n" +
            "$ErrorActionPreference='Stop'\n[Console]::OutputEncoding=[System.Text.UTF8Encoding]::new($false)\n" +
            "try { & $ScriptPath -InputJson $InputJson -OutputDirectory $OutputDirectory; if (-not $?) { exit 1 } } catch { [Console]::Error.WriteLine($_.Exception.Message); exit 1 }\n", new UTF8Encoding(true), token);
        var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe"))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = Path.Combine(work, "skill")
        };
        start.Environment.Clear();
        foreach (string key in new[] { "SystemRoot", "WINDIR", "TEMP", "TMP", "USERPROFILE", "APPDATA", "LOCALAPPDATA" })
        { if (Environment.GetEnvironmentVariable(key) is string value) { start.Environment[key] = value; } }
        start.Environment["PATH"] = Environment.SystemDirectory;
        foreach (string argument in new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", wrapper,
            "-ScriptPath", Path.Combine(work, "skill", path), "-InputJson", inputPath, "-OutputDirectory", output }) { start.ArgumentList.Add(argument); }
        using var process = new Process { StartInfo = start };
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(60));
        string status;
        try
        {
            process.Start();
            using var job = new WindowsJob(process);
            Task<string> stdout = ReadBoundedAsync(process.StandardOutput, timeout.Token), stderr = ReadBoundedAsync(process.StandardError, timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            string result = await stdout, error = await stderr;
            status = process.ExitCode == 0 ? "成功" : "失敗";
            if (error.Length > 0) { result += "\n標準エラー:\n" + error; }
            var artifacts = new List<string>(); long total = 0; int visited = 0;
            var directories = new Stack<string>(); directories.Push(output);
            while (directories.TryPop(out string? folder))
            {
                SkillStore.RejectLinks(folder, output);
                foreach (string entry in Directory.EnumerateFileSystemEntries(folder))
                {
                    token.ThrowIfCancellationRequested(); SkillStore.RejectLinks(entry, output);
                    if (++visited > 256) { throw new InvalidDataException("スキルの出力ファイル数が上限を超えています。"); }
                    if (Directory.Exists(entry)) { directories.Push(entry); }
                    else
                    {
                        long size = new FileInfo(entry).Length; total += size;
                        if (size > 16 * 1024 * 1024 || total > SkillStore.MaxBytes || artifacts.Count >= 20) { throw new InvalidDataException("スキルの出力は20ファイル・合計32 MB・各16 MBまでです。"); }
                        artifacts.Add(entry);
                    }
                }
            }
            return new(path, input, status, result, artifacts);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        { return new(path, input, "時間切れ", "スクリプトは60秒の上限で停止しました。完了したと説明しないでください。", []); }
        finally
        {
            try { if (!process.HasExited) { process.Kill(true); await process.WaitForExitAsync(); } }
            catch (InvalidOperationException) { }
        }
    }
    private static async Task<string> ReadBoundedAsync(StreamReader reader, CancellationToken token)
    {
        var result = new StringBuilder(); char[] buffer = new char[4096]; int count; bool truncated = false;
        while ((count = await reader.ReadAsync(buffer.AsMemory(), token)) > 0)
        { int take = Math.Min(count, Math.Max(0, 16000 - result.Length)); result.Append(buffer, 0, take); truncated |= take < count; }
        if (truncated) { result.Append("\n［出力は先頭16,000文字まで］"); }
        return result.ToString();
    }
}
