using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Eluna.App.Models;

namespace Eluna.App.Services;

// Uses only the dedicated fixture; never operates a user application or changes system settings.
internal static class ComputerEffectsVerification
{
    private static void Check(bool value, string message) { if (!value) { throw new InvalidOperationException(message); } }
    internal static async Task RunAsync(string output)
    {
        Directory.CreateDirectory(output); using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(10)); CancellationToken token = deadline.Token;
        Process? fixture = null; WindowsJob? job = null; Window? occluder = null; ComputerUseOverlay? overlay = null;
        var checks = new List<string>();
        async Task Until(Func<bool> condition) { while (!condition()) { await Task.Delay(50, token); } }
        try
        {
            ProcessStartInfo start = ComputerDesktop.StartInfo("--computer-fixture"); start.ArgumentList.Add(Path.Combine(output, "fixture"));
            var visibleStart = new ProcessStartInfo(start.FileName) { UseShellExecute = true, WindowStyle = ProcessWindowStyle.Normal };
            foreach (string argument in start.ArgumentList) { visibleStart.ArgumentList.Add(argument); }
            start = visibleStart;
            fixture = Process.Start(start)!; job = new(fixture); var desktop = new ComputerDesktop();
            await Until(() => { fixture.Refresh(); return fixture.MainWindowHandle != 0; });
            ComputerTarget target = (await desktop.WindowsAsync(token)).Single(w => w.ProcessId == fixture.Id);
            await File.WriteAllTextAsync(Path.Combine(output, "ready.json"), JsonSerializer.Serialize(target, ComputerJson.Options), token);
            ComputerObservation preview = await desktop.ObserveAsync(target, token);
            bool paused = false, stopped = false;
            overlay = new(() => paused = true, () => stopped = true); Check(overlay.Available, "Capture exclusion was not available on this Windows version.");
            overlay.Observe(preview); overlay.BeforeAction(preview, new("click", X: 700, Y: 440));
            await overlay.RenderPreviewAsync(Path.Combine(output, "preview.png"));
            await Until(() => ComputerDesktopNative.TargetInForeground(target)); await Task.Delay(500, token);
            ComputerObservation original = await desktop.ObserveScreenAsync(target, token);
            overlay.Observe(original); overlay.SetActive(true); await Task.Delay(300, token);
            Check(overlay.Visible && ComputerDesktopNative.TargetInForeground(target), "Overlay stole foreground focus or did not appear.");
            Check((await desktop.WindowsAsync(token)).All(w => w.ProcessId != Environment.ProcessId), "Effects appeared in the app permission list.");
            ComputerObservation captured = await desktop.ObserveScreenAsync(target, token);
            double difference = original.Signature.Zip(captured.Signature, (a, b) => Math.Abs(a - b)).Average();
            Check(difference < 2, "Overlay contaminated the visible-screen capture: " + difference);
            ComputerActionPolicy.RequireFresh(original, captured, new("key", Keys: "Tab"));
            checks.Add("Nonactivating effects and native screen fallback exclusion; signature difference " + difference);
            ComputerElement edit = captured.Elements.First(e => e.Name == "作業メモ" && e.Type == "Edit");
            int x = (edit.Bounds.Left + edit.Bounds.Width / 2 - captured.Bounds.Left) * 1000 / captured.Bounds.Width;
            int y = (edit.Bounds.Top + edit.Bounds.Height / 2 - captured.Bounds.Top) * 1000 / captured.Bounds.Height;
            var click = new ComputerAction("click", X: x, Y: y);
            overlay.BeforeAction(captured, click); await overlay.RenderPreviewAsync(Path.Combine(output, "click.png"));
            await desktop.ExecuteAsync(captured, click, token); overlay.AfterAction();
            ComputerObservation focused = await desktop.ObserveAsync(target, token);
            var type = new ComputerAction("type_text", Element: edit.Id, Text: "画面効果を重ねた日本語入力");
            overlay.BeforeAction(focused, type); await desktop.ExecuteAsync(focused, type, token); overlay.AfterAction();
            ComputerObservation typed = await desktop.ObserveAsync(target, token);
            Check(typed.Elements.Any(e => e.Value == type.Text), "Input failed through the effects window.");
            overlay.Observe(typed); overlay.BeforeAction(typed, type); await overlay.RenderPreviewAsync(Path.Combine(output, "typing.png"));
            overlay.BeforeAction(typed, new("drag", X: 180, Y: 330, X2: 740, Y2: 330)); await overlay.RenderPreviewAsync(Path.Combine(output, "drag.png"));
            checks.Add("Real coordinate click and Unicode keyboard input pass through; click, typing and drag rendering");
            occluder = new Window
            {
                Title = "Eluna capture mask fixture",
                Width = 140,
                Height = 60,
                Left = typed.Bounds.Left + 30,
                Top = typed.Bounds.Top + 40,
                WindowStyle = WindowStyle.None,
                ShowActivated = false,
                ShowInTaskbar = false,
                Topmost = true,
                Background = Brushes.Red
            };
            occluder.Show(); await Task.Delay(250, token);
            ComputerObservation masked = await desktop.ObserveScreenAsync(target, token);
            using (var stream = new MemoryStream(Convert.FromBase64String(masked.ImageBase64)))
            {
                var bitmap = new FormatConvertedBitmap(BitmapFrame.Create(stream), PixelFormats.Bgra32, null, 0); byte[] pixel = new byte[4];
                int px = 70 * bitmap.PixelWidth / typed.Bounds.Width, py = 65 * bitmap.PixelHeight / typed.Bounds.Height;
                bitmap.CopyPixels(new Int32Rect(px, py, 1, 1), pixel, 4, 0);
                Check(pixel.Take(3).All(c => c is > 12 and < 40), "A normal Eluna window was excluded from privacy masking.");
            }
            occluder.Close(); occluder = null; checks.Add("Other Eluna windows remain privacy-masked above the target");
            overlay.SetActive(false); Check(!overlay.Visible, "Effects remained visible after pause/review.");
            overlay.SetActive(true); await Task.Delay(100, token); Check(overlay.Visible, "Effects did not resume.");
            overlay.SetActive(false); overlay.Dispose(); Check(!overlay.Visible, "Effects remained after disposal.");
            checks.Add("Pause/review hides effects; resume restores; disposal closes both surfaces");
            overlay = new(() => paused = true, () => stopped = true); overlay.Observe(typed); overlay.SetActive(true);
            await File.WriteAllTextAsync(Path.Combine(output, "controls-ready.json"), "{}", token);
            await Until(() => paused); Check(!overlay.Visible, "Pause button did not hide the effects.");
            overlay.SetActive(true); await Until(() => stopped); Check(!overlay.Visible, "Stop button did not hide the effects.");
            checks.Add("Floating pause and stop buttons work without activating the target or leaving effects behind");
            await File.WriteAllTextAsync(Path.Combine(output, "verification.json"), JsonSerializer.Serialize(new { Success = true, Checks = checks }, ComputerJson.Options), token);
        }
        catch (Exception ex) { await File.WriteAllTextAsync(Path.Combine(output, "error.txt"), ex.ToString()); }
        finally
        {
            overlay?.Dispose(); occluder?.Close(); job?.Dispose();
            if (fixture is not null) { try { if (!fixture.HasExited) { fixture.Kill(true); await fixture.WaitForExitAsync(); } } catch (InvalidOperationException) { } fixture.Dispose(); }
            Application.Current.Shutdown(File.Exists(Path.Combine(output, "error.txt")) ? 1 : 0);
        }
    }
}
