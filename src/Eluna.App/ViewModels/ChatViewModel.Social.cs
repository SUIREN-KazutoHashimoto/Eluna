using System.Text;
using System.Windows.Threading;
using Eluna.App.Models;
using Eluna.App.Services;

namespace Eluna.App.ViewModels;

public sealed partial class ChatViewModel
{
    private DispatcherTimer? _socialTimer;
    internal event Action<Guid, string>? SocialDecisionObserved;
    private readonly Dictionary<Guid, CancellationTokenSource> _socialDelays = [];
    private readonly Dictionary<Guid, Guid> _socialCycles = [];
    private readonly Dictionary<Guid, DateTimeOffset> _socialLastStart = [];
    private readonly HashSet<Guid> _socialStopped = [];
    private DateTimeOffset _socialBoot = DateTimeOffset.UtcNow;
    public bool IsSocialChannel => SelectedConversation?.Room?.Social?.Enabled == true;
    public bool SocialStopped => SelectedConversation is { } room && (room.Experience.SocialPaused || _socialStopped.Contains(room.Id));
    public string SocialStatus => !IsSocialChannel ? "自律会話はオフです" : SocialStopped ? "自律会話を停止中" :
        SelectedConversation?.Room?.Social?.Spontaneous == true ? "会話への参加・自発的な話題が有効" : "会話への参加が有効";
    private void StartSocialClock()
    {
        if (_socialTimer is not null || System.Windows.Application.Current is null) { return; }
        _socialBoot = DateTimeOffset.UtcNow;
        _socialTimer = new(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(15) };
        _socialTimer.Tick += SocialTick; _socialTimer.Start();
    }
    private void StopSocialClock()
    {
        if (_socialTimer is not null) { _socialTimer.Stop(); _socialTimer.Tick -= SocialTick; _socialTimer = null; }
        foreach (CancellationTokenSource delay in _socialDelays.Values) { delay.Cancel(); }
        _socialDelays.Clear(); _socialCycles.Clear();
    }
    public void StopSocialConversation(Guid roomId)
    {
        _socialStopped.Add(roomId); CancelSocialCycle(roomId);
        if (Conversations.FirstOrDefault(c => c.Id == roomId) is { } room) { room.Experience = room.Experience with { SocialPaused = true }; _ = PersistSafelyAsync(); }
        Notify(nameof(SocialStopped)); Notify(nameof(SocialStatus));
    }
    public void ResumeSocialConversation(Guid roomId)
    { _socialStopped.Remove(roomId); if (Conversations.FirstOrDefault(c => c.Id == roomId) is { } room) { room.Experience = room.Experience with { SocialPaused = false }; _ = PersistSafelyAsync(); } _socialLastStart[roomId] = DateTimeOffset.UtcNow; Notify(nameof(SocialStopped)); Notify(nameof(SocialStatus)); }
    private void CancelSocialCycle(Guid roomId)
    {
        _socialCycles.Remove(roomId);
        if (_socialDelays.Remove(roomId, out CancellationTokenSource? delay)) { delay.Cancel(); }
        foreach (InferenceJob job in Inferences.Items.Where(j => j.Request.ConversationId == roomId && j.Request.Social is not null && j.IsPending).ToArray()) { Inferences.Cancel(job); }
    }
    private async void SocialTick(object? sender, EventArgs e)
    {
        if (!CanCompose || !HasModel || _submitting || _shuttingDown || _dataMaintenance || Inferences.ActiveCount >= Inferences.Limit) { return; }
        DateTimeOffset now = DateTimeOffset.Now;
        foreach (Conversation room in Conversations.Where(c => !c.IsArchived && c.Room?.Social is { Enabled: true, Spontaneous: true }).ToArray())
        {
            SocialPolicy policy = room.Room!.Social!;
            if (_socialStopped.Contains(room.Id) || room.Experience.SocialPaused || _socialDelays.ContainsKey(room.Id) || Inferences.Contains(room.Id) ||
                !SocialConversation.WithinHours(policy, now.Hour) || now - _socialLastStart.GetValueOrDefault(room.Id, _socialBoot) < TimeSpan.FromMinutes(policy.IntervalMinutes)) { continue; }
            // Read permission and recent messages are evaluated before an account is selected.
            List<SavedMessage> recent = SocialHistory(room.ActiveBranch);
            AgentAccount? actor = SocialConversation.Choose(SocialMembers(room), recent, now, true, Random.Shared.NextDouble());
            _socialLastStart[room.Id] = now;
            if (actor is null) { continue; }
            Guid cycle = Guid.NewGuid(); _socialCycles[room.Id] = cycle;
            try { await QueueAnswerAsync(room, null, agent: actor, social: new(cycle, recent.LastOrDefault()?.Id, policy.MaxTurns, policy.MaxTokens, Initiation: true)); }
            catch (Exception ex) { _lastError = ex; room.Activity = "自律会話を開始できませんでした。処理リストを確認してください。"; }
            break; // One initiation per tick; other channels keep their fair chance next tick.
        }
    }
    private IEnumerable<AgentAccount> SocialMembers(Conversation room)
    {
        AppState state = CaptureState(); SavedConversation saved = room.Save();
        return Agents.Where(a => room.Room?.Members.Contains(a.Id) == true && a.Enabled && ChannelAccess.CanSpeak(state, saved, a.Id) &&
            (a.ModelId is null ? SelectedModel is not null : Models.Any(m => m.Id == a.ModelId))).ToArray();
    }
    private static List<SavedMessage> SocialHistory(ConversationBranch branch) => branch.Messages
        .Where(m => !m.Pending && !m.IsGenerating && !m.WasInterrupted && m.Content.Length > 0 && m.Identity?.Detail != "member-joined").TakeLast(24).Select(m => m.Save()).ToList();
    private async Task BeginSocialReplyAsync(Conversation room, IReadOnlyList<AgentAccount> targets)
    {
        CancelSocialCycle(room.Id);
        SocialPolicy policy = room.Room!.Social!; Guid cycle = Guid.NewGuid(); _socialCycles[room.Id] = cycle;
        _socialLastStart[room.Id] = DateTimeOffset.UtcNow;
        AgentAccount? actor = targets.FirstOrDefault() ?? SocialConversation.Choose(SocialMembers(room), SocialHistory(room.ActiveBranch), DateTimeOffset.UtcNow, false, Random.Shared.NextDouble());
        if (actor is null) { return; }
        await QueueAnswerAsync(room, null, agent: actor, social: new(cycle, room.Messages.LastOrDefault(m => !m.Pending)?.Id,
            policy.MaxTurns, policy.MaxTokens, Prompted: true, Called: targets.Skip(1).Take(3).Select(a => a.Id).ToList()));
    }
    private async Task RunSocialTurnAsync(Conversation context, ChatMessage answer, InferenceJob job, LlamaService engine, CancellationToken token)
    {
        SocialTurn turn = job.Request.Social!; AgentAccount actor = job.Request.Agent!;
        if (turn.RemainingTurns <= 0 || turn.RemainingTokens < 64 || _socialStopped.Contains(context.Id) || context.Room?.Social?.Enabled != true)
        { job.Detail = "会話の上限または停止設定により発言しませんでした"; return; }
        if (_socialCycles.TryGetValue(context.Id, out Guid current) && current != turn.SessionId) { throw new OperationCanceledException(token); }
        _socialCycles[context.Id] = turn.SessionId;
        var allowance = new GenerationAllowance(turn.RemainingTokens); using var quota = engine.WithAllowance(allowance);
        List<SavedMessage> recent = SocialHistory(context.ActiveBranch);
        if (turn.Prompted && await TryArtistRequestAsync(context, answer, job, engine, token))
        {
            MediaJob drawing = MediaJobs.Last(j => j.Request?.Agent?.Id == actor.Id && j.Request.ConversationId == job.Request.ConversationId && j.Request.ArtistGenerated);
            ScheduleSocialNext(job, WorkspaceService.StableId(drawing.Id, "media-message"), turn.Called ?? [], allowance.Used, drawing.Finished); return;
        }
        if (turn.Prompted && context.Messages.Any(m => m.Attachments.Count > 0))
        {
            await GenerateAnswerCoreAsync(context, answer, job, engine, token);
            ScheduleSocialNext(job, answer.Id, turn.Called ?? [], allowance.Used); return;
        }
        if (turn.Prompted && recent.LastOrDefault() is { } request)
        { await PrepareSkillsAsync(answer, job, engine, request.PromptText, token); }
        KnowledgeContext knowledge = SkillRunner.WithKnowledge(new(job.Request.Instruction, job.Request.References), answer.Skills); answer.Knowledge = knowledge.References;
        // Include only permitted channel members. The local member is a peer and may be called, but is never auto-generated.
        List<AgentAccount> members = [SelfAccount, .. SocialMembers(Conversations.First(c => c.Id == context.Id))];
        await engine.EnsureModelAsync(job.Request.Model, job.Request.Settings, message => job.Detail = message, token);
        job.Detail = turn.Initiation ? actor.Name + " が話題を考えています…" : actor.Name + " が会話を読んでいます…";
        var json = new StringBuilder();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromMinutes(3));
        int? explicitCall = turn.Prompted ? SocialConversation.ExplicitCall(recent.LastOrDefault()?.Content ?? "", members, actor.Id) : null;
        string? requestedEmoji = turn.Prompted && explicitCall is null ? SocialConversation.ExplicitReaction(recent.LastOrDefault()?.Content ?? "") : null;
        string instruction = knowledge.Instruction + "\n" + SocialConversation.Instruction(actor, turn.Prompted) + (explicitCall is int target ? "\nThis is an explicit request to ask " + members[target].Name + " for their opinion. Generate the actual question to them using kind ask, call [" + target + "]. Do not merely promise to ask." : "");
        string referenceText = knowledge.References.Count == 0 ? "" : "\n" + knowledge.ReferenceText;
        string input = SocialConversation.Input(recent, members, turn) + referenceText;
        while (recent.Count > 1 && await engine.CountInputTokensAsync([new("user", input)], instruction, deadline.Token) > job.Request.Settings.ContextSize * .65)
        { recent.RemoveAt(0); input = SocialConversation.Input(recent, members, turn) + referenceText; }
        while (referenceText.Length > 200 && await engine.CountInputTokensAsync([new("user", input)], instruction, deadline.Token) > job.Request.Settings.ContextSize * .65)
        { int length = referenceText.Length / 2; if (char.IsHighSurrogate(referenceText[length - 1])) { length--; } referenceText = referenceText[..length]; input = SocialConversation.Input(recent, members, turn) + referenceText; }
        await foreach (GenerationUpdate piece in engine.GenerateAsync([new("user", input)],
            InferenceModes.Find(job.Request.Settings.InferenceMode), deadline.Token,
            instruction, Math.Min(1024, turn.RemainingTokens), SocialConversation.CreateSchema(recent.Count, members.Count, members.FindIndex(a => a.Id == actor.Id), actor.Artist?.Enabled == true, explicitCall, requestedEmoji), preserveSampling: true))
        { json.Append(piece.Content); }
        token.ThrowIfCancellationRequested();
        SocialDecisionObserved?.Invoke(job.Request.Id, json.ToString());
        DiagnosticScope.Current?.Content("conversation", "decision", json.ToString());
        SocialAction action = SocialConversation.Parse(json.ToString(), recent.Count, members.Count, actor.Artist?.Enabled == true);
        DiagnosticScope.Current?.Write("info", "conversation", "action", "kind=" + action.Kind + "; calls=" + string.Join(",", action.Call) + "; reply=" + action.Reply);

        Guid? reply = action.Reply >= 0 ? recent[action.Reply].Id : null;
        var calls = action.Kind == "ask" ? action.Call.Select(i => members[i].Id).Where(id => id != actor.Id).Distinct().ToList() : new List<Guid>();
        Task? waitForMedia = null; Guid? mediaPost = null;
        if (action.Kind is "say" or "reply" or "ask")
        {
            answer.Content = action.Text;
            if (action.Kind == "ask")
            { answer.Content = string.Join(" ", calls.Select(id => MentionService.Format(members.First(a => a.Id == id)).TrimEnd())) + " " + answer.Content; }
            answer.Identity = answer.Identity! with { ReplyTo = action.Kind is "reply" or "ask" ? reply : null, Detail = "会話" };
            job.Detail = "発言を投稿しました";
        }
        else if (action.Kind == "react")
        {
            Conversation room = Conversations.First(c => c.Id == context.Id); Guid branch = job.Request.BranchId;
            if (reply is Guid id && MessageLocations.In(room).Any(m => m.Branch == branch && m.Message == id))
            {
                LocalReaction reaction = new(branch, id, action.Emoji, actor.Id);
                List<LocalReaction> reactions = room.Experience.Reactions ?? [];
                if (!reactions.Contains(reaction) && reactions.Count < 1000) { room.Experience = room.Experience with { Reactions = [.. reactions, reaction] }; ApplyTimelineExperience(); ExperienceChanged(); }
            }
            job.Detail = "リアクションしました";
        }
        else if (action.Kind == "research")
        {
            if (!context.Messages.Any(m => m.Role == "user"))
            { SavedMessage? trigger = recent.FirstOrDefault(m => m.Id == turn.TriggerId) ?? recent.LastOrDefault(); if (trigger is not null) { context.Messages.Add(ChatMessage.Restore(trigger with { Role = "user" })); } }
            await GenerateAnswerCoreAsync(context, answer, job, engine, token, allowTeam: false);
        }
        else if (action.Kind == "draw")
        {
            if (!turn.Prompted && actor.Artist?.Spontaneous != true) { job.Detail = "自発的な描画はオフです"; return; }
            MediaJob drawing = await QueueArtistImageAsync(actor, job, action.Text, action.ImagePrompt, reply, token);
            waitForMedia = drawing.Finished; mediaPost = WorkspaceService.StableId(drawing.Id, "media-message");
            job.Detail = "画像生成を予約しました。完成後に投稿します";
        }
        else { job.Detail = "今回は発言せずに見守っています"; }
        int spent = allowance.Used;
        job.Metrics = "会話の残り " + Math.Max(0, turn.RemainingTurns - 1) + " 発言機会 / " + Math.Max(0, turn.RemainingTokens - spent) + " token";
        if (action.Kind != "wait" || turn.Called?.Count > 0)
        { ScheduleSocialNext(job, mediaPost ?? (answer.Content.Length > 0 ? answer.Id : turn.TriggerId), [.. turn.Called ?? [], .. calls], spent, waitForMedia); }
    }
    private void ScheduleSocialNext(InferenceJob job, Guid? trigger, List<Guid> called, int spent, Task? waitForMedia = null)
    {
        SocialTurn previous = job.Request.Social!;
        Conversation? room = Conversations.FirstOrDefault(c => c.Id == job.Request.ConversationId);
        if (room?.Room?.Social is not { Enabled: true } policy || previous.RemainingTurns <= 1 || previous.RemainingTokens - spent < 64) { return; }
        if (_socialDelays.Remove(room.Id, out CancellationTokenSource? old)) { old.Cancel(); }
        var delay = new CancellationTokenSource(); _socialDelays[room.Id] = delay;
        _ = ContinueSocialAsync();
        async Task ContinueSocialAsync()
        {
            try
            {
                await job.Finished.WaitAsync(delay.Token);
                if (waitForMedia is not null) { await waitForMedia.WaitAsync(delay.Token); }
                await Task.Delay(TimeSpan.FromSeconds(policy.PaceSeconds), delay.Token);
                if (_shuttingDown || !CanCompose || !Conversations.Contains(room) || room.IsArchived || room.ActiveBranch.Id != job.Request.BranchId || room.Room?.Social?.Enabled != true ||
                    _socialStopped.Contains(room.Id) || room.Experience.SocialPaused || _socialCycles.GetValueOrDefault(room.Id) != previous.SessionId || job.State != "complete") { return; }
                AgentAccount[] eligible = SocialMembers(room).ToArray();
                // A direct call on the local member is left for that person to answer.
                AgentAccount? next = called.Where(id => id != AgentAccount.LocalId).Select(id => eligible.FirstOrDefault(a => a.Id == id)).FirstOrDefault(a => a is not null);
                bool prompted = next is not null;
                if (next is null && !called.Contains(AgentAccount.LocalId) && room.Room.Social.Interjections)
                { next = SocialConversation.Choose(eligible, SocialHistory(room.ActiveBranch), DateTimeOffset.UtcNow, false, Random.Shared.NextDouble()); }
                if (next is null) { return; }
                await QueueAnswerAsync(room, null, agent: next, social: previous with
                {
                    TriggerId = trigger,
                    RemainingTurns = previous.RemainingTurns - 1,
                    RemainingTokens = previous.RemainingTokens - spent,
                    Prompted = prompted,
                    Initiation = false,
                    Called = called.Where(id => id != next.Id).Take(3).ToList()
                });
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { _lastError = ex; room.Activity = "会話の継続を停止しました。処理リストを確認してください。"; }
            finally { if (_socialDelays.TryGetValue(room.Id, out CancellationTokenSource? current) && ReferenceEquals(current, delay)) { _socialDelays.Remove(room.Id); } delay.Dispose(); }
        }
    }
}
