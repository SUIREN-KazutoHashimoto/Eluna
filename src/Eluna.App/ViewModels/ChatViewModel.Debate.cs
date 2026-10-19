using Eluna.App.Models;
using Eluna.App.Services;

namespace Eluna.App.ViewModels;

public sealed partial class ChatViewModel
{
    private ChatMessage? DebateAnswer(WorkSession work) => Conversations.SelectMany(c => c.Branches).SelectMany(b => b.Messages).FirstOrDefault(m => m.Work == work);
    private async Task<bool> TrySendDebateAsync()
    {
        WorkSession? work = SelectedConversation?.Messages.LastOrDefault(m => m.Work is { Mode: "debate", Options.DebateStyle: "free" } w &&
            w.Stage is not ("complete" or "limited" or "moved") && (w.IsRunning || SelectedWorkMode.Id == "debate"))?.Work;
        if (work is null) { return false; }
        if (PendingAttachments.Count > 0) { Error = "議論への追加資料はサーバーの資料へ登録するか、新しい依頼に添付してください。"; return true; }
        await AddDebateInputAsync(work, false); return true;
    }
    private async Task AddDebateInputAsync(WorkSession work, bool interrupt)
    {
        string text = Input.Trim();
        if (text.Length is < 1 or > 4000 || work.Discussion.Inputs?.Count >= 100) { Error = "追加発言は1〜4,000文字、100件までです。"; return; }
        Conversation? source = Conversations.FirstOrDefault(c => c.Branches.Any(b => b.Messages.Any(m => m.Work == work)));
        if (source is null || DebateAnswer(work) is not { } answer) { return; }
        List<DebateRole> roles = work.Discussion.PendingParticipants ?? work.Options.Participants.ToList();
        Guid mentioned = MentionService.Scan(text, Agents).SelectMany(m => m.Accounts).FirstOrDefault(id => roles.Any(r => r.AgentId == id));
        string? target = roles.FirstOrDefault(r => r.AgentId == mentioned || text.StartsWith("@" + r.Name + " ", StringComparison.OrdinalIgnoreCase) || text.StartsWith("@{" + r.Name + "}", StringComparison.OrdinalIgnoreCase))?.Id;
        Guid id = Guid.NewGuid();
        source.Branches.First(b => b.Messages.Contains(answer)).Messages.Add(new() { Id = id, Role = "user", Content = text, Identity = SelfIdentity(_replyTo) });
        work.Discussion = work.Discussion with { Inputs = [.. work.Discussion.Inputs ?? [], new(id, text, target, Identity: SelfIdentity(_replyTo))] };
        Input = ""; ReplyTo(null); work.Advance();
        if (interrupt)
        {
            InferenceJob? job = Inferences.Items.FirstOrDefault(j => j.Request.AnswerId == answer.Id && j.IsPending);
            if (job is not null) { Inferences.Cancel(job); await job.Finished; }
        }
        if (!work.IsRunning && CanResumeWorkAtSource(answer)) { await ResumeWorkAtSourceAsync(answer); }
        await PersistSafelyAsync();
    }
    public Task InterruptDebateAsync(WorkSession work) => AddDebateInputAsync(work, true);
    public async Task ControlDebateAsync(WorkSession work, string command, string? speaker = null)
    {
        if (DebateAnswer(work) is not { } answer) { return; }
        if (command == "pause") { StopAnswer(answer); return; }
        if (command == "resume") { await ResumeWorkAtSourceAsync(answer); return; }
        if (command is "finish" or "organize")
        {
            if (command == "finish") { work.Discussion = work.Discussion with { Finish = true }; }
            else { work.Discussion = work.Discussion with { Inputs = [.. work.Discussion.Inputs ?? [], new(Guid.NewGuid(), "ここまでの合意点・相違点・未回答の質問を整理してから議論を続けてください。", speaker)] }; }
        }
        if (command == "auto") { work.Discussion = work.Discussion with { Automatic = true }; }
        if (command == "manual") { work.Discussion = work.Discussion with { Automatic = false }; }
        if (speaker is not null) { work.Discussion = work.Discussion with { RequestedSpeaker = speaker }; }
        if (command != "manual") { work.Advance(); }
        if (!work.IsRunning && work.CanResume && command != "manual") { await ResumeWorkAtSourceAsync(answer); }
        await PersistSafelyAsync();
    }
    public async Task SetDebateParticipantsAsync(WorkSession work, IReadOnlyList<Guid> members)
    {
        List<DebateRole> roles = members.Select(id => Agents.First(a => a.Id == id)).Select(a => new DebateRole(a.Id.ToString("N"), a.Name,
            "あなたの専門分野と立場から、具体的な根拠・反論・改善を示してください。", a.ModelId, a.Id, a.Icon)).ToList();
        ResearchRunner.ValidateOptions(work.Options with { Roles = roles });
        if (roles.Any(r => r.ModelId is string id && !Models.Any(m => m.Id == id))) { throw new InvalidOperationException("参加者のモデルを用意してください。"); }
        Conversation source = Conversations.First(c => c.Branches.Any(b => b.Messages.Any(m => m.Work == work)));
        if (source.Room is { } room) { source.Room = room with { Members = members.ToList(), DefaultAgentId = members[0], PastMembers = (room.PastMembers ?? []).Concat(room.Members).Distinct().ToList() }; }
        work.Discussion = work.Discussion with { PendingParticipants = roles };
        work.Activities.Add(new(DateTimeOffset.UtcNow, "participants", "参加者を変更しました", "次の発言から反映します。"));
        RefreshRoom(); await PersistSafelyAsync();
    }
    private void EnsureDebateMembers(Conversation conversation)
    {
        if (conversation.Room?.Members.Count >= 2) { return; }
        var members = new List<Guid>();
        var additions = new List<AgentAccount>();
        foreach (DebateRole role in (_settings.WorkOptions ?? new()).Participants)
        {
            Guid id = role.AgentId ?? WorkspaceService.StableId(conversation.Id, "role:" + role.Id);
            if (!Agents.Any(a => a.Id == id)) { additions.Add(new(id, role.Name, role.Icon, role.Instruction, ModelId: role.ModelId)); }
            members.Add(id);
        }
        RoomProfile old = conversation.Room ?? new("channel", []);
        WorkspaceService.Validate(CaptureState() with { Agents = [.. Agents, .. additions] });
        foreach (AgentAccount account in additions) { Agents.Add(account); }
        conversation.Room = old with { Members = members, DefaultAgentId = members[0], PastMembers = (old.PastMembers ?? []).Concat(old.Members).Distinct().ToList() };
        _recipient = members[0]; RefreshRoom();
    }
}
