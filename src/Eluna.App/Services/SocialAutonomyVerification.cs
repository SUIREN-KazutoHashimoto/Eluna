using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Eluna.App.Models;

namespace Eluna.App.Services;

internal static class SocialAutonomyVerification
{
    private static void Check(bool ok, string message) { if (!ok) { throw new InvalidOperationException(message); } }
    private static IEnumerable<T> Find<T>(DependencyObject root) where T : DependencyObject
    { for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) { DependencyObject child = VisualTreeHelper.GetChild(root, i); if (child is T item) { yield return item; } foreach (T nested in Find<T>(child)) { yield return nested; } } }
    internal static async Task RunAsync(AppStore store, string output, string? metadata, string? imageModel = null)
    {
        Directory.CreateDirectory(output); MainWindow? main = null;
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(10)); CancellationToken token = deadline.Token;
        async Task Until(Func<bool> ready) { while (!ready()) { await Task.Delay(30, token); } }
        var evidence = new List<string>();
        try
        {
            UnsavedChanges.Review = _ => false;
            LocalModel model = metadata is null ? new("autonomy-fixture", "画面確認モデル", "fixture", "local", "fixture", "", 0) : JsonSerializer.Deserialize<LocalModel>(await File.ReadAllTextAsync(metadata, token), AppStore.JsonOptions)!;
            model = model with { Preferences = new(ContextSize: 8192, Temperature: .65, MaxOutputTokens: 1024, DefaultMode: "fast") };
            if (metadata is not null) { string folder = Path.Combine(store.ModelsDirectory, model.Id); Directory.CreateDirectory(folder); await File.WriteAllTextAsync(Path.Combine(folder, "model.json"), JsonSerializer.Serialize(model, AppStore.JsonOptions), token); }
            main = new(store) { WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = -20000 }; main.Show(); var chat = main.ViewModel;
            chat.SocialDecisionObserved += (id, decision) => File.WriteAllText(Path.Combine(output, "decision-" + id.ToString("N") + ".json"), decision);
            await Until(() => chat.IsReady || chat.HasError); Check(chat.IsReady, chat.Error);
            if (metadata is null) { chat.Models.Add(model); }
            chat.SelectedModel = model;
            await chat.ApplySettingsAsync(chat.Settings with { WorkMode = "offline", InferenceMode = "fast", ContextSize = 8192, Compute = chat.ComputeChoices.Any(c => c.Id == "Vulkan0") ? "Vulkan0" : "cpu" });
            foreach (Conversation starter in chat.Conversations.Where(c => c.Room?.Social?.Spontaneous == true).ToArray()) { await chat.UpdateRoomAsync(starter, starter.Title, starter.Room! with { Social = starter.Room!.Social! with { Spontaneous = false } }, false); }
            Check(chat.Agents.Count == 12 && main.Icon is not null, "Starter cast or application icon missing.");
            var a = new AgentAccount(Guid.NewGuid(), "アオ", "🌙", "仲間と気軽に話す。案を求められたらまずレンに短い質問をする。kind=ask、callでレンを指定する。", "ゲームと物語", model.Id, Generation: new(.65, .95, 1024), Social: new("ゲーム、物語", .6, CooldownSeconds: 5));
            var b = new AgentAccount(Guid.NewGuid(), "レン", "🎮", "自分の考えを短く返す。単なる回答の枠を使わず、ゲームについて仲間と会話する。", "ゲーム", model.Id, Generation: new(.8, .95, 1024), Social: new("ゲーム", .8, CooldownSeconds: 5), Artist: new(true, "水彩"));
            await chat.SaveAgentAsync(a); await chat.SaveAgentAsync(b);
            Conversation room = await chat.CreateRoomAsync("対等な会話", "group", [a.Id, b.Id]);
            await chat.UpdateRoomAsync(room, room.Title, room.Room! with { Social = new(Enabled: true, MaxTurns: 3, MaxTokens: 3000, PaceSeconds: 1) }, false);
            for (int i = 0; i < 70; i++) { room.Messages.Add(new() { Role = i % 2 == 0 ? "user" : "assistant", Content = $"過去の発言 {i}: " + new string('文', 100), Identity = i % 2 == 0 ? new(AgentAccount.LocalId, chat.Self.Name, chat.Self.Icon, Time: DateTimeOffset.UtcNow) : new(b.Id, b.Name, b.Icon, Time: DateTimeOffset.UtcNow.AddMinutes(-100 + i)) }); }
            var draft = new ChatMessage { Role = "assistant", Pending = true, IsGenerating = true, Identity = new(a.Id, a.Name, a.Icon) }; room.Messages.Add(draft);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); int delivered = chat.Timeline.Count;
            for (int i = 0; i < 80; i++) { draft.Content += "まだ書いている内容 "; }
            Check(chat.Timeline.Count == delivered && !chat.Timeline.Contains(draft), "A streaming draft appeared in the timeline.");
            var scroll = Find<ScrollViewer>((ListBox)main.FindName("MessagesList")).First();
            await Task.Delay(750, token);
            var wheel = new System.Windows.Input.MouseWheelEventArgs(System.Windows.Input.Mouse.PrimaryDevice, Environment.TickCount, 120) { RoutedEvent = UIElement.PreviewMouseWheelEvent };
            ((ListBox)main.FindName("MessagesList")).RaiseEvent(wheel); Check(wheel.Handled && !main.FollowingOutput, "Mouse wheel did not exit following mode.");
            scroll.ScrollToVerticalOffset(scroll.ScrollableHeight * .35); await Task.Delay(150, token); double before = scroll.VerticalOffset;
            (Guid Id, double Top) Anchor()
            {
                var visible = Find<ListBoxItem>((ListBox)main.FindName("MessagesList")).Where(item => item.DataContext is ChatMessage).Select(item => (Message: (ChatMessage)item.DataContext, Top: item.TransformToAncestor(scroll).Transform(new Point()).Y, Height: item.ActualHeight)).Where(item => item.Top + item.Height > 0 && item.Top < scroll.ViewportHeight).OrderBy(item => item.Top).First();
                return (visible.Message.Id, visible.Top);
            }
            var anchorBefore = Anchor();
            draft.IsGenerating = false; draft.Pending = false;
            await Task.Delay(250, token); var anchorAfter = Anchor();
            await File.WriteAllTextAsync(Path.Combine(output, "reading.json"), JsonSerializer.Serialize(new { offsetBefore = before, offsetAfter = scroll.VerticalOffset, before = new { anchorBefore.Id, anchorBefore.Top }, after = new { anchorAfter.Id, anchorAfter.Top }, following = main.FollowingOutput }, AppStore.JsonOptions), token);
            Check(chat.Timeline.Contains(draft) && anchorBefore.Id == anchorAfter.Id && Math.Abs(anchorBefore.Top - anchorAfter.Top) < 3 && !main.FollowingOutput, "Completed delivery moved the visible reading anchor: " + anchorBefore + " → " + anchorAfter);
            var input = (TextBox)main.FindName("InputBox"); var placeholder = (TextBlock)main.FindName("InputPlaceholder");
            input.Text = "日本語の入力途中"; Check(placeholder.Visibility == Visibility.Collapsed, "Placeholder overlapped direct text input."); input.Clear(); Check(placeholder.Visibility == Visibility.Visible, "Placeholder did not return after clearing.");
            evidence.Add("Unpublished streaming draft, completion delivery, retained reading position, immediate placeholder visibility and window icon: OK");
            foreach (string theme in new[] { "dark", "light" })
            {
                Appearance.Apply(theme, 1.3); main.Width = 820; main.Height = 600;
                await PrototypePreview.RenderAsync(main, Path.Combine(output, "minimum-" + theme + ".png"));
                var profile = new AgentProfileWindow(chat, b.Id) { Owner = main, WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = -20000 };
                profile.Show(); profile.Width = profile.MinWidth; profile.Height = profile.MinHeight;
                await PrototypePreview.RenderAsync(profile, Path.Combine(output, "profile-minimum-" + theme + ".png")); profile.Close();
                var options = new RoomWindow(chat, room) { Owner = main, WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = -20000 }; options.Show(); options.Width = options.MinWidth; options.Height = options.MinHeight;
                await PrototypePreview.RenderAsync(options, Path.Combine(output, "channel-minimum-" + theme + ".png")); options.Close();
            }
            Appearance.Apply("dark", 1); main.Width = 1280; main.Height = 900;
            if (metadata is not null)
            {
                room.Messages.Clear(); chat.SelectedRecipient = chat.Recipients.Single(r => r.Id == a.Id);
                chat.Input = "@アオ ゲーム作りの題材を一緒に考えよう。まずレンにどんなジャンルが好きか聞いてみて。"; int count = chat.Inferences.Items.Count; chat.SendCommand.Execute(null);
                await Until(() => chat.Inferences.Items.Count > count); await Until(() => chat.Inferences.ActiveCount > 0 || chat.Inferences.Items.Last().State == "failed");
                Check(!chat.Timeline.Any(m => m.Pending || m.IsGenerating) && chat.TypingNotice.Contains(a.Name, StringComparison.Ordinal), "Real inference appeared as an unfinished post or lacked typing notice.");
                await PrototypePreview.RenderAsync(main, Path.Combine(output, "real-typing.png"));
                await Until(() => chat.Inferences.Items.Count > count + 1 || chat.Inferences.Items.Skip(count).Any(j => j.State == "failed"));
                Check(!chat.Inferences.Items.Skip(count).Any(j => j.State == "failed"), "The real model call failed: " + string.Join(" / ", chat.Inferences.Items.Skip(count).Select(j => j.Detail)));
                await Until(() => chat.Inferences.Items.Skip(count).Any(j => j.Request.Agent?.Id == b.Id && !j.IsPending));
                Check(chat.Inferences.Items.Skip(count).Any(j => j.Request.Agent?.Id == b.Id && j.Request.Social?.Prompted == true), "The second account only interjected; a real account call was not delivered.");
                chat.StopSocialConversation(room.Id); await Task.WhenAll(chat.Inferences.Items.Skip(count).Select(j => j.Finished)).WaitAsync(token);
                Check(chat.Inferences.Items.Skip(count).All(j => j.State is "complete" or "canceled") && room.Messages.Any(m => !m.Pending && m.Identity?.AgentId == b.Id && m.Content.Length > 0), "Real account-to-account call failed: " + string.Join(" / ", chat.Inferences.Items.Skip(count).Select(j => j.Detail)));
                Check(room.Experience.SocialPaused, "Stop did not persist the channel pause.");
                await PrototypePreview.RenderAsync(main, Path.Combine(output, "real-conversation.png"));
                if (imageModel is not null)
                {
                    await File.WriteAllTextAsync(Path.Combine(store.Root, "image-model.json"), JsonSerializer.Serialize(Path.GetFullPath(imageModel)), token);
                    chat.ResumeSocialConversation(room.Id); chat.SelectedRecipient = chat.Recipients.Single(r => r.Id == b.Id);
                    int beforeImages = chat.MediaJobs.Count, beforeDrawing = chat.Inferences.Items.Count;
                    chat.Input = "@レン 夕焼けの湖畔に小さな家がある水彩の絵を描いて。"; chat.SendCommand.Execute(null);
                    await Until(() => chat.MediaJobs.Count > beforeImages || chat.Inferences.Items.Skip(beforeDrawing).Any(j => j.State is "complete" or "failed"));
                    Check(chat.MediaJobs.Count > beforeImages, "The artist did not queue its drawing: " + string.Join(" / ", chat.Inferences.Items.Skip(beforeDrawing).Select(j => j.Detail)));
                    MediaJob drawing = chat.MediaJobs.Last();
                    Check(drawing.Request is { ArtistGenerated: true, Agent.Id: var artistId, Image.Prompt.Length: > 0 } && artistId == b.Id && !chat.Timeline.Any(m => m.Artifact?.JobId == drawing.Id), "The drawing lost its account author or appeared before completion.");
                    Conversation separateDrawingRoom = await chat.CreateRoomAsync("描画中に別の会話を読む", "dm", [a.Id]);
                    await drawing.Finished.WaitAsync(token);
                    Check(chat.SelectedConversation == separateDrawingRoom && !separateDrawingRoom.Messages.Any(m => m.Artifact?.JobId == drawing.Id), "Drawing delivery followed the currently selected channel.");
                    chat.SelectedConversation = room; await Until(() => room.Messages.Any(m => !m.Pending && m.Artifact?.JobId == drawing.Id && m.ArtifactImage is not null));
                    ChatMessage imagePost = room.Messages.Single(m => m.Artifact?.JobId == drawing.Id);
                    Check(imagePost.Identity?.AgentId == b.Id && imagePost.Content.Length > 0 && !imagePost.Content.Contains(drawing.Request!.Image!.Prompt, StringComparison.Ordinal) && imagePost.ArtifactImage!.PixelWidth > 100, "The completed drawing did not arrive as the artist's caption and image.");
                    chat.StopSocialConversation(room.Id); await PrototypePreview.RenderAsync(main, Path.Combine(output, "real-artist-post.png"));
                    evidence.Add("Real stable-diffusion backend: artist-generated English prompt, queued unpublished drawing, completed image preview and account-authored caption: OK");
                }
                evidence.Add("Real local model: initial peer post, typed-only progress, structured account call, second account response, stop and saved pause: OK");
            }
            await chat.PersistSafelyAsync(); AppStore.Validate(await store.LoadAsync());
            await File.WriteAllTextAsync(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new { success = true, evidence, physicalIme = "not verified", realDpiSwitch = "not verified" }, AppStore.JsonOptions), token);
        }
        catch (Exception ex) { Environment.ExitCode = 1; await File.WriteAllTextAsync(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new { success = false, error = ex.ToString(), evidence }, AppStore.JsonOptions)); }
        finally { if (main is not null) { foreach (Window child in main.OwnedWindows.Cast<Window>().ToArray()) { child.Close(); } await main.ViewModel.ShutdownAsync(); main.Close(); } Application.Current.Shutdown(); }
    }
}
