using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace Eluna.App.Services;

internal static class WindowPlacementVerification
{
    internal static async Task RunAsync(AppStore store, string output)
    {
        Directory.CreateDirectory(output);
        var results = new List<object>();
        try
        {
            IReadOnlyList<WindowDisplay> displays = WindowNative.Displays();
            Check(displays.Count > 0, "No display was enumerated.");
            foreach (WindowDisplay display in displays)
            {
                double scale = display.Dpi / 96;
                var saved = new WindowPlacement(display.WorkArea.Left + 40 * scale, display.WorkArea.Top + 40 * scale,
                    Math.Min(900 * scale, display.WorkArea.Width - 40 * scale), Math.Min(660 * scale, display.WorkArea.Height - 40 * scale),
                    false, 2, display.Id, display.Device, display.WorkArea, display.Dpi);
                for (int pass = 0; pass < 4; pass++)
                {
                    string root = Path.Combine(store.Root, display.Device.Replace('\\', '_').Replace('.', '_'), "pass-" + pass);
                    Directory.CreateDirectory(root);
                    await File.WriteAllTextAsync(Path.Combine(root, "window.json"), JsonSerializer.Serialize(saved));
                    Window window = CreateWindow(root);
                    try
                    {
                        window.Show(); await IdleAsync();
                        WindowPlacement actual = WindowNative.Capture(window) ?? throw new InvalidOperationException("Native placement was not captured.");
                        WindowRestorePlan planned = WindowPlacement.Plan(saved, displays, 820, 600)!;
                        Check(actual.MonitorDevice == display.Device && Close(actual.Bounds, planned.Bounds), "Native restore differs from plan: " + JsonSerializer.Serialize(new { actual, planned }));
                        Check(Close(WindowNative.ScreenRectangle(window)!, planned.Bounds), "Visible screen rectangle differs from the saved normal position.");
                        Check(Math.Abs(VisualTreeHelper.GetDpi(window).PixelsPerInchX - display.Dpi) <= 1, "WPF/native monitor DPI differ.");
                        if (pass == 0)
                        {
                            await PrototypePreview.RenderAsync(window, Path.Combine(output, "normal-" + results.Count + ".png"));
                            await WindowPlacement.SaveAsync(window, root);
                            WindowPlacement disk = JsonSerializer.Deserialize<WindowPlacement>(await File.ReadAllTextAsync(Path.Combine(root, "window.json")))!;
                            Check(disk.Version == 2 && Close(disk.Bounds, actual.Bounds), "Saved native geometry differs.");
                        }
                        if (pass is 1 or 3)
                        {
                            window.WindowState = WindowState.Maximized; await IdleAsync();
                            if (pass == 3) { window.WindowState = WindowState.Minimized; await IdleAsync(); }
                        }
                        else if (pass == 2) { window.WindowState = WindowState.Minimized; await IdleAsync(); }
                        await WindowPlacement.SaveAsync(window, root);
                        WindowPlacement persisted = JsonSerializer.Deserialize<WindowPlacement>(await File.ReadAllTextAsync(Path.Combine(root, "window.json")))!;
                        Check(persisted.Maximized == (pass is 1 or 3) && Close(persisted.Bounds, actual.Bounds), "Normal bounds/maximized state changed during minimize/maximize.");
                        saved = persisted;
                        results.Add(new { display.Device, display.Dpi, Pass = pass, Persisted = persisted });
                    }
                    finally { window.Close(); }
                    // Reopen the persisted maximized/minimized state; start visible, never minimized.
                    Window reopened = CreateWindow(root);
                    try
                    {
                        reopened.Show(); await IdleAsync();
                        Check(reopened.WindowState == (saved.Maximized ? WindowState.Maximized : WindowState.Normal), "Reopen restored an incorrect show state.");
                        reopened.WindowState = WindowState.Normal; await IdleAsync();
                        Check(Close(WindowNative.Capture(reopened)!.Bounds, saved.Bounds), "Restoring a maximized window lost its normal size.");
                    }
                    finally { reopened.Close(); }
                    saved = saved with { Maximized = false };
                }
            }
            WindowDisplay mainDisplay = displays.First();
            string mainRoot = Path.Combine(store.Root, "main"); Directory.CreateDirectory(mainRoot);
            double mainScale = mainDisplay.Dpi / 96;
            var mainSaved = new WindowPlacement(mainDisplay.WorkArea.Left + 40, mainDisplay.WorkArea.Top + 40,
                Math.Min(1000 * mainScale, mainDisplay.WorkArea.Width - 40), Math.Min(740 * mainScale, mainDisplay.WorkArea.Height - 40),
                false, 2, mainDisplay.Id, mainDisplay.Device, mainDisplay.WorkArea, mainDisplay.Dpi);
            await File.WriteAllTextAsync(Path.Combine(mainRoot, "window.json"), JsonSerializer.Serialize(mainSaved));
            var main = new MainWindow(new(mainRoot)); Application.Current.MainWindow = main;
            try
            {
                main.Show();
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                while (!main.ViewModel.IsReady && !main.ViewModel.HasError) { await Task.Delay(50, timeout.Token); }
                Check(main.ViewModel.IsReady, main.ViewModel.Error); await IdleAsync();
                Check(Close(WindowNative.ScreenRectangle(main)!, mainSaved.Bounds), "The main window did not restore to the requested physical position.");
                await PrototypePreview.RenderAsync(main, Path.Combine(output, "main-restored.png"));
            }
            finally { main.Close(); while (main.IsVisible) { await Task.Delay(25); } }
            WindowPlacement mainDisk = JsonSerializer.Deserialize<WindowPlacement>(await File.ReadAllTextAsync(Path.Combine(mainRoot, "window.json")))!;
            Check(Close(mainDisk.Bounds, mainSaved.Bounds), "The main window close handler did not persist the restored position.");
            // Read the original schema without a version, monitor or DPI field and upgrade on save.
            string legacyRoot = Path.Combine(store.Root, "legacy"); Directory.CreateDirectory(legacyRoot);
            double legacyScale = displays.First(d => d.Primary).Dpi / 96;
            string legacyJson = JsonSerializer.Serialize(new { Left = mainSaved.Left / legacyScale, Top = mainSaved.Top / legacyScale, Width = 900d, Height = 660d, Maximized = true });
            await File.WriteAllTextAsync(Path.Combine(legacyRoot, "window.json"), legacyJson);
            Window legacy = CreateWindow(legacyRoot);
            try
            {
                legacy.Show(); await IdleAsync(); Check(legacy.WindowState == WindowState.Maximized, "Legacy maximization was lost.");
                await WindowPlacement.SaveAsync(legacy, legacyRoot);
                WindowPlacement upgraded = JsonSerializer.Deserialize<WindowPlacement>(await File.ReadAllTextAsync(Path.Combine(legacyRoot, "window.json")))!;
                Check(upgraded.Version == 2 && upgraded.WorkArea is not null && upgraded.IsValid, "Legacy data was not upgraded.");
            }
            finally { legacy.Close(); }
            string unwritableRoot = Path.Combine(store.Root, "blocked-save"); Directory.CreateDirectory(Path.Combine(unwritableRoot, "window.json"));
            Window blocked = CreateWindow(unwritableRoot);
            try { blocked.Show(); await IdleAsync(); await WindowPlacement.SaveAsync(blocked, unwritableRoot); }
            finally { blocked.Close(); }
            foreach (string text in new[] { "{", "{}", "null", "{\"Left\":0,\"Top\":0,\"Width\":-10,\"Height\":900}", new string('x', 17000) })
            {
                await File.WriteAllTextAsync(Path.Combine(store.Root, "window.json"), text);
                Window window = CreateWindow(store.Root);
                try { window.Show(); await IdleAsync(); Check(WindowNative.Capture(window) is not null, "Invalid save prevented a usable initial window."); }
                finally { window.Close(); }
            }
            Window manual = CreateWindow(store.Root); manual.WindowStartupLocation = WindowStartupLocation.Manual; manual.Left = -20000; manual.Top = -20000;
            string original = await File.ReadAllTextAsync(Path.Combine(store.Root, "window.json"));
            try { manual.Show(); await IdleAsync(); await WindowPlacement.SaveAsync(manual, store.Root); Check(await File.ReadAllTextAsync(Path.Combine(store.Root, "window.json")) == original, "Diagnostic positioning replaced the user's saved geometry."); }
            finally { manual.Close(); }
            Check(!Directory.EnumerateFiles(store.Root, "*.tmp", SearchOption.AllDirectories).Any(), "Temporary placement files were left behind.");
            await File.WriteAllTextAsync(Path.Combine(output, "window-verification.json"), JsonSerializer.Serialize(new
            {
                Success = true,
                Displays = displays,
                Results = results,
                MainWindowRestored = true,
                LegacyUpgrade = true,
                FailedSaveCleanup = true,
                InvalidFiles = 5,
                DiagnosticIsolation = true
            }, new JsonSerializerOptions { WriteIndented = true }));
            Application.Current.Shutdown(0);
        }
        catch (Exception ex)
        {
            await File.WriteAllTextAsync(Path.Combine(output, "window-verification-error.txt"), ex.ToString()); Application.Current.Shutdown(1);
        }
    }
    private static Window CreateWindow(string root)
    {
        var window = new Window
        {
            Title = "Eluna · ウィンドウ復元の検証",
            Width = 1000,
            Height = 740,
            MinWidth = 820,
            MinHeight = 600,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            ShowActivated = false,
            ShowInTaskbar = false,
            Content = new TextBlock { Text = "Eluna\nウィンドウ位置・通常サイズ・最大化状態の復元", Margin = new Thickness(32), FontSize = 20 }
        };
        window.SourceInitialized += (_, _) => WindowPlacement.Restore(window, root); return window;
    }
    private static bool Close(WindowArea a, WindowArea b) => Math.Abs(a.Left - b.Left) <= 2 && Math.Abs(a.Top - b.Top) <= 2 && Math.Abs(a.Width - b.Width) <= 2 && Math.Abs(a.Height - b.Height) <= 2;
    private static async Task IdleAsync() { await Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle); await Task.Delay(80); }
    private static void Check(bool condition, string message) { if (!condition) { throw new InvalidOperationException(message); } }
}
