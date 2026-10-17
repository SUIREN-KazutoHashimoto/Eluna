using System.IO;
using System.Text.Json;
using Eluna.App.Models;
using Eluna.App.Services;

namespace Eluna.Checks;

internal static class SocialAutonomyChecks
{
    private static void Check(bool condition, string message) { if (!condition) { throw new InvalidOperationException(message); } }
    private static void Reject(Action action)
    { try { action(); } catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException) { return; } throw new InvalidOperationException("Invalid autonomous conversation data was accepted."); }
    internal static async Task RunAsync(string root)
    {
        AppState initial = WorkspaceService.Upgrade(new(1, new(), [])); AppStore.Validate(initial);
        Check(initial.Agents?.Count == 12 && initial.Agents.Select(a => a.Name).Distinct().Count() == 12 && initial.Agents.Count(a => a.Artist?.Enabled == true) >= 2,
            "First run did not provide twelve distinct accounts and artists.");
        Check(initial.Agents!.All(a => a.ModelId is null) && initial.Conversations.Any(c => c.Room?.Social is { Enabled: true, Spontaneous: true }), "Starter accounts required separate models or lacked a social channel.");
        Check(WorkspaceService.Upgrade(initial) == initial && initial.Agents!.All(a => a.Connections?.All(id => initial.Agents!.Any(b => b.Id == id)) == true), "Starter accounts duplicated or had broken relationships.");
        AgentAccount[] accounts = Enumerable.Range(0, 50).Select(i => new AgentAccount(Guid.NewGuid(), "メンバー" + i)).ToArray();
        Guid server = Guid.NewGuid(); var project = new KnowledgeProject(server, "50人のサーバー", "", "project-only", false, [], []);
        var room = new Conversation { ProjectId = server, Room = new("channel", accounts.Take(49).Select(a => a.Id).ToList(), accounts[0].Id, Social: new(Enabled: true)) };
        AppState state = new(4, new(), [room.Save()], [project], Agents: accounts.ToList()); AppStore.Validate(state);
        Check(AccountDefaults.ServerMembers(state, server).Count + 1 == 50, "The local member was omitted from the server limit.");
        Reject(() => ChannelInvitations.Apply(state, room.Save(), [accounts[49].Id], null));
        var other = new Conversation { ProjectId = server, Room = new("channel", [accounts[49].Id], accounts[49].Id) };
        Reject(() => AppStore.Validate(state with { Conversations = [room.Save(), other.Save()] }));
        Check(AccountDefaults.ServerMembers(state with { Conversations = [room.Save(), room.Save() with { Id = Guid.NewGuid() }] }, server).Count == 49, "Channel duplicates consumed extra server places.");
        AgentAccount account = accounts[0] with { Generation = new(.35, .85, 1024), Social = new("科学、工作", .7, false, 30), Artist = new(true, "水彩"), Connections = [accounts[1].Id] };
        AccountDefaults.Validate(account);
        Reject(() => AccountDefaults.Validate(account with { Generation = new(double.NaN) }));
        Reject(() => AccountDefaults.Validate(account with { Generation = new(MaxOutputTokens: 32768) }));
        Reject(() => AccountDefaults.Validate(account with { Social = new(Participation: 1.1) }));
        Reject(() => AccountDefaults.Validate(new SocialPolicy(PaceSeconds: 0)));
        Check(SocialConversation.WithinHours(new(StartHour: 22, EndHour: 5), 23) && SocialConversation.WithinHours(new(StartHour: 22, EndHour: 5), 3) &&
            !SocialConversation.WithinHours(new(StartHour: 22, EndHour: 5), 12) && SocialConversation.WithinHours(new(StartHour: 0, EndHour: 0), 12), "Overnight or all-day conversation hours were wrong.");
        using (JsonDocument schema = JsonDocument.Parse(JsonSerializer.Serialize(SocialConversation.CreateSchema(1, 3, 1, false))))
        {
            JsonElement[] alternatives = schema.RootElement.GetProperty("oneOf").EnumerateArray().ToArray();
            Check(!alternatives.Any(x => x.GetProperty("properties").GetProperty("kind").GetProperty("enum")[0].GetString() == "draw"), "A non-artist was offered a draw action.");
            foreach (JsonElement alternative in alternatives)
            {
                int[] replies = alternative.GetProperty("properties").GetProperty("reply").GetProperty("enum").EnumerateArray().Select(x => x.GetInt32()).ToArray();
                int[] calls = alternative.GetProperty("properties").GetProperty("call").GetProperty("items").GetProperty("enum").EnumerateArray().Select(x => x.GetInt32()).ToArray();
                Check(replies.All(x => x is -1 or 0) && calls.SequenceEqual([0, 2]), "The decoder was permitted to invent a reply index or call itself.");
            }
        }
        string Action(SocialAction action) => JsonSerializer.Serialize(action, AppStore.JsonOptions);
        Check(SocialConversation.Parse(Action(new("say", "😀", -1, [])), 0, 3, false).Text == "😀", "Emoji-only posts were rejected.");
        Check(SocialConversation.Parse(Action(new("reply", "草", 0, [])), 1, 3, false).Text == "草", "Short quoted replies were rejected.");
        Check(SocialConversation.Parse(Action(new("react", "", 0, [], "😀")), 1, 3, false).Kind == "react", "An actual reaction was rejected.");
        Check(SocialConversation.Parse(Action(new("wait", "", -1, [])), 0, 3, false).Kind == "wait", "Silence was rejected.");
        Check(SocialConversation.Parse(Action(new("say", "@メンバー1 の話を引用しているだけ", -1, [])), 1, 3, false).Call.Count == 0, "An incidental mention became a call.");
        foreach (SocialAction invalid in new[] { new SocialAction("ask", "どう思う？", 0, []), new("ask", "どう思う？", 0, [3]), new("ask", "どう思う？", 0, [1, 1]), new("react", "", 1, [], "😀"), new("draw", "水彩の猫", -1, []) })
        { Reject(() => SocialConversation.Parse(Action(invalid), 1, 3, false)); }
        var budget = new GenerationAllowance(256); Check(budget.Limit(1024) == 256, "A generation exceeded its remaining allowance."); budget.Spend(128, 256);
        Check(budget.Remaining == 128 && budget.Used == 128 && budget.Limit(256) == 128, "Actual token usage was not charged across model calls."); budget.Spend(0, 128);
        bool exhausted = false; try { budget.Limit(256); } catch (ConversationBudgetException) { exhausted = true; }
        Check(exhausted && budget.Remaining == 0, "Missing usage or exhausted budget permitted another model call.");
        Check(SocialConversation.Parse(Action(new("draw", "水彩で描いてみたよ", -1, [], ImagePrompt: "a watercolor cat")), 0, 3, true).ImagePrompt == "a watercolor cat", "An artist-authored image prompt was rejected.");
        string clean = WebSearchService.PlainText("<p>山の説明<sup class='reference' id='cite_ref-7'><a href='#cite_note-7'>[7]</a></sup>。数式 x<sup>2</sup> と配列[3]。<a href='#cite_note-8'>[8]</a></p>");
        Check(!clean.Contains("[7]", StringComparison.Ordinal) && !clean.Contains("[8]", StringComparison.Ordinal) && clean.Contains("x2", StringComparison.Ordinal) && clean.Contains("配列[3]", StringComparison.Ordinal), "Page-local citations leaked into knowledge or legitimate notation was stripped.");
        WorkSource[] sources = [new(1, "資料", "https://example.com/source", "この資料は十分に長い検証用の原文です。", DateTimeOffset.UtcNow)];
        string report = JsonSerializer.Serialize(new { evidence = new[] { new { source = 1, quote = sources[0].Text, claim = "確認した点" } }, sections = new[] { new { heading = "結論", text = "会話に合わせて整理した返事", kind = "fact", sources = new[] { 1 } } } });
        Check(ResearchRunner.TryGroundReport(report, sources, out string conversational, out List<WorkClaim> claims, conversational: true) && claims.Count == 1 && conversational.Contains("[S1](<https://example.com/source>)", StringComparison.Ordinal) && !conversational.Contains("資料に基づく説明", StringComparison.Ordinal) && !conversational.Contains(sources[0].Text, StringComparison.Ordinal), "Conversational research leaked the evidence template or lost its grounded source mapping.");
        Check(SocialConversation.ExplicitCall("まずメンバー1に好きなジャンルを聞いてみて。", accounts.Take(3).ToArray(), account.Id) == 1, "An explicit request to consult another account did not route.");
        foreach (string quoted in new[] { "「メンバー1に聞いて」という文章", "次の文を翻訳: メンバー1に聞いて", "メンバー1に聞かないで", "メンバー1の発言を読んだよ" })
        { Check(SocialConversation.ExplicitCall(quoted, accounts.Take(3).ToArray(), account.Id) is null, "Quoted, refused or incidental account text forced a call."); }
        DateTimeOffset now = DateTimeOffset.UtcNow;
        SavedMessage[] recent = [new("assistant", "工作の話", Identity: new(accounts[1].Id, accounts[1].Name, Time: now)), new("assistant", "なるほど", Identity: new(accounts[2].Id, accounts[2].Name, Time: now))];
        Check(SocialConversation.Choose([account, accounts[1], accounts[2]], recent, now, false, .1)?.Id == account.Id, "Cooldown or last-speaker exclusion failed.");
        Check(SocialConversation.Choose([account], [], now, true, .1) is null && SocialConversation.Choose([account with { Social = new(Participation: 0) }], [], now, false, .1) is null, "Initiation or participation opt-out was ignored.");
        var pending = new ChatMessage { Role = "assistant", Pending = true, IsGenerating = true, Identity = new(account.Id, account.Name) };
        int arrived = 0; var rooms = new System.Collections.ObjectModel.ObservableCollection<Conversation> { room };
        using (var incoming = new IncomingMessages(rooms, (_, _, _, _) => arrived++))
        {
            room.Messages.Add(pending);
            for (int i = 0; i < 200; i++) { pending.Content += "途中"; }
            pending.IsGenerating = false;
            Check(arrived == 0 && !MessageLocations.In(room).Any(m => m.Message == pending.Id) && ChatMessage.Restore(pending.Save()).Pending, "A draft entered unread/search or lost its pending state on disk.");
            pending.Pending = false;
            Check(arrived == 1 && MessageLocations.In(room).Single(m => m.Message == pending.Id).Complete, "A completed post was not delivered exactly once.");
            pending.Content += "。"; Check(arrived == 1, "Repeated updates delivered the same post twice.");
        }
        room.Experience = new(Reactions: [new(room.ActiveBranch.Id, pending.Id, "😀"), new(room.ActiveBranch.Id, pending.Id, "😀", accounts[1].Id)], SocialPaused: true);
        state = state with { Agents = accounts.Select(a => a.Id == account.Id ? account : a).ToList(), Conversations = [room.Save()] };
        AppStore.Validate(state); Directory.CreateDirectory(root); var store = new AppStore(root); await store.SaveAsync(state);
        AppState loaded = await store.LoadAsync();
        Check(loaded.Agents![0].Generation == account.Generation && loaded.Agents[0].Social == account.Social && loaded.Conversations[0].Experience?.SocialPaused == true && loaded.Conversations[0].Experience?.Reactions?.Count == 2,
            "Account tuning, multiple reaction authors or stopped state failed persistence.");
        Console.WriteLine("Social autonomy: twelve starter accounts, 50-member union limit including self, per-account settings, hours/cooldowns, action protocol and incidental mentions, unpublished drafts/unread delivery and multi-author reactions persistence: OK");
    }
}
