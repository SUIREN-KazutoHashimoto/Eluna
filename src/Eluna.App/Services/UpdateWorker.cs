using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using Eluna.App.Models;

namespace Eluna.App.Services;

internal static class UpdateWorker
{
    internal static bool Under(string file, string directory) => Path.GetFullPath(file).StartsWith(Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    internal static async Task<string> PrepareAsync(AppStore store, StagedUpdate staged, bool background = false)
    {
        InstalledApp installed = InstallerService.Current ?? throw new InvalidOperationException("自動更新はインストール版で利用できます。");
        if (!Under(staged.File, Path.Combine(store.Root, "Updates")) || staged.DataSchema < 5) { throw new InvalidDataException("更新ファイルの保存先を確認できません。"); }
        await UpdateService.ValidateFileAsync(staged.File, staged.Sha256, staged.Size, CancellationToken.None);
        using Process current = Process.GetCurrentProcess();
        var plan = new UpdatePlan(Guid.NewGuid(), installed.Directory, installed.Id, store.Root, current.Id, current.StartTime.ToUniversalTime().Ticks, staged, background);
        string path = Path.Combine(store.Root, "Updates", "apply-" + plan.Token.ToString("N") + ".json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(plan, AppStore.JsonOptions)); return path;
    }
    internal static void Launch(string planPath)
    {
        string helper = Path.Combine(Path.GetDirectoryName(planPath)!, "Eluna-worker-" + Guid.NewGuid().ToString("N") + ".exe");
        File.Copy(InstallerService.Executable ?? throw new IOException("実行中のexeを確認できません。"), helper); InstallerService.Start(helper, "--apply-update", planPath).Dispose();
    }
    internal static async Task<int> RunAsync(string planPath)
    {
        UpdatePlan? plan = null;
        try
        {
            plan = JsonSerializer.Deserialize<UpdatePlan>(await File.ReadAllTextAsync(planPath), AppStore.JsonOptions) ?? throw new InvalidDataException("更新計画が空です。");
            if (!Under(planPath, Path.Combine(plan.DataRoot, "Updates")) || Path.GetFileName(planPath) != "apply-" + plan.Token.ToString("N") + ".json") { throw new InvalidDataException("更新計画の保存先を確認できません。"); }
            try
            {
                using var previous = Process.GetProcessById(plan.PreviousPid);
                if (previous.StartTime.ToUniversalTime().Ticks != plan.PreviousStart || !string.Equals(previous.MainModule?.FileName, Path.Combine(plan.Directory, "Eluna.exe"), StringComparison.OrdinalIgnoreCase)) { throw new IOException("終了待ちのElunaが変更されています。"); }
                using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(2)); await previous.WaitForExitAsync(deadline.Token);
            }
            catch (ArgumentException) { }
            await ApplyAsync(plan, async (target, health) =>
            {
                using Process next = InstallerService.Start(target, "--data-dir", plan.DataRoot, "--update-health", plan.Token.ToString("N"), plan.Background ? "--updated-background" : "--updated");
                await File.WriteAllTextAsync(Path.Combine(plan.DataRoot, "Updates", "child-" + plan.Token.ToString("N") + ".json"), JsonSerializer.Serialize(new { pid = next.Id, start = next.StartTime.ToUniversalTime() }, AppStore.JsonOptions));
                for (int i = 0; i < 1200; i++) { if (File.Exists(health)) { return true; } if (next.HasExited) { return false; } await Task.Delay(100); }
                if (!next.HasExited) { next.Kill(true); await next.WaitForExitAsync(); }
                return false;
            }, () => { });
            return 0;
        }
        catch (Exception e)
        {
            if (plan is not null)
            {
                try { await RecordFailureAsync(plan, e); } catch (Exception writeError) when (writeError is IOException or UnauthorizedAccessException) { }
                bool previousAlive;
                try { using Process previous = Process.GetProcessById(plan.PreviousPid); previousAlive = previous.StartTime.ToUniversalTime().Ticks == plan.PreviousStart && !previous.HasExited; } catch (ArgumentException) { previousAlive = false; }
                if (!previousAlive && !File.Exists(Path.Combine(plan.DataRoot, "Updates", "health-" + plan.Token.ToString("N"))) && InstallerService.Read(plan.Directory)?.Id == plan.InstallId && File.Exists(Path.Combine(plan.Directory, "Eluna.exe")))
                { try { InstallerService.Start(Path.Combine(plan.Directory, "Eluna.exe"), "--data-dir", plan.DataRoot, "--update-rolled-back", plan.Background ? "--updated-background" : "--updated").Dispose(); } catch (Exception restartError) when (restartError is IOException or System.ComponentModel.Win32Exception) { } }
            }
            return 1;
        }
    }
    internal static async Task ApplyAsync(UpdatePlan plan, Func<string, string, Task<bool>> launchAndCheck, Action restartPrevious)
    {
        InstalledApp installed = InstallerService.Read(plan.Directory) ?? throw new IOException("インストール記録を確認できません。");
        if (installed.Id != plan.InstallId || plan.Update.DataSchema < 5 || !Under(plan.Update.File, Path.Combine(plan.DataRoot, "Updates"))) { throw new InvalidDataException("更新計画とインストール記録が一致しません。"); }
        InstallerService.RejectLink(plan.Directory); InstallerService.RejectLink(Path.GetDirectoryName(plan.Update.File)!);
        await using var guard = new FileStream(Path.Combine(plan.DataRoot, "Updates", "update.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        await UpdateService.ValidateFileAsync(plan.Update.File, plan.Update.Sha256, plan.Update.Size, CancellationToken.None);
        string target = Path.Combine(plan.Directory, "Eluna.exe"), backup = Path.Combine(plan.Directory, "Eluna.previous.exe"), temporary = Path.Combine(plan.Directory, "Eluna.next.tmp");
        string health = Path.Combine(plan.DataRoot, "Updates", "health-" + plan.Token.ToString("N"));
        string snapshot = Path.Combine(plan.DataRoot, "Updates", "data-before-" + plan.Token.ToString("N")); Directory.CreateDirectory(snapshot);
        string[] dataFiles = ["state.json", "state-knowledge.json", "state-skills.json", "state-workspace.json", "state-social.json", "media-jobs.json", "downloads-v2.json"];
        foreach (string name in dataFiles) { string file = Path.Combine(plan.DataRoot, name); if (File.Exists(file)) { File.Copy(file, Path.Combine(snapshot, name), false); } }
        bool replaced = false, healthy = false;
        try
        {
            File.Copy(plan.Update.File, temporary, true); await UpdateService.ValidateFileAsync(temporary, plan.Update.Sha256, plan.Update.Size, CancellationToken.None);
            if (File.Exists(health)) { File.Delete(health); }
            for (int attempt = 0; ; attempt++)
            {
                try { File.Replace(temporary, target, backup, true); replaced = true; break; }
                catch (IOException) when (attempt < 8) { await Task.Delay(250 * (attempt + 1)); }
            }
            if (!await launchAndCheck(target, health)) { throw new IOException("更新版の正常起動を確認できなかったため、前の版へ戻しました。"); }
            healthy = true; await InstallerService.WriteMarkerAsync(installed with { Version = plan.Update.Version });
            await File.WriteAllTextAsync(Path.Combine(plan.DataRoot, "Updates", "last-result.json"), JsonSerializer.Serialize(new { success = true, time = DateTimeOffset.UtcNow, version = plan.Update.Version, previous = installed.Version, snapshot }, AppStore.JsonOptions));
            string ready = Path.Combine(plan.DataRoot, "Updates", "ready.json"); if (File.Exists(ready)) { File.Delete(ready); }
        }
        catch (Exception failure)
        {
            if (replaced && !healthy)
            {
                File.Move(backup, target, true); await InstallerService.WriteMarkerAsync(installed);
                foreach (string name in dataFiles) { string saved = Path.Combine(snapshot, name); if (File.Exists(saved)) { File.Copy(saved, Path.Combine(plan.DataRoot, name), true); } }
                string readyPath = Path.Combine(plan.DataRoot, "Updates", "ready.json"); if (File.Exists(readyPath)) { File.Delete(readyPath); }
                try { await RecordFailureAsync(plan, failure); } catch (Exception writeError) when (writeError is IOException or UnauthorizedAccessException) { }
                restartPrevious();
            }
            throw;
        }
        finally { if (File.Exists(temporary)) { File.Delete(temporary); } }
    }
    private static Task RecordFailureAsync(UpdatePlan plan, Exception error) => File.WriteAllTextAsync(Path.Combine(plan.DataRoot, "Updates", "last-result.json"), JsonSerializer.Serialize(new { success = false, time = DateTimeOffset.UtcNow, version = plan.Update.Version, train = plan.Update.Train, error = AppTrace.Redact(error.ToString()) }, AppStore.JsonOptions));
    internal static async Task ConfirmHealthAsync(MainWindow main, string root, string token)
    {
        if (!Guid.TryParseExact(token, "N", out Guid id)) { return; }
        for (int i = 0; i < 1000; i++) { if (main.ViewModel.HasError) { return; } if (main.ViewModel.IsReady) { string directory = Path.Combine(root, "Updates"); Directory.CreateDirectory(directory); await File.WriteAllTextAsync(Path.Combine(directory, "health-" + id.ToString("N")), UpdateService.CurrentVersion); return; } await Task.Delay(100); }
    }
}
