using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Eluna.App.Models;

namespace Eluna.App.Services;

internal static class MessageExperienceVerification
{
    private static void Check(bool ok, string message) { if (!ok) { throw new InvalidOperationException(message); } }
    private static IEnumerable<T> Find<T>(DependencyObject root) where T : DependencyObject
    { for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) { DependencyObject child = VisualTreeHelper.GetChild(root, i); if (child is T item) { yield return item; } foreach (T nested in Find<T>(child)) { yield return nested; } } }
    internal static async Task RunAsync(AppStore store, string output)
    {
        Directory.CreateDirectory(output); MainWindow? main = null; using var stop = new CancellationTokenSource(TimeSpan.FromMinutes(5)); CancellationToken token = stop.Token;
        async Task Until(Func<bool> ready) { while (!ready()) { await Task.Delay(25, token); } }
        try
        {
            UnsavedChanges.Review = _ => false; main = new MainWindow(store) { ShowActivated = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = -20000 }; main.Show(); var chat = main.ViewModel; await Until(() => chat.IsReady || chat.HasError); Check(chat.IsReady, chat.Error);
            await chat.SaveSelfAsync(new("カズト", "☾")); var peer = new AgentAccount(Guid.NewGuid(), "星乃", "星", Specialty: "一緒に夜空を眺める読書仲間"); await chat.SaveAgentAsync(peer);
            Conversation lounge = await chat.CreateRoomAsync("夜更かしラウンジ", "dm", [peer.Id]); var reply = new ChatMessage { Role = "assistant", IsGenerating = true, Identity = new(peer.Id, peer.Name, peer.Icon, Time: DateTimeOffset.Now), Content = "@カズト 星の本を一緒に読もう。" }; lounge.Messages.Add(reply);
            Conversation other = await chat.CreateRoomAsync("今日の予定", "group", [peer.Id]); chat.Input = "別の会話の下書き"; Check(!lounge.HasUnread, "Incomplete reply was already unread."); reply.IsGenerating = false; reply.Content += " おすすめを探してみたよ。";
            Check(lounge.UnreadCount == 1 && lounge.HasMentions && chat.InboxCount == 1, "Completion or named mention was missing or duplicated.");
            chat.HistorySearch = "今日"; await chat.HistorySearchFinished; chat.Input += "！"; chat.HistorySearch = "夜更かし"; await chat.HistorySearchFinished; chat.Input = "別の会話の下書き"; chat.HistorySearch = ""; Check(chat.SelectedConversation == other && chat.Input == "別の会話の下書き", "Autosaving a draft interrupted search or switched the channel.");
            var inbox = new MessageCenterWindow(chat, "mentions") { Owner = main, ShowActivated = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = -20000 }; inbox.Show(); await inbox.SearchFinished; Check(inbox.Results.Items.Count == 1, "Mention inbox could not resolve the finished reply.");
            foreach (string theme in new[] { "dark", "light" }) { Appearance.Apply(theme, 1.3); inbox.Width = inbox.MinWidth; inbox.Height = inbox.MinHeight; await PrototypePreview.RenderAsync(inbox, Path.Combine(output, "inbox-minimum-" + theme + ".png")); Check(inbox.Results.ActualHeight >= 80, "Inbox controls left no usable results area."); }
            Check(inbox.OpenSelected() && chat.SelectedConversation == lounge && lounge.UnreadCount == 0 && other.Draft == "別の会話の下書き" && reply.JumpHighlighted, "Inbox jump lost the message/draft or did not mark the selected receipt read.");
            await chat.ToggleMessagePinAsync(lounge.Id, lounge.ActiveBranch.Id, reply.Id); await chat.ToggleReactionAsync(lounge.Id, lounge.ActiveBranch.Id, reply.Id, "👍");
            await PrototypePreview.RenderAsync(main, Path.Combine(output, "pinned-reaction.png")); Check(reply.PinnedMessage && reply.Reactions.Single().Label == "👍 1 ✓", "Pinned/reaction state did not appear in the actual row.");
            Button reaction = Find<Button>(main).Single(b => b.DataContext is ReactionBadge); reaction.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); await Until(() => !reply.HasReactions); await chat.ToggleReactionAsync(lounge.Id, lounge.ActiveBranch.Id, reply.Id, "❤️");
            ConversationBranch original = lounge.ActiveBranch; ConversationBranch fork = lounge.Fork(lounge.Messages.Count, "別の読み方"); chat.SelectedBranch = original; chat.SelectedConversation = other;
            var pins = new MessageCenterWindow(chat, "pins", all: true) { Owner = main, ShowActivated = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = -20000 }; pins.Show(); await pins.SearchFinished; Check(pins.Results.Items.Count == 1, "Pin was duplicated by a fork."); Check(pins.OpenSelected() && chat.SelectedConversation == lounge && chat.SelectedBranch?.Id == original.Id && other.Draft.Length > 0, "Pin jump opened the wrong branch.");
            var search = new MessageCenterWindow(chat, "search", "from:星乃 in:夜更かし", true) { Owner = main, ShowActivated = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = -20000 }; search.Show(); await search.SearchFinished; Check(search.Results.Items.Count == 2, "Search omitted a branch or ignored speaker/place terms.");
            foreach (string theme in new[] { "dark", "light" }) { Appearance.Apply(theme, 1.3); search.Width = search.MinWidth; search.Height = search.MinHeight; await PrototypePreview.RenderAsync(search, Path.Combine(output, "search-minimum-" + theme + ".png")); }
            search.Query.Text = "存在しない"; await search.SearchFinished; Check(search.Results.Items.Count == 0 && !search.OpenSelected(), "Empty results navigated unexpectedly."); search.Query.Text = "星"; search.Close(); await search.SearchFinished;
            chat.MarkUnread(lounge.Id, original.Id, reply.Id); Check(lounge.UnreadCount == 1, "Manual unread did not persist while the row was visible."); chat.UpdateReadingState(true); Check(lounge.HasUnread, "Automatic read immediately erased manual unread."); chat.MarkRead(lounge.Id); Check(!lounge.HasUnread, "Explicit mark read failed.");
            chat.UpdateReadingState(true); var foreground = new ChatMessage { Role = "assistant", Content = "読んでいる間の返事", Identity = new(peer.Id, peer.Name, peer.Icon, Time: DateTimeOffset.Now) }; lounge.Messages.Add(foreground); Check(!lounge.HasUnread, "Foreground reading created an unread badge."); chat.UpdateReadingState(false);
            chat.SelectedConversation = other; var background = new ChatMessage { Role = "assistant", Content = "あとで話そう。", Identity = new(peer.Id, peer.Name, peer.Icon, Time: DateTimeOffset.Now) }; lounge.Messages.Add(background); Check(lounge.UnreadCount == 1, "Background arrival did not become unread."); await chat.SetMutedAsync(lounge.Id, true); Check(chat.InboxCount == 0 && lounge.HasUnread, "Mute discarded receipts or retained the inbox count."); await chat.SetMutedAsync(lounge.Id, false);
            foreach (string theme in new[] { "dark", "light" }) { Appearance.Apply(theme, 1.3); main.Width = 820; main.Height = 600; await PrototypePreview.RenderAsync(main, Path.Combine(output, "unread-navigation-minimum-" + theme + ".png")); }
            await chat.PersistSafelyAsync(); AppState saved = await store.LoadAsync(); AppStore.Validate(saved); SavedConversation persisted = saved.Conversations.Single(c => c.Id == lounge.Id); Check(persisted.Experience is { Unread.Count: 1, Pins.Count: 1, Reactions.Count: 1 }, "Unread/pins/reactions were lost after save.");
            chat.MarkRead(lounge.Id); chat.SelectedConversation = lounge; var at = new DateTimeOffset(DateTime.Today.AddHours(21));
            var firstRow = new ChatMessage { Role = "assistant", Content = "今日は読みたい本が二冊あるんだ。", Identity = new(peer.Id, peer.Name, peer.Icon, Time: at) };
            var continuation = new ChatMessage { Role = "assistant", Content = "一冊目は星座の物語。もう一冊は月を観測した人たちの記録。", Identity = new(peer.Id, peer.Name, peer.Icon, Time: at.AddMinutes(1)) };
            lounge.Messages.Add(firstRow); lounge.Messages.Add(continuation); lounge.Messages.Add(new() { Role = "user", Content = "先に星座の話を聞きたいな。", Identity = new(null, chat.Self.Name, chat.Self.Icon, Time: at.AddMinutes(2)) });
            lounge.Messages.Add(new() { Role = "assistant", Content = "昨日の続きから話そう。", Identity = new(peer.Id, peer.Name, peer.Icon, Time: at.AddDays(1)) });
            await Until(() => !reply.JumpHighlighted); Appearance.Apply("dark", 1); main.Width = 1280; main.Height = 900; await PrototypePreview.RenderAsync(main, Path.Combine(output, "grouped-conversation.png"));
            Border full = Find<Border>(main).Single(b => b.Name == "MessageBorder" && ReferenceEquals(b.DataContext, firstRow)); Border compact = Find<Border>(main).Single(b => b.Name == "MessageBorder" && ReferenceEquals(b.DataContext, continuation));
            Check(continuation.Compact && compact.ActualHeight < full.ActualHeight && chat.Timeline.Last().HasDateSeparator && Find<FrameworkElement>(compact).Any(x => x.Name == "MessageActions" && x.Visibility == Visibility.Visible), "Compact rows lost their actions or did not reduce occupied height.");
            await File.WriteAllTextAsync(Path.Combine(output, "result.txt"), "Message experience: streaming completion/mentions/inbox/jump/read/manual/mute, exact branch/pins/search/cancel, reaction chip toggle, grouped rows/date boundaries/density, preserved drafts, themes/minimum/130%, persistence: OK", token);
        }
        catch (Exception ex) { Environment.ExitCode = 1; await File.WriteAllTextAsync(Path.Combine(output, "result.txt"), ex.ToString()); }
        finally { if (main is not null) { foreach (Window child in main.OwnedWindows.Cast<Window>().ToArray()) { child.Close(); } await main.ViewModel.ShutdownAsync(); main.Close(); } Application.Current.Shutdown(); }
    }
}
