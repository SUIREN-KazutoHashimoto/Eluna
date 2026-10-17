using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Eluna.App.Models;

namespace Eluna.App.Services;

internal static class DiscordReferenceVerification
{
    private static void Check(bool value, string message) { if (!value) { throw new InvalidOperationException(message); } }
    private static IEnumerable<T> Find<T>(DependencyObject root) where T : DependencyObject
    { for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) { DependencyObject child = VisualTreeHelper.GetChild(root, i); if (child is T item) { yield return item; } foreach (T nested in Find<T>(child)) { yield return nested; } } }
    internal static async Task RunAsync(MainWindow main, string output, CancellationToken token)
    {
        var chat = main.ViewModel; var report = new Dictionary<string, object>();
        var peer = new AgentAccount(Guid.NewGuid(), "星乃", "星", Specialty: "星と読書の話が好き"); var friend = new AgentAccount(Guid.NewGuid(), "ミオ", "澪", Specialty: "ものづくりと日々の雑談"); await chat.SaveAgentAsync(peer); await chat.SaveAgentAsync(friend);
        await chat.SaveProjectAsync(new(Guid.NewGuid(), "夜更かしラウンジ", "", "project-only", false, [], [])); Conversation room = await chat.CreateRoomAsync("雑談", "channel", [peer.Id, friend.Id]);
        foreach (string name in new[] { "アイデア", "作業メモ", "読んだもの" }) { await chat.CreateRoomAsync(name, "channel", [peer.Id, friend.Id]); }
        await chat.CreateRoomAsync("少し話そう", "voice", [peer.Id, friend.Id]); chat.SelectedConversation = room;
        DateTimeOffset time = new(2026, 10, 17, 13, 20, 0, TimeSpan.FromHours(9));
        var first = new ChatMessage { Role = "assistant", Content = "宇宙船の内装って、全部白だと落ち着かなさそうじゃない？", Identity = new(peer.Id, peer.Name, peer.Icon, Time: time) }; room.Messages.Add(first);
        var reply = new ChatMessage { Role = "assistant", Content = "木目がほしい。小さい本棚も。", Identity = new(friend.Id, friend.Name, friend.Icon, Time: time.AddMinutes(1), ReplyTo: first.Id) }; room.Messages.Add(reply);
        room.Messages.Add(new() { Role = "assistant", Content = "窓際に植物を置こう。", Identity = new(friend.Id, friend.Name, friend.Icon, Time: time.AddMinutes(2)) });
        room.Messages.Add(new() { Role = "user", Content = "読書スペースにしよう。", Identity = new(null, chat.Self.Name, chat.Self.Icon, Time: time.AddMinutes(3)) });
        room.Messages.Add(new() { Role = "assistant", Content = "いいね。照明も暖かい色にしたいな。", Identity = new(peer.Id, peer.Name, peer.Icon, Time: time.AddMinutes(4)) });
        var input = (TextBox)main.FindName("InputBox"); var placeholder = (TextBlock)main.FindName("InputPlaceholder"); var composer = (FrameworkElement)main.FindName("ComposerArea"); var user = (FrameworkElement)main.FindName("UserPanel");
        chat.Input = ""; Check(placeholder.Visibility == Visibility.Visible, "Empty input lost its placeholder."); chat.Input = "入力している文章"; Check(placeholder.Visibility == Visibility.Collapsed, "Placeholder overlapped native text input."); chat.Input = "";
        var composition = new TextComposition(InputManager.Current, input, "あ"); input.RaiseEvent(new TextCompositionEventArgs(Keyboard.PrimaryDevice, composition) { RoutedEvent = TextCompositionManager.PreviewTextInputStartEvent }); Check(placeholder.Visibility == Visibility.Collapsed, "Placeholder overlapped an IME composition before committing text.");
        input.RaiseEvent(new TextCompositionEventArgs(Keyboard.PrimaryDevice, composition) { RoutedEvent = TextCompositionManager.PreviewTextInputEvent }); chat.Input = "";
        foreach (string theme in new[] { "dark", "light" })
        {
            Appearance.Apply(theme, 1); main.Width = 1280; main.Height = 900; await PrototypePreview.RenderAsync(main, Path.Combine(output, "reference-desktop-" + theme + ".png"));
            Check(((System.Windows.Controls.ColumnDefinition)main.FindName("ChannelColumn")).ActualWidth == 304 && ((System.Windows.Controls.ColumnDefinition)main.FindName("MembersColumn")).ActualWidth == 264, "Desktop columns did not match the planned channel/member proportions.");
            Button quoted = Find<Button>(main).Single(b => b.Name == "ReplyPreviewButton" && ReferenceEquals(b.DataContext, reply)); Button avatar = Find<Button>(main).Single(b => b.Name == "AvatarButton" && ReferenceEquals(b.DataContext, reply)); Check(quoted.TranslatePoint(new Point(), main).Y < avatar.TranslatePoint(new Point(), main).Y, "The reply quotation was displayed below its speaker.");
            Check(user.ActualWidth > 350 && composer.TranslatePoint(new Point(0, composer.ActualHeight), main).Y <= main.ActualHeight && user.TranslatePoint(new Point(0, user.ActualHeight), main).Y <= main.ActualHeight, "The user panel/composer escaped the desktop window.");
            Appearance.Apply(theme, 1.3); main.Width = 820; main.Height = 600; await PrototypePreview.RenderAsync(main, Path.Combine(output, "reference-minimum-" + theme + ".png")); Check(input.ActualWidth > 130 && placeholder.FontSize == input.FontSize && ((ListBox)main.FindName("MessagesList")).ActualHeight > 250, "Minimum-size input or message space was crowded out.");
        }
        Appearance.Apply("dark", 1); main.Width = 3440; main.Height = 1392; await PrototypePreview.RenderAsync(main, Path.Combine(output, "reference-ultrawide.png"));
        report["layout"] = new { channels = 304, members = 264, avatar = 40, newDefaultBodyText = new AppSettings().ChatFontSize, ultrawide = "3440x1392", minimum = "820x600/130%", placeholder = "native text and IME start/commit", reply = "above author" };
        main.WindowState = WindowState.Maximized; await Task.Delay(250, token); main.UpdateLayout();
        WindowDisplay display = WindowNative.DisplayFor(new WindowInteropHelper(main).Handle)!; Point bottom = input.PointToScreen(new Point(0, input.ActualHeight)); Point userBottom = user.PointToScreen(new Point(0, user.ActualHeight));
        Check(bottom.Y <= display.WorkArea.Bottom && userBottom.Y <= display.WorkArea.Bottom && userBottom.Y > display.WorkArea.Top, "Maximized input or local profile was hidden underneath the Windows taskbar.");
        await PrototypePreview.RenderAsync(main, Path.Combine(output, "reference-native-maximized.png")); report["nativeMaximized"] = new { display.Device, display.Dpi, display.WorkArea, inputBottom = bottom.Y, userBottom = userBottom.Y, window = WindowNative.ScreenRectangle(main) };
        main.WindowState = WindowState.Normal; main.Left = -20000; main.Top = -20000; main.Width = 1280; main.Height = 900;
        await File.WriteAllTextAsync(Path.Combine(output, "reference-layout.json"), JsonSerializer.Serialize(report, AppStore.JsonOptions), token);
    }
}
