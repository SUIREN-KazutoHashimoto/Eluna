using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Eluna.App.Controls;
using Eluna.App.Models;

namespace Eluna.App.Services;

internal static partial class ExperienceVerification
{
    private static async Task AuditAsync(MainWindow main, string output, CancellationToken token)
    {
        var report = new Dictionary<string, object>();
        UiMotion.Configure(false); bool enabled = UiMotion.Enabled;
        report["environment"] = new { windowsAnimations = SystemParameters.ClientAreaAnimation, highContrast = SystemParameters.HighContrast, motionEnabled = enabled, physicalKeyboard = false, physicalDpiChange = false };
        var body = new StackPanel(); var button = new Button { Content = "確認する", Margin = new(12) }; var entrance = new Border { Height = 40 }; var typing = new TypingIndicator { Text = "星乃 が入力中…" }; var progress = new ProgressBar { IsIndeterminate = true, Width = 240, Height = 6, Margin = new(10) };
        body.Children.Add(button); body.Children.Add(entrance); body.Children.Add(typing); body.Children.Add(progress); UiMotion.SetFeedback(entrance, "entrance");
        var host = new Window { Content = body, Owner = main, ShowActivated = false, Width = 400, Height = 300, WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = -20000 }; Appearance.Attach(host); host.Show();
        try
        {
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            button.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.PreviewMouseLeftButtonDownEvent }); UiMotion.Reveal(entrance);
            ScaleTransform scale = ((TransformGroup)button.RenderTransform).Children.OfType<ScaleTransform>().Single();
            Check(!enabled || scale.HasAnimatedProperties && entrance.HasAnimatedProperties, "Press/entrance feedback did not run when Windows allowed motion.");
            report["started"] = new { press = scale.HasAnimatedProperties, entrance = entrance.HasAnimatedProperties };
            bool activated = host.Activate(); await Task.Delay(40, token); bool activeTyping = host.IsActive && enabled;
            var travel = (TranslateTransform)Find<Border>(progress).Single(b => b.Name == "BusyIndicator").RenderTransform;
            Check(!activeTyping || Find<Ellipse>(typing).All(dot => dot.HasAnimatedProperties), "Typing dots did not run in an active window.");
            Check(!activeTyping || travel.HasAnimatedProperties, "Indeterminate progress did not animate in an active window.");
            report["typing"] = new { activateReturned = activated, hostActive = host.IsActive, animated = Find<Ellipse>(typing).Any(dot => dot.HasAnimatedProperties) };
            UiMotion.Configure(true);
            Check(!UiMotion.Enabled && !scale.HasAnimatedProperties && !entrance.HasAnimatedProperties && !travel.HasAnimatedProperties && Find<Ellipse>(typing).All(dot => !dot.HasAnimatedProperties), "Reduced motion left a running clock.");
            UiMotion.Configure(false);
            progress.IsIndeterminate = false; await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); Check(!travel.HasAnimatedProperties, "Completed progress retained its repeating clock."); progress.IsIndeterminate = true;
            for (int repeat = 0; repeat < 3; repeat++)
            {
                button.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.PreviewMouseLeftButtonDownEvent }); scale = ((TransformGroup)button.RenderTransform).Children.OfType<ScaleTransform>().Single();
                body.Children.Remove(button); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                Check(!scale.HasAnimatedProperties && button.RenderTransform is not TransformGroup, "Unloading retained a press clock or wrapped transform."); body.Children.Add(button); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                Check(((TransformGroup)button.RenderTransform).Children.OfType<ScaleTransform>().Count() == 1, "Reloading accumulated transforms/event state.");
            }
            host.Hide(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); Check(!travel.HasAnimatedProperties && Find<Ellipse>(typing).All(dot => !dot.HasAnimatedProperties), "A hidden window kept repeating animations alive.");
            report["motionLifecycle"] = "reduced-motion immediate stop; three unload/reload cycles; hidden typing/progress stopped; completed progress stopped";
        }
        finally { host.Close(); }

        var chat = main.ViewModel; var messages = (ListBox)main.FindName("MessagesList");
        var streaming = new ChatMessage { Role = "assistant", Content = "途中の発言", IsGenerating = true }; chat.SelectedConversation!.Messages.Add(streaming); UiMotion.Reveal(messages); await Task.Delay(90, token); double before = messages.Opacity;
        streaming.Content += "を更新"; await Task.Delay(25, token); Check(messages.Opacity + .01 >= before, "Streaming output restarted the channel entrance animation."); streaming.IsGenerating = false; chat.SelectedConversation.Messages.Remove(streaming);
        report["streamingEntrance"] = "content updates did not restart the fade";
        main.Width = 820; main.Height = 600; Appearance.Apply("dark", 1.3); main.UpdateLayout();
        for (int i = 0; i < 5; i++) { var extra = new AgentAccount(Guid.NewGuid(), "とても長い参加者の表示名を省略しても詳細を確認できる " + i); await chat.SaveAgentAsync(extra); await chat.InviteMembersAsync(chat.SelectedConversation.Id, [extra.Id]); }
        main.InvokeNavigationShortcut(Key.U, ModifierKeys.Control); var members = (Popup)main.FindName("MembersPopup"); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        Check(members.IsOpen && KeyboardNavigation.GetTabNavigation(members.Child) == KeyboardNavigationMode.Cycle, "Member popup did not provide a keyboard focus scope.");
        var memberScroll = Find<ScrollViewer>(members.Child).First();
        await PrototypePreview.RenderAsync(main, System.IO.Path.Combine(output, "members-minimum-large-text.png"), members);
        report["memberPopup"] = new { memberScroll.ExtentHeight, memberScroll.ViewportHeight, memberScroll.ScrollableHeight };
        Check(memberScroll.ViewportHeight > 100 && (memberScroll.ScrollableHeight > 0 || ((FrameworkElement)memberScroll.Content).ActualHeight <= memberScroll.ViewportHeight + 1), "Many members overflowed the popup without a scrollbar.");
        ((FrameworkElement)members.Child).MaxHeight = 260; await PrototypePreview.RenderAsync(main, System.IO.Path.Combine(output, "members-constrained-large-text.png"), members); Check(memberScroll.ScrollableHeight > 0, "Member popup did not scroll when its available height was constrained.");
        var escape = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(main)!, 0, Key.Escape) { RoutedEvent = Keyboard.PreviewKeyDownEvent }; members.Child.RaiseEvent(escape); Check(escape.Handled && !members.IsOpen, "Esc failed to close the keyboard member popup.");
        Check(FocusManager.GetFocusedElement(main) == main.FindName("MembersButton"), "Closing the member popup did not restore logical focus.");
        main.OpenQuickSwitcher(); var quick = main.OwnedWindows.OfType<QuickSwitcherWindow>().Single(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        Check(FocusManager.GetFocusedElement(quick) == quick.Query, "Quick switcher did not focus its search field."); quick.Close(); Check(FocusManager.GetFocusedElement(main) == main.FindName("InputBox"), "Quick switcher did not return logical focus to the draft.");
        var names = new Dictionary<string, string>(); foreach (string id in new[] { "QuickSwitcherButton", "BackButton", "InboxButton", "MembersButton", "InputBox", "EmojiButton", "HistorySearchBox" }) { var element = (UIElement)main.FindName(id); string name = (UIElementAutomationPeer.CreatePeerForElement(element) ?? throw new InvalidOperationException("Missing automation peer: " + id)).GetName(); Check(!string.IsNullOrWhiteSpace(name), "Missing accessible name: " + id); names[id] = name; }
        report["automationNames"] = names; report["focus"] = "member popup scroll/Tab scope/Esc/logical focus; quick switcher logical focus";
        Appearance.Apply("dark", 1.3, true); await PrototypePreview.RenderAsync(main, System.IO.Path.Combine(output, "experience-high-contrast-simulation.png"));
        Check(Equals(Application.Current.Resources["MentionTextBrush"], SystemColors.HighlightTextBrush) && Equals(Application.Current.Resources["TextBrush"], SystemColors.WindowTextBrush), "High-contrast resources did not apply to conversation controls.");
        var history = (ListBox)main.FindName("HistoryList"); var selected = (ListBoxItem)history.ItemContainerGenerator.ContainerFromItem(chat.SelectedConversation); var channelTile = (Border)selected.Template.FindName("Tile", selected); Check(Equals(channelTile.BorderBrush, SystemColors.HighlightBrush) && Equals(channelTile.Background, SystemColors.WindowBrush), "High-contrast selection lost its outline or used an incompatible text/background pair.");
        report["highContrast"] = "resource simulation, OS setting unchanged"; Appearance.Apply(chat.Settings.Theme, chat.Settings.UiScale); UiMotion.Configure(chat.Settings.ReduceMotion);
        await File.WriteAllTextAsync(System.IO.Path.Combine(output, "audit.json"), JsonSerializer.Serialize(report, AppStore.JsonOptions), token);
    }
}
