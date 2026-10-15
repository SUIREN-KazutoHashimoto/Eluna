using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using Eluna.App.Services;

namespace Eluna.App;

public partial class App : Application
{
    private Mutex? _instance;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            int waitIndex = Array.IndexOf(e.Args, "--wait-for-exit");
            if (waitIndex >= 0 && waitIndex + 1 < e.Args.Length && int.TryParse(e.Args[waitIndex + 1], out int previousId))
            { try { using var previous = System.Diagnostics.Process.GetProcessById(previousId); if (!previous.WaitForExit(60000)) { Shutdown(1); return; } } catch (ArgumentException) { } }
            string? root = null;
            int captureIndex = Array.IndexOf(e.Args, "--capture-dir");
            string? captureDirectory = captureIndex >= 0 && captureIndex + 1 < e.Args.Length ? Path.GetFullPath(e.Args[captureIndex + 1]) : null;
            int index = Array.IndexOf(e.Args, "--data-dir");
            if (index >= 0 && index + 1 < e.Args.Length)
            {
                root = Path.GetFullPath(e.Args[index + 1]);
            }
            if (captureDirectory is not null)
            {
                UnsavedChanges.Review = _ => false;
                root = Path.Combine(captureDirectory, "preview-data");
            }
            var store = new AppStore(root);
            int restoreCycle = Array.IndexOf(e.Args, "--verify-restore-cycle"), restoredCycle = Array.IndexOf(e.Args, "--verify-restored");
            if ((restoreCycle >= 0 && restoreCycle + 1 < e.Args.Length) || (restoredCycle >= 0 && restoredCycle + 1 < e.Args.Length))
            {
                if (root is null) { throw new InvalidOperationException("復元サイクル検証には --data-dir で専用の保存先を指定してください。"); }
                ShutdownMode = ShutdownMode.OnExplicitShutdown;
                _ = RestoreCycleVerification.RunAsync(store, e.Args[(restoreCycle >= 0 ? restoreCycle : restoredCycle) + 1], restoreCycle >= 0); return;
            }
            int backupIndex = Array.IndexOf(e.Args, "--verify-backup");
            if (backupIndex >= 0 && backupIndex + 1 < e.Args.Length)
            {
                if (root is null) { throw new InvalidOperationException("バックアップ検証には --data-dir で専用の保存先を指定してください。"); }
                int realIndex = Array.IndexOf(e.Args, "--model-meta"); string? model = realIndex >= 0 && realIndex + 1 < e.Args.Length ? e.Args[realIndex + 1] : null;
                int stressIndex = Array.IndexOf(e.Args, "--stress-seconds"); int seconds = stressIndex >= 0 && stressIndex + 1 < e.Args.Length && int.TryParse(e.Args[stressIndex + 1], out int duration) ? Math.Clamp(duration, 0, 1800) : 0;
                ShutdownMode = ShutdownMode.OnExplicitShutdown; _ = BackupVerification.RunAsync(store, e.Args[backupIndex + 1], model, seconds); return;
            }
            int placementIndex = Array.IndexOf(e.Args, "--verify-window-placement");
            if (placementIndex >= 0 && placementIndex + 1 < e.Args.Length)
            {
                if (root is null) { throw new InvalidOperationException("ウィンドウ復元の検証には --data-dir で専用の保存先を指定してください。"); }
                ShutdownMode = ShutdownMode.OnExplicitShutdown; _ = WindowPlacementVerification.RunAsync(store, e.Args[placementIndex + 1]); return;
            }
            int skillsIndex = Array.IndexOf(e.Args, "--verify-skills");
            if (skillsIndex >= 0 && skillsIndex + 1 < e.Args.Length)
            {
                if (root is null) { throw new InvalidOperationException("スキル検証には --data-dir で専用の保存先を指定してください。"); }
                ShutdownMode = ShutdownMode.OnExplicitShutdown; _ = SkillsVerification.RunAsync(store, e.Args[skillsIndex + 1]); return;
            }
            int concurrentIndex = Array.IndexOf(e.Args, "--verify-concurrent");
            if (concurrentIndex >= 0 && concurrentIndex + 1 < e.Args.Length)
            {
                if (root is null) { throw new InvalidOperationException("並行処理の動作検証には --data-dir で検証専用の保存先を指定してください。"); }
                ShutdownMode = ShutdownMode.OnExplicitShutdown; _ = ConcurrentVerification.RunAsync(store, e.Args[concurrentIndex + 1]); return;
            }
            int projectsIndex = Array.IndexOf(e.Args, "--verify-projects");
            if (projectsIndex >= 0 && projectsIndex + 2 < e.Args.Length)
            {
                if (root is null) { throw new InvalidOperationException("プロジェクトの動作検証には --data-dir で検証専用の保存先を指定してください。"); }
                ShutdownMode = ShutdownMode.OnExplicitShutdown; _ = ProjectsVerification.RunAsync(store, e.Args[projectsIndex + 1], e.Args[projectsIndex + 2]); return;
            }
            int mediaIndex = Array.IndexOf(e.Args, "--verify-media");
            if (mediaIndex >= 0 && mediaIndex + 3 < e.Args.Length)
            { ShutdownMode = ShutdownMode.OnExplicitShutdown; _ = MediaVerification.RunAsync(store, e.Args[mediaIndex + 1], e.Args[mediaIndex + 2], e.Args[mediaIndex + 3]); return; }
            if (e.Args.Contains("--verify-recovery"))
            {
                ShutdownMode = ShutdownMode.OnExplicitShutdown; _ = PrototypeVerification.CheckRecoveryAsync(store); return;
            }
            int verifyIndex = Array.IndexOf(e.Args, "--verify-model");
            if (verifyIndex >= 0 && verifyIndex + 1 < e.Args.Length)
            {
                ShutdownMode = ShutdownMode.OnExplicitShutdown;
                int debateIndex = Array.IndexOf(e.Args, "--verify-debate-model");
                string? debateMetadata = debateIndex >= 0 && debateIndex + 1 < e.Args.Length ? e.Args[debateIndex + 1] : null;
                _ = PrototypeVerification.RunAsync(store, e.Args[verifyIndex + 1], Path.Combine(store.Root, "verification.json"), e.Args.Contains("--verify-workflows"), e.Args.Contains("--verify-workflows-only"), e.Args.Contains("--verify-model-management"), debateMetadata, e.Args.Contains("--verify-interruption"), e.Args.Contains("--verify-search-routing"));
                return;
            }
            string id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(store.Root.ToUpperInvariant())))[..20];
            _instance = new Mutex(true, "Local\\Eluna-" + id, out bool created);
            if (!created)
            {
                MessageBox.Show("Elunaは既に起動しています。", "Eluna");
                Shutdown();
                return;
            }
            var window = new MainWindow(store);
            MainWindow = window;
            if (captureDirectory is not null)
            {
                window.WindowStartupLocation = WindowStartupLocation.Manual;
                window.Left = -20000;
                window.Top = -20000;
            }
            window.Show();
            if (captureDirectory is not null)
            {
                int remaining = Array.IndexOf(e.Args, "--verify-remaining");
                _ = remaining >= 0 && remaining + 2 < e.Args.Length ? RemainingFeaturesVerification.RunAsync(window, store, captureDirectory, e.Args[remaining + 1], e.Args[remaining + 2]) : e.Args.Contains("--verify-ui-ux") ? UiUxPolishVerification.RunAsync(window, store, captureDirectory) : e.Args.Contains("--audit-ui") ? UiUxAudit.RunAsync(window, store, captureDirectory) : PrototypePreview.CaptureAsync(window, store, captureDirectory);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show("Elunaを起動できませんでした。\n" + ex.Message, "Eluna", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _instance?.Dispose();
        base.OnExit(e);
    }

    internal static void Restart(string root, string? verification = null)
    {
        var start = new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
        if (string.Equals(Path.GetFileNameWithoutExtension(Environment.ProcessPath), "dotnet", StringComparison.OrdinalIgnoreCase)) { start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, typeof(App).Assembly.GetName().Name + ".dll")); }
        start.ArgumentList.Add("--data-dir"); start.ArgumentList.Add(root); start.ArgumentList.Add("--wait-for-exit"); start.ArgumentList.Add(Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (verification is not null) { start.ArgumentList.Add("--verify-restored"); start.ArgumentList.Add(verification); }
        System.Diagnostics.Process.Start(start)?.Dispose();
    }
}
