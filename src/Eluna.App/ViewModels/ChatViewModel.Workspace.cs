using System.Collections.ObjectModel;
using System.IO;
using Eluna.App.Models;
using Eluna.App.Services;

namespace Eluna.App.ViewModels;

public sealed partial class ChatViewModel
{
    public ObservableCollection<AgentAccount> Agents { get; } = [];
    public ObservableCollection<AgentMemory> AgentMemories { get; } = [];
    public ObservableCollection<AgentChoice> Recipients { get; } = [];
    private Guid? _recipient;
    private bool _selectingRoomAgent;
    private Guid? _replyTo;
    public AgentChoice? SelectedRecipient
    {
        get => Recipients.FirstOrDefault(r => r.Id == _recipient);
        set { if (value is not null) { _recipient = value.Id; if (value.Id is Guid id && SelectedConversation?.Room is { } room && room.Members.Contains(id)) { SelectedConversation.Room = room with { DefaultAgentId = id }; } SelectRecipientModel(); Notify(); } }
    }
    public string RoomLabel => SelectedConversation is { } room ? room.NavigationLabel + (room.Room?.AcceptAgentMemory == true && (SelectedProject is null || SelectedProject.AcceptAgentMemory) ? " · 記憶持込" : " · 場所限定") + (room.Room?.ShareHistory == true ? " · 持出可" : "") : "チャンネルを選択";
    public string RoomMembersLabel => string.Join(" · ", Agents.Where(a => SelectedConversation?.Room?.Members.Contains(a.Id) == true).Select(a => a.Icon + " " + a.Name));
    public string ReplyLabel => _replyTo is Guid id ? "返信先: " + (Timeline.FirstOrDefault(m => m.Id == id)?.Speaker ?? "発言") + "  ·  クリックして解除" : "";
    public bool HasReplyTarget => _replyTo is not null;
    public Guid? ReplyTarget => _replyTo;
    public event Action<ChatMessage>? MessageRequested;
    public void OpenMessage(Guid roomId, Guid messageId)
    {
        Conversation? room = Conversations.FirstOrDefault(c => c.Id == roomId); if (room is null) { return; }
        ConversationBranch? branch = room.Branches.FirstOrDefault(b => b.Messages.Any(m => m.Id == messageId || m.Work?.Turns.Any(t => (t.Id ?? WorkspaceService.StableId(m.Work.Id, t.Round + ":" + t.Role)) == messageId) == true));
        SelectedConversation = room; if (branch is not null) { SelectedBranch = branch; }
        if (Timeline.FirstOrDefault(m => m.Id == messageId) is { } message) { MessageRequested?.Invoke(message); }
    }
    private AgentAccount? DefaultRoomAgent => Agents.FirstOrDefault(a => a.Id == SelectedConversation?.Room?.DefaultAgentId) ?? Agents.FirstOrDefault();
    private void RememberRoomMode()
    { if (SelectedConversation?.Room is { } room) { SelectedConversation.Room = room with { WorkMode = _settings.WorkMode, InferenceMode = _settings.InferenceMode }; } }
    private void SelectRecipientModel()
    {
        if (Agents.FirstOrDefault(a => a.Id == _recipient)?.ModelId is not string id || Models.FirstOrDefault(m => m.Id == id) is not { } model) { return; }
        _selectingRoomAgent = true;
        try { SelectedModel = model; } finally { _selectingRoomAgent = false; }
        if (model.Profile?.SupportsThinking == false) { _settings = _settings with { InferenceMode = "fast" }; }
        Notify(nameof(SelectedMode));
    }
    internal void RefreshRoom()
    {
        RefreshMentionAccounts(); RoomMembers.Clear(); if (SelectedConversation is not null) { RoomMembers.Add(SelfAccount); }
        foreach (Guid member in SelectedConversation?.Room?.Members ?? []) { if (Agents.FirstOrDefault(a => a.Id == member) is { } account) { RoomMembers.Add(account); } }
        Recipients.Clear();
        foreach (Guid id in SelectedConversation?.Room?.Members ?? [])
        { if (Agents.FirstOrDefault(a => a.Id == id) is { } agent) { Recipients.Add(new(id, agent.Icon + " " + agent.Name + (agent.Enabled ? "" : "（無効）"))); } }
        if (Recipients.Count > 1) { Recipients.Add(new(null, "@全員")); }
        if (!Recipients.Any(r => r.Id == _recipient)) { _recipient = SelectedConversation?.Room?.DefaultAgentId ?? Recipients.FirstOrDefault()?.Id; }
        Notify(nameof(SelectedRecipient)); Notify(nameof(RoomLabel)); Notify(nameof(RoomMembersLabel));
        SelectRecipientModel();
        BindTimeline();
        NotifyShell();
    }
    public void ReplyTo(ChatMessage? message)
    { _replyTo = message?.Id; Notify(nameof(ReplyLabel)); Notify(nameof(HasReplyTarget)); }
    public Task SetRecipientModelAsync(LocalModel model)
    { AgentAccount? agent = Agents.FirstOrDefault(a => a.Id == _recipient); return agent is null || agent.ModelId == model.Id ? Task.CompletedTask : SaveAgentAsync(agent with { ModelId = model.Id }); }
    public async Task SaveAgentAsync(AgentAccount agent)
    {
        if (Agents.Any(a => a.Id != agent.Id && a.Name.Equals(agent.Name.Trim(), StringComparison.OrdinalIgnoreCase)) || Self.Name.Equals(agent.Name.Trim(), StringComparison.OrdinalIgnoreCase)) { throw new InvalidOperationException("メンションで区別できるよう、別の表示名を選んでください。"); }
        var next = Agents.Where(a => a.Id != agent.Id).Append(agent).ToList();
        WorkspaceService.Validate(CaptureState() with { Agents = next });
        AgentAccount? before = Agents.FirstOrDefault(a => a.Id == agent.Id);
        if (!agent.Enabled || before is not null && ((before.Autonomy ?? new()) != (agent.Autonomy ?? new()) || !(before.Connections ?? []).SequenceEqual(agent.Connections ?? []))) { CancelWorkspaceRequests(agent: agent.Id); }
        int index = Agents.ToList().FindIndex(a => a.Id == agent.Id);
        if (index < 0) { Agents.Add(agent); } else { Agents[index] = agent; }
        if (agent.Enabled)
        {
            foreach (WorkSession work in Conversations.SelectMany(c => c.Branches).SelectMany(b => b.Messages).Select(m => m.Work).OfType<WorkSession>().Distinct().Where(w => w.Options.DebateStyle == "free" && w.Stage is not ("complete" or "limited" or "moved")))
            {
                List<DebateRole> roles = work.Discussion.PendingParticipants ?? work.Options.Participants.ToList();
                if (!roles.Any(r => r.AgentId == agent.Id)) { continue; }
                string persona = AgentPersonas.Instruction(agent);
                work.Discussion = work.Discussion with { PendingParticipants = roles.Select(r => r.AgentId == agent.Id ? r with { Name = agent.Name, Icon = agent.Icon, Instruction = persona.Length > 3000 ? persona[..3000] : string.IsNullOrWhiteSpace(persona) ? "自分の観点から具体的に検討する。" : persona, ModelId = agent.ModelId } : r).ToList() };
            }
        }
        RefreshRoom(); await PersistSafelyAsync();
    }
    public async Task SaveAgentMemoryAsync(AgentMemory memory)
    {
        List<AgentMemory> next = AgentMemories.Where(m => m.Id != memory.Id).Append(memory).ToList();
        WorkspaceService.Validate(CaptureState() with { AgentMemories = next });
        CancelWorkspaceRequests(agent: memory.AgentId, memory: memory.Id);
        AgentMemories.Clear(); foreach (AgentMemory item in next) { AgentMemories.Add(item); }
        await PersistSafelyAsync();
    }
    public async Task DeleteAgentMemoryAsync(AgentMemory memory)
    { CancelWorkspaceRequests(agent: memory.AgentId, memory: memory.Id); AgentMemories.Remove(memory); await PersistSafelyAsync(); }
    private Task[] CancelWorkspaceRequests(IReadOnlySet<Guid>? rooms = null, Guid? agent = null, Guid? memory = null, Guid? project = null, Guid? exceptJob = null)
    {
        var tasks = new List<Task>(); bool all = rooms is null && agent is null && memory is null && project is null;
        bool Affects(Guid roomId, Guid? actor, IEnumerable<KnowledgeReference> references, WorkSession? work = null)
        {
            return all || rooms?.Contains(roomId) == true || project is not null && Conversations.Any(c => c.Id == roomId && c.ProjectId == project) || agent is not null &&
                (actor == agent || work?.Options.Participants.Any(r => r.AgentId == agent) == true || work?.Discussion.PendingParticipants?.Any(r => r.AgentId == agent) == true) ||
                references.Any(r => rooms is not null && r.SourceConversationId is Guid source && rooms.Contains(source) || memory is not null && r.MemoryId == memory || agent is not null && r.AgentId == agent);
        }
        foreach (InferenceJob job in Inferences.Items.Where(j => j.IsPending).ToArray())
        {
            if (job.Request.Id == exceptJob) { continue; }
            ChatMessage? answer = FindAnswer(job.Request); WorkSession? work = answer?.Work;
            bool teamAffected = answer?.Team?.Assignments.Any(a => rooms?.Contains(a.RoomId) == true || agent == a.AgentId || project is not null && Conversations.Any(c => c.Id == a.RoomId && c.ProjectId == project)) == true;
            if (!teamAffected && !Affects(job.Request.ConversationId, job.Request.Agent?.Id, job.Request.References.Concat(work?.Turns.SelectMany(t => t.Knowledge ?? []) ?? []), work)) { continue; }
            tasks.Add(job.Finished); Inferences.Cancel(job);
        }
        foreach ((Guid answerId, ComputerChatController controller) in _computerControllers.ToArray())
        {
            Conversation? source = Conversations.FirstOrDefault(c => c.Branches.Any(b => b.Messages.Any(m => m.Id == answerId)));
            ChatMessage? answer = source?.Branches.SelectMany(b => b.Messages).FirstOrDefault(m => m.Id == answerId);
            if (source is not null && answer is not null && Affects(source.Id, answer.Identity?.AgentId, answer.Knowledge)) { tasks.Add(controller.Completion); controller.Stop(); }
        }
        foreach (MediaJob job in MediaJobs.Where(j => j.IsPending).ToArray())
        {
            ArtifactContext? context = job.Request?.Context;
            if (context is not null && !Affects(context.Conversation.Id, context.AgentId, context.FrozenReferences ?? [])) { continue; }
            if (context is null && !Affects(job.Request?.ConversationId ?? Guid.Empty, job.Request?.Agent?.Id, [])) { continue; }
            tasks.Add(job.Finished); job.Cancel();
        }
        return tasks.ToArray();
    }
    private async Task StopWorkspaceRequestsAsync(IReadOnlySet<Guid>? rooms = null, Guid? project = null)
    {
        Task[] pending = CancelWorkspaceRequests(rooms, project: project);
        try { await Task.WhenAll(pending); } catch (OperationCanceledException) { }
    }
    private void RemoveRoomMemories(IReadOnlySet<Guid> rooms)
    { foreach (AgentMemory memory in AgentMemories.Where(m => m.SourceConversationId is Guid id && rooms.Contains(id)).ToArray()) { AgentMemories.Remove(memory); } }
    public async Task<Conversation> CreateRoomAsync(string title, string kind, IReadOnlyList<Guid> members, Conversation? parent = null, ChatMessage? origin = null)
    {
        if (!CanNavigate) { throw new InvalidOperationException("現在は作成できません。"); }
        Guid? project = kind is "dm" or "group" ? null : parent?.ProjectId ?? SelectedProject?.Id;
        var room = new Conversation
        {
            Title = title.Trim(),
            ProjectId = project,
            Room = new(kind, members.ToList(), members.FirstOrDefault(), parent?.Id, origin?.Id, WorkMode: _settings.WorkMode, InferenceMode: _settings.InferenceMode)
        };
        if (room.Title.Length is < 1 or > 100) { throw new InvalidDataException("名前は1〜100文字にしてください。"); }
        WorkspaceService.Validate(CaptureState() with { Conversations = [.. Conversations.Select(c => c.Save()), room.Save()] });
        Conversations.Insert(0, room); SelectedConversation = room; await PersistSafelyAsync(); return room;
    }
    public async Task UpdateRoomAsync(Conversation conversation, string title, RoomProfile profile, bool acceptServerMemory)
    {
        if (string.IsNullOrWhiteSpace(title) || title.Length > 100) { throw new InvalidOperationException("名前は1〜100文字にしてください。"); }
        profile = profile with { PastMembers = (profile.PastMembers ?? []).Concat((conversation.Room?.Members ?? []).Except(profile.Members)).Distinct().ToList() };
        AppState next = CaptureState() with { Conversations = Conversations.Select(c => c.Id == conversation.Id ? c.Save() with { Title = title, Room = profile } : c.Save()).ToList() };
        WorkspaceService.Validate(next);
        AppState current = CaptureState();
        next = next with { Projects = next.Projects?.Select(p => p.Id == conversation.ProjectId ? p with { AcceptAgentMemory = acceptServerMemory } : p).ToList() };
        bool policyChanged = WorkspaceService.AccessStamp(current, conversation.Id, null, []) != WorkspaceService.AccessStamp(next, conversation.Id, null, []);
        if (policyChanged) { CancelWorkspaceRequests(Conversations.Where(c => c.Id == conversation.Id || c.Room?.ParentId == conversation.Id).Select(c => c.Id).ToHashSet(), project: current.Projects?.FirstOrDefault(p => p.Id == conversation.ProjectId)?.AcceptAgentMemory != acceptServerMemory ? conversation.ProjectId : null); }
        conversation.Title = title; conversation.Room = profile;
        if (conversation.ProjectId is Guid project)
        { int index = Projects.ToList().FindIndex(p => p.Id == project); if (index >= 0) { Projects[index] = Projects[index] with { AcceptAgentMemory = acceptServerMemory }; } }
        conversation.Summary = ""; RefreshRoom(); NotifyProjectScope(); await PersistSafelyAsync();
    }
    internal IReadOnlyList<AgentAccount> TargetAgents(Conversation conversation, string text)
    {
        List<AgentAccount> members = Agents.Where(a => conversation.Room?.Members.Contains(a.Id) == true).ToList();
        if (members.Count == 0) { return []; }
        List<MentionToken> mentions = MentionService.Scan(text, MentionAccounts);
        if (mentions.Any(m => m.Accounts.Count > 1)) { throw new InvalidDataException("同名のメンバーがいます。プロフィールで区別できる名前を設定してください。"); }
        Guid[] ids = mentions.SelectMany(m => m.Accounts).Where(id => id != AgentAccount.LocalId).Distinct().ToArray();
        if (!ChannelInvitations.Requested(text) && ids.Any(id => !members.Any(a => a.Id == id))) { throw new InvalidOperationException("未参加のメンバーが含まれています。メンバー一覧の「招待」から参加させてください。"); }
        bool all = mentions.Any(m => m.All);
        var mentioned = members.Where(a => ids.Contains(a.Id)).ToList();
        if (ChannelInvitations.Requested(text)) { all = false; mentioned = members.Where(a => mentions.FirstOrDefault() is { Start: 0 } first && first.Accounts.Contains(a.Id)).Take(1).ToList(); }
        IReadOnlyList<AgentAccount> targets = mentioned.Count > 0 ? mentioned : all || _recipient is null ? members : members.Where(a => a.Id == _recipient).ToList();
        AppState state = CaptureState();
        if (mentioned.Count > 0 || !all && _recipient is not null) { foreach (AgentAccount target in targets) { ChannelAccess.RequireSpeaker(state, conversation.Save(), target.Id); } }
        return targets.Where(a => ChannelAccess.CanSpeak(state, conversation.Save(), a.Id)).ToArray();
    }
    internal WorkOptions RoomWorkOptions(Conversation conversation, WorkOptions options)
    {
        if (conversation.Room?.Members.Count is not >= 2) { return options; }
        return options with
        {
            Roles = conversation.Room.Members.Select(id => Agents.First(a => a.Id == id)).Where(a => ChannelAccess.CanSpeak(CaptureState(), conversation.Save(), a.Id)).Select(a => new DebateRole(a.Id.ToString("N"), a.Name,
            string.IsNullOrWhiteSpace(AgentPersonas.Instruction(a)) ? "あなたの専門分野「" + a.Specialty + "」から根拠と異なる視点を示してください。" : AgentPersonas.Instruction(a)[..Math.Min(3000, AgentPersonas.Instruction(a).Length)], a.ModelId, a.Id, a.Icon)).ToList()
        };
    }
    public async Task SetChannelPermissionsAsync(Guid roomId, IReadOnlyList<ChannelGrant> grants)
    {
        Conversation room = Conversations.FirstOrDefault(c => c.Id == roomId) ?? throw new InvalidOperationException("対象のチャンネルがありません。");
        RoomProfile profile = room.Room ?? throw new InvalidOperationException("チャンネル情報がありません。");
        AppState next = CaptureState() with { Conversations = Conversations.Select(c => c.Id == roomId ? c.Save() with { Room = profile with { Permissions = grants.ToList() } } : c.Save()).ToList() };
        WorkspaceService.Validate(next);
        var affected = Conversations.Where(c => c.Id == roomId || c.Room?.ParentId == roomId).Select(c => c.Id).ToHashSet();
        Task[] pending = CancelWorkspaceRequests(affected);
        room.Room = profile with { Permissions = grants.ToList() };
        if (VoiceJoined && !VoiceValid) { await LeaveVoiceAsync(); }
        RefreshRoom(); await PersistSafelyAsync();
        try { await Task.WhenAll(pending); } catch (OperationCanceledException) { }
    }
}
