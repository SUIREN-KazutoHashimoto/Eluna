using System.IO;
using System.Windows.Threading;
using Eluna.App.Models;
using Eluna.App.Services;

namespace Eluna.Checks;

internal static class InferenceQueueChecks
{
    internal static Task RunAsync(string root)
    {
        var result = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            dispatcher.BeginInvoke(async () =>
            {
                try { await CheckAsync(root); result.SetResult(); }
                catch (Exception ex) { result.SetException(ex); }
                finally { dispatcher.BeginInvokeShutdown(DispatcherPriority.Background); }
            });
            Dispatcher.Run();
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); return result.Task;
    }
    private static void Check(bool value, string message) { if (!value) { throw new InvalidOperationException(message); } }
    private static async Task CheckAsync(string root)
    {
        var gates = new Dictionary<Guid, TaskCompletionSource>(); var started = new List<Guid>(); int concurrent = 0, peak = 0;
        var queue = new InferenceQueue(async (job, token) =>
        {
            started.Add(job.Request.Id); concurrent++; peak = Math.Max(peak, concurrent);
            try { await gates[job.Request.Id].Task.WaitAsync(token); }
            finally { concurrent--; }
        });
        var model = new LocalModel("test", "test", Path.Combine(root, "test.gguf"), "local", "local", "", 1);
        SavedInferenceJob Request(Guid conversationId)
        {
            var request = new SavedInferenceJob(Guid.NewGuid(), conversationId, Guid.NewGuid(), Guid.NewGuid(), "予約テスト", DateTimeOffset.UtcNow,
                model, [model], new(WorkMode: "offline"), "予約時の指示", []);
            gates.Add(request.Id, new(TaskCreationOptions.RunContinuationsAsynchronously)); return request;
        }
        async Task Until(Func<bool> condition)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (!condition()) { await Task.Delay(5, timeout.Token); }
        }
        Guid a = Guid.NewGuid(), b = Guid.NewGuid(), c = Guid.NewGuid();
        InferenceJob a1 = queue.Add(Request(a)), a2 = queue.Add(Request(a)), b1 = queue.Add(Request(b));
        Check(started.SequenceEqual([a1.Request.Id]) && queue.WaitingCount == 2, "Default queue did not serialize work.");
        queue.Limit = 2;
        Check(started.SequenceEqual([a1.Request.Id, b1.Request.Id]) && peak == 2, "A queued sibling blocked another conversation or parallelism did not start.");
        queue.Limit = 1; gates[b1.Request.Id].SetResult(); await Until(() => b1.State == "complete");
        Check(a2.State == "queued" && queue.ActiveCount == 1, "Lowering concurrency canceled work or started over the new limit.");
        queue.Cancel(a1); await Until(() => a2.State == "active");
        Check(a1.State == "canceled" && started.Last() == a2.Request.Id, "Canceling the active job did not release its conversation.");
        InferenceJob canceled = queue.Add(Request(c)); queue.Cancel(canceled);
        Check(!started.Contains(canceled.Request.Id), "A canceled reservation executed.");
        gates[a2.Request.Id].SetException(new InvalidOperationException("失敗の検証")); await Until(() => a2.State == "failed");
        InferenceJob a3 = queue.Add(Request(a)), c1 = queue.Add(Request(c));
        Check(a3.State == "queued" && c1.State == "active", "A failed predecessor did not preserve order or blocked another conversation.");
        queue.Cancel(a2); gates[c1.Request.Id].SetResult(); await Until(() => a3.State == "active");
        InferenceJob waiting = queue.Add(Request(b));
        await queue.ShutdownAsync();
        Check(a3.State == "paused" && waiting.State == "paused" && concurrent == 0 && queue.ActiveCount == 0, "Shutdown lost reservations or left active work.");
        queue.Resume(a3); Check(a3.State == "paused", "A closed queue resumed work.");

        var store = new AppStore(root); var conversation = new Conversation { Id = a };
        conversation.Messages.Add(new() { Role = "user", Content = "予約した質問" });
        conversation.Messages.Add(new() { Id = a3.Request.AnswerId, Role = "assistant", Content = "途中の回答", IsGenerating = true });
        SavedInferenceJob saved = a3.Request with { BranchId = conversation.ActiveBranch.Id, Settings = a3.Request.Settings with { ContextSize = 32768, MaxConcurrentInferences = 2 } };
        var state = new AppState(2, new(MaxConcurrentInferences: 2), [conversation.Save()], InferenceJobs: [saved]);
        AppStore.Validate(state); await store.SaveAsync(state); AppState loaded = await store.LoadAsync();
        Check(loaded.InferenceJobs?.Single().Instruction == "予約時の指示" && loaded.Settings.MaxConcurrentInferences == 2 && loaded.InferenceJobs[0].Settings.ContextSize == 32768,
            "Queue persistence lost captured settings or instructions.");
        bool rejected = false;
        try { AppStore.Validate(state with { Settings = state.Settings with { MaxConcurrentInferences = 0 } }); } catch (InvalidDataException) { rejected = true; }
        Check(rejected, "Invalid parallelism was accepted.");
        rejected = false;
        try { AppStore.Validate(state with { InferenceJobs = [saved with { BranchId = Guid.NewGuid() }] }); } catch (InvalidDataException) { rejected = true; }
        Check(rejected, "A queue with a missing answer branch was accepted.");
        Console.WriteLine("Inference queue: per-chat ordering, parallel limits, cancellation, failure isolation, shutdown and saved snapshots: OK");
    }
}
