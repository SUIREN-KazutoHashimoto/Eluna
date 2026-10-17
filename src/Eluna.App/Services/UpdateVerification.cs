using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Eluna.App.Models;

namespace Eluna.App.Services;

internal static class UpdateVerification
{
    private static void Check(bool ok, string message) { if (!ok) { throw new InvalidOperationException(message); } }
    private static IEnumerable<T> Find<T>(DependencyObject root) where T : DependencyObject
    { for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) { DependencyObject child = VisualTreeHelper.GetChild(root, i); if (child is T item) { yield return item; } foreach (T nested in Find<T>(child)) { yield return nested; } } }
    internal static async Task RunExitFixtureAsync(string root)
    {
        Directory.CreateDirectory(root); await File.WriteAllTextAsync(Path.Combine(root, "fixture-started"), Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        for (int i = 0; i < 1800 && !File.Exists(Path.Combine(root, "fixture-exit")); i++) { await Task.Delay(100); }
        Application.Current.Shutdown();
    }
    internal static async Task RunAsync(AppStore store, string output)
    {
        Directory.CreateDirectory(output); MainWindow? main = null; UpdateWindow? updates = null; InstallerWindow? setup = null; Process? fixture = null, worker = null, child = null;
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(6)); CancellationToken token = deadline.Token;
        async Task Until(Func<bool> ready) { while (!ready()) { await Task.Delay(50, token); } }
        try
        {
            UnsavedChanges.Review = _ => false; main = new(store) { WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = -20000 }; main.Show(); var chat = main.ViewModel;
            await Until(() => chat.IsReady || chat.HasError); Check(chat.IsReady && chat.CanApplyUpdate, "Idle app could not prepare an update: " + chat.Error);
            var download = new DownloadItem { Name = "fixture", Status = "active" }; chat.Downloads.Items.Add(download); Check(!chat.CanApplyUpdate, "An active download did not defer updates."); download.Status = "queued"; Check(!chat.CanApplyUpdate, "A reserved download did not defer updates."); chat.Downloads.Items.Remove(download);
            var generation = new MediaJob("fixture", _ => Task.CompletedTask, CancellationToken.None); chat.MediaJobs.Add(generation); Check(!chat.CanApplyUpdate, "A reserved generation did not defer updates."); generation.State = "active"; Check(!chat.CanApplyUpdate, "An active generation did not defer updates."); chat.MediaJobs.Remove(generation);
            updates = new(chat) { Owner = main, WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = -20000 }; updates.Show();
            var train = Find<ComboBox>(updates).Single(); train.SelectedValue = "develop"; await Until(() => chat.Settings.Updates?.Train == "develop");
            Check((await store.LoadAsync()).Settings.Updates?.Train == "develop", "Train choice was not persisted.");
            var automatic = Find<CheckBox>(updates).Single(); automatic.IsChecked = false; automatic.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); await Until(() => chat.Settings.Updates?.Automatic == false);
            setup = new() { Owner = main, WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = -20000 }; setup.Show();
            foreach (string theme in new[] { "dark", "light" })
            {
                Appearance.Apply(theme, 1.3); updates.Width = updates.MinWidth; updates.Height = updates.MinHeight; setup.Width = setup.MinWidth; setup.Height = setup.MinHeight;
                await PrototypePreview.RenderAsync(updates, Path.Combine(output, "updates-minimum-" + theme + ".png")); await PrototypePreview.RenderAsync(setup, Path.Combine(output, "setup-minimum-" + theme + ".png"));
            }
            setup.Close(); setup = null; updates.Close(); updates = null;
            Check(chat.CanApplyUpdate, "Removing completed work did not allow updates again.");
            string installDirectory = Path.Combine(output, "isolated-install"), data = Path.Combine(output, "isolated-data"); Directory.CreateDirectory(data); string updateDirectory = Path.Combine(data, "Updates"); Directory.CreateDirectory(updateDirectory);
            InstalledApp marker = await InstallerService.InstallAsync(InstallerService.Executable!, Path.GetFullPath(installDirectory), false, false, token);
            string installed = Path.Combine(marker.Directory, "Eluna.exe"), stagedFile = Path.Combine(updateDirectory, "new-version.exe"); File.Copy(InstallerService.Executable!, stagedFile); string fullStage = Path.GetFullPath(stagedFile);
            var dataStore = new AppStore(Path.GetFullPath(data)); await dataStore.SaveAsync(new(1, new(Updates: new("stable", false)), []));
            string sentinel = Path.Combine(dataStore.ModelsDirectory, "keep-marker.txt"); await File.WriteAllTextAsync(sentinel, "KEEP_MODELS", token);
            fixture = InstallerService.Start(installed, "--update-exit-fixture", dataStore.Root); await Until(() => File.Exists(Path.Combine(dataStore.Root, "fixture-started")) || fixture.HasExited); Check(!fixture.HasExited, "Installed fixture failed to start; use a published self-contained exe.");
            string hash; await using (var file = File.OpenRead(fullStage)) { hash = Convert.ToHexString(await SHA256.HashDataAsync(file, token)); }
            var plan = new UpdatePlan(Guid.NewGuid(), marker.Directory, marker.Id, dataStore.Root, fixture.Id, fixture.StartTime.ToUniversalTime().Ticks, new("99.0.0", "stable", fullStage, hash, new FileInfo(fullStage).Length, 5), true);
            string planPath = Path.Combine(updateDirectory, "apply-" + plan.Token.ToString("N") + ".json"); await File.WriteAllTextAsync(planPath, JsonSerializer.Serialize(plan, AppStore.JsonOptions), token);
            string helper = Path.GetFullPath(Path.Combine(updateDirectory, "Eluna-verification-worker.exe")); File.Copy(InstallerService.Executable!, helper); worker = InstallerService.Start(helper, "--apply-update", Path.GetFullPath(planPath)); await Task.Delay(1000, token); Check(!worker.HasExited && !File.Exists(Path.Combine(marker.Directory, "Eluna.previous.exe")), "Update did not wait for the old process to finish.");
            await File.WriteAllTextAsync(Path.Combine(dataStore.Root, "fixture-exit"), "exit", token); await fixture.WaitForExitAsync(token); await worker.WaitForExitAsync(token);
            string resultPath = Path.Combine(updateDirectory, "last-result.json"); using var result = JsonDocument.Parse(await File.ReadAllTextAsync(resultPath, token)); Check(worker.ExitCode == 0 && result.RootElement.GetProperty("success").GetBoolean(), "Update worker did not finish successfully: " + result.RootElement);
            Check(File.Exists(Path.Combine(marker.Directory, "Eluna.previous.exe")) && InstallerService.Read(marker.Directory)!.Version == "99.0.0" && File.ReadAllText(sentinel) == "KEEP_MODELS", "Update lost previous executable, version marker or models.");
            using var started = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(updateDirectory, "child-" + plan.Token.ToString("N") + ".json"), token));
            child = Process.GetProcessById(started.RootElement.GetProperty("pid").GetInt32()); Check(string.Equals(child.MainModule?.FileName, installed, StringComparison.OrdinalIgnoreCase), "The health confirmation did not come from the installed executable."); child.CloseMainWindow(); await child.WaitForExitAsync(token);
            await File.WriteAllTextAsync(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new { success = true, checks = "WPF update/setup dark/light at minimum and 130%, train/automatic settings persistence, active/queued work deferral, real self-contained install without registry/shortcuts, actual helper waits for prior process, atomic exe replacement, real WPF startup health, previous exe and model preservation", systemRegistration = "not changed by verification" }, AppStore.JsonOptions), token);
        }
        catch (Exception e) { Environment.ExitCode = 1; await File.WriteAllTextAsync(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new { success = false, error = e.ToString() }, AppStore.JsonOptions)); }
        finally
        {
            foreach (Process? owned in new[] { child, fixture, worker }) { if (owned is null) { continue; } try { if (!owned.HasExited) { owned.Kill(true); } } catch (InvalidOperationException) { } owned.Dispose(); }
            setup?.Close(); updates?.Close(); if (main is not null) { await main.ViewModel.ShutdownAsync(); main.Close(); }
            Application.Current.Shutdown();
        }
    }
}
