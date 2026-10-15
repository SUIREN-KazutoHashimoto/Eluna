using System.IO;
using System.Windows.Threading;
using Eluna.App.Models;
using Eluna.App.Services;

namespace Eluna.Checks;

internal static class QueueStabilityChecks
{
    internal static Task RunAsync()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            Dispatcher dispatcher = Dispatcher.CurrentDispatcher; SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            dispatcher.BeginInvoke(async () => { try { await StressAsync(); completion.SetResult(); } catch (Exception ex) { completion.SetException(ex); } finally { dispatcher.BeginInvokeShutdown(DispatcherPriority.Background); } });
            Dispatcher.Run();
        }); thread.SetApartmentState(ApartmentState.STA); thread.Start(); return completion.Task;
    }
    private static void Check(bool value, string message) { if (!value) { throw new InvalidOperationException(message); } }
    private static async Task StressAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var model = new LocalModel("fixture", "fixture", "fixture.gguf", "fixture", "fixture", new string('a', 64), 1);
        for (int limit = 1; limit <= 4; limit++)
        {
            int active = 0, maximum = 0, succeeded = 0, failed = 0, canceled = 0;
            var activeChats = new HashSet<Guid>(); var failedIds = new HashSet<Guid>();
            Guid[] chats = Enumerable.Range(0, 8).Select(_ => Guid.NewGuid()).ToArray();
            var queue = new InferenceQueue(async (job, token) =>
            {
                Check(activeChats.Add(job.Request.ConversationId), "Two jobs from the same conversation overlapped.");
                active++; maximum = Math.Max(maximum, active); Check(active <= limit, "Inference concurrency exceeded the configured limit.");
                try
                {
                    int index = int.Parse(job.Title, System.Globalization.CultureInfo.InvariantCulture);
                    await Task.Delay(1 + index % 5, token);
                    if (index % 17 == 0) { throw new OutOfMemoryException("simulated backend memory exhaustion"); }
                    if (index % 19 == 0) { throw new IOException("simulated backend exit"); }
                    succeeded++;
                }
                finally { active--; activeChats.Remove(job.Request.ConversationId); }
            })
            { Limit = limit };
            queue.Changed += () =>
            {
                foreach (InferenceJob job in queue.Items.Where(j => j.State == "failed").ToArray())
                { if (failedIds.Add(job.Request.Id)) { failed++; } queue.Cancel(job); }
            };
            for (int round = 0; round < 5; round++)
            {
                var batch = new List<InferenceJob>();
                for (int i = 0; i < 48; i++)
                {
                    int index = round * 48 + i;
                    var request = new SavedInferenceJob(Guid.NewGuid(), chats[i % chats.Length], Guid.NewGuid(), Guid.NewGuid(), index.ToString(System.Globalization.CultureInfo.InvariantCulture), DateTimeOffset.UtcNow, model, [model], new(), "", []);
                    InferenceJob job = queue.Add(request); batch.Add(job);
                    if (index % 11 == 0) { queue.Cancel(job); canceled++; }
                }
                await Task.WhenAll(batch.Select(j => j.Finished)).WaitAsync(timeout.Token);
                while (queue.ActiveCount > 0 || queue.WaitingCount > 0) { await Task.Delay(1, timeout.Token); }
                Check(!queue.HasPending, "Failures or cancellation left a conversation queue stuck.");
            }
            await queue.ShutdownAsync(); Check(active == 0 && activeChats.Count == 0 && maximum == limit && succeeded > 100 && failed > 0 && canceled > 0, "Stress run did not exercise concurrency, failure isolation and cancellation.");
        }
        Console.WriteLine("Queue stability: 960 jobs at limits 1–4, repeated enqueue/cancel, per-chat ordering, simulated memory exhaustion/backend exit, failure isolation and clean shutdown: OK");
    }
}
