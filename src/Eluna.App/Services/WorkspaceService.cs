using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Eluna.App.Models;

namespace Eluna.App.Services;

internal static class WorkspaceService
{
    internal static readonly Guid DefaultAgent = Guid.Parse("5663b202-915f-4bba-8d48-aee04e3d696a");
    internal static AppState Upgrade(AppState state)
    {
        if (state.Agents is not null) { return state; }
        return state with
        {
            SchemaVersion = 4,
            Agents = [new(DefaultAgent, "Eluna", ModelId: state.Settings.ModelId)],
            AgentMemories = [],
            Conversations = state.Conversations.Select(c => c with
            {
                Room = c.Room ?? new(c.ProjectId is null ? "legacy" : "channel", [DefaultAgent], DefaultAgent,
                    ReferenceSiblingChats: state.Projects?.FirstOrDefault(p => p.Id == c.ProjectId)?.ReferenceChats == true, WorkMode: state.Settings.WorkMode, InferenceMode: state.Settings.InferenceMode)
            }).ToList()
        };
    }
    internal static IEnumerable<SavedMessage> Messages(SavedConversation room)
    {
        var seen = new HashSet<Guid>();
        foreach (SavedMessage message in room.Branches is { Count: > 0 } ? room.Branches.SelectMany(b => b.Messages) : room.Messages)
        { if (message.Id is not Guid id || seen.Add(id)) { yield return message; } }
    }
    internal static Guid StableId(Guid parent, string key) => new(SHA256.HashData(Encoding.UTF8.GetBytes(parent + ":" + key)).AsSpan(0, 16));
    internal static SavedMessage[] AddressAgent(IReadOnlyList<SavedMessage> history, AgentAccount? agent)
    {
        if (agent is null || history.LastOrDefault()?.Role != "assistant" || history.LastOrDefault(m => m.Role == "user") is not { } question) { return history.ToArray(); }
        // A broadcast's later replies must answer the user, not continue another assistant's completed turn.
        return [.. history, new("user", "現在の話者は" + agent.Name + "です。他の参加者の返答は参考として扱い、あなた自身の回答を返してください。\n今回の依頼:\n" + question.Content)];
    }
    internal static bool Participated(SavedConversation source, Guid agent) => source.Room?.Members.Contains(agent) == true ||
        source.Room?.PastMembers?.Contains(agent) == true || Messages(source).Any(m => m.Identity?.AgentId == agent || m.Work?.Turns.Any(t => t.AgentId == agent) == true);
    internal static string AccessStamp(AppState state, Guid room, Guid? agent, IReadOnlyList<KnowledgeReference> references) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
    {
        Agent = state.Agents?.Where(a => a.Id == agent).Select(a => new { a.Id, a.Enabled }),
        Rooms = state.Conversations.Where(c => c.Id == room || references.Any(r => r.SourceConversationId == c.Id)).OrderBy(c => c.Id).Select(c => new
        {
            c.Id,
            c.ProjectId,
            c.IsArchived,
            Kind = c.Room?.Kind,
            Members = c.Room?.Members.Order().ToArray(),
            c.Room?.ParentId,
            c.Room?.OriginMessageId,
            c.Room?.ReferenceSiblingChats,
            c.Room?.ShareHistory,
            c.Room?.AcceptAgentMemory,
            Past = (c.Room?.PastMembers ?? []).Order().ToArray()
        }),
        Projects = state.Projects?.Where(p => state.Conversations.Any(c => c.Id == room && c.ProjectId == p.Id)).Select(p => new { p.Id, p.AcceptAgentMemory, p.ReferenceChats, p.MemoryMode }),
        Memories = state.AgentMemories?.Where(m => references.Any(r => r.MemoryId == m.Id))
    })));
    internal static bool CanRecall(AppState state, SavedConversation destination, SavedConversation source, Guid agent)
    {
        if (source.Id == destination.Id || source.IsArchived || source.Room?.ShareHistory != true || !Participated(source, agent)) { return false; }
        return AcceptsMemory(state, destination, agent);
    }
    internal static bool AcceptsMemory(AppState state, SavedConversation destination, Guid agent) =>
        destination.Room?.AcceptAgentMemory == true && destination.Room.Members.Contains(agent) &&
        (destination.ProjectId is null || state.Projects?.Any(p => p.Id == destination.ProjectId && p.AcceptAgentMemory) == true);
    internal static bool CanRecallMemory(AppState state, SavedConversation room, AgentMemory memory, Guid agent)
    {
        if (!memory.Enabled || memory.AgentId != agent || room.Room?.Members.Contains(agent) != true) { return false; }
        if (memory.SourceConversationId is Guid origin && !state.Conversations.Any(c => c.Id == origin && Participated(c, agent))) { return false; }
        return memory.Scope switch
        {
            "room" => memory.SourceConversationId == room.Id || room.Room.ParentId == memory.SourceConversationId,
            "server" => memory.ProjectId is not null && memory.ProjectId == room.ProjectId,
            "agent" => memory.SourceConversationId == room.Id || AcceptsMemory(state, room, agent),
            _ => false
        };
    }
    internal static AppState ImportCopy(AppState source)
    {
        source = Upgrade(source);
        Dictionary<Guid, Guid> agents = source.Agents!.ToDictionary(a => a.Id, _ => Guid.NewGuid());
        Dictionary<Guid, Guid> rooms = source.Conversations.ToDictionary(c => c.Id, _ => Guid.NewGuid());
        Dictionary<Guid, Guid> projects = (source.Projects ?? []).ToDictionary(p => p.Id, _ => Guid.NewGuid());
        var ids = new Dictionary<Guid, Guid>();
        Guid Map(Guid id) { if (!ids.TryGetValue(id, out Guid next)) { ids[id] = next = Guid.NewGuid(); } return next; }
        Guid? Agent(Guid? id) => id is Guid value ? agents.GetValueOrDefault(value, value) : null;
        List<KnowledgeReference>? References(List<KnowledgeReference>? items) => items?.Select(r => r with { SourceConversationId = r.SourceConversationId is Guid id ? rooms.GetValueOrDefault(id) : null, AgentId = Agent(r.AgentId), MemoryId = r.MemoryId is Guid memory ? Map(memory) : null }).ToList();
        DebateRole Role(DebateRole role) => role with { AgentId = Agent(role.AgentId) };
        SavedWork? Work(SavedWork? work) => work is null ? null : work with
        {
            Id = Map(work.Id),
            Options = work.Options with { Roles = work.Options.Participants.Select(Role).ToList() },
            Turns = work.Turns.Select(t => t with { Id = t.Id is Guid id ? Map(id) : Map(StableId(work.Id, t.Round + ":" + t.Role)), AgentId = Agent(t.AgentId), ReplyTo = t.ReplyTo is Guid reply ? Map(reply) : null, Knowledge = References(t.Knowledge) }).ToList(),
            Discussion = work.Discussion is null ? null : work.Discussion with { Inputs = work.Discussion.Inputs?.Select(i => i with { Id = Map(i.Id) }).ToList(), PendingParticipants = work.Discussion.PendingParticipants?.Select(Role).ToList() }
        };
        SavedMessage Message(SavedMessage m) => m with
        {
            Id = m.Id is Guid id ? Map(id) : null,
            Work = Work(m.Work),
            Knowledge = References(m.Knowledge),
            Identity = m.Identity is null ? null : m.Identity with { AgentId = Agent(m.Identity.AgentId), ReplyTo = m.Identity.ReplyTo is Guid reply ? Map(reply) : null, WorkId = m.Identity.WorkId is Guid work ? Map(work) : null },
            Computer = m.Computer is null ? null : m.Computer with { ProjectId = m.Computer.ProjectId is Guid p ? projects[p] : null },
            Artifact = m.Artifact is null ? null : m.Artifact with { JobId = Map(m.Artifact.JobId) }
        };
        return source with
        {
            Settings = source.Settings with
            {
                SelectedProjectId = source.Settings.SelectedProjectId is Guid selected ? projects[selected] : null,
                WorkOptions = source.Settings.WorkOptions is null ? null : source.Settings.WorkOptions with { Roles = source.Settings.WorkOptions.Participants.Select(Role).ToList() }
            },
            Agents = source.Agents!.Select(a => a with { Id = agents[a.Id] }).ToList(),
            AgentMemories = source.AgentMemories?.Select(m => m with { Id = Map(m.Id), AgentId = agents[m.AgentId], SourceConversationId = m.SourceConversationId is Guid id ? rooms[id] : null, ProjectId = m.ProjectId is Guid p ? projects[p] : null }).ToList(),
            Projects = source.Projects?.Select(p => p with { Id = projects[p.Id], UseCommonSkills = false }).ToList(),
            Conversations = source.Conversations.Select(c => c with
            {
                Id = rooms[c.Id],
                ProjectId = c.ProjectId is Guid p ? projects[p] : null,
                SkillIds = [],
                Room = c.Room is null ? null : c.Room with { Members = c.Room.Members.Select(id => agents[id]).ToList(), DefaultAgentId = Agent(c.Room.DefaultAgentId), PastMembers = c.Room.PastMembers?.Select(id => agents[id]).ToList(), ParentId = c.Room.ParentId is Guid parent ? rooms[parent] : null, OriginMessageId = c.Room.OriginMessageId is Guid origin ? Map(origin) : null },
                Messages = c.Messages.Select(Message).ToList(),
                Branches = c.Branches?.Select(b => b with { Id = Map(b.Id), Messages = b.Messages.Select(Message).ToList() }).ToList(),
                ActiveBranchId = c.ActiveBranchId is Guid branch ? Map(branch) : null
            }).ToList(),
            InferenceJobs = null,
            Skills = null
        };
    }
    internal static void Validate(AppState state)
    {
        if (state.Agents is null)
        { if (state.Conversations.Any(c => c.Room is not null) || state.AgentMemories is { Count: > 0 }) { throw new InvalidDataException("エージェント情報がありません。"); } return; }
        if (state.Agents.Count > 500 || state.Agents.Any(a => a is null || a.Id == Guid.Empty || string.IsNullOrWhiteSpace(a.Name) || a.Name.Length > 80 ||
            a.Icon is null || a.Icon.Length > 16 || a.Instruction is null || a.Instruction.Length > 8000 || a.Specialty is null || a.Specialty.Length > 1000) ||
            state.Agents.Select(a => a.Id).Distinct().Count() != state.Agents.Count) { throw new InvalidDataException("エージェントの形式を確認してください。"); }
        HashSet<Guid> agents = state.Agents.Select(a => a.Id).ToHashSet();
        foreach (SavedConversation c in state.Conversations)
        {
            if (c.Room is not { } r) { continue; }
            if (r.Kind is not ("channel" or "dm" or "group" or "thread" or "legacy") || r.Members is null || r.Members.Count is < 1 or > 6 ||
                r.WorkMode is not null && !WorkModes.All.Any(m => m.Id == r.WorkMode) || r.InferenceMode is not null && !InferenceModes.IsValid(r.InferenceMode) ||
                r.Members.Distinct().Count() != r.Members.Count || r.Members.Any(id => !agents.Contains(id)) || r.DefaultAgentId is Guid chosen && !r.Members.Contains(chosen) ||
                r.Kind == "dm" && r.Members.Count != 1 || r.Kind is "dm" or "group" && c.ProjectId is not null ||
                r.PastMembers?.Any(id => !agents.Contains(id)) == true ||
                r.ParentId is Guid parent && (parent == c.Id || !state.Conversations.Any(p => p.Id == parent && p.ProjectId == c.ProjectId && p.Room?.Kind != "thread")) ||
                r.Kind == "thread" && (r.ParentId is null || r.OriginMessageId is null)) { throw new InvalidDataException("チャンネルの所属・参加者・スレッドを確認してください。"); }
            if (r.ParentId is Guid parentId && r.OriginMessageId is Guid origin && !Messages(state.Conversations.First(p => p.Id == parentId)).Any(m => m.Id == origin || m.Work?.Turns.Any(t => (t.Id ?? StableId(m.Work.Id, t.Round + ":" + t.Role)) == origin) == true))
            { throw new InvalidDataException("スレッドの元の発言がありません。"); }
            if (Messages(c).Any(m => m.Identity is { } i && (string.IsNullOrWhiteSpace(i.Name) || i.Name.Length > 80 || i.Icon is null || i.Icon.Length > 16 || i.AgentId is Guid id && !agents.Contains(id))))
            { throw new InvalidDataException("発言のエージェントが不正です。"); }
            foreach (SavedWork work in Messages(c).Where(m => m.Work is not null).Select(m => m.Work!))
            {
                if (work.Turns.Count > 500 || work.Turns.Any(t => t.AgentId is Guid id && !agents.Contains(id)) || work.Options.Participants.Any(r => r.AgentId is Guid id && !agents.Contains(id)) ||
                    work.Discussion?.Inputs is { Count: > 100 } || work.Discussion?.Inputs?.Any(i => i is null || i.Text is null || i.Text.Length > 4000) == true)
                { throw new InvalidDataException("議論の参加者または追加指示が不正です。"); }
                if (work.Discussion?.PendingParticipants is { } pending) { ResearchRunner.ValidateOptions(work.Options with { Roles = pending }); if (pending.Any(r => r.AgentId is Guid id && !agents.Contains(id))) { throw new InvalidDataException("議論の参加者がありません。"); } }
            }
        }
        if (state.AgentMemories?.Any(m => m is null || m.Id == Guid.Empty || !agents.Contains(m.AgentId) || string.IsNullOrWhiteSpace(m.Text) || m.Text.Length > 4000 ||
            m.Scope is not ("room" or "server" or "agent") || m.Scope == "room" && m.SourceConversationId is null || m.Scope == "server" && m.ProjectId is null ||
            m.SourceConversationId is Guid id && !state.Conversations.Any(c => c.Id == id && c.ProjectId == m.ProjectId && Participated(c, m.AgentId)) || m.ProjectId is Guid p && state.Projects?.Any(s => s.Id == p) != true) == true ||
            state.AgentMemories is { Count: > 10000 } || state.AgentMemories?.Select(m => m.Id).Distinct().Count() != state.AgentMemories?.Count)
        { throw new InvalidDataException("エージェントの記憶の形式を確認してください。"); }
    }
}
