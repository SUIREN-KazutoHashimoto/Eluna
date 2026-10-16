using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Eluna.App.Controls;
using Eluna.App.Models;

namespace Eluna.App.Services;

internal static class SocialChatVerification
{
    private static void Check(bool ok, string message) { if (!ok) { throw new InvalidOperationException(message); } }
    private static IEnumerable<T> Find<T>(DependencyObject root) where T : DependencyObject
    { for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) { DependencyObject child = VisualTreeHelper.GetChild(root, i); if (child is T item) { yield return item; } foreach (T nested in Find<T>(child)) { yield return nested; } } }
    private static IEnumerable<Hyperlink> Links(InlineCollection inlines)
    { foreach (Inline inline in inlines) { if (inline is Hyperlink link) { yield return link; } if (inline is Span span) { foreach (Hyperlink nested in Links(span.Inlines)) { yield return nested; } } } }
    internal static async Task RunAsync(AppStore store, string output, string? metadata)
    {
        Directory.CreateDirectory(output); MainWindow? main = null; using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(8)); CancellationToken token = deadline.Token;
        async Task Until(Func<bool> ready) { while (!ready()) { await Task.Delay(30, token); } }
        async Task Progress(string stage) => await File.WriteAllTextAsync(Path.Combine(output, "progress.json"), JsonSerializer.Serialize(new { stage }), token);
        try
        {
            UnsavedChanges.Review = _ => false;
            LocalModel model = metadata is null ? new("social-fixture", "画面確認モデル", "fixture", "local", "fixture", "", 0) : JsonSerializer.Deserialize<LocalModel>(await File.ReadAllTextAsync(metadata, token), AppStore.JsonOptions)!;
            model = model with { Preferences = new(ContextSize: 8192, Temperature: .15, MaxOutputTokens: 384, DefaultMode: "fast") };
            if (metadata is not null) { string folder = Path.Combine(store.ModelsDirectory, model.Id); Directory.CreateDirectory(folder); await File.WriteAllTextAsync(Path.Combine(folder, "model.json"), JsonSerializer.Serialize(model, AppStore.JsonOptions), token); }
            main = new MainWindow(store) { WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = -20000 }; main.Show(); var chat = main.ViewModel;
            await Until(() => chat.IsReady || chat.HasError); Check(chat.IsReady, chat.Error); if (metadata is null) { chat.Models.Add(model); }
            chat.SelectedModel = model;
            await chat.ApplySettingsAsync(chat.Settings with { WorkMode = "offline", InferenceMode = "fast", ContextSize = 8192, Compute = chat.ComputeChoices.Any(c => c.Id == "Vulkan0") ? "Vulkan0" : "cpu" });
            await chat.SaveSelfAsync(new("カズト", "月", "読書と天文学が好き。一緒に考えを出し合う。"));
            var selfProfile = new SelfProfileWindow(chat) { Owner = main, WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = -20000 }; selfProfile.Show(); selfProfile.IconBox.Text = "☾";
            Find<Button>(selfProfile).Single(b => b.Content is "プロフィールを保存").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent)); await Until(() => chat.Self.Icon == "☾");
            await PrototypePreview.RenderAsync(selfProfile, Path.Combine(output, "self-profile.png")); selfProfile.Close();
            var mio = new AgentAccount(Guid.NewGuid(), "設計 ミオ", "星", "落ち着いて仲間と対等に話す。", ModelId: model.Id);
            var jin = new AgentAccount(Guid.NewGuid(), "ジン", "灯", ModelId: model.Id); var rea = new AgentAccount(Guid.NewGuid(), "レア", "花", ModelId: model.Id);
            await chat.SaveAgentAsync(mio); await chat.SaveAgentAsync(jin); await chat.SaveAgentAsync(rea);
            var nova = new AgentAccount(Guid.NewGuid(), "Nova", "N", "仲間の名前を使い、対等に短く会話する。", ModelId: model.Id, Connections: [mio.Id, jin.Id, rea.Id], Autonomy: new(InviteConnections: true)); await chat.SaveAgentAsync(nova);
            Conversation room = await chat.CreateRoomAsync("みんなのラウンジ", "group", [nova.Id]); await chat.SetChannelPermissionsAsync(room.Id, [new(nova.Id, true, true, true)]);
            Check(chat.RoomMembers.Count == 2 && chat.RoomMembers.Any(a => a.IsLocal && a.Name == "カズト"), "Local member was absent from member list.");
            var invite = new InviteMembersWindow(chat, room) { Owner = main, WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = -20000 }; invite.Show(); invite.SearchBox.Text = "ミオ"; invite.Candidates.SelectedItem = mio;
            Conversation separate = await chat.CreateRoomAsync("別の会話", "dm", [jin.Id]); Check(await invite.InviteAsync(), "UI invitation failed.");
            Check(room.Room!.Members.Contains(mio.Id) && !separate.Room!.Members.Contains(mio.Id), "Invitation changed the currently viewed room instead of the captured target.");
            foreach (string theme in new[] { "dark", "light" }) { Appearance.Apply(theme, 1.3); invite.Width = invite.MinWidth; invite.Height = invite.MinHeight; await PrototypePreview.RenderAsync(invite, Path.Combine(output, "invite-minimum-" + theme + ".png")); }
            invite.Close(); chat.SelectedConversation = room; Appearance.Apply("dark", 1);
            var input = (TextBox)main.FindName("InputBox"); input.Text = "この案は @ジ と話そう"; input.CaretIndex = "この案は @ジ".Length; main.UpdateMentions(false);
            Check(main.MentionPopup.IsOpen && main.MentionList.Items.OfType<MainWindow.MemberSuggestion>().Any(s => s.Account?.Id == jin.Id && s.Detail.Contains("招待", StringComparison.Ordinal)), "Typed mentions did not offer invitations.");
            await PrototypePreview.RenderAsync(main, Path.Combine(output, "mention-completion.png"));
            await main.AcceptMentionAsync(main.MentionList.Items.OfType<MainWindow.MemberSuggestion>().Single(s => s.Account?.Id == jin.Id));
            Check(room.Room.Members.Contains(jin.Id) && input.Text == "この案は @ジン  と話そう", "Completion did not preserve the surrounding draft or invite the member.");
            await PrototypePreview.RenderAsync(main, Path.Combine(output, "draft-mention-highlight.png"));
            Check(AdornerLayer.GetAdornerLayer(input)?.GetAdorners(input)?.OfType<MentionAdorner>().Any(a => a.HighlightCount > 0) == true, "Completed mentions in the composer were not highlighted.");
            input.Text = "続き @No"; input.CaretIndex = input.Text.Length; main.UpdateMentions(false); main.MentionList.SelectedItem = main.MentionList.Items.OfType<MainWindow.MemberSuggestion>().Single(s => s.Account?.Id == nova.Id);
            var tab = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(main)!, 0, Key.Tab) { RoutedEvent = Keyboard.PreviewKeyDownEvent }; input.RaiseEvent(tab); await Task.Delay(30, token); Check(tab.Handled && input.Text == "続き @Nova ", "Tab did not complete a typed mention.");
            input.Text = "@No"; input.CaretIndex = input.Text.Length; main.UpdateMentions(false); var escape = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(main)!, 0, Key.Escape) { RoutedEvent = Keyboard.PreviewKeyDownEvent }; input.RaiseEvent(escape); Check(escape.Handled && !main.MentionPopup.IsOpen && input.Text == "@No", "Esc did not dismiss mention completion without discarding text.");
            IReadOnlyList<AgentAccount> targets = chat.TargetAgents(room, "相談は @Nova と @{設計 ミオ} にお願い。"); Check(targets.Select(a => a.Id).ToHashSet().SetEquals([nova.Id, mio.Id]), "Multiple mentions in prose did not route to the named peers.");
            var message = new ChatMessage { Role = "user", Content = "@Nova と @{設計 ミオ}、この **案** を一緒に考えよう。", Identity = new(null, chat.Self.Name, chat.Self.Icon, Time: DateTimeOffset.UtcNow) }; room.Messages.Add(message);
            main.Width = 1280; main.Height = 900; await PrototypePreview.RenderAsync(main, Path.Combine(output, "peer-chat.png"));
            MarkdownView? body = Find<MarkdownView>(main).FirstOrDefault(v => ReferenceEquals(v.DataContext, message)); Check(body is not null && body.PlainText, "Local messages did not use the shared mention renderer.");
            await Until(() => Find<RichTextBox>(body!).Any(r => r.Document.Blocks.OfType<Paragraph>().SelectMany(p => Links(p.Inlines)).Any()));
            RichTextBox reader = Find<RichTextBox>(body!).First(); Hyperlink mention = reader.Document.Blocks.OfType<Paragraph>().SelectMany(p => Links(p.Inlines)).First();
            Check(mention.Background is not null && mention.Foreground.ToString() != ((SolidColorBrush)Application.Current.Resources["TextBrush"]).ToString(), "Mentions were indistinguishable from body text.");
            Check(new TextRange(reader.Document.ContentStart, reader.Document.ContentEnd).Text.Contains("**案**", StringComparison.Ordinal), "Local text was interpreted as Markdown and changed its content.");
            mention.RaiseEvent(new RoutedEventArgs(Hyperlink.ClickEvent)); Check(main.OwnedWindows.OfType<AgentProfileWindow>().Any(w => w.IsVisible), "Mention click did not open a profile."); foreach (AgentProfileWindow profile in main.OwnedWindows.OfType<AgentProfileWindow>().ToArray()) { profile.Close(); }
            async Task<ChatMessage> Send(string text)
            {
                chat.SelectedConversation = room; chat.SelectedRecipient = chat.Recipients.Single(r => r.Id == nova.Id); int count = chat.Inferences.Items.Count; chat.Input = text; chat.SendCommand.Execute(null); await Until(() => chat.Inferences.Items.Count > count);
                InferenceJob job = chat.Inferences.Items.Last(); await job.Finished.WaitAsync(token); Check(job.State == "complete", "Chat operation failed: " + job.Detail); return room.Messages.Single(m => m.Id == job.Request.AnswerId);
            }
            ChatMessage result = await Send("@Nova レアをこのチャンネルに招待して"); Check(room.Room.Members.Contains(rea.Id) && result.Content.Contains("招待しました", StringComparison.Ordinal) && result.Identity?.AgentId == nova.Id, "Explicit AI invitation did not execute.");
            int members = room.Room.Members.Count; await Send("@Nova レアをこのチャンネルに招待して"); Check(room.Room.Members.Count == members, "Repeated invitation duplicated membership.");
            var denied = new AgentAccount(Guid.NewGuid(), "ユウ", ModelId: model.Id); await chat.SaveAgentAsync(denied);
            await chat.SetChannelPermissionsAsync(room.Id, [new(nova.Id, true, true)]); result = await Send("@Nova ユウを招待して"); Check(!room.Room.Members.Contains(denied.Id) && result.Content.Contains("招待権限", StringComparison.Ordinal), "AI invitation bypassed ACL or hid the reason.");
            await Progress("invitations-mentions-peer-ui-complete");
            foreach (string theme in new[] { "dark", "light" }) { Appearance.Apply(theme, 1.3); main.Width = 820; main.Height = 600; await PrototypePreview.RenderAsync(main, Path.Combine(output, "main-minimum-" + theme + ".png")); Check(((ListBox)main.FindName("MessagesList")).ActualHeight > 160, "New controls covered the message history."); }
            Appearance.Apply("dark", 1);
            if (metadata is not null)
            {
                var manager = new AgentsWindow(chat) { Owner = main, WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = -20000 }; manager.CharacterPanel.Brief.Text = "落ち着いた天文学好きで少し皮肉屋。短い名前で、対等な友人として話す。"; manager.BeginCreate(); manager.Show();
                await Until(() => manager.CharacterPanel.IsBusy); chat.SelectedConversation = separate; chat.Input = "生成中に別の会話で書いた下書き";
                await Until(() => !manager.CharacterPanel.IsBusy); Check(manager.NameBox.Text != "新しいアカウント" && manager.InstructionBox.Text.Length > 10, "New account did not automatically generate its character.");
                Check(chat.SelectedConversation == separate && chat.Input == "生成中に別の会話で書いた下書き", "Character generation hijacked navigation or the other draft.");
                foreach (string theme in new[] { "dark", "light" })
                {
                    Appearance.Apply(theme, 1.3); manager.Width = manager.MinWidth; manager.Height = manager.MinHeight;
                    await PrototypePreview.RenderAsync(manager, Path.Combine(output, "character-minimum-" + theme + ".png"));
                    string foreground = ((SolidColorBrush)Application.Current.Resources["TextBrush"]).ToString(), muted = ((SolidColorBrush)Application.Current.Resources["MutedBrush"]).ToString();
                    Check(Find<Expander>(manager).Single(e => ReferenceEquals(e.Content, manager.CharacterPanel)).Foreground.ToString() == foreground && Find<TextBlock>(manager.CharacterPanel).Where(t => t.Text.Length > 0).All(t => t.Foreground.ToString() == foreground || t.Foreground.ToString() == muted), "Character generation labels or status lost their theme contrast.");
                }
                Appearance.Apply("dark", 1); manager.Width = 1000; manager.Height = 780;
                await PrototypePreview.RenderAsync(manager, Path.Combine(output, "generated-character.png")); manager.SaveButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent)); await Until(() => chat.Agents.Any(a => a.Name == manager.NameBox.Text));
                AgentAccount generated = chat.Agents.Single(a => a.Name == manager.NameBox.Text); Check(generated.ModelId == model.Id && generated.Personality is { Preset: "roleplay", Role.Length: > 0 } && generated.Autonomy?.Automatic != true, "Character generation changed capabilities or lost roleplay/model assignment.");
                string previous = manager.NameBox.Text; Task<bool> again = manager.CharacterPanel.GenerateAsync(); manager.CharacterPanel.Cancel(); Check(!await again && manager.NameBox.Text == previous, "Canceled generation overwrote the account."); manager.Close();
                await chat.OpenDirectMessageAsync(generated.Id); Conversation dm = chat.SelectedConversation!; Check(dm.Room!.Kind == "dm", "Profile-to-DM navigation failed.");
                chat.Input = "カズトと呼んで、同じ仲間として一言で挨拶して。"; int before = chat.Inferences.Items.Count; chat.SendCommand.Execute(null); await Until(() => chat.Inferences.Items.Count > before); InferenceJob peer = chat.Inferences.Items.Last(); await peer.Finished.WaitAsync(token);
                ChatMessage greeting = dm.Messages.Single(m => m.Id == peer.Request.AnswerId); Check(peer.State == "complete" && greeting.Content.Length > 0 && string.IsNullOrEmpty(greeting.Notice) && greeting.Identity?.Detail != "招待の実行結果", "Generated character could not join a real conversation, or a greeting was mistaken for an invitation: " + peer.Detail);
                await PrototypePreview.RenderAsync(main, Path.Combine(output, "real-character-conversation.png")); await File.WriteAllTextAsync(Path.Combine(output, "character.json"), JsonSerializer.Serialize(generated, AppStore.JsonOptions), token); await Progress("real-character-generation-conversation-complete");
            }
            await chat.PersistSafelyAsync(); AppState saved = await store.LoadAsync(); AppStore.Validate(saved); Check(saved.Self == chat.Self && saved.Conversations.Single(c => c.Id == room.Id).Room!.Members.Contains(rea.Id), "Self or invitations were lost on persistence.");
            await File.WriteAllTextAsync(Path.Combine(output, "result.txt"), "Social chat: local peer/profile, typed completion/invite/prose routing, styled clickable mentions/plain text, captured UI invitation, AI invite/idempotency/ACL, themes/minimum/130%, persistence" + (metadata is null ? "" : ", real character generation/cancel/save/conversation") + ": OK", token);
        }
        catch (Exception ex) { Environment.ExitCode = 1; await File.WriteAllTextAsync(Path.Combine(output, "result.txt"), ex.ToString()); }
        finally { if (main is not null) { foreach (Window child in main.OwnedWindows.Cast<Window>().ToArray()) { child.Close(); } await main.ViewModel.ShutdownAsync(); main.Close(); } Application.Current.Shutdown(); }
    }
}
