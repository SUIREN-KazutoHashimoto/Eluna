using System.IO;
using Eluna.App.Models;
using Eluna.App.Services;

namespace Eluna.Checks;

internal static class VoiceChannelChecks
{
    private static void Check(bool ok, string message) { if (!ok) { throw new InvalidOperationException(message); } }
    private static void Reject(Action action) { try { action(); } catch (InvalidDataException) { return; } throw new InvalidOperationException("Invalid voice settings were accepted."); }
    internal static async Task RunAsync(string root)
    {
        var store = new AppStore(root); Guid server = Guid.NewGuid(), account = Guid.NewGuid();
        var agent = new AgentAccount(account, "会話担当", ModelId: "chosen-model", VoiceId: "Windows\\Speech\\Voice", SpeechRate: 3);
        var room = new Conversation { Title = "音声相談", ProjectId = server, Room = new("voice", [account], account, WorkMode: "offline", InferenceMode: "fast") };
        var project = new KnowledgeProject(server, "サーバー", "SERVER_ONLY_INSTRUCTION", "project-only", false, [new(Guid.NewGuid(), "資料", "SERVER_ONLY_DOCUMENT", DateTimeOffset.UtcNow)], [new(Guid.NewGuid(), "SERVER_ONLY_MEMORY", DateTimeOffset.UtcNow)]);
        var state = new AppState(4, new(SelectedProjectId: server), [room.Save()], [project], Agents: [agent], AgentMemories: []);
        await store.SaveAsync(state); AppState loaded = await store.LoadAsync();
        Check(loaded.Conversations.Single().Room?.Kind == "voice" && loaded.Agents!.Single().VoiceId == agent.VoiceId && loaded.Agents![0].SpeechRate == 3, "Voice channels or account voices were lost on reload.");
        KnowledgeContext knowledge = KnowledgeService.Build(loaded, room, "SERVER_ONLY_DOCUMENT SERVER_ONLY_MEMORY", 8192, CancellationToken.None, agentId: account);
        Check(knowledge.Instruction.Contains("SERVER_ONLY_INSTRUCTION", StringComparison.Ordinal), "Voice channel did not use server instructions.");
        var outside = new Conversation { Title = "他のサーバー", Room = new("channel", [account], account) };
        state = loaded with { Conversations = [.. loaded.Conversations, outside.Save()] };
        knowledge = KnowledgeService.Build(state, outside, "SERVER_ONLY_DOCUMENT SERVER_ONLY_MEMORY", 8192, CancellationToken.None, agentId: account);
        Check(!knowledge.Instruction.Contains("SERVER_ONLY", StringComparison.Ordinal) && knowledge.References.All(r => !r.Text.Contains("SERVER_ONLY", StringComparison.Ordinal)), "Server knowledge escaped through a voice account.");
        Reject(() => AppStore.Validate(state with { Agents = [agent with { SpeechRate = 11 }] })); Reject(() => AppStore.Validate(state with { Agents = [agent with { VoiceId = new string('a', 2049) }] }));
        AppState copy = WorkspaceService.ImportCopy(state); AppStore.Validate(copy); Check(copy.Conversations.Any(c => c.Room?.Kind == "voice") && copy.Agents!.Single().VoiceId == agent.VoiceId && copy.Projects!.Single().Id != server, "Voice import broke account settings or server identity.");
        Console.WriteLine("Voice channel storage/import, account voice validation and server knowledge isolation: OK");
    }
}
