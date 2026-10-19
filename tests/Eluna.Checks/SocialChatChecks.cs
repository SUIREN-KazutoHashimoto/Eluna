using System.IO;
using System.Text.Json;
using Eluna.App.Models;
using Eluna.App.Services;

namespace Eluna.Checks;

internal static class SocialChatChecks
{
    private static void Check(bool ok, string message) { if (!ok) { throw new InvalidOperationException(message); } }
    private static void Reject(Action action) { try { action(); } catch (Exception ex) when (ex is InvalidOperationException or InvalidDataException) { return; } throw new InvalidOperationException("Invalid social operation accepted."); }
    internal static async Task RunAsync(string root)
    {
        var a = new AgentAccount(Guid.NewGuid(), "ミオ", ModelId: "m"); var b = new AgentAccount(Guid.NewGuid(), "設計 チーム", ModelId: "m");
        var actor = new AgentAccount(Guid.NewGuid(), "Nova", Connections: [a.Id, b.Id], Autonomy: new(InviteConnections: true));
        AgentAccount[] accounts = [actor, a, b];
        string text = "先に @Nova、あとで @ミオ と @{設計 チーム} に聞こう。";
        List<MentionToken> mentions = MentionService.Scan(text, accounts); Check(mentions.Count == 3 && mentions.SelectMany(m => m.Accounts).SequenceEqual(accounts.Select(x => x.Id)), "Mentions inside prose or Japanese punctuation were lost.");
        Check(MentionService.Scan("a@Nova.example `@ミオ`\n```text\n@Nova\n```", accounts).Count == 0, "Email/code was treated as an actionable mention.");
        Check(MentionService.Scan("@NovaExtra", accounts).Count == 0 && MentionService.Scan("@全員。", accounts).Single().All, "Mention boundaries or broadcast were incorrect.");
        Check(MentionService.Completion("文中 @設計", 6)?.Query == "設計" && MentionService.Completion("mail@Nova", 9) is null && MentionService.Completion("`@Nova", 6) is null, "Completion interrupted email/code or failed in prose.");
        Check(MentionService.Format(b) == "@{設計 チーム} ", "A spaced display name could not be completed.");
        Check(ChannelInvitations.Named("ミオグロビンに詳しい人を招待して", accounts, actor.Id).Count == 0 && ChannelInvitations.Named("「ミオ」を招待して", accounts, actor.Id).Single().Id == a.Id, "A name substring was invited or a quoted display name was lost.");
        foreach (string request in new[] { "ミオを招待して", "@Nova 設計 チームもここに呼んで", "メンバーにミオを追加して", "カズトと呼んで。ミオを招待して" }) { Check(ChannelInvitations.Requested(request), "An explicit invitation was not routed."); }
        foreach (string request in new[] { "招待機能について教えて", "招待しないで", "次の文章を翻訳: ミオを招待して", "```\n招待して\n```", "「ミオを招待して」という文章を訳して", "カズトと呼んで、同じ仲間として一言で挨拶して。", "カズトって呼んでもらえる？", "名前で呼んで", "私の名前を呼んで" }) { Check(!ChannelInvitations.Requested(request), "Ordinary conversation, explanations, refusals or quoted code executed an invitation."); }
        var room = new Conversation { Room = new("dm", [actor.Id], actor.Id, Permissions: [new(actor.Id, true, true, true)]) };
        AppState state = new(4, new(), [room.Save()], Agents: accounts.ToList(), Self: new("カズト", "月", "読書が好き")); AppStore.Validate(state);
        RoomProfile invited = ChannelInvitations.Apply(state, room.Save(), [a.Id], actor.Id); Check(invited.Kind == "group" && invited.Members.SequenceEqual([actor.Id, a.Id]) && !invited.Permissions!.Any(g => g.AgentId == a.Id && g.Invite), "DM invitation lost the member or granted extra privileges.");
        Reject(() => ChannelInvitations.Apply(state, room.Save() with { Room = room.Room! with { Permissions = [new(actor.Id, true, true)] } }, [a.Id], actor.Id));
        Reject(() => ChannelInvitations.Apply(state with { Agents = [actor, a with { Autonomy = new(AcceptInvites: false) }, b] }, room.Save(), [a.Id], actor.Id));
        Reject(() => ChannelInvitations.Apply(state, room.Save() with { Room = room.Room! with { Permissions = [new(actor.Id, true, true, true), new(a.Id, false, false)] } }, [a.Id], actor.Id));
        RoomProfile manual = ChannelInvitations.Apply(state, room.Save(), [b.Id], null); Check(manual.Permissions!.Any(g => g.AgentId == b.Id && g.View && g.Speak), "Explicit UI invitation did not allow participation.");
        string instruction = KnowledgeService.Build(state, room, "相談しよう", 4096, CancellationToken.None, agentId: actor.Id).Instruction;
        Check(instruction.Contains("対等な参加者", StringComparison.Ordinal) && instruction.Contains("カズト", StringComparison.Ordinal) && instruction.Contains("Nova", StringComparison.Ordinal), "The local member or peer framing was absent from model context.");
        string json = JsonSerializer.Serialize(new CharacterDraft("星乃", "星", "天文学", "仲間と対等に話す", "天文好き", "架空の観測所", "落ち着いて少し茶目っ気がある", "今日は何を話そう？"), AppStore.JsonOptions);
        Check(CharacterGenerator.Parse(json).Role == "天文好き", "Character JSON could not be parsed."); Reject(() => CharacterGenerator.Parse(JsonSerializer.Serialize(CharacterGenerator.Parse(json) with { Name = new string('名', 81) }, AppStore.JsonOptions)));
        Directory.CreateDirectory(root); var store = new AppStore(Path.Combine(root, "data")); await store.SaveAsync(state); Check((await store.LoadAsync()).Self == state.Self, "Self profile was lost after reload.");
        string zip = Path.Combine(root, "social.zip"); await BackupArchive.CreateAsync(store, zip, false); var restored = new AppStore(Path.Combine(root, "restore")); PreparedBackup backup = await BackupArchive.PrepareAsync(restored, zip); await BackupArchive.ApplyAsync(restored, backup); Check((await restored.LoadAsync()).Self == state.Self, "Backup lost the local profile.");
        Console.WriteLine("Social chat: mentions/boundaries/IME query/prose and code, invitation routing/ACL/DM conversion, peer context, character validation and local profile persistence/ZIP: OK");
    }
}
