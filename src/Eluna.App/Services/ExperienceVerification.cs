using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Eluna.App.Models;

namespace Eluna.App.Services;

internal static partial class ExperienceVerification
{
    private static void Check(bool ok, string message) { if (!ok) { throw new InvalidOperationException(message); } }
    private static IEnumerable<T> Find<T>(DependencyObject root) where T : DependencyObject
    { for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) { DependencyObject child = VisualTreeHelper.GetChild(root, i); if (child is T value) { yield return value; } foreach (T nested in Find<T>(child)) { yield return nested; } } }
    internal static async Task RunAsync(AppStore store, string output)
    {
        Directory.CreateDirectory(output); MainWindow? main = null; using var stop = new CancellationTokenSource(TimeSpan.FromMinutes(4)); CancellationToken token = stop.Token;
        async Task Until(Func<bool> condition) { while (!condition()) { await Task.Delay(25, token); } }
        try
        {
            UnsavedChanges.Review = _ => false;
            main = new MainWindow(store) { WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = -20000 }; main.Show(); var chat = main.ViewModel;
            await Until(() => chat.IsReady || chat.HasError); Check(chat.IsReady, chat.Error);
            var friend = new AgentAccount(Guid.NewGuid(), "星乃", "星", Specialty: "天文学と読書"); await chat.SaveAgentAsync(friend);
            var first = await chat.CreateRoomAsync("夜更かしラウンジ", "dm", [friend.Id]); chat.Input = "一つ目の下書き";
            var second = await chat.CreateRoomAsync("長い名前でも重ならずに読める会話の名前を確認する", "group", [friend.Id]); chat.Input = "二つ目の下書き";
            Check(main.InvokeNavigationShortcut(Key.Left, ModifierKeys.Alt), "Back shortcut was not handled."); Check(chat.SelectedConversation == first && chat.Input == "一つ目の下書き", "Back navigation lost the previous channel draft."); main.MoveHistory(1); Check(chat.SelectedConversation == second && chat.Input == "二つ目の下書き", "Forward navigation lost its draft.");
            Check(!main.InvokeNavigationShortcut(Key.K, ModifierKeys.Control | ModifierKeys.Shift), "Quick switch hijacked an unrelated key combination.");
            await chat.SaveProjectAsync(new(Guid.NewGuid(), "星の図書館", "", "project-only", false, [], [])); var third = await chat.CreateRoomAsync("今夜のおすすめ", "channel", [friend.Id]);
            first.Messages.Add(new() { Role = "user", Content = "今日読んだ本の話をしよう。", Identity = new(null, "自分", "◉", Time: DateTimeOffset.Now) }); first.Messages.Add(new() { Role = "assistant", Content = "いいね。夜空を眺めながら読める本なら、何から話そうか？", Identity = new(friend.Id, friend.Name, friend.Icon, Time: DateTimeOffset.Now) });
            main.OpenQuickSwitcher(); var picker = main.OwnedWindows.OfType<QuickSwitcherWindow>().Single(); picker.Query.Text = "@夜更かし"; Check(picker.Results.Items.Count == 1, "Quick-switch search did not find a DM in another server scope.");
            foreach (string theme in new[] { "dark", "light" }) { Appearance.Apply(theme, 1.3); picker.Width = picker.MinWidth; picker.Height = picker.MinHeight; await PrototypePreview.RenderAsync(picker, Path.Combine(output, "switcher-minimum-" + theme + ".png")); Check(picker.Query.ActualWidth > 250 && picker.Results.ActualHeight > 30, "Quick switcher clipped its search/results at minimum size."); }
            Check(await picker.OpenSelectedAsync() && chat.SelectedConversation == first && chat.Input == "一つ目の下書き", "Quick switch did not preserve scope and draft.");
            main.OpenQuickSwitcher(); picker = main.OwnedWindows.OfType<QuickSwitcherWindow>().Single(); picker.Query.Text = "*星の図書館"; Check(await picker.OpenSelectedAsync() && chat.SelectedProject?.Name == "星の図書館", "Server navigation did not change scope.");
            main.OpenQuickSwitcher(); picker = main.OwnedWindows.OfType<QuickSwitcherWindow>().Single(); picker.Query.Text = "@星乃"; picker.Results.SelectedItem = picker.Results.Items.OfType<NavigationTarget>().Single(t => t.Account == friend.Id); int rooms = chat.Conversations.Count;
            Check(await picker.OpenSelectedAsync() && chat.SelectedConversation == first && chat.Conversations.Count == rooms, "Member navigation did not reuse the existing DM.");
            main.OpenQuickSwitcher(); picker = main.OwnedWindows.OfType<QuickSwitcherWindow>().Single(); picker.Query.Text = "存在しない"; Check(picker.Results.Items.Count == 0 && !await picker.OpenSelectedAsync(), "Empty search triggered navigation.");
            var escape = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(picker)!, 0, Key.Escape) { RoutedEvent = Keyboard.PreviewKeyDownEvent }; picker.Query.RaiseEvent(escape); Check(escape.Handled && !picker.IsVisible && chat.SelectedConversation == first && chat.Input == "一つ目の下書き", "Esc did not cancel navigation while preserving the draft.");
            await chat.ApplySettingsAsync(chat.Settings with { ReduceMotion = true }); Check(!UiMotion.Enabled && (await store.LoadAsync()).Settings.ReduceMotion, "Reduced motion did not stop or persist.");
            var settings = new SettingsWindow(chat) { Owner = main, Left = -20000, Top = -20000, WindowStartupLocation = WindowStartupLocation.Manual }; settings.Show(); Check(settings.ReduceMotionBox.IsChecked == true, "Settings did not reflect reduced motion.");
            settings.ReduceMotionBox.IsChecked = false; settings.SaveButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); await Until(() => !chat.Settings.ReduceMotion && settings.SaveButton.IsEnabled); Check(!(await store.LoadAsync()).Settings.ReduceMotion, "Reduced motion did not save from the settings UI."); settings.Close();
            Appearance.Apply("dark", 1); main.Width = 1280; main.Height = 900; await PrototypePreview.RenderAsync(main, Path.Combine(output, "main-dark.png"));
            var servers = (ListBox)main.FindName("ServerList"); servers.SelectedItem = chat.ProjectChoices.First(p => p.Id == third.ProjectId); servers.UpdateLayout();
            var item = (ListBoxItem)servers.ItemContainerGenerator.ContainerFromItem(servers.SelectedItem); var tile = (Border)item.Template.FindName("Tile", item); var mark = (Border)item.Template.FindName("Mark", item);
            Check(!UiMotion.Enabled || tile.HasAnimatedProperties && mark.HasAnimatedProperties, "Server selection did not start its transition.");
            await Task.Delay(200, token); Check(tile.CornerRadius.TopLeft == 16 && mark.Height == 36 && mark.Opacity == 1, "Server state did not update its shape/selection mark.");
            var button = (Button)main.FindName("QuickSwitcherButton"); button.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.PreviewMouseLeftButtonDownEvent });
            Check(!UiMotion.Enabled || button.RenderTransform is TransformGroup group && group.Children.OfType<ScaleTransform>().Any(s => s.HasAnimatedProperties), "Press feedback did not run.");
            UiMotion.Configure(true); Check(button.RenderTransform is TransformGroup reset && reset.Children.OfType<ScaleTransform>().All(s => s.ScaleX == 1 && !s.HasAnimatedProperties) && !tile.HasAnimatedProperties, "Disabling motion left active transformations.");
            button.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.PreviewMouseLeftButtonUpEvent });
            chat.SelectedConversation = first;
            foreach (string theme in new[] { "dark", "light" }) { Appearance.Apply(theme, 1.3); main.Width = 820; main.Height = 600; await PrototypePreview.RenderAsync(main, Path.Combine(output, "main-minimum-" + theme + ".png")); Check(((Button)main.FindName("QuickSwitcherButton")).ActualWidth <= 280 && ((ListBox)main.FindName("MessagesList")).ActualHeight > 150, "Navigation controls crowded out the conversation."); }
            var guide = new ShortcutsWindow { Owner = main, WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = -20000 }; guide.Show(); guide.Width = guide.MinWidth; guide.Height = guide.MinHeight; await PrototypePreview.RenderAsync(guide, Path.Combine(output, "shortcuts-minimum.png")); guide.Close();
            await AuditAsync(main, output, token);
            await chat.PersistSafelyAsync(); AppStore.Validate(await store.LoadAsync());
            await File.WriteAllTextAsync(Path.Combine(output, "result.txt"), "Experience: back/forward/draft/scope, quick switch/empty/minimum themes, shortcuts, reduced-motion settings/persistence/stop, server state/press/entrance/typing lifecycle, streaming, member scroll/Esc/logical focus, automation names/high-contrast simulation: OK", token);
        }
        catch (Exception ex) { Environment.ExitCode = 1; await File.WriteAllTextAsync(Path.Combine(output, "result.txt"), ex.ToString()); }
        finally { UiMotion.Configure(false); if (main is not null) { foreach (Window child in main.OwnedWindows.Cast<Window>().ToArray()) { child.Close(); } await main.ViewModel.ShutdownAsync(); main.Close(); } Application.Current.Shutdown(); }
    }
}
