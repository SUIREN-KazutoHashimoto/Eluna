using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using Eluna.App.Models;
using Eluna.App.Services;

namespace Eluna.App.ViewModels;

public sealed partial class ChatViewModel
{
    public ObservableCollection<MediaJob> MediaJobs { get; } = [];
    private readonly Dictionary<MediaJob, Task> _mediaWorkers = [];
    private readonly Dictionary<MediaJob, LlamaService> _mediaEngines = [];
    private string? _mediaRecoveryNotice;
    private bool _mediaReadOnly;
    internal IReadOnlyList<string> ActiveMediaComputeLabels => _mediaEngines.Values.Select(e => e.ComputeLabel).ToArray();
    private readonly AsyncLocal<(MediaJob Job, LlamaService Engine)?> _mediaContext = new();
    private readonly SemaphoreSlim _mediaSaveGate = new(1, 1);
    private readonly CancellationTokenSource _mediaShutdown = new();
    private bool _pumpingMedia, _mediaSuspended, _suspensionBeforeMedia;
    private bool HasMediaReservations => MediaJobs.Any(j => j.IsPending || j.CanResume);
    public string ProcessSummary
    {
        get
        {
            int active = Inferences.ActiveCount + Downloads.Items.Count(d => d.Status == "active") + MediaJobs.Count(m => m.State == "active") + ComputerActivities.Count(c => c.State == "active");
            int waiting = Inferences.WaitingCount + Downloads.Items.Count(d => d.Status == "queued") + MediaJobs.Count(m => m.State == "queued") + ComputerActivities.Count(c => c.State == "queued");
            int attention = Inferences.Items.Count(i => i.State is "paused" or "failed") + Downloads.Items.Count(d => d.Status is "paused" or "failed") + MediaJobs.Count(m => m.CanResume) + ComputerActivities.Count(c => c.State is "paused" or "failed");
            return active + waiting > 0 ? $"実行 {active} · 予約 {waiting}" : attention > 0 ? $"要確認 {attention}" : "";
        }
    }
    public Task QueueMediaOperationAsync(string title, Func<CancellationToken, Task> operation, CancellationToken token)
    { CheckMediaCapacity(); var job = new MediaJob(title, operation, token); AddMediaJob(job); PumpMedia(); return job.Finished; }
    private void CheckMediaCapacity()
    {
        if (!_ready || _modelsChanging || _shuttingDown || _dataMaintenance) { throw new InvalidOperationException("起動の準備・モデル管理・バックアップ・終了処理が終わってから予約してください。"); }
        if (MediaJobs.Count(j => j.IsPending || j.CanResume) >= 20) { throw new InvalidOperationException("作成処理の予約は20件までです。処理リストで整理してください。"); }
    }
    private void AddMediaJob(MediaJob job)
    {
        MediaJobs.Add(job); job.PropertyChanged += (_, e) => { Notify(nameof(ProcessSummary)); SyncMediaMessage(job); if (e.PropertyName == nameof(MediaJob.State) && job.Request is not null) { _ = SaveMediaJobsAsync(); } };
        SyncMediaMessage(job);
        _ = ObserveMediaAsync(job); _ = AwaitMediaCancellationAsync(job);
    }
    private static async Task ObserveMediaAsync(MediaJob job) { try { await job.Finished; } catch (Exception) { } }
    private async Task AwaitMediaCancellationAsync(MediaJob job)
    {
        using var registration = job.Cancellation.Token.Register(() => System.Windows.Application.Current.Dispatcher.BeginInvoke(() =>
        { if (job.State == "queued") { job.State = _shuttingDown && job.Request is not null ? "paused" : "canceled"; job.Detail = "停止しました"; job.Completion.TrySetCanceled(); PumpMedia(); } }));
        try { await job.Finished; } catch (Exception) { }
    }
    internal async Task<MediaJob> EnqueueMediaAsync(string title, MediaRequest request)
    {
        if (request.ConversationId is null && Conversations.FirstOrDefault(c => c.Id == (request.Context?.Conversation.Id ?? SelectedConversation?.Id) && c.ProjectId == request.ProjectId) is { } source)
        { request = request with { ConversationId = source.Id, BranchId = request.Context?.Conversation.ActiveBranchId ?? source.ActiveBranch.Id, Agent = Agents.FirstOrDefault(a => a.Id == (request.Context?.AgentId ?? _recipient ?? source.Room?.DefaultAgentId)) }; }
        CheckMediaCapacity(); ValidateMediaRequest(request);
        if (request.Context is { FrozenInstruction: null } context)
        {
            KnowledgeContext knowledge = await Task.Run(() => KnowledgeService.Build(context.State, Conversation.Restore(context.Conversation), request.Prompt, context.Settings.ContextSize, _mediaShutdown.Token, request.Instruction, context.AgentId), _mediaShutdown.Token);
            SavedConversation conversation = context.Conversation with { Messages = [], Branches = null, ActiveBranchId = null, Draft = null, DraftAttachments = null, Summary = null };
            SavedConversation compactRoom = conversation with { Room = conversation.Room is null ? null : conversation.Room with { Kind = conversation.Room.Kind == "thread" ? "channel" : conversation.Room.Kind, ParentId = null, OriginMessageId = null } };
            AppState compact = context.State with { Conversations = [compactRoom], InferenceJobs = null, CommonMemories = [], AgentMemories = [], Agents = context.State.Agents?.Where(a => compactRoom.Room?.Members.Contains(a.Id) == true || compactRoom.Room?.PastMembers?.Contains(a.Id) == true).ToList(), Projects = context.State.Projects?.Where(p => p.Id == conversation.ProjectId).Select(p => p with { Documents = [], Memories = [] }).ToList(), Skills = context.State.Skills?.Where(s => s.ProjectId is null || s.ProjectId == conversation.ProjectId).ToList() };
            request = request with
            {
                Context = context with
                {
                    State = compact,
                    Conversation = conversation,
                    FrozenInstruction = knowledge.Instruction,
                    FrozenReferences = knowledge.References,
                    AccessStamp = WorkspaceService.AccessStamp(context.State, conversation.Id, context.AgentId, knowledge.References)
                }
            };
        }
        CheckMediaCapacity(); ValidateMediaRequest(request); var job = new MediaJob(title, request); AddMediaJob(job);
        try { await SaveMediaJobsAsync(true); job.IsCommitted = true; PumpMedia(); return job; }
        catch { job.State = "failed"; job.Completion.TrySetException(new IOException("予約を保存できませんでした。")); throw; }
    }
    private void SyncMediaMessage(MediaJob job)
    {
        if (job.Request is not { ConversationId: Guid roomId } request) { return; }
        Conversation? room = Conversations.FirstOrDefault(c => c.Id == roomId && c.ProjectId == request.ProjectId); if (room is null) { return; }
        ConversationBranch? branch = room.Branches.FirstOrDefault(b => b.Id == request.BranchId); if (branch is null) { return; }
        Guid id = WorkspaceService.StableId(job.Id, "media-message"); ChatMessage? message = branch.Messages.FirstOrDefault(m => m.Id == id);
        if (message is null)
        {
            message = new()
            {
                Id = id,
                Role = "assistant",
                Identity = new(request.Agent?.Id, request.Agent?.Name ?? "生成", request.Agent?.Icon ?? "◇",
                request.Context?.Model.Id, request.Context?.Model.DisplayName, job.CreatedAt, Detail: request.Kind == "image" ? "画像生成" : "文書生成")
            };
            branch.Messages.Add(message);
        }
        message.Content = job.Title; message.Notice = job.Detail; message.Knowledge = job.Knowledge; message.Skills = job.Skills;
        if (job.State == "complete" && File.Exists(request.OutputPath)) { message.Artifact = new(job.Id, job.Title, request.Kind, request.OutputPath); }
    }
    public async Task ResumeMediaAsync(MediaJob job)
    {
        if (_shuttingDown || !job.CanResume || !MediaJobs.Contains(job)) { return; }
        if (!_ready || _modelsChanging || _dataMaintenance) { throw new InvalidOperationException("起動の準備・モデル管理・バックアップが終わってから再開してください。"); }
        ValidateMediaRequest(job.Request!); job.IsCommitted = false; job.Cancellation.Dispose(); job.Cancellation = new(); job.Completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _ = ObserveMediaAsync(job); _ = AwaitMediaCancellationAsync(job); job.State = "queued"; job.Detail = "保存した依頼を最初から再実行します";
        try { await SaveMediaJobsAsync(true); job.IsCommitted = true; PumpMedia(); } catch { job.State = "paused"; job.Completion.TrySetCanceled(); throw; }
    }
    private void PumpMedia()
    {
        Notify(nameof(ProcessSummary)); Notify(nameof(IsBusy)); RefreshCommands();
        if (_pumpingMedia || _shuttingDown || _dataMaintenance) { return; }
        _pumpingMedia = true;
        try
        {
            bool exclusive = MediaJobs.Any(j => j.State == "active" && j.Request is null) || MediaJobs.FirstOrDefault(j => j.State == "queued") is { Request: null };
            if (exclusive && !_mediaSuspended) { _suspensionBeforeMedia = Inferences.Suspended; _mediaSuspended = true; if (!Inferences.Suspended) { Inferences.Suspended = true; } }
            else if (!exclusive && _mediaSuspended) { _mediaSuspended = false; bool suspended = _isBusy || _suspensionBeforeMedia; if (Inferences.Suspended != suspended) { Inferences.Suspended = suspended; } }
            if (_isBusy || _modelsChanging || _mediaWorkers.Keys.Any(j => j.Request is null)) { return; }
            while (_mediaWorkers.Count < _settings.MaxConcurrentMedia)
            {
                MediaJob? job = MediaJobs.FirstOrDefault(j => j.State == "queued"); if (job is null || job.Request is not null && !job.IsCommitted) { break; }
                if (job.Request is null && (_mediaWorkers.Count > 0 || Inferences.ActiveCount > 0)) { break; }
                ReleaseIdleEngines(); _llama.Stop();
                job.State = "active"; job.Detail = "処理を開始しています";
                _mediaWorkers.Add(job, Task.CompletedTask); _mediaWorkers[job] = RunQueuedMediaAsync(job);
                if (job.Request is null) { break; }
            }
        }
        finally { _pumpingMedia = false; Notify(nameof(IsBusy)); RefreshCommands(); }
    }
    private async Task RunQueuedMediaAsync(MediaJob job)
    {
        await Task.Yield(); using LlamaService engine = _llama.CreateWorker(); _mediaEngines[job] = engine; _mediaContext.Value = (job, engine);
        try
        {
            if (job.Operation is not null) { await job.Operation(job.Cancellation.Token); }
            else { await ExecuteSavedMediaAsync(job, job.Cancellation.Token); }
            job.State = "complete"; job.Detail = "完了 · " + job.Request?.OutputPath; job.Completion.TrySetResult();
        }
        catch (OperationCanceledException) { job.State = _shuttingDown && job.Request is not null ? "paused" : "canceled"; job.Detail = job.State == "paused" ? "再開待ち · 保存した依頼を最初から実行できます" : "停止しました"; job.Completion.TrySetCanceled(); }
        catch (Exception ex) { job.State = "failed"; job.Detail = DiagnosticsService.Explain(ex); job.Completion.TrySetException(ex); }
        finally
        {
            engine.Stop(); _mediaEngines.Remove(job); _mediaContext.Value = null; _mediaWorkers.Remove(job);
            foreach (MediaJob old in MediaJobs.Where(j => !j.IsPending && !j.CanResume).Reverse().Skip(30).ToArray()) { MediaJobs.Remove(old); }
            await SaveMediaJobsAsync(); PumpMedia(); await FlushModelRefreshAsync();
        }
    }
    private void ValidateMediaRequest(MediaRequest request, bool requireProject = true)
    {
        if (request.Kind is not ("document" or "translation" or "image") || request.Prompt is null || request.Prompt.Length > 60000 || request.Instruction is null || request.OutputPath is null || !Path.GetFullPath(request.OutputPath).StartsWith(Path.Combine(_store.Root, "Generated") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || request.Kind == "image" && request.Image is null || request.Kind != "image" && request.Context is null)
        { throw new InvalidDataException("作成予約の形式が不正です。"); }
        if (request.Context is { } context) { AppStore.Validate(context.State); if (context.Conversation.ProjectId != request.ProjectId) { throw new InvalidDataException("作成予約の参照範囲が一致しません。"); } }
        if (requireProject && request.ProjectId is Guid id && !Projects.Any(p => p.Id == id)) { throw new InvalidOperationException("対象のプロジェクトが存在しません。予約を取り消してください。"); }
        if (requireProject && request.ConversationId is Guid roomId && !Conversations.Any(c => c.Id == roomId && c.ProjectId == request.ProjectId && c.Branches.Any(b => b.Id == request.BranchId)))
        { throw new InvalidOperationException("作成先のチャンネルまたは分岐が存在しません。予約を取り消してください。"); }
    }
    private async Task ExecuteSavedMediaAsync(MediaJob job, CancellationToken token)
    {
        MediaRequest request = job.Request!; ValidateMediaRequest(request); Directory.CreateDirectory(Path.GetDirectoryName(request.OutputPath)!);
        if (request.Kind == "image") { await DiffusionService.GenerateAsync(_store, request.Image!, request.OutputPath, text => job.Detail = text, token); return; }
        long savedAt = 0;
        await GenerateArtifactTextAsync(request.Prompt, request.Instruction, text => { job.Text = text; if (Environment.TickCount64 - savedAt > 2000) { savedAt = Environment.TickCount64; _ = SaveMediaJobsAsync(); } }, token, request.Context);
        await File.WriteAllTextAsync(request.OutputPath + ".tmp", job.Text, token); token.ThrowIfCancellationRequested(); File.Move(request.OutputPath + ".tmp", request.OutputPath, true);
    }
    internal string NewMediaOutput(string extension) => Path.Combine(_store.Root, "Generated", "Jobs", Guid.NewGuid().ToString("N") + extension);
    internal async Task ClearFinishedMediaAsync() { foreach (MediaJob job in MediaJobs.Where(j => !j.IsPending && !j.CanResume).ToArray()) { MediaJobs.Remove(job); } await SaveMediaJobsAsync(); Notify(nameof(ProcessSummary)); }
    private async Task SaveMediaJobsAsync(bool required = false)
    {
        if (_mediaReadOnly) { if (required) { throw new IOException("以前の作成予約を読み取れないため、ファイルを保持しています。保存先のアクセス権を確認してください。"); } return; }
        await _mediaSaveGate.WaitAsync();
        try
        {
            SavedMediaJob[] snapshot = MediaJobs.Where(j => j.Request is not null).Select(j => j.Save()).ToArray();
            string json = await Task.Run(() => JsonSerializer.Serialize(snapshot, AppStore.JsonOptions));
            string path = Path.Combine(_store.Root, "media-jobs.json"); await File.WriteAllTextAsync(path + ".tmp", json); File.Move(path + ".tmp", path, true);
        }
        catch (Exception ex) when (!required && ex is IOException or UnauthorizedAccessException) { Error = "画像・文書の予約を保存できませんでした。空き容量とアクセス権を確認してください。"; }
        finally { _mediaSaveGate.Release(); }
    }
    private async Task LoadMediaJobsAsync()
    {
        string path = Path.Combine(_store.Root, "media-jobs.json"); if (!File.Exists(path) || MediaJobs.Count > 0) { return; }
        try
        {
            if (new FileInfo(path).Length > (256L << 20)) { throw new InvalidDataException("作成予約の保存ファイルが大きすぎます。"); }
            var saved = await Task.Run(async () => JsonSerializer.Deserialize<List<SavedMediaJob>>(await File.ReadAllTextAsync(path, _mediaShutdown.Token), AppStore.JsonOptions) ?? [], _mediaShutdown.Token);
            if (saved.Count > 100 || saved.Any(s => s is null || s.Request is null || s.Title is null || s.Text is null || s.Text.Length > 4000000) || saved.Select(s => s.Id).Distinct().Count() != saved.Count) { throw new InvalidDataException("作成予約の形式が不正です。"); }
            foreach (var item in saved) { ValidateMediaRequest(item.Request, false); }
            foreach (var item in saved)
            {
                var job = new MediaJob(item.Title, item.Request) { Id = item.Id, IsCommitted = true, CreatedAt = item.CreatedAt, Text = item.Text, Knowledge = item.Knowledge ?? [], Skills = item.Skills ?? [] };
                job.State = item.State is "queued" or "active" or "paused" or "failed" ? "paused" : item.State == "complete" ? "complete" : "canceled";
                job.Detail = job.State == "paused" ? "再開待ち · 処理リストから再開できます" : "保存した出力 · " + item.Request.OutputPath;
                job.Completion.TrySetResult(); AddMediaJob(job);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException or ArgumentException)
        {
            try { File.Copy(path, path + ".broken-" + Guid.NewGuid().ToString("N")); _mediaRecoveryNotice = "画像・文書の予約を復元できませんでした。元のファイルを別名で保管しました。"; }
            catch (Exception copyError) when (copyError is IOException or UnauthorizedAccessException) { _mediaReadOnly = true; _mediaRecoveryNotice = "画像・文書の予約を読み取れません。元のファイルを保持し、新規予約の保存を停止しています。アクセス権を確認してください。"; }
        }
    }
}
