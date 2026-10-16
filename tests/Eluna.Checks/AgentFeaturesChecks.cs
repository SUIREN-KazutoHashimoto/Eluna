using System.IO;
using System.Text.Json;
using Eluna.App.Models;
using Eluna.App.Services;

namespace Eluna.Checks;

internal static class AgentFeaturesChecks
{
    private static void Check(bool ok, string text) { if (!ok) { throw new InvalidOperationException(text); } }
    private static void Reject(Action action) { try { action(); } catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException) { return; } throw new InvalidOperationException("Invalid permission or team plan accepted."); }
    internal static async Task RunAsync(string root)
    {
        Guid a = Guid.NewGuid(), b = Guid.NewGuid(), outsider = Guid.NewGuid(), server = Guid.NewGuid();
        var owner = new AgentAccount(a, "進行役", Instruction: "BASE", ModelId: "m", Personality: new("roleplay", "図書館の司書", "月の図書館", "一人称は私"), Connections: [b, outsider], Autonomy: new(true, true, true, true));
        var friend = new AgentAccount(b, "検証役", ModelId: "m"); var unknown = new AgentAccount(outsider, "招待拒否", ModelId: "m", Autonomy: new(AcceptInvites: false));
        var source = new Conversation { Title = "依頼元", ProjectId = server, Room = new("channel", [a, b], a, ReferenceSiblingChats: true, Permissions: [new(a, true, true, true, true)]) };
        var secret = new Conversation { Title = "秘密", ProjectId = server, Room = new("channel", [a], a, ShareHistory: true) }; secret.Messages.Add(new() { Role = "user", Content = "PROJECT_SECRET = moon-library" });
        AppState state = new(4, new(), [source.Save(), secret.Save()], [new(server, "チーム", "", "project-only", true, [], [])], Agents: [owner, friend, unknown]);
        AppStore.Validate(state);
        Check(ChannelAccess.CanSpeak(state, source.Save(), a) && ChannelAccess.CanSpeak(state, source.Save(), b) && !ChannelAccess.CanView(state, source.Save(), outsider), "Membership defaults incorrect.");
        Check(ChannelAccess.CanOrganize(state, source.Save(), a) && !ChannelAccess.CanOrganize(state, source.Save(), b), "Organization grant escaped its account.");
        KnowledgeContext Read(Guid? agent) => KnowledgeService.Build(state, source, "PROJECT_SECRET", 8192, CancellationToken.None, agentId: agent);
        Check(Read(a).References.Any(r => r.SourceConversationId == secret.Id) && !Read(b).References.Any(r => r.Text.Contains("PROJECT_SECRET", StringComparison.Ordinal)) && !Read(null).References.Any(r => r.SourceConversationId == secret.Id), "Private sibling context leaked to another participant/common debate.");
        secret.Room = secret.Room! with { Permissions = [new(a, false, false), new(b, true, false)] }; state = state with { Conversations = [source.Save(), secret.Save()] };
        Check(!Read(a).References.Any(r => r.SourceConversationId == secret.Id) && Read(b).References.Any(r => r.SourceConversationId == secret.Id), "Read-only grants/revocation ignored.");
        Check(!ChannelAccess.CanSpeak(state, secret.Save(), b), "Read-only nonmember could speak.");
        Check(!WorkspaceService.CanRecall(state, source.Save(), secret.Save(), a) && !WorkspaceService.CanRecallMemory(state, source.Save(), new(Guid.NewGuid(), a, "note", "agent", secret.Id, server), a), "Revoked origin leaked through memory.");
        source.Messages.Add(new() { Role = "user", Content = "スレッドの元" });
        var thread = new Conversation { ProjectId = server, Room = new("thread", [a, b], b, secret.Id, secret.Messages[0].Id) };
        state = state with { Conversations = [source.Save(), secret.Save(), thread.Save()] }; AppStore.Validate(state);
        Check(!ChannelAccess.CanView(state, thread.Save(), a) && ChannelAccess.CanSpeak(state, thread.Save(), b), "A thread escalated parent access.");
        Reject(() => ChannelAccess.RequireSpeaker(state, secret.Save(), a));
        Reject(() => AppStore.Validate(state with { Conversations = [source.Save() with { Room = source.Room! with { Permissions = [new(a, false, true)] } }] }));
        string instruction = Read(a).Instruction; Check(instruction.Contains("BASE", StringComparison.Ordinal) && instruction.Contains("図書館の司書", StringComparison.Ordinal) && instruction.Contains("月の図書館", StringComparison.Ordinal), "Persona was not composed with existing instruction.");
        Check(AgentPersonas.Instruction(friend) == "", "Default personality modified existing prompts.");
        Check(TeamPlanner.Candidates(state, source.Save(), owner, new HashSet<string> { "m" }, "m").Select(c => c.Id).SequenceEqual(new[] { a, b }), "Unconnected/declining agents could be invited.");
        var allowed = new HashSet<Guid> { a, b };
        string Plan(Guid actor, bool newServer = true) => JsonSerializer.Serialize(new { organize = true, newServer, server = "独立作業", channels = new[] { new { name = "検討", assignments = new[] { new { agent = actor, task = "具体案をまとめてください" } } } } });
        Check(TeamPlanner.Parse(Plan(b), allowed, true).Channels[0].Assignments[0].Agent == b, "Valid team plan rejected.");
        Reject(() => TeamPlanner.Parse(Plan(outsider), allowed, true)); Reject(() => TeamPlanner.Parse(Plan(b), allowed, false));
        TeamPlan validPlan = TeamPlanner.Parse(Plan(b), allowed, true);
        Reject(() => TeamPlanner.Parse(JsonSerializer.Serialize(validPlan with { Channels = Enumerable.Repeat(validPlan.Channels[0], 4).ToList() }), allowed, true));
        Reject(() => TeamPlanner.Parse(JsonSerializer.Serialize(validPlan with { Channels = [validPlan.Channels[0] with { Assignments = Enumerable.Repeat(new PlannedAssignment(a, "担当"), 7).ToList() }] }), allowed, true));
        string stamp = WorkspaceService.AccessStamp(state, source.Id, a, []);
        Check(stamp != WorkspaceService.AccessStamp(state with { Agents = [owner with { Autonomy = new() }, friend, unknown] }, source.Id, a, []), "Autonomy revocation did not invalidate a queued request.");
        var response = new ChatMessage { Role = "assistant", Team = new(Guid.NewGuid(), a, server, [new(secret.Id, secret.ActiveBranch.Id, b, "結果を確認", Guid.NewGuid(), true)], "paused") };
        source.Messages.Add(response); state = state with { Conversations = [source.Save(), secret.Save(), thread.Save()] };
        var store = new AppStore(root); await store.SaveAsync(state); AppState saved = await store.LoadAsync(); AppStore.Validate(saved);
        Check(saved.Agents!.Single(c => c.Id == a).Personality == owner.Personality && saved.Conversations.Single(c => c.Id == source.Id).Messages.Last().Team!.Assignments[0].Complete, "Personality/team state lost on save.");
        AppState copy = WorkspaceService.ImportCopy(saved); AppStore.Validate(copy);
        TeamRun imported = copy.Conversations.Single(c => c.Title == source.Title).Messages.Last().Team!;
        Check(copy.Agents!.All(c => c.Autonomy?.Automatic != true) && copy.Conversations.Any(c => c.Id == imported.Assignments[0].RoomId) && imported.Assignments[0].RoomId != secret.Id && imported.CoordinatorId != a && copy.Agents!.Single(c => c.Name == owner.Name).Connections!.All(c => copy.Agents!.Any(x => x.Id == c)), "Import retained automation or broke linked IDs.");
        Directory.CreateDirectory(Path.Combine(root, "Backups")); string archive = Path.Combine(root, "Backups", "team.zip"); await BackupArchive.CreateAsync(store, archive, false); var restoredStore = new AppStore(Path.Combine(root, "restored")); PreparedBackup backup = await BackupArchive.PrepareAsync(restoredStore, archive); await BackupArchive.ApplyAsync(restoredStore, backup);
        Check((await restoredStore.LoadAsync()).Conversations.Single(c => c.Id == source.Id).Messages.Last().Team == saved.Conversations.Single(c => c.Id == source.Id).Messages.Last().Team || (await restoredStore.LoadAsync()).Conversations.Single(c => c.Id == source.Id).Messages.Last().Team!.Id == response.Team.Id, "Backup lost team state.");
        Console.WriteLine("Agent features: ACL inheritance/read-only/sibling intersection/memory revocation, persona composition, invitation allowlist/plan bounds, persistence/import/ZIP: OK");
    }
}
