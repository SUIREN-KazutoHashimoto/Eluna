using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Eluna.App.Controls;
using Eluna.App.Models;

namespace Eluna.App.Services;

internal static class DiagnosticsVerification
{
    private static void Check(bool ok, string message) { if (!ok) { throw new InvalidOperationException(message); } }
    private static IEnumerable<T> Find<T>(DependencyObject root) where T : DependencyObject
    { for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) { DependencyObject child = VisualTreeHelper.GetChild(root, i); if (child is T item) { yield return item; } foreach (T nested in Find<T>(child)) { yield return nested; } } }
    internal static async Task RunAsync(AppStore store, string output)
    {
        Directory.CreateDirectory(output); MainWindow? main = null; SettingsWindow? settings = null;
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(3)); CancellationToken token = deadline.Token;
        async Task Until(Func<bool> ready) { while (!ready()) { await Task.Delay(30, token); } }
        try
        {
            UnsavedChanges.Review = _ => false;
            await store.SaveAsync(WorkspaceService.Upgrade(new(1, new(Logs: new("detailed", true)), [])));
            main = new(store) { WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = -20000 }; main.Show(); var chat = main.ViewModel;
            await Until(() => chat.IsReady || chat.HasError); Check(chat.IsReady, chat.Error);
            string sentinel = "UI_FIELD_VALUE_NOT_FOR_LOG_" + Guid.NewGuid().ToString("N");
            ((TextBox)main.FindName("InputBox")).Text = sentinel; await chat.Trace.FlushAsync();
            IReadOnlyList<DiagnosticEvent> initial = await chat.Trace.ReadAsync();
            Check(initial.Any(r => r.Area == "ui" && r.Event == "text.changed") && !initial.Any(r => r.Detail.Contains(sentinel, StringComparison.Ordinal)), "UI tracing copied a field value or failed to record input metadata.");
            Guid operation = Guid.NewGuid(); ChatMessage message = new() { Role = "assistant", Content = "確認した内容を短く返します。", DiagnosticsId = operation, Identity = new(chat.Agents[0].Id, chat.Agents[0].Name) };
            chat.SelectedConversation!.Messages.Add(message);
            chat.Trace.Write("warning", "web", "provider.warning", "fixture provider timed out", operation, chat.SelectedConversation.Id);
            chat.Trace.Content("model", "input", "fixture only: password=never-store-this", operation, chat.SelectedConversation.Id);
            settings = new(chat, 7) { Owner = main, WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = -20000 }; settings.Show(); settings.ShowMessage(message);
            Check(Find<DeveloperLogPanel>(settings).All(p => p.Visibility == Visibility.Collapsed), "Developer diagnostics were shown with developer mode off.");
            Check(!message.Content.Contains("provider", StringComparison.Ordinal), "A technical failure entered the conversation text.");
            settings.SelectTab(10); await Task.Delay(150, token);
            CheckBox developer = Find<CheckBox>(settings).Single(c => c.Content is string s && s.StartsWith("開発者モード", StringComparison.Ordinal)); developer.IsChecked = true; developer.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            ((Button)settings.FindName("SaveButton")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); await Until(() => chat.Settings.DeveloperMode);
            settings.SelectTab(7); settings.ShowMessage(message); await Task.Delay(250, token);
            DeveloperLogPanel panel = Find<DeveloperLogPanel>(settings).Single(p => p.IsVisible); await panel.RefreshAsync();
            Check(Find<ListBox>(panel).Single().Items.Count >= 2, "Message details did not show its correlated technical events.");
            foreach (string theme in new[] { "dark", "light" })
            {
                Appearance.Apply(theme, 1.3); settings.Width = settings.MinWidth; settings.Height = settings.MinHeight;
                settings.SelectTab(10); await PrototypePreview.RenderAsync(settings, Path.Combine(output, "settings-minimum-" + theme + ".png"));
                Check(((Button)settings.FindName("SaveButton")).IsVisible, "Log settings covered the save action at minimum size.");
                settings.SelectTab(7); await PrototypePreview.RenderAsync(settings, Path.Combine(output, "message-details-minimum-" + theme + ".png"));
            }
            await chat.Trace.ExportAsync(Path.Combine(output, "logs.zip"), token); AppState saved = await store.LoadAsync();
            Check(saved.Settings.DeveloperMode && saved.Settings.Logs == new LogOptions("detailed", true), "Developer visibility changed logging preferences or did not persist.");
            await File.WriteAllTextAsync(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new { success = true, checks = "UI metadata without field values, developer visibility independent from capture, message-scoped errors and model I/O, no diagnostic text in reply, persistence, ZIP and minimum dark/light at 130%" }, AppStore.JsonOptions), token);
        }
        catch (Exception ex) { Environment.ExitCode = 1; await File.WriteAllTextAsync(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new { success = false, error = ex.ToString() }, AppStore.JsonOptions)); }
        finally { settings?.Close(); if (main is not null) { await main.ViewModel.ShutdownAsync(); main.Close(); } Application.Current.Shutdown(); }
    }
}
