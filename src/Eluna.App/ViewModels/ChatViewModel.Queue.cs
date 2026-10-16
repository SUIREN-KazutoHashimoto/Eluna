using Eluna.App.Models;
using Eluna.App.Services;

namespace Eluna.App.ViewModels;

public sealed partial class ChatViewModel
{
    private readonly Stack<LlamaService> _idleEngines = new();
    private readonly HashSet<LlamaService> _busyEngines = [];
    private bool _submitting, _shuttingDown, _modelsChanging;
    public InferenceQueue Inferences { get; }
    internal IReadOnlyList<string> ActiveComputeLabels => _busyEngines.Select(e => e.ComputeLabel).ToArray();
    private long EngineMemoryBytes => _llama.MemoryUsageBytes + _busyEngines.Sum(e => e.MemoryUsageBytes) + _idleEngines.Sum(e => e.MemoryUsageBytes) + _mediaEngines.Values.Sum(e => e.MemoryUsageBytes);
    public bool CanNavigate => !_shuttingDown && !_dataMaintenance;
    public bool CanCompose => _ready && !_shuttingDown && !_modelsChanging && !_dataMaintenance;
    public bool CanEditConversation => CanGenerate && !_submitting && SelectedConversation is not null && !Inferences.Contains(SelectedConversation.Id) && !HasComputerPending(SelectedConversation.Id);
    public string QueueLabel => $"推論 · 実行 {Inferences.ActiveCount} / 予約 {Inferences.WaitingCount}" + (Inferences.Items.Any(j => j.CanResume) ? " · 再開待ちあり" : "");
    private InferenceJob? SelectedJob => Inferences.Items.LastOrDefault(j => j.Request.ConversationId == SelectedConversation?.Id && j.Request.BranchId == SelectedBranch?.Id && j.State == "active") ??
        Inferences.Items.LastOrDefault(j => j.Request.ConversationId == SelectedConversation?.Id && j.Request.BranchId == SelectedBranch?.Id);
    public bool CanStopSelected => SelectedComputer is not null || SelectedJob?.State == "active" || _generation is not null;
    public string StopTip => SelectedComputer is not null ? "表示中の会話のPC操作を停止" : SelectedJob?.State == "active" ? "表示中の会話の生成を停止" : "画像・文書・モデルの準備を停止";
    public string SendLabel => SelectedComputer is not null ? "追加指示を送信" : !_ready ? "起動の準備中です" : !HasModel ? "設定の「モデルを探す」からモデルを追加してください" : _modelsChanging ? "モデルの準備が終わるまで待っています" : _submitting ? "前の依頼を予約しています" : Inferences.Items.Count(j => j.IsPending) >= 100 ? "予約上限の100件です。処理リストから予約を整理してください" : _isBusy || MediaJobs.Any(j => j.IsPending) || Inferences.ActiveCount >= Inferences.Limit || SelectedConversation is not null && Inferences.Contains(SelectedConversation.Id) ? "予約して送信" : "送信";
    private void QueueChanged()
    {
        VoiceQueueChanged();
        Notify(nameof(TypingNotice));
        Notify(nameof(ProcessSummary)); PumpMedia(); Notify(nameof(IsBusy)); Notify(nameof(QueueLabel)); Notify(nameof(Status)); Notify(nameof(Metrics));
        Notify(nameof(CanStopSelected)); Notify(nameof(SendLabel)); RefreshCommands();
        foreach (Conversation conversation in Conversations)
        {
            InferenceJob? job = Inferences.Items.FirstOrDefault(j => j.Request.ConversationId == conversation.Id && j.IsPending);
            conversation.Activity = job is null ? "" : job.Label;
        }
        foreach (InferenceJob job in Inferences.Items.Where(j => j.State == "canceled"))
        {
            ChatMessage? answer = FindAnswer(job.Request);
            if (answer is { IsGenerating: false, Content.Length: 0 }) { answer.Notice = "予約をキャンセルしました"; }
        }
        if (_ready && !_submitting && !_shuttingDown) { _ = PersistSafelyAsync(); }
    }
    private ChatMessage? FindAnswer(SavedInferenceJob request) => Conversations.FirstOrDefault(c => c.Id == request.ConversationId)?.Branches
        .FirstOrDefault(b => b.Id == request.BranchId)?.Messages.FirstOrDefault(m => m.Id == request.AnswerId);
    private void NotifyJob(InferenceJob job)
    {
        Notify(nameof(TypingNotice));
        if (ReferenceEquals(SelectedJob, job)) { Notify(nameof(Status)); Notify(nameof(Metrics)); }
    }
    private void ScrollJob(InferenceJob job, bool force)
    {
        if (SelectedConversation?.Id == job.Request.ConversationId && SelectedBranch?.Id == job.Request.BranchId) { ScrollRequested?.Invoke(force); }
    }
    private void StopSelected()
    {
        if (SelectedComputer is { } computer) { computer.Invoke("stop"); return; }
        if (SelectedJob is { State: "active" } job) { Inferences.Cancel(job); }
        else { _generation?.Cancel(); }
    }
    private Task GenerateAnswerAsync(Conversation conversation, WorkSession? previousWork = null) => QueueAnswerAsync(conversation, previousWork);
    private async Task QueueAnswerAsync(Conversation conversation, WorkSession? previousWork, ChatMessage? resumeAnswer = null, AgentAccount? agent = null)
    {
        if (!CanGenerate || SelectedModel is null) { return; }
        if (Inferences.Items.Count(j => j.IsPending) >= 100) { Error = "推論の予約は100件までです。"; return; }
        ConversationBranch branch = conversation.ActiveBranch;
        if (previousWork is null && _settings.WorkMode == "debate") { EnsureDebateMembers(conversation); }
        agent ??= Agents.FirstOrDefault(a => a.Id == (resumeAnswer?.Identity?.AgentId ?? conversation.Room?.DefaultAgentId));
        if (agent is { Enabled: false }) { Error = "このエージェントは無効です。"; return; }
        LocalModel model = agent?.ModelId is string modelId ? Models.FirstOrDefault(m => m.Id == modelId) ?? throw new InvalidOperationException(agent.Name + "のモデルがありません。エージェント設定から選択してください。") : SelectedModel;
        AppSettings settings = EffectiveSettings with { WorkMode = conversation.Room?.WorkMode ?? _settings.WorkMode, InferenceMode = conversation.Room?.InferenceMode ?? _settings.InferenceMode, ModelId = model.Id, ContextSize = model.Options.ContextSize ?? EffectiveSettings.ContextSize, SelectedProjectId = conversation.ProjectId, WorkOptions = RoomWorkOptions(conversation, _settings.WorkOptions ?? new()) };
        List<LocalModel> models = [.. Models];
        var answer = resumeAnswer ?? new ChatMessage
        {
            Role = "assistant",
            Mode = settings.InferenceMode,
            Notice = "推論を予約しています…",
            Identity = agent is null ? null : new(agent.Id, agent.Name, agent.Icon, model.Id, model.DisplayName, DateTimeOffset.UtcNow, conversation.Messages.LastOrDefault(m => m.Role == "user")?.Id)
        };
        if (resumeAnswer is null) { branch.Messages.Add(answer); }
        conversation.UpdatedAt = DateTimeOffset.UtcNow; Notify(nameof(IsEmpty)); ScrollRequested?.Invoke(true);
        SavedMessage? question = branch.Messages.TakeWhile(m => m != answer).LastOrDefault(m => m.Role == "user")?.Save();
        if (question is null) { return; }
        SkillRequest skillRequest;
        try { skillRequest = SkillRunner.Capture(Skills, conversation.ProjectId, Projects.FirstOrDefault(p => p.Id == conversation.ProjectId)?.UseCommonSkills == true, conversation.SkillsMode ?? _settings.SkillMode, conversation.SkillIds, question.Content); }
        catch (Exception ex) { Error = ex.Message; answer.Notice = "スキルの設定を確認してから再生成してください。"; return; }
        AppState snapshot = CaptureState();
        Conversation captured = Conversation.Restore(conversation.Save());
        _submitting = true; RefreshCommands();
        try
        {
            KnowledgeContext knowledge = await Task.Run(() => KnowledgeService.Build(snapshot, captured, previousWork?.Question ?? question.Content, settings.ContextSize, CancellationToken.None, agentId: settings.WorkMode == "debate" ? null : agent?.Id));
            if (_shuttingDown) { return; }
            var request = new SavedInferenceJob(Guid.NewGuid(), conversation.Id, branch.Id, answer.Id, conversation.Title,
                DateTimeOffset.UtcNow, model, models, settings, knowledge.Instruction, knowledge.References, previousWork?.Save(), resumeAnswer is not null, skillRequest, agent,
                WorkspaceService.AccessStamp(snapshot, conversation.Id, agent?.Id, knowledge.References));
            InferenceJob job = Inferences.Add(request, paused: true);
            job.PropertyChanged += (_, _) => NotifyJob(job);
            await _store.SaveAsync(CaptureState());
            answer.Notice = "予約中 · 推論一覧からキャンセルできます";
            Inferences.Resume(job);
        }
        catch (Exception ex) { Error = DiagnosticsService.Explain(ex); answer.Notice = "予約を保存できませんでした。推論一覧を確認してください。"; }
        finally { _submitting = false; RefreshCommands(); if (!_shuttingDown) { await PersistSafelyAsync(); } }
    }
    private async Task ExecuteInferenceAsync(InferenceJob job, CancellationToken token)
    {
        SavedInferenceJob request = job.Request;
        Conversation source = Conversations.FirstOrDefault(c => c.Id == request.ConversationId) ?? throw new InvalidOperationException("予約先の会話がありません。");
        if (request.AccessStamp is not null && request.AccessStamp != WorkspaceService.AccessStamp(CaptureState(), source.Id, request.Agent?.Id, request.References))
        { throw new InvalidOperationException("参加者または記憶の参照設定が変更されています。予約をキャンセルして送信し直してください。"); }
        if (source.ProjectId != request.Settings.SelectedProjectId) { throw new InvalidOperationException("会話の所属が変更されています。予約をキャンセルして送信し直してください。"); }
        ConversationBranch branch = source.Branches.First(b => b.Id == request.BranchId);
        ChatMessage answer = branch.Messages.First(m => m.Id == request.AnswerId);
        var history = branch.Messages.TakeWhile(m => m != answer).Where(m => m.Role == "user" || m.Content.Length > 0).Select(m => m.Save()).ToList();
        Conversation context = Conversation.Restore(new(source.Id, source.Title, history, CustomInstruction: source.CustomInstruction));
        context.Summary = branch.Summary; context.SummaryThrough = Math.Min(branch.SummaryThrough, history.Count);
        context.ProjectId = source.ProjectId;
        context.Room = source.Room;
        if (source.Room is { ParentId: Guid parentId, OriginMessageId: Guid origin } && Conversations.FirstOrDefault(c => c.Id == parentId && c.ProjectId == source.ProjectId) is { } parent)
        {
            List<SavedMessage> ancestors = new(); var seen = new HashSet<Guid>();
            ConversationBranch parentBranch = parent.Branches.FirstOrDefault(b => b.Messages.Any(m => m.Id == origin || m.Work?.Turns.Any(t => (t.Id ?? WorkspaceService.StableId(m.Work.Id, t.Round + ":" + t.Role)) == origin) == true))
                ?? throw new InvalidOperationException("スレッドの元の発言がありません。");
            foreach (ChatMessage message in parentBranch.Messages)
            {
                if (message.Work is { Mode: "debate" } parentWork)
                {
                    bool found = false;
                    foreach (DebateTurn turn in parentWork.Turns)
                    {
                        Guid id = turn.Id ?? WorkspaceService.StableId(parentWork.Id, turn.Round + ":" + turn.Role);
                        if (seen.Add(id)) { ancestors.Add(new(turn.Kind == "user" ? "user" : "assistant", turn.Content, Id: id, Incomplete: turn.Incomplete, Identity: new(turn.AgentId, turn.Name ?? turn.Role, turn.Icon))); }
                        if (id == origin) { found = true; break; }
                    }
                    if (found) { break; }
                }
                if (seen.Add(message.Id)) { ancestors.Add(message.Save() with { Work = null, Computer = null }); }
                if (message.Id == origin) { break; }
            }
            var threadContext = Conversation.Restore(new(source.Id, source.Title, ancestors.TakeLast(12).Concat(history).ToList(), CustomInstruction: source.CustomInstruction, ProjectId: source.ProjectId, Room: source.Room));
            context = threadContext;
        }
        LlamaService engine = _idleEngines.TryPop(out LlamaService? idle) ? idle : _llama.CreateWorker();
        _busyEngines.Add(engine);
        try
        {
            answer.Notice = ""; answer.Content = ""; answer.IsGenerating = true; answer.WasInterrupted = false;
            if (request.ResumeWork && answer.Work is not null) { if (answer.Skills.Count == 0) { await PrepareSkillsAsync(answer, job, engine, answer.Work.Question, token); } await RunWorkCoreAsync(answer, token, job, engine, context); job.Detail = answer.Work.Label; }
            else { await GenerateAnswerCoreAsync(context, answer, job, engine, token, request.PreviousWork is null ? null : WorkSession.Restore(request.PreviousWork)); }
            token.ThrowIfCancellationRequested();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        { answer.Notice = "生成を停止しました"; answer.WasInterrupted = true; throw; }
        catch (Exception ex)
        {
            engine.Stop(); answer.Notice = "処理を完了できませんでした。推論一覧から再試行またはキャンセルしてください。";
            answer.WasInterrupted = true;
            _lastError = ex; if (SelectedConversation?.Id == source.Id) { SetError(DiagnosticsService.Explain(ex), () => { if (job.CanResume) { Inferences.Resume(job); } return Task.CompletedTask; }); }
            throw;
        }
        finally
        {
            answer.IsGenerating = false; answer.IsThinking = false; source.UpdatedAt = DateTimeOffset.UtcNow;
            branch.Summary = context.Summary; branch.SummaryThrough = context.SummaryThrough;
            _busyEngines.Remove(engine);
            if (token.IsCancellationRequested || _shuttingDown || _idleEngines.Count + _busyEngines.Count >= Inferences.Limit) { engine.Dispose(); }
            else { _idleEngines.Push(engine); }
            await PersistSafelyAsync();
        }
    }
    public void OpenInference(InferenceJob job)
    {
        Conversation? conversation = Conversations.FirstOrDefault(c => c.Id == job.Request.ConversationId);
        if (conversation is null) { return; }
        SelectedConversation = conversation; ShowArchived = conversation.IsArchived;
        SelectedBranch = conversation.Branches.FirstOrDefault(b => b.Id == job.Request.BranchId);
    }
    private void RestoreInferences(IEnumerable<SavedInferenceJob> requests)
    {
        Inferences.Limit = _settings.MaxConcurrentInferences;
        foreach (SavedInferenceJob request in requests)
        {
            InferenceJob job = Inferences.Add(request, paused: true);
            job.PropertyChanged += (_, _) => NotifyJob(job);
            if (FindAnswer(request) is ChatMessage answer) { answer.Notice = "再開待ち · 推論一覧から再開またはキャンセルしてください"; }
        }
    }
    public bool CanResumeWorkAtSource(ChatMessage answer) => CanGenerate && Conversations.Any(c => !Inferences.Contains(c.Id) && c.Branches.Any(b => b.Messages.Contains(answer)));
    public void OpenAnswer(ChatMessage answer)
    {
        Conversation? conversation = Conversations.FirstOrDefault(c => c.Branches.Any(b => b.Messages.Contains(answer)));
        if (conversation is null) { return; }
        SelectedConversation = conversation; SelectedBranch = conversation.Branches.First(b => b.Messages.Contains(answer));
    }
    public Task ResumeWorkAtSourceAsync(ChatMessage answer) { if (!CanResumeWorkAtSource(answer)) { return Task.CompletedTask; } OpenAnswer(answer); return ResumeWorkAsync(answer); }
    public void StopAnswer(ChatMessage answer)
    {
        InferenceJob? job = Inferences.Items.FirstOrDefault(j => j.Request.AnswerId == answer.Id && j.State == "active");
        if (job is not null) { Inferences.Cancel(job); }
    }
    private void ReleaseIdleEngines()
    {
        while (_idleEngines.TryPop(out LlamaService? engine)) { engine.Dispose(); }
    }
}
