using Eluna.App.Models;

namespace Eluna.App.Services;

internal sealed record ComputerRunOptions(Func<bool> Automatic, IReadOnlyList<ComputerStep> PreviousSteps,
    Func<string, CancellationToken, Task<IReadOnlyList<ComputerTarget>>>? RequestApps = null, double SecondsRemaining = 1200,
    Action<ComputerObservation, ComputerAction>? BeforeAction = null, Action? AfterAction = null);

internal sealed class ComputerUseRunner(IComputerDesktop desktop, IComputerPlanner planner)
{
    internal async Task<ComputerUseResult> RunAsync(string task, IReadOnlyList<ComputerTarget> allowed, int maxSteps, bool navigationOnlyAuto,
        Action<ComputerObservation> observe, Action<ComputerStep> record, Action<string> status,
        Func<ComputerReview, CancellationToken, Task<bool>> review, CancellationToken token, ComputerRunOptions? options = null)
    {
        if (string.IsNullOrWhiteSpace(task) || task.Length > 8000 || allowed.Count is < 1 or > 8 || allowed.DistinctBy(t => t.Handle).Count() != allowed.Count || maxSteps is < 1 or > 100)
        { throw new ArgumentException("依頼は8,000文字以内、対象は1〜8アプリ、操作上限は1〜100回にしてください。"); }
        using IDisposable lease = await ComputerUseLease.AcquireAsync(token);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, options?.SecondsRemaining ?? 1200)));
        var steps = options?.PreviousSteps.ToList() ?? []; allowed = allowed.ToList(); ComputerTarget target = allowed[0]; int retries = 0;
        for (int number = steps.Count + 1; number <= maxSteps; number++)
        {
            deadline.Token.ThrowIfCancellationRequested(); status("対象の画面を確認しています…");
            ComputerObservation observation = await desktop.ObserveAsync(target, deadline.Token); observe(observation);
            ComputerAction action = await planner.NextAsync(task, observation, allowed, steps, deadline.Token); ComputerActionPolicy.Validate(action);
            if (action.Type == "finish") { return new("complete", action.Reason, steps.Count); }
            if (action.Type == "ask") { return new("needs_user", action.Reason, steps.Count); }
            if (action.Type == "request_app")
            {
                if (options?.RequestApps is null) { return new("needs_user", "別のアプリの操作許可が必要です。" + action.Text, steps.Count); }
                IReadOnlyList<ComputerTarget> additions = await options.RequestApps(action.Text, deadline.Token);
                var combined = allowed.Concat(additions).DistinctBy(w => w.Handle).ToList();
                if (additions.Count == 0 || combined.Count > 8) { return new("needs_user", "アプリが許可されていません。", steps.Count); }
                allowed = combined; target = additions[0];
                var permission = new ComputerStep(number, DateTimeOffset.UtcNow, observation.Title, action, "利用者が対象アプリを許可しました。最新画面を取得します。"); steps.Add(permission); record(permission); continue;
            }
            if (action.Type == "switch_window" && action.Window > allowed.Count) { throw new InvalidOperationException("許可した対象以外へは切り替えられません。"); }
            string? confirmation = options?.Automatic() == true ? ComputerActionPolicy.AutomaticConfirmation(action, observation) : ComputerActionPolicy.Confirmation(action, observation, navigationOnlyAuto);
            if (confirmation is not null)
            {
                status("操作の確認待ちです");
                var proposal = new ComputerReview(observation, action, confirmation);
                if (!await review(proposal, deadline.Token)) { return new("stopped", "操作を実行せず終了しました。", steps.Count); }
                if (proposal.ApprovedAction is { } approved)
                {
                    if (approved with { Text = action.Text } != action) { throw new InvalidOperationException("確認画面では入力文字だけを変更できます。"); }
                    action = approved; ComputerActionPolicy.Validate(action);
                }
            }
            deadline.Token.ThrowIfCancellationRequested(); status(action.Label + "…");
            string result = "実行済み。次の画面で結果を確認します。";
            try
            {
                options?.BeforeAction?.Invoke(observation, action);
                if (action.Type == "switch_window") { target = allowed[action.Window - 1]; }
                else if (action.Type == "wait") { await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(action.Amount, 1, 5)), deadline.Token); }
                else { await desktop.ExecuteAsync(observation, action, deadline.Token); }
                retries = 0;
            }
            catch (ComputerScreenChangedException ex)
            { result = "未実行 · " + ex.Message; if (++retries >= 3) { return new("needs_user", "画面の変化が続いています。対象アプリの状態を確認して再実行してください。", steps.Count); } }
            finally { options?.AfterAction?.Invoke(); }
            var step = new ComputerStep(number, DateTimeOffset.UtcNow, observation.Title, action, result); steps.Add(step); record(step);
            await Task.Delay(450, deadline.Token);
        }
        ComputerObservation final = await desktop.ObserveAsync(target, deadline.Token); observe(final);
        return new("limit", "操作上限に達したため停止しました。最後の画面と実行履歴を確認してください。", steps.Count);
    }
}

// A named mutex survives force termination safely; the owning background thread also releases it on cancellation.
internal sealed class ComputerUseLease : IDisposable
{
    private readonly ManualResetEventSlim _release = new(false);
    private readonly TaskCompletionSource<IDisposable> _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal static Task<IDisposable> AcquireAsync(CancellationToken token)
    {
        var lease = new ComputerUseLease();
        var thread = new Thread(() =>
        {
            using var mutex = new Mutex(false, "Local\\Eluna-ComputerUse-Interactive"); bool owned = false;
            try
            {
                token.ThrowIfCancellationRequested();
                try { owned = mutex.WaitOne(200); } catch (AbandonedMutexException) { owned = true; }
                if (!owned) { throw new InvalidOperationException("別のComputer Useが実行中です。同時に操作することはできません。"); }
                lease._ready.TrySetResult(lease); lease._release.Wait();
            }
            catch (Exception ex) { lease._ready.TrySetException(ex); }
            finally { if (owned) { mutex.ReleaseMutex(); } lease._release.Dispose(); }
        })
        { IsBackground = true, Name = "Eluna Computer Use lease" };
        thread.Start(); return lease._ready.Task;
    }
    public void Dispose() { try { _release.Set(); } catch (ObjectDisposedException) { } }
}
