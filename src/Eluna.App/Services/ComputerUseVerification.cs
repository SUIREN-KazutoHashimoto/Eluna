using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Eluna.App.Models;

namespace Eluna.App.Services;

internal static class ComputerUseVerification
{
    internal static Window Fixture(string output)
    {
        Directory.CreateDirectory(output); int count = 0;
        var window = new Window { Title = "Eluna Computer Use 検証対象", Width = 620, Height = 460, Left = 40, Top = 50, WindowStartupLocation = WindowStartupLocation.Manual };
        var panel = new StackPanel { Margin = new(24) }; window.Content = panel;
        panel.Children.Add(new TextBlock { Text = "Computer Use 検証用メモ", FontSize = 23, Margin = new(0, 0, 0, 20) });
        var editor = new TextBox { Height = 85, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap }; AutomationProperties.SetName(editor, "作業メモ"); panel.Children.Add(editor);
        var save = new Button { Content = "保存（検証）", Margin = new(0, 12, 0, 12), Height = 45, Background = Brushes.DodgerBlue, Foreground = Brushes.White }; panel.Children.Add(save);
        var state = new TextBlock { Text = "まだ保存していません", TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 10) }; panel.Children.Add(state);
        save.Click += (_, _) => { count++; File.WriteAllText(Path.Combine(output, "fixture-result.json"), JsonSerializer.Serialize(new { Text = editor.Text, Count = count }, ComputerJson.Options)); state.Text = "保存済み: " + editor.Text; };
        var password = new PasswordBox { Password = "fixture-secret-not-for-model", Width = 210, HorizontalAlignment = HorizontalAlignment.Left }; panel.Children.Add(password); AutomationProperties.SetName(password, "検証用パスワード");
        var hang = new Button { Content = "応答停止（検証）", Height = 30, Margin = new(0, 8, 0, 0) }; panel.Children.Add(hang);
        hang.Click += (_, _) => { File.WriteAllText(Path.Combine(output, "provider-blocked.txt"), "blocked"); Thread.Sleep(30000); };
        window.Loaded += (_, _) => editor.Focus();
        var timeout = new DispatcherTimer { Interval = TimeSpan.FromMinutes(15) }; timeout.Tick += (_, _) => { timeout.Stop(); window.Close(); }; timeout.Start();
        return window;
    }

    private static void Check(bool condition, string message) { if (!condition) { throw new InvalidOperationException(message); } }
    internal static async Task RunAsync(AppStore store, string output, string? metadata)
    {
        Directory.CreateDirectory(output); using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(8)); CancellationToken token = timeout.Token;
        Process? fixture = null; WindowsJob? fixtureJob = null; MainWindow? main = null; ComputerUseWindow? computer = null;
        var checks = new List<string>(); var modelSteps = new List<ComputerStep>();
        async Task Until(Func<bool> condition) { while (!condition()) { await Task.Delay(40, token); } }
        try
        {
            string fixtureFolder = Path.Combine(output, "fixture");
            ProcessStartInfo start = ComputerDesktop.StartInfo("--computer-fixture"); start.ArgumentList.Add(fixtureFolder);
            fixture = Process.Start(start)!; fixtureJob = new(fixture);
            var desktop = new ComputerDesktop(); ComputerTarget? target = null;
            await Until(() => { fixture.Refresh(); return fixture.MainWindowHandle != 0; });
            target = (await desktop.WindowsAsync(token)).Single(w => w.ProcessId == fixture.Id);
            ComputerObservation first = await desktop.ObserveAsync(target, token);
            Check(first.ImageWidth > 100 && first.ImageHeight > 100 && first.ImageBase64.Length > 1000, "Native screenshot missing.");
            Check(first.Elements.Any(e => e.Password) && first.Elements.All(e => !e.Name.Contains("fixture-secret", StringComparison.Ordinal) && !e.Value.Contains("fixture-secret", StringComparison.Ordinal)), "Password text leaked into observation.");
            using (var pixels = new MemoryStream(Convert.FromBase64String(first.ImageBase64)))
            {
                var bitmap = new FormatConvertedBitmap(BitmapFrame.Create(pixels), PixelFormats.Bgra32, null, 0);
                ComputerBounds password = first.Elements.First(e => e.Password).Bounds;
                int px = (password.Left + password.Width / 2 - first.Bounds.Left) * bitmap.PixelWidth / first.Bounds.Width;
                int py = (password.Top + password.Height / 2 - first.Bounds.Top) * bitmap.PixelHeight / first.Bounds.Height;
                byte[] sample = new byte[4]; bitmap.CopyPixels(new Int32Rect(px, py, 1, 1), sample, 4, 0);
                Check(sample.Take(3).All(c => c < 55), "Password pixels were not masked.");
            }
            ComputerElement editor = first.Elements.First(e => e.Name == "作業メモ" && e.Type == "Edit");
            await File.WriteAllTextAsync(Path.Combine(output, "ready-for-input.json"), JsonSerializer.Serialize(new { Fixture = fixture.Id, Target = target.Handle }, ComputerJson.Options), token);
            await Until(() => ComputerDesktopNative.TargetInForeground(target));
            first = await desktop.ObserveAsync(target, token); editor = first.Elements.First(e => e.Name == "作業メモ" && e.Type == "Edit");
            await File.WriteAllTextAsync(Path.Combine(output, "observation-before.json"), JsonSerializer.Serialize(first, ComputerJson.Options), token);
            await desktop.ExecuteAsync(first, new("set_value", "入力の確認", editor.Id, Text: "日本語の入力確認"), token);
            ComputerObservation typed = await desktop.ObserveAsync(target, token); Check(typed.Elements.Any(e => e.Value == "日本語の入力確認"), "UIA Unicode set_value failed.");
            try { await desktop.ExecuteAsync(first, new("click", Element: first.Elements.First(e => e.Name == "保存（検証）").Id), token); throw new Exception("Stale screen executed."); }
            catch (ComputerScreenChangedException) { checks.Add("Live stale-screen rejection"); }
            editor = typed.Elements.First(e => e.Name == "作業メモ" && e.Type == "Edit");
            await desktop.ExecuteAsync(typed, new("set_value", Element: editor.Id, Text: ""), token);
            typed = await desktop.ObserveAsync(target, token); editor = typed.Elements.First(e => e.Name == "作業メモ" && e.Type == "Edit");
            await desktop.ExecuteAsync(typed, new("type_text", Element: editor.Id, Text: "日本語ABC123"), token);
            await Task.Delay(250, token); typed = await desktop.ObserveAsync(target, token);
            Check(typed.Elements.Any(e => e.Value == "日本語ABC123"), "Native Unicode keyboard input failed.");
            ComputerElement save = typed.Elements.First(e => e.Name == "保存（検証）");
            int x = (save.Bounds.Left + save.Bounds.Width / 2 - typed.Bounds.Left) * 1000 / typed.Bounds.Width;
            int y = (save.Bounds.Top + save.Bounds.Height / 2 - typed.Bounds.Top) * 1000 / typed.Bounds.Height;
            await desktop.ExecuteAsync(typed, new("click", X: x, Y: y), token);
            string resultFile = Path.Combine(fixtureFolder, "fixture-result.json"); await Until(() => File.Exists(resultFile));
            using (JsonDocument saved = JsonDocument.Parse(await File.ReadAllTextAsync(resultFile, token))) { Check(saved.RootElement.GetProperty("text").GetString() == "日本語ABC123", "Coordinate click saved wrong content."); }
            checks.Add("Window identity, screenshot, password redaction, UIA input, native Unicode keyboard, coordinate mouse input");

            LocalModel? model = null;
            if (metadata is not null)
            {
                model = JsonSerializer.Deserialize<LocalModel>(await File.ReadAllTextAsync(metadata, token), AppStore.JsonOptions)!;
                model = model with { Preferences = new(ContextSize: 8192, Temperature: 0.1, MaxOutputTokens: 1024, DefaultMode: "fast") };
                string folder = Path.Combine(store.ModelsDirectory, model.Id); Directory.CreateDirectory(folder);
                await File.WriteAllTextAsync(Path.Combine(folder, "model.json"), JsonSerializer.Serialize(model, AppStore.JsonOptions), token);
            }
            main = new MainWindow(store) { WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = -20000 }; main.Show();
            await Until(() => main.ViewModel.IsReady || main.ViewModel.HasError); Check(main.ViewModel.IsReady, main.ViewModel.Error);
            computer = main.OpenComputerUse(); computer.WindowStartupLocation = WindowStartupLocation.Manual; computer.Left = -20000; computer.Top = -20000;
            await Task.Delay(750, token);
            computer.PreviewVerification(model, target, typed);
            await PrototypePreview.RenderAsync(computer, Path.Combine(output, "computer-dark.png"));
            computer.Width = computer.MinWidth; computer.Height = computer.MinHeight;
            await PrototypePreview.RenderAsync(computer, Path.Combine(output, "computer-minimum.png"));
            Appearance.Apply("light", 1.3); await PrototypePreview.RenderAsync(computer, Path.Combine(output, "computer-light-130.png")); Appearance.Apply("dark");
            editor = typed.Elements.First(e => e.Name == "作業メモ" && e.Type == "Edit");
            Task<bool> review = computer.ReviewVerificationAsync(new(typed, new("type_text", "入力内容を確認します", editor.Id, Text: "日本語の確認用入力"), "入力内容と対象を確認してください。"), token);
            await PrototypePreview.RenderAsync(computer, Path.Combine(output, "computer-confirm-minimum.png"));
            Appearance.Apply("light", 1.3); await PrototypePreview.RenderAsync(computer, Path.Combine(output, "computer-confirm-light-130.png")); Appearance.Apply("dark");
            computer.DecideVerification(false); Check(!await review, "Review rejection failed.");
            checks.Add("Review UI at minimum size, light/dark themes and 130 percent text scale");
            computer.Width = 1180; computer.Height = 830;
            if (model is not null)
            {
                typed = await desktop.ObserveAsync(target, token); editor = typed.Elements.First(e => e.Name == "作業メモ" && e.Type == "Edit");
                await desktop.ExecuteAsync(typed, new("set_value", Element: editor.Id, Text: ""), token);
                string instruction = "作業メモ欄に「明日の作業を確認する」と正確に入力し、「保存（検証）」をクリックしてください。保存済み表示に同じ内容が出たことを確認したら完了してください。それ以外の欄やアプリを操作しないでください。";
                Task run = computer.StartVerificationAsync(model, target, instruction); bool captured = false;
                while (!run.IsCompleted)
                {
                    token.ThrowIfCancellationRequested();
                    if (computer.AwaitingApproval && computer.PendingReview is { } proposal)
                    {
                        if (!captured) { await PrototypePreview.RenderAsync(computer, Path.Combine(output, "computer-confirm.png")); captured = true; }
                        bool allowedAction = proposal.Action.Type is "type_text" or "set_value" && proposal.Action.Text == "明日の作業を確認する" ||
                            proposal.Action.Type == "click" && proposal.Observation.Elements.FirstOrDefault(e => e.Id == proposal.Action.Element)?.Name is "作業メモ" or "保存（検証）" ||
                            proposal.Action.Type == "key" && proposal.Action.Keys is "Ctrl+A" or "Tab";
                        Check(allowedAction, "Real model proposed an action outside the fixture task: " + JsonSerializer.Serialize(proposal.Action, ComputerJson.Options));
                        computer.DecideVerification(true);
                    }
                    await Task.Delay(60, token);
                }
                await run; modelSteps.AddRange(computer.Steps);
                await File.WriteAllTextAsync(Path.Combine(output, "model-result.json"), JsonSerializer.Serialize(new { computer.OperationStatus, Steps = modelSteps }, ComputerJson.Options), token);
                using JsonDocument saved = JsonDocument.Parse(await File.ReadAllTextAsync(resultFile, token));
                Check(saved.RootElement.GetProperty("text").GetString() == "明日の作業を確認する" && computer.OperationStatus.StartsWith("モデルの完了報告", StringComparison.Ordinal), "Real model did not complete and verify the fixture task: " + computer.OperationStatus);
                checks.Add("Real llama.cpp model + " + (model.Projector is null ? "UI elements" : "image/projector + UI elements") + " + actual review UI + native application result");
            }
            typed = await desktop.ObserveAsync(target, token);
            bool hotkeyStopped = false;
            using (var hotkey = new ComputerStopHotkey(computer, () => hotkeyStopped = true))
            {
                await desktop.ExecuteAsync(typed, new("key", Keys: "Ctrl+Alt+Esc"), token);
                await Until(() => hotkeyStopped);
            }
            checks.Add("Global Ctrl+Alt+Esc received from a target application");
            if (model is not null)
            {
                Task canceledRun = computer.StartVerificationAsync(model, target, "作業メモに入力してください。");
                await Task.Delay(200, token); computer.Close(); await computer.PendingClose; await canceledRun;
                Check(!computer.IsVisible && computer.Steps.Count == 0 && computer.OperationStatus.StartsWith("停止しました", StringComparison.Ordinal), "Closing while planning did not cancel before input.");
                checks.Add("Closing the operation window during model preparation cancels before any input");
            }
            typed = await desktop.ObserveAsync(target, token);
            ComputerElement hang = typed.Elements.First(e => e.Name == "応答停止（検証）");
            x = (hang.Bounds.Left + hang.Bounds.Width / 2 - typed.Bounds.Left) * 1000 / typed.Bounds.Width;
            y = (hang.Bounds.Top + hang.Bounds.Height / 2 - typed.Bounds.Top) * 1000 / typed.Bounds.Height;
            await desktop.ExecuteAsync(typed, new("click", X: x, Y: y), token);
            await Until(() => File.Exists(Path.Combine(fixtureFolder, "provider-blocked.txt")));
            using (var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(700)))
            {
                var clock = Stopwatch.StartNew();
                bool canceled = false;
                try { await desktop.ObserveAsync(target, cancel.Token); }
                catch (OperationCanceledException) { canceled = true; }
                Check(canceled && clock.Elapsed < TimeSpan.FromSeconds(5), "Cancellation waited for a blocked provider.");
            }
            checks.Add("Cancel a worker blocked by a target application without blocking Eluna");
            await File.WriteAllTextAsync(Path.Combine(output, "verification.json"), JsonSerializer.Serialize(new { Success = true, Checks = checks, Model = model?.DisplayName, Vision = model?.Projector is not null, Steps = modelSteps }, ComputerJson.Options), token);
        }
        catch (Exception ex) { computer?.Stop(); await File.WriteAllTextAsync(Path.Combine(output, "error.txt"), ex.ToString()); }
        finally
        {
            if (computer is not null) { computer.Close(); await computer.PendingClose; }
            fixtureJob?.Dispose(); if (fixture is not null) { try { if (!fixture.HasExited) { fixture.Kill(true); await fixture.WaitForExitAsync(); } } catch (InvalidOperationException) { } fixture.Dispose(); }
            if (main is not null) { main.Close(); }
            Application.Current.Shutdown(File.Exists(Path.Combine(output, "error.txt")) ? 1 : 0);
        }
    }
}
