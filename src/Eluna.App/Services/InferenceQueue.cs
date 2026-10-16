using System.Collections.ObjectModel;
using Eluna.App.Models;

namespace Eluna.App.Services;

// All mutations run on the owning synchronization context (the WPF dispatcher in the app).
public sealed class InferenceQueue(Func<InferenceJob, CancellationToken, Task> execute)
{
    private int _limit = 1;
    private bool _suspended, _closing, _pumping, _pumpAgain;
    private readonly Dictionary<Guid, HashSet<Guid>> _reservations = [];
    internal void Reserve(Guid owner, IEnumerable<Guid> rooms)
    {
        HashSet<Guid> ids = rooms.ToHashSet();
        if (Items.Any(j => j.Request.Id != owner && j.State == "active" && ids.Contains(j.Request.ConversationId)) || _reservations.Any(r => r.Key != owner && r.Value.Overlaps(ids))) { throw new InvalidOperationException("作業先のチャンネルが使用中です。処理の完了後に再開してください。"); }
        _reservations[owner] = ids;
    }
    internal void Release(Guid owner) { _reservations.Remove(owner); Pump(); }
    public ObservableCollection<InferenceJob> Items { get; } = [];
    public event Action? Changed;
    public int ActiveCount => Items.Count(j => j.State == "active");
    public int WaitingCount => Items.Count(j => j.State == "queued");
    public bool HasPending => Items.Any(j => j.IsPending);
    public int Limit { get => _limit; set { if (value is < 1 or > 4) { throw new ArgumentOutOfRangeException(nameof(value)); } _limit = value; Pump(); } }
    internal bool Suspended { get => _suspended; set { _suspended = value; Pump(); } }
    public bool Contains(Guid conversationId) => _reservations.Values.Any(r => r.Contains(conversationId)) || Items.Any(j => j.Request.ConversationId == conversationId && j.IsPending);
    public InferenceJob Add(SavedInferenceJob request, bool paused = false)
    {
        if (_closing) { throw new InvalidOperationException("アプリを終了しています。"); }
        if (Items.Count(j => j.IsPending) >= 100) { throw new InvalidOperationException("推論の予約は100件までです。予約を完了またはキャンセルしてください。"); }
        var job = new InferenceJob(request);
        if (paused) { job.State = "paused"; job.Detail = "前回の処理です。「再開」で実行します"; }
        Items.Add(job); Pump(); return job;
    }
    public void Resume(InferenceJob job)
    {
        if (_closing || !Items.Contains(job) || !job.CanResume) { return; }
        if (job.Completion.Task.IsCompleted) { job.Completion = new(TaskCreationOptions.RunContinuationsAsynchronously); }
        job.State = "queued"; job.Detail = "順番を待っています"; Pump();
    }
    public void Cancel(InferenceJob job)
    {
        if (!Items.Contains(job) || !job.CanCancel) { return; }
        if (job.State == "active") { job.Detail = "停止しています…"; job.Cancellation?.Cancel(); }
        else { job.State = "canceled"; job.Detail = "予約をキャンセルしました"; job.Completion.TrySetResult(); }
        Pump();
    }
    public void ClearFinished()
    {
        foreach (InferenceJob job in Items.Where(j => !j.IsPending).ToArray()) { Items.Remove(job); }
        Changed?.Invoke();
    }
    private void Pump()
    {
        if (_pumping) { _pumpAgain = true; return; }
        _pumping = true;
        try
        {
            while (!_closing && !_suspended && ActiveCount < Limit)
            {
                InferenceJob? next = Items.FirstOrDefault(j => j.State == "queued" &&
                    !_reservations.Any(r => r.Key != j.Request.Id && r.Value.Contains(j.Request.ConversationId)) &&
                    !Items.TakeWhile(p => p != j).Any(p => p.Request.ConversationId == j.Request.ConversationId && p.IsPending));
                if (next is null) { break; }
                next.State = "active"; next.Cancellation = new(); _ = RunAsync(next);
            }
            int position = 0;
            foreach (InferenceJob pending in Items.Where(j => j.State == "queued"))
            {
                position++;
                InferenceJob? prior = Items.TakeWhile(j => j != pending).LastOrDefault(j => j.Request.ConversationId == pending.Request.ConversationId && j.IsPending);
                pending.Detail = prior is { State: "failed" or "paused" } ? "同じ会話の前の依頼が再開待ちです。前の依頼を再開またはキャンセルしてください。" :
                    prior is not null ? "同じ会話の前の依頼が終わるまで待っています" : _suspended ? "準備・作成処理が終わるまで待っています" : $"推論の空き待ち · 予約 {position}";
            }
            foreach (InferenceJob old in Items.Where(j => !j.IsPending).Reverse().Skip(50).ToArray()) { Items.Remove(old); }
            Changed?.Invoke();
        }
        finally { _pumping = false; if (_pumpAgain) { _pumpAgain = false; Pump(); } }
    }
    private async Task RunAsync(InferenceJob job)
    {
        try { await execute(job, job.Cancellation!.Token); job.State = "complete"; }
        catch (OperationCanceledException) when (job.Cancellation!.IsCancellationRequested)
        { job.State = _closing ? "paused" : "canceled"; job.Detail = _closing ? "終了時に中断しました。「再開」で実行します" : "停止しました"; }
        catch (Exception ex) { job.State = "failed"; job.Detail = DiagnosticsService.Explain(ex); }
        finally
        {
            job.Cancellation!.Dispose(); job.Cancellation = null; job.Completion.TrySetResult(); Pump();
        }
    }
    public async Task ShutdownAsync()
    {
        _closing = true;
        foreach (InferenceJob job in Items.Where(j => j.State == "active").ToArray()) { job.Cancellation!.Cancel(); }
        await Task.WhenAll(Items.Where(j => j.State == "active").Select(j => j.Finished));
        foreach (InferenceJob job in Items.Where(j => j.State == "queued")) { job.State = "paused"; }
        Changed?.Invoke();
    }
}
