using System.IO;
using Eluna.App.Models;
using Eluna.App.Services;

namespace Eluna.Checks;

internal static class WorkspaceChecks
{
    private static void Check(bool success, string message) { if (!success) { throw new InvalidOperationException(message); } }
    private static void Reject(Action action) { try { action(); } catch (InvalidDataException) { return; } throw new InvalidOperationException("Invalid workspace data was accepted."); }
    internal static async Task RunAsync(string root)
    {
        Guid a = Guid.NewGuid(), b = Guid.NewGuid(), p = Guid.NewGuid(), q = Guid.NewGuid();
        var accounts = new List<AgentAccount> { new(a, "設計担当", Instruction: "PERSONA_A"), new(b, "検証担当", Instruction: "PERSONA_B") };
        var project = new KnowledgeProject(p, "開発", "", "project-only", false, [], []);
        var otherProject = new KnowledgeProject(q, "別の領域", "", "project-only", false, [], []);
        var source = new Conversation { Title = "DMの記録", Room = new("dm", [a], a) };
        source.Messages.Add(new() { Role = "user", Content = "朝食の好みはBLUEBERRYです。" });
        source.Messages.Add(new() { Role = "assistant", Content = "好みを確認しました。", Identity = new(a, "当時の名前", "A", "old-model", "当時のモデル", DateTimeOffset.UtcNow) });
        var target = new Conversation { Title = "相談", ProjectId = p, Room = new("channel", [a, b], a) };
        AppState state = new(4, new(SelectedProjectId: p), [source.Save(), target.Save()], [project, otherProject], Agents: accounts, AgentMemories: []);
        AppStore.Validate(state);
        KnowledgeContext Query(AppState snapshot, Guid agent = default) => KnowledgeService.Build(snapshot, Conversation.Restore(snapshot.Conversations.Single(c => c.Id == target.Id)), "朝食のBLUEBERRYの好み", 4096, CancellationToken.None, agentId: agent == default ? a : agent);
        Check(!Query(state).References.Any(r => r.Text.Contains("BLUEBERRY", StringComparison.Ordinal)), "A private DM crossed a server boundary.");
        AppState Shared(bool sourceGrant, bool roomAccept, bool serverAccept) => state with
        {
            Projects = [project with { AcceptAgentMemory = serverAccept }, otherProject],
            Conversations = [source.Save() with { Room = source.Room! with { ShareHistory = sourceGrant } }, target.Save() with { Room = target.Room! with { AcceptAgentMemory = roomAccept } }]
        };
        foreach ((bool grant, bool accept, bool server) in new[] { (false, true, true), (true, false, true), (true, true, false) })
        { Check(!Query(Shared(grant, accept, server)).References.Any(r => r.Text.Contains("BLUEBERRY", StringComparison.Ordinal)), "One-sided memory permission leaked history."); }
        AppState allowed = Shared(true, true, true);
        KnowledgeContext recall = Query(allowed);
        Check(recall.Instruction.Contains("PERSONA_A", StringComparison.Ordinal) && !recall.Instruction.Contains("PERSONA_B", StringComparison.Ordinal) && recall.References.Any(r => r.SourceConversationId == source.Id && r.AgentId == a && r.Text.Contains("BLUEBERRY", StringComparison.Ordinal)), "Allowed cross-server recall lost its identity or provenance.");
        Check(!Query(allowed, b).References.Any(r => r.Text.Contains("BLUEBERRY", StringComparison.Ordinal)), "Another participant received an agent's private history.");
        var memory = new AgentMemory(Guid.NewGuid(), a, "朝食にはBLUEBERRYを選ぶ。", "agent", source.Id);
        AppState memorized = Shared(false, true, true) with { AgentMemories = [memory] };
        Check(Query(memorized).References.Any(r => r.MemoryId == memory.Id), "An explicitly shared note incorrectly required full-history sharing.");
        Check(!Query(memorized with { AgentMemories = [memory with { Enabled = false }] }).References.Any(r => r.MemoryId == memory.Id), "A revoked memory remained searchable.");
        Check(!Query(memorized with { AgentMemories = [memory with { Scope = "room" }] }).References.Any(r => r.MemoryId == memory.Id), "A room-only note crossed rooms.");
        Reject(() => AppStore.Validate(state with { AgentMemories = [memory with { Scope = "server", ProjectId = p }] }));
        var child = new Conversation { Title = "スレッド", ProjectId = p, Room = new("thread", [a], a, target.Id, Guid.NewGuid()) };
        Reject(() => AppStore.Validate(state with { Conversations = [.. state.Conversations, child.Save()] }));
        target.Messages.Add(new() { Role = "user", Content = "深掘りしたい発言" });
        child.Room = child.Room! with { OriginMessageId = target.Messages[0].Id };
        AppState threaded = allowed with { Conversations = [allowed.Conversations[0], target.Save(), child.Save()], AgentMemories = [memory] };
        AppStore.Validate(threaded);
        AppState copy = WorkspaceService.ImportCopy(threaded); AppStore.Validate(copy);
        Check(copy.Agents!.All(x => accounts.All(old => old.Id != x.Id)) && copy.Conversations.All(c => threaded.Conversations.All(old => old.Id != c.Id)), "Import merged account or conversation identities.");
        SavedConversation copiedThread = copy.Conversations.Single(c => c.Room!.Kind == "thread"), copiedParent = copy.Conversations.Single(c => c.Id == copiedThread.Room!.ParentId);
        Check(copiedParent.Messages.Any(m => m.Id == copiedThread.Room!.OriginMessageId) && copy.AgentMemories![0].SourceConversationId == copy.Conversations.Single(c => c.Room!.Kind == "dm").Id, "Import broke thread or memory origin links.");
        string stamp = WorkspaceService.AccessStamp(allowed, target.Id, a, recall.References);
        Check(stamp != WorkspaceService.AccessStamp(Shared(false, true, true), target.Id, a, recall.References), "Revocation did not invalidate a frozen request.");
        Check(stamp == WorkspaceService.AccessStamp(allowed with { Conversations = [.. allowed.Conversations, new Conversation { Title = "無関係", Room = new("channel", [b], b) }.Save()] }, target.Id, a, recall.References), "Creating an unrelated room invalidated an existing request.");
        var legacy = new Conversation { Title = "旧会話" }; legacy.Messages.Add(new() { Role = "user", Content = "旧本文" }); legacy.Fork(1, "別の分岐");
        AppState oldState = new(1, new(), [legacy.Save()]); var store = new AppStore(root); await store.SaveAsync(oldState);
        byte[] original = await File.ReadAllBytesAsync(Path.Combine(root, "state.json"));
        AppState upgraded = WorkspaceService.Upgrade(await store.LoadAsync()); AppStore.Validate(upgraded); await store.SaveAsync(upgraded);
        Check((await File.ReadAllBytesAsync(Path.Combine(root, "state.json"))).SequenceEqual(original) && File.Exists(Path.Combine(root, "state-workspace.json")), "Migration overwrote the old state file.");
        AppState restored = await store.LoadAsync();
        Check(restored.SchemaVersion == 4 && restored.Conversations.Single().Branches!.Count == 2 && restored.Conversations.Single().Room!.Kind == "legacy", "Migration discarded branch history or room identity.");
        Check(ChatMessage.Restore(source.Messages[1].Save()).Speaker == "当時の名前" && ChatMessage.Restore(source.Messages[1].Save()).SpeakerDetail.Contains("当時のモデル", StringComparison.Ordinal), "Profile/model changes lost historical speaker snapshots.");
        Check(WorkspaceService.Messages(new(Guid.NewGuid(), "同じ本文の旧発言", [new("user", "同じ本文"), new("user", "同じ本文")])).Count() == 2, "Legacy messages without IDs were merged by content.");
        SavedMessage[] broadcast = WorkspaceService.AddressAgent([new("user", "全員への元の質問"), new("assistant", "最初の参加者の回答")], accounts[1]);
        Check(broadcast.Length == 3 && broadcast[^1].Role == "user" && broadcast[^1].Content.Contains(accounts[1].Name, StringComparison.Ordinal) && broadcast[^1].Content.Contains("全員への元の質問", StringComparison.Ordinal) && broadcast[1].Content == "最初の参加者の回答", "A later broadcast reply was prompted as an assistant continuation or lost the original question.");
        await File.WriteAllTextAsync(Path.Combine(root, "state-workspace.json"), "{broken");
        AppState recovered = await store.LoadAsync();
        Check(recovered.Conversations.Single().Id == legacy.Id && store.RecoveryNotice is not null && Directory.GetFiles(root, "state.broken-*.json").Length > 0 && (await File.ReadAllBytesAsync(Path.Combine(root, "state.json"))).SequenceEqual(original), "Corrupt workspace recovery lost the original history.");
        await CheckBackupAsync(Path.Combine(root, "workspace-backup"), threaded);
        await CheckDebateAsync();
        await CheckSearchAndModelsAsync();
        Console.WriteLine("Workspace: migration/old-file preservation, accounts/snapshots, bidirectional recall permissions, provenance/revocation, thread/import IDs and free-debate control/budgets: OK");
    }
    private static async Task CheckBackupAsync(string root, AppState state)
    {
        var source = new AppStore(Path.Combine(root, "source"));
        string artifact = Path.Combine(source.Root, "Generated", "result.md"); Directory.CreateDirectory(Path.GetDirectoryName(artifact)!); await File.WriteAllTextAsync(artifact, "GENERATED_WORKSPACE_DOCUMENT");
        var message = new ChatMessage { Role = "assistant", Artifact = new(Guid.NewGuid(), "結果", "document", artifact) }.Save();
        Conversation withArtifact = Conversation.Restore(state.Conversations[0]); withArtifact.Messages.Add(ChatMessage.Restore(message));
        state = state with { Conversations = state.Conversations.Select((c, i) => i == 0 ? withArtifact.Save() : c).ToList() };
        await source.SaveAsync(state); string archive = Path.Combine(root, "workspace.zip"); await BackupArchive.CreateAsync(source, archive, false);
        var target = new AppStore(Path.Combine(root, "restored")); PreparedBackup prepared = await BackupArchive.PrepareAsync(target, archive); await BackupArchive.ApplyAsync(target, prepared);
        AppState restored = await target.LoadAsync(); AppStore.Validate(restored);
        Check(restored.Agents!.Select(a => a.Id).SequenceEqual(state.Agents!.Select(a => a.Id)) && restored.AgentMemories!.Single().SourceConversationId == state.AgentMemories!.Single().SourceConversationId && restored.Conversations.Single(c => c.Room!.Kind == "thread").Room!.OriginMessageId == state.Conversations.Single(c => c.Room!.Kind == "thread").Room!.OriginMessageId, "ZIP restore lost account, memory or thread links.");
        GeneratedArtifact file = restored.Conversations.Select(Conversation.Restore).SelectMany(c => c.Messages).Single(m => m.Artifact is not null).Artifact!;
        Check(file.ResultPath.StartsWith(target.Root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) && await File.ReadAllTextAsync(file.ResultPath) == "GENERATED_WORKSPACE_DOCUMENT", "Generated result was not restored and rebased.");
    }
    private static async Task CheckSearchAndModelsAsync()
    {
        int searches = 0, smallCalls = 0;
        var roles = new List<DebateRole> { new("a", "提案", "提案する", "small"), new("b", "検証", "検証する", "base") };
        Task<WebSearchResult> Search(string query, int count, CancellationToken token) { searches++; return Task.FromResult(new WebSearchResult(query, [], [])); }
        int sequence = 0;
        Task<WorkReply> Generate(WorkPrompt prompt, Action<string>? stream, CancellationToken token)
        {
            string text;
            if (prompt.System == SearchRouter.Instruction) { text = "{\"search\":false}"; }
            else if (prompt.System.Contains("進行役です", StringComparison.Ordinal)) { text = "{\"speaker\":\"a\",\"kind\":\"search\",\"replyTo\":\"\",\"query\":\"設定 UI 一次資料\"}"; }
            else { text = "具体的な検討 " + ++sequence; }
            if (prompt.ModelId == "small") { smallCalls++; Check(prompt.MaxTokens + 800 + 64 <= 2048, "A role generation exceeded its own model context capacity."); }
            stream?.Invoke(text); return Task.FromResult(new WorkReply(text, 20));
        }
        var runner = new ResearchRunner(Search, (_, _) => throw new InvalidOperationException("No fixture search hits to fetch."), Generate,
            (prompt, _) => Task.FromResult(prompt.ModelId == "small" ? 800 : 400), 4096, id => id == "small" ? 2048 : 4096);
        var options = new WorkOptions(TokenLimit: 8000, Roles: roles, DebateStyle: "free", MaxTurns: 3);
        WorkSession Work(string question, string policy = "auto") => new() { Mode = "debate", ModelId = "base", Question = question, Options = options with { DebateSearch = policy } };
        Task Run(WorkSession work) => runner.RunAsync(work, (_, _) => Task.FromResult(true), () => Task.CompletedTask, _ => { }, _ => { }, CancellationToken.None);
        WorkSession automatic = Work("画面構成の案を相談"); await Run(automatic);
        Check(automatic.Stage == "complete" && searches == 1 && automatic.WebEnabled == true && smallCalls > 0, "Auto debate could not add research after initially choosing no search, or repeated the same query.");
        int before = searches; await Run(WorkSession.Restore(automatic.Save())); Check(searches == before, "Resuming repeated a completed search.");
        WorkSession offline = Work("検索して比較してください", "off"); offline.WebEnabled = true; await Run(offline); Check(searches == before && offline.WebEnabled == false, "A saved flag bypassed the no-search policy.");
        WorkSession forbidden = Work("検索しないで画面構成の案を相談"); await Run(forbidden); Check(searches == before, "The director ignored a user's no-search instruction.");
        WorkSession requested = Work("画面構成の案を相談"); requested.Discussion = new(Inputs: [new(Guid.NewGuid(), "設定 UI 一次資料を検索してください")]); await Run(requested);
        Check(searches > before && requested.Discussion.Inputs!.Single().Applied && requested.Turns.Count(t => t.Kind == "user") == 1, "Explicit research requested during a discussion was lost or duplicated.");
        Check(!SearchRouter.RequestsSearch("次の文章を翻訳: 「検索してください」") && !SearchRouter.ForbidsSearch("引用: 「検索しないで」"), "Quoted text changed discussion search permissions.");
        var tooLarge = new ResearchRunner(Search, (_, _) => throw new InvalidOperationException(), Generate, (prompt, _) => Task.FromResult(prompt.ModelId == "small" ? 3000 : 400), 4096, id => id == "small" ? 2048 : 4096);
        bool rejected = false;
        try { await tooLarge.RunAsync(Work("画面構成の案", "off"), (_, _) => Task.FromResult(true), () => Task.CompletedTask, _ => { }, _ => { }, CancellationToken.None); }
        catch (InvalidOperationException ex) when (ex.Message.Contains("入力上限", StringComparison.Ordinal)) { rejected = true; }
        Check(rejected, "A participant's smaller context limit was ignored.");
    }
    private static async Task CheckDebateAsync()
    {
        var roles = new List<DebateRole> { new("a", "設計", "提案する"), new("b", "検証", "異論を示す") };
        int searches = 0, speech = 0;
        Task<WebSearchResult> Search(string query, int count, CancellationToken token) { searches++; return Task.FromResult(new WebSearchResult(query, [], [])); }
        Task<WorkReply> Generate(WorkPrompt prompt, Action<string>? stream, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            string text = prompt.System.Contains("進行役です", StringComparison.Ordinal)
                ? "{\"speaker\":\"a\",\"kind\":\"speech\",\"replyTo\":\"\",\"query\":\"\",\"reason\":\"検討を進める\"}"
                : prompt.Final ? "合意点と相違点、未確認事項を整理しました。" : "異なる具体案を示す発言 " + ++speech;
            stream?.Invoke(text); return Task.FromResult(new WorkReply(text, 30));
        }
        var runner = new ResearchRunner(Search, (_, _) => throw new InvalidOperationException("Offline debate fetched a page."), Generate, (_, _) => Task.FromResult(400), 4096);
        var work = new WorkSession { Mode = "debate", ModelId = "fixture", Question = "アプリの設計を議論", Options = new(TokenLimit: 8000, Roles: roles, DebateStyle: "free", MaxTurns: 4, DebateSearch: "off") };
        await runner.RunAsync(work, (_, _) => Task.FromResult(true), () => Task.CompletedTask, _ => { }, _ => { }, CancellationToken.None);
        Check(work.Stage == "complete" && work.Turns.Count == 4 && work.Turns.All(t => !t.Incomplete) && searches == 0 && work.Turns.Any(t => t.Role == "b"), "Free debate did not finish offline or allowed a third consecutive speaker.");
        Check(work.TokensUsed <= work.Options.TokenLimit && work.Report.Contains("外部資料", StringComparison.Ordinal), "Free debate exceeded its budget or implied verified offline facts.");
        Check(!DebateDirector.TryParse("{\"speaker\":\"outside\",\"kind\":\"speech\",\"replyTo\":\"\",\"query\":\"\"}", roles, work.Turns, out _), "Director accepted an unknown participant.");
        Check(!DebateDirector.TryParse("{\"speaker\":\"a\",\"kind\":\"speech\",\"replyTo\":\"" + Guid.NewGuid() + "\",\"query\":\"\"}", roles, work.Turns, out _), "Director accepted a reply to another conversation.");
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var manual = new WorkSession { Mode = "debate", ModelId = "fixture", Question = "手動の相談", Options = work.Options with { AutomaticDebate = false }, Discussion = new(false, Inputs: [new(Guid.NewGuid(), "検証役へ質問", "b")]) };
        Task run = runner.RunAsync(manual, (_, _) => Task.FromResult(true), () => Task.CompletedTask, _ => { }, _ => { }, stop.Token);
        async Task Until(Func<bool> condition) { while (!condition()) { await Task.Delay(5, stop.Token); } }
        await Until(() => manual.Stage == "debate-wait");
        Check(manual.Turns.Count(t => t.Kind != "user") == 0, "Manual debate generated a speech before an advance.");
        manual.Advance(); await Until(() => manual.Turns.Any(t => t.Kind != "user" && !t.Incomplete));
        Check(manual.Turns.First(t => t.Kind != "user").Role == "b", "A user's targeted question did not choose its recipient.");
        manual.Discussion = manual.Discussion with { Finish = true }; manual.Advance(); await run;
        Check(manual.Stage == "complete" && manual.Turns.Count(t => t.Kind != "user") == 1, "Finish from a manual wait generated an extra speech.");
        using var cancel = new CancellationTokenSource();
        var partial = new WorkSession { Mode = "debate", ModelId = "fixture", Question = "中断と再開", Options = work.Options with { MaxTurns = 2 } };
        var interrupted = new ResearchRunner(Search, (_, _) => throw new InvalidOperationException(), (prompt, stream, token) =>
        {
            if (prompt.System.Contains("進行役です", StringComparison.Ordinal)) { return Generate(prompt, stream, token); }
            stream?.Invoke("途中の発言を保持"); cancel.Cancel(); token.ThrowIfCancellationRequested(); return Task.FromResult(new WorkReply("", 0));
        }, (_, _) => Task.FromResult(400), 4096);
        await interrupted.RunAsync(partial, (_, _) => Task.FromResult(true), () => Task.CompletedTask, _ => { }, _ => { }, cancel.Token);
        Check(partial.Stage == "paused" && partial.Turns.Single().Incomplete && partial.Turns[0].Content == "途中の発言を保持", "Interrupted streaming lost its partial speech.");
        WorkSession resumed = WorkSession.Restore(partial.Save()); Guid? old = resumed.Turns[0].Id;
        await runner.RunAsync(resumed, (_, _) => throw new InvalidOperationException("Repeated review."), () => Task.CompletedTask, _ => { }, _ => { }, CancellationToken.None);
        Check(resumed.Stage == "complete" && resumed.Turns[0].Id == old && resumed.Turns[0].Incomplete && resumed.Turns.Count(t => !t.Incomplete) == 1, "Resume rewrote or replayed a partial speech.");
    }
}
