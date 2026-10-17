using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Eluna.App.Controls;
using Eluna.App.Models;

namespace Eluna.App.Services;

internal static class DiscordShellVerification
{
    private static void Check(bool value, string message) { if (!value) { throw new InvalidOperationException(message); } }
    private static IEnumerable<T> Find<T>(DependencyObject root) where T : DependencyObject
    { for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) { DependencyObject child = VisualTreeHelper.GetChild(root, i); if (child is T found) { yield return found; } foreach (T nested in Find<T>(child)) { yield return nested; } } }
    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
    private static T Offscreen<T>(T window) where T : Window { window.WindowStartupLocation = WindowStartupLocation.Manual; window.Left = -20000; window.Top = -20000; window.Show(); return window; }
    internal static async Task RunAsync(AppStore store, string output)
    {
        Directory.CreateDirectory(output); MainWindow? main = null; using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3)); CancellationToken token = timeout.Token;
        async Task Until(Func<bool> ready) { while (!ready()) { await Task.Delay(20, token); } }
        async Task Capture(Window window, string name) => await PrototypePreview.RenderAsync(window, Path.Combine(output, name + ".png"));
        try
        {
            UnsavedChanges.Review = _ => false; main = Offscreen(new MainWindow(store)); var chat = main.ViewModel; await Until(() => chat.IsReady || chat.HasError); Check(chat.IsReady, chat.Error);
            var model = new LocalModel("shell-model", "画面に出さないモデル名", "fixture", "local", "fixture", "", 0); chat.Models.Add(model); chat.SelectedModel = model;
            var second = model with { Id = "shell-second", DisplayName = "別アカウントのモデル" }; chat.Models.Add(second);
            var design = new AgentAccount(Guid.NewGuid(), "佐藤", "佐", Specialty: "デザインと使いやすさ", ModelId: model.Id);
            var critic = new AgentAccount(Guid.NewGuid(), "田中 リサ", "田", Specialty: "品質と検証", ModelId: model.Id);
            await chat.SaveAgentAsync(design); await chat.SaveAgentAsync(critic); await chat.SaveProjectAsync(new(Guid.NewGuid(), "Eluna 開発チーム", "", "project-only", false, [], []));
            Conversation room = await chat.CreateRoomAsync("デザイン相談", "channel", [design.Id, critic.Id]);
            Check(((ScrollViewer)main.FindName("WelcomeScroll")).Visibility == Visibility.Visible, "Empty channel introduction was missing.");
            room.Messages.Add(new() { Role = "user", Content = "メッセージの読みやすさと、普段使う操作を中心に考えたいです。", Identity = new(null, "あなた", "あ", Time: DateTimeOffset.Now.AddMinutes(-4)) });
            room.Messages.Add(new() { Role = "assistant", Content = "了解です。左側でチャンネルを選び、ここで会話を続けましょう。\n\nまずは余白と文字の密度を確認してみませんか？", Identity = new(design.Id, design.Name, design.Icon, model.Id, model.DisplayName, DateTimeOffset.Now.AddMinutes(-3)) });
            room.Messages.Add(new() { Role = "assistant", Content = "使う頻度の低い設定は、左下の歯車から開けると良さそうです。", Identity = new(critic.Id, critic.Name, critic.Icon, model.Id, model.DisplayName, DateTimeOffset.Now.AddMinutes(-2)) });
            room.Messages.Add(new() { Role = "user", Content = "👍 その方向で進めましょう。", Identity = new(null, "あなた", "あ", Time: DateTimeOffset.Now.AddMinutes(-1)) });
            Check(((ScrollViewer)main.FindName("WelcomeScroll")).Visibility == Visibility.Collapsed, "The empty introduction overlapped existing messages.");
            foreach (string title in new[] { "一般", "アイデア", "作業メモ", "リリース" }) { await chat.CreateRoomAsync(title, "channel", [design.Id, critic.Id]); }
            chat.SelectedConversation = room; chat.Input = ""; main.Width = 1280; main.Height = 900; await Capture(main, "channel-dark");
            Check(!Find<ComboBox>(main).Any() && !Find<TextBlock>(main).Any(t => t.Text.Contains(model.DisplayName, StringComparison.Ordinal)), "Model/AI selectors or model metadata leaked into the shell.");
            Check(main.FindName("ModeButton") is null && main.FindName("WorkModeSelector") is null && main.FindName("ModelSelector") is null, "Removed AI controls remain in the main window.");
            Check(((Border)main.FindName("MembersPane")).IsVisible && chat.RoomMembers.Count == 3, "Wide member list was missing.");
            main.Width = 820; main.Height = 600; await Capture(main, "channel-minimum"); Check(((ListBox)main.FindName("MessagesList")).ActualHeight >= 300 && !((Border)main.FindName("MembersPane")).IsVisible, "Compact shell lost message space or kept the member column.");
            Click((Button)main.FindName("MembersButton")); Check(((Popup)main.FindName("MembersPopup")).IsOpen, "Member list was inaccessible at minimum width."); ((Popup)main.FindName("MembersPopup")).IsOpen = false;
            TextBox input = (TextBox)main.FindName("InputBox"); chat.Input = "質問です。"; input.CaretIndex = 0;
            Button mention = Find<Button>(main).Single(b => AutomationProperties.GetName(b) == "メンバーにメンション"); Click(mention);
            MenuItem target = mention.ContextMenu.Items.Cast<MenuItem>().First(m => m.Header is string name && name.Contains(critic.Name, StringComparison.Ordinal)); target.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); mention.ContextMenu.IsOpen = false;
            Check(chat.Input == "@{田中 リサ} 質問です。", "Mention selection overwrote the draft or failed to quote a spaced name.");
            Button emoji = Find<Button>(main).Single(b => AutomationProperties.GetName(b) == "絵文字を選ぶ"); input.CaretIndex = input.Text.Length; Click(emoji); var emojiPicker = main.OpenEmojiPanel ?? throw new InvalidOperationException("Emoji picker did not open."); emojiPicker.Search.Text = "いいね"; emojiPicker.ChooseSelected(); Check(chat.Input.EndsWith("👍", StringComparison.Ordinal), "Emoji insertion failed.");
            chat.Input = "保存する下書き"; Conversation other = chat.Conversations.First(c => c.Id != room.Id && c.ProjectId == room.ProjectId); chat.SelectedConversation = other; chat.SelectedConversation = room; Check(chat.Input == "保存する下書き", "Channel navigation lost the draft.");
            var settings = Offscreen(new SettingsWindow(chat, 7) { Owner = main });
            ((ComboBox)settings.FindName("WorkModeBox")).SelectedItem = WorkModes.Find("offline"); ((ComboBox)settings.FindName("ThinkingBox")).SelectedItem = InferenceModes.Find("thorough");
            Click((Button)settings.FindName("SaveButton")); await Until(() => ((Button)settings.FindName("SaveButton")).IsEnabled);
            Check(room.Room!.WorkMode == "offline" && room.Room.InferenceMode == "thorough" && (await store.LoadAsync()).Conversations.Single(c => c.Id == room.Id).Room?.WorkMode == "offline", "Moved AI controls did not save to their channel.");
            ((ComboBox)settings.FindName("WorkModeBox")).SelectedItem = WorkModes.Find("chat"); chat.SelectedConversation = other; string? previous = other.Room?.WorkMode;
            Click((Button)settings.FindName("SaveButton")); await Until(() => ((Button)settings.FindName("SaveButton")).IsEnabled); Check(other.Room?.WorkMode == previous && room.Room.WorkMode == "offline" && ((TextBlock)settings.FindName("SaveStateText")).Text.Contains("対象のチャンネル", StringComparison.Ordinal), "Settings wrote a different channel after navigation.");
            settings.Close(); chat.SelectedConversation = room;
            settings = Offscreen(new SettingsWindow(chat, 5) { Owner = main }); settings.ShowAccounts(design.Id);
            AgentsWindow accounts = settings.OwnedWindows.OfType<AgentsWindow>().Single(); ComboBox models = Find<ComboBox>(accounts).Single(c => AutomationProperties.GetName(c) == "使用モデル"); models.SelectedItem = models.Items.Cast<DebateModelChoice>().Single(c => c.Id == second.Id); Click(accounts.SaveButton); await Until(() => accounts.SaveButton.IsEnabled);
            Check(chat.Agents.Single(a => a.Id == design.Id).ModelId == second.Id && chat.SelectedModel?.Id == second.Id, "Account model selection no longer controlled inference after removing the header selector.");
            accounts.NameBox.Text = "未保存のプロフィール"; UnsavedChanges.Review = _ => null; settings.Close(); await Task.Delay(80, token); Check(settings.IsVisible && accounts.IsVisible, "Closing settings discarded its account editor's unsaved changes."); UnsavedChanges.Review = _ => false; accounts.Close();
            foreach (string theme in new[] { "dark", "light" })
            {
                Appearance.Apply(theme, 1); main.Width = 1280; main.Height = 900; await Capture(main, "channel-" + theme);
                foreach (int tab in new[] { 0, 4, 5, 6, 7, 8, 9 }) { settings.SelectTab(tab); await Capture(settings, "settings-" + tab + "-" + theme); }
                settings.Width = settings.MinWidth; settings.Height = settings.MinHeight; Appearance.Apply(theme, 1.3); settings.SelectTab(7); await Capture(settings, "settings-minimum-" + theme);
                Check(((Button)settings.FindName("SaveButton")).IsVisible && ((Button)settings.FindName("SaveButton")).ActualWidth > 50, "Settings lost the Save action at minimum size.");
                settings.Width = 1000; settings.Height = 780; main.Width = 820; main.Height = 600; await Capture(main, "channel-large-text-" + theme);
            }
            settings.Close(); Appearance.Apply("dark"); chat.Input = "";
            var creation = Offscreen(new RoomWindow(chat, basicOnly: true) { Owner = main }); await Capture(creation, "create-channel"); Check(!Find<CheckBox>(creation).Any(c => c.IsVisible), "AI memory settings were exposed in the basic creation flow."); creation.Close();
            Conversation dm = await chat.CreateRoomAsync("佐藤", "dm", [design.Id]); chat.SelectedConversation = dm; main.Width = 1280; main.Height = 900; await Capture(main, "dm");
            await File.WriteAllTextAsync(Path.Combine(output, "verification.json"), JsonSerializer.Serialize(new { shell = true, modelSelectorsRemoved = true, accountModel = true, settingsChannelGuard = true, mentions = true, emoji = true, drafts = true, welcome = true, themes = "dark/light", min = "820x600 / settings660x500", text = "130%", settingsCategories = 10 }, AppStore.JsonOptions), token);
            await File.WriteAllTextAsync(Path.Combine(output, "result.txt"), "Discord shell: no AI controls on main, accounts/model assignment, settings scopes/persistence, members, mentions/emoji/drafts, empty state, themes/minimum/130%: OK", token);
        }
        catch (Exception ex) { Environment.ExitCode = 1; await File.WriteAllTextAsync(Path.Combine(output, "result.txt"), ex.ToString()); }
        finally { UnsavedChanges.Review = _ => false; if (main is not null) { foreach (Window child in main.OwnedWindows.Cast<Window>().ToArray()) { child.Close(); } await main.ViewModel.ShutdownAsync(); main.Close(); } Application.Current.Shutdown(); }
    }
}
