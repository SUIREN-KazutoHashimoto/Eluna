using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using Eluna.App.Models;
using Eluna.App.Services;

namespace Eluna.Checks;

internal static class MessageExperienceChecks
{
    private static void Check(bool ok, string message) { if (!ok) { throw new InvalidOperationException(message); } }
    private static void Reject(Action action) { try { action(); } catch (InvalidDataException) { return; } throw new InvalidOperationException("Invalid message metadata was accepted."); }
    internal static async Task RunAsync(string root)
    {
        var account = new AgentAccount(Guid.NewGuid(), "星乃", "星"); var room = new Conversation { Title = "夜の相談", Room = new("dm", [account.Id]) };
        room.Messages.Add(new() { Role = "assistant", Content = "以前の発言", Identity = new(account.Id, account.Name) });
        var rooms = new ObservableCollection<Conversation> { room }; var arrivals = new List<(Guid Branch, Guid Message)>();
        using var incoming = new IncomingMessages(rooms, (_, branch, message, _) => arrivals.Add((branch, message)));
        Check(arrivals.Count == 0, "Loading history announced old messages.");
        rooms.Move(0, 0); Check(arrivals.Count == 0, "Reordering channels replayed historical arrivals.");
        room.Messages.Add(new() { Role = "user", Content = "自分の発言", Identity = new(null, "自分") }); Check(arrivals.Count == 0, "A local message was treated as an incoming account.");
        var message = new ChatMessage { Role = "assistant", IsGenerating = true }; room.Messages.Add(message); message.Content = "途中"; message.Content += "の文章"; Check(arrivals.Count == 0, "Streaming fragments became separate unread messages."); message.IsGenerating = false; message.Content += "。"; Check(arrivals.Count == 1 && arrivals[0].Message == message.Id, "Completion was not recorded once.");
        var canceled = new ChatMessage { Role = "assistant", IsGenerating = true, Content = "中断", WasInterrupted = true }; room.Messages.Add(canceled); canceled.IsGenerating = false; Check(arrivals.Count == 1, "Canceled content was announced as a finished reply.");
        canceled.IsGenerating = true; canceled.WasInterrupted = false; canceled.IsGenerating = false; Check(arrivals.Count == 2, "A successful retry never completed.");
        var work = new WorkSession { Mode = "debate", Options = new(DebateStyle: "free", MaxTurns: 4) }; var discussion = new ChatMessage { Role = "assistant", IsGenerating = true, Work = work }; room.Messages.Add(discussion); Guid turnId = Guid.NewGuid();
        work.Turns.Add(new(1, "a", "途中", Name: account.Name, Id: turnId, AgentId: account.Id, Incomplete: true)); Check(arrivals.Count == 2, "An incomplete debate turn was announced."); work.Turns[0] = work.Turns[0] with { Content = "検討した意見", Incomplete = false }; work.Turns[0] = work.Turns[0] with { Content = "検討した意見。" }; Check(arrivals.Count == 3 && arrivals.Last().Message == turnId, "Debate completion was lost or duplicated.");
        work.Turns.Add(new(2, "user", "本人の発言", Kind: "user")); Check(arrivals.Count == 3, "The local participant's own message was unread.");
        ConversationBranch old = room.ActiveBranch; ConversationBranch fork = room.Fork(room.Messages.Count, "別の案"); Check(arrivals.Count == 3, "Forking replayed old notifications.");
        var forkReply = new ChatMessage { Role = "assistant", Content = "別の分岐の回答" }; fork.Messages.Add(forkReply); Check(arrivals.Last() == (fork.Id, forkReply.Id), "New branch replies lost their branch identity.");
        rooms.Remove(room); int before = arrivals.Count; work.Turns.Add(new(3, "a", "削除後の変更")); old.Messages.Add(new() { Role = "assistant", Content = "削除後" }); Check(arrivals.Count == before, "Removed rooms retained notification subscriptions.");
        var delegated = new Conversation(); var task = new ChatMessage { Role = "user", Content = "この作業を一緒に進めよう。", Identity = new(account.Id, account.Name) }; delegated.Messages.Add(task); rooms.Add(delegated); Check(arrivals.Count == before + 1 && arrivals.Last().Message == task.Id, "A peer account's delegated message was hidden behind the transport user role."); rooms.Remove(delegated);
        int mediaBefore = arrivals.Count; var mediaRoom = new Conversation(); var media = new ChatMessage { Role = "assistant", Content = "文書の作成", Identity = new(account.Id, account.Name, Detail: "文書生成") }; mediaRoom.Messages.Add(media); rooms.Add(mediaRoom);
        Check(arrivals.Count == mediaBefore && !MessageLocations.In(mediaRoom).Single().Complete, "A queued media title was treated as a completed artifact."); media.Artifact = new(Guid.NewGuid(), "文書", "document", Path.Combine(root, "draft.docx")); Check(arrivals.Count == mediaBefore + 1 && MessageLocations.In(mediaRoom).Single().Complete, "A completed artifact never became a finished incoming message."); rooms.Remove(mediaRoom);
        var large = new Conversation(); for (int i = 0; i < 10000; i++) { large.Messages.Add(new() { Role = "assistant", Content = "履歴 " + i }); }
        rooms.Add(large); var watch = Stopwatch.StartNew(); for (int i = 0; i < 10; i++) { large.Messages.Add(new() { Role = "assistant", Content = "新着 " + i }); }
        watch.Stop(); Check(watch.Elapsed < TimeSpan.FromSeconds(1), "Appending replies rescanned a 10,000-message history quadratically.");
        rooms.Add(room); room.Experience = new(Unread: [new(old.Id, message.Id, DateTimeOffset.UtcNow, true)], Pins: [new(old.Id, turnId)], Reactions: [new(old.Id, message.Id, "👍")]);
        AppState state = new(4, new(), [room.Save()], Agents: [account]); AppStore.Validate(state); AppState copy = WorkspaceService.ImportCopy(state); AppStore.Validate(copy); SavedConversation copied = copy.Conversations.Single();
        Check(copied.Experience!.Unread!.Single().Message != message.Id && copied.Experience.Pins!.Single().Message != turnId && copied.Experience.Reactions!.Single().Message == copied.Experience.Unread!.Single().Message, "Import did not remap message/branch metadata consistently.");
        MessageLocation[] rows = MessageLocations.In(room).ToArray(); Check(rows.Any(m => m.Message == turnId && m.Pinned) && rows.Any(m => m.Message == message.Id && m.Mention), "Debate pins or mention receipts were not resolved.");
        Check(MessageSearch.Find(rows, "from:星乃 in:夜 検討", "pins", CancellationToken.None).Total == 1 && MessageSearch.Find(rows, "存在しない", "search", CancellationToken.None).Total == 0, "Speaker/place/body search or zero results were incorrect.");
        using var canceledSearch = new CancellationTokenSource(); canceledSearch.Cancel(); bool stopped = false; try { MessageSearch.Find(rows, "", "search", canceledSearch.Token); } catch (OperationCanceledException) { stopped = true; }
        Check(stopped, "Canceled search kept running.");
        var at = new DateTimeOffset(DateTime.Today.AddHours(21));
        ChatMessage First(DateTimeOffset time, Guid? actor = null, Guid? replyTo = null) => new() { Role = "assistant", Content = "同じ人の発言", Identity = new(actor ?? account.Id, account.Name, Time: time, ReplyTo: replyTo) };
        ChatMessage[] presentation = [First(at), First(at.AddMinutes(1)), First(at.AddMinutes(2), replyTo: message.Id), First(at.AddMinutes(3), Guid.NewGuid()), First(at.AddDays(1)), new() { Role = "assistant", Content = "古い記録" }];
        TimelinePresentation.Apply(presentation); Check(presentation[0].HasDateSeparator && !presentation[0].Compact && presentation[1].Compact && !presentation[1].HasDateSeparator && !presentation[2].Compact && !presentation[3].Compact && presentation[4].HasDateSeparator && !presentation[5].HasDateSeparator && !presentation[5].Compact, "Date boundaries, replies, actor IDs or missing historical timestamps grouped incorrectly.");
        TimelinePresentation.Apply([presentation[0], presentation[3], presentation[1]]); Check(!presentation[1].Compact, "Recycling/reordering retained stale compact state.");
        Reject(() => AppStore.Validate(state with { Conversations = [room.Save() with { Experience = new(Pins: [new(old.Id, Guid.NewGuid())]) }] }));
        Reject(() => AppStore.Validate(state with { Conversations = [room.Save() with { Experience = new(Reactions: [new(old.Id, message.Id, "<script>")]) }] }));
        Reject(() => AppStore.Validate(state with { Conversations = [room.Save() with { Experience = new(Unread: [new(old.Id, message.Id, DateTimeOffset.UtcNow), new(old.Id, message.Id, DateTimeOffset.UtcNow)]) }] }));
        var store = new AppStore(Path.Combine(root, "data")); await store.SaveAsync(state); AppState saved = await store.LoadAsync(); Check(saved.Conversations.Single().Experience!.Reactions!.Single().Emoji == "👍", "Message experience was lost on reload.");
        string zip = Path.Combine(root, "backup.zip"); await BackupArchive.CreateAsync(store, zip, false); var restored = new AppStore(Path.Combine(root, "restored")); PreparedBackup prepared = await BackupArchive.PrepareAsync(restored, zip); await BackupArchive.ApplyAsync(restored, prepared); AppState after = await restored.LoadAsync(); Check(after.Conversations.Single().Experience!.Unread!.Single().Mention && after.Conversations.Single().Experience!.Pins!.Single().Message == turnId, "Backup lost unread mentions or debate pins.");
        Console.WriteLine("Messages: completion/streaming/cancel/retry/debate/branch/disposal/10k appends, search/cancel, metadata validation/import/reload/ZIP: OK");
    }
}
