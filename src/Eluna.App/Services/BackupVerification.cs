using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Eluna.App.Models;
using Eluna.App.ViewModels;

namespace Eluna.App.Services;

internal static class BackupVerification
{
    private static void Check(bool value, string message) { if (!value) { throw new InvalidOperationException(message); } }
    private static IEnumerable<T> Find<T>(DependencyObject parent) where T : DependencyObject
    { for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++) { DependencyObject child = VisualTreeHelper.GetChild(parent, i); if (child is T typed) { yield return typed; } foreach (T nested in Find<T>(child)) { yield return nested; } } }
    internal static async Task RunAsync(AppStore store, string output, string? metadata, int stressSeconds = 0)
    {
        Directory.CreateDirectory(output); MainWindow? main = null;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Max(480, stressSeconds + 240))); CancellationToken token = timeout.Token;
        async Task Until(Func<bool> ready) { while (!ready()) { await Task.Delay(20, token); } }
        try
        {
            if (metadata is not null)
            {
                LocalModel model = JsonSerializer.Deserialize<LocalModel>(await File.ReadAllTextAsync(metadata, token), AppStore.JsonOptions)!;
                Directory.CreateDirectory(Path.Combine(store.ModelsDirectory, model.Id)); await File.WriteAllTextAsync(Path.Combine(store.ModelsDirectory, model.Id, "model.json"), JsonSerializer.Serialize(model with { Preferences = new(ContextSize: 4096, MaxOutputTokens: 1024, Temperature: 0.1, DefaultMode: "fast") }, AppStore.JsonOptions), token);
            }
            main = new MainWindow(store) { WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = -20000 }; main.Show(); ChatViewModel chat = main.ViewModel;
            await Until(() => chat.IsReady || chat.HasError); Check(chat.IsReady, chat.Error);
            await chat.PersistSafelyAsync();
            string stateFile = new[] { "state-skills.json", "state-knowledge.json", "state.json" }.Select(n => Path.Combine(store.Root, n)).First(File.Exists);
            var saves = new List<Task>();
            using (var locked = new FileStream(stateFile, FileMode.Open, FileAccess.Read, FileShare.Read))
            { for (int i = 0; i < 300; i++) { chat.SelectedConversation!.Title = "連続保存 " + i; saves.Add(chat.PersistSafelyAsync()); } }
            await Task.WhenAll(saves).WaitAsync(token);
            Check((await store.LoadAsync()).Conversations.Single().Title == "連続保存 299", "Burst saves completed before the latest mutation was durable.");
            chat.SelectedConversation!.Title = "バックアップ確認"; await chat.PersistSafelyAsync();
            var data = new DataWindow(chat) { Owner = main, WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = -20000 }; data.Show();
            await Until(() => Find<TextBlock>(data).Any(t => t.Text.StartsWith("対象:", StringComparison.Ordinal)));
            await PrototypePreview.RenderAsync(data, Path.Combine(output, "backup-dark.png")); data.Width = data.MinWidth; data.Height = data.MinHeight;
            await PrototypePreview.RenderAsync(data, Path.Combine(output, "backup-minimum.png"));
            await chat.ApplySettingsAsync(chat.Settings with { Theme = "light", UiScale = 1.3 }); await PrototypePreview.RenderAsync(data, Path.Combine(output, "backup-light-large.png"));
            ((TabControl)data.Content).SelectedIndex = 1; await PrototypePreview.RenderAsync(data, Path.Combine(output, "history-json.png")); data.Close();
            await chat.ApplySettingsAsync(chat.Settings with { Theme = "dark", UiScale = 1, WorkMode = "offline", MaxConcurrentMedia = 4, MaxConcurrentInferences = 4, ContextSize = 4096, InferenceMode = "fast" });
            var hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Task<bool> frozen = chat.WithBackupAsync(async (_, _) => { entered.SetResult(); await hold.Task; return true; }, token);
            await entered.Task; Check(!chat.CanNavigate && !chat.CanCompose && !chat.CanChange, "Backup did not freeze editable state."); hold.SetResult(); await frozen; Check(chat.CanNavigate && chat.CanCompose, "Backup did not release navigation.");
            string archive = Path.Combine(output, "whole-backup.zip");
            await chat.WithBackupAsync(async (s, ct) => { await BackupArchive.CreateAsync(s, archive, false, token: ct); return true; }, token);
            var restoredStore = new AppStore(Path.Combine(output, "restored-data")); PreparedBackup prepared = await BackupArchive.PrepareAsync(restoredStore, archive, token: token); await BackupArchive.ApplyAsync(restoredStore, prepared);
            Check((await restoredStore.LoadAsync()).Conversations.Count == chat.Conversations.Count, "View-model backup did not restore all conversations.");
            int mediaMaximum = 0, mediaActive = 0, mediaFailed = 0;
            var mediaTasks = Enumerable.Range(0, 16).Select(i => chat.QueueMediaOperationAsync("負荷 " + i, async ct =>
            {
                mediaActive++; mediaMaximum = Math.Max(mediaMaximum, mediaActive);
                try { await Task.Delay(20, ct); if (i % 7 == 0) { throw new OutOfMemoryException("simulated media backend memory exhaustion"); } }
                finally { mediaActive--; }
            }, token)).ToArray();
            foreach (Task work in mediaTasks) { try { await work; } catch (OutOfMemoryException) { mediaFailed++; } }
            await Until(() => !chat.IsBusy); Check(mediaMaximum == 1 && mediaActive == 0 && mediaFailed > 0, "Exclusive preparation/failure isolation was not exercised.");
            int actualCompleted = 0, actualMediaMaximum = 0; var rounds = new List<object>();
            if (metadata is not null)
            {
                const string prompt = "日本語で『春夏秋冬です。』と一文だけ回答してください。";
                ArtifactContext context = chat.CaptureArtifactContext(prompt);
                var generated = new List<MediaJob>();
                for (int i = 0; i < 4; i++) { generated.Add(await chat.EnqueueMediaAsync("文書負荷 " + i, new("document", prompt, "簡潔に回答してください。", null, chat.NewMediaOutput(".txt"), context))); }
                await Until(() => chat.MediaJobs.Count(j => j.State == "active") == 4 || generated.All(j => j.State is "complete" or "failed"));
                actualMediaMaximum = chat.MediaJobs.Count(j => j.State == "active");
                MediaJob missingImage = await chat.EnqueueMediaAsync("失敗する画像", new("image", "画像", "", null, chat.NewMediaOutput(".png"), Image: new(Path.Combine(store.Root, "missing.gguf"), "画像")));
                foreach (MediaJob job in generated) { await job.Finished.WaitAsync(token); Check(job.State == "complete" && File.Exists(job.Request!.OutputPath), "Actual document generation failed: " + job.Detail); }
                try { await missingImage.Finished.WaitAsync(token); } catch (Exception) when (missingImage.State == "failed") { }
                Check(missingImage.State == "failed" && actualMediaMaximum == 4, "Failed image generation affected its peers or four-way documents were not exercised.");
                var elapsed = System.Diagnostics.Stopwatch.StartNew();
                for (int round = 0; round < 3 || elapsed.Elapsed.TotalSeconds < stressSeconds; round++)
                {
                    var jobs = new List<InferenceJob>();
                    for (int i = 0; i < 4; i++) { chat.NewChat(); chat.Input = "短い日本語の箇条書きで、季節の名前と特徴を4つ説明してください。検索はしないで。"; await Until(() => chat.SendCommand.CanExecute(null)); chat.SendCommand.Execute(null); await Until(() => chat.Inferences.Items.Any(j => j.Request.ConversationId == chat.SelectedConversation!.Id)); jobs.Add(chat.Inferences.Items.Last(j => j.Request.ConversationId == chat.SelectedConversation!.Id)); }
                    await Until(() => chat.Inferences.ActiveCount == 4 || jobs.All(j => j.State is "complete" or "failed"));
                    int activeAtOnce = chat.Inferences.ActiveCount;
                    if (round % 3 == 1) { chat.Inferences.Cancel(jobs[0]); }
                    await Task.WhenAll(jobs.Select(j => j.Finished)).WaitAsync(token);
                    foreach (InferenceJob job in jobs) { Check(job.State != "failed", "Actual inference failed: " + job.Detail); }
                    actualCompleted += jobs.Count(j => j.State == "complete"); rounds.Add(new { Round = round, ActiveAtOnce = activeAtOnce, States = jobs.Select(j => j.State).ToArray(), Compute = chat.ComputeLabel });
                    await File.WriteAllTextAsync(Path.Combine(output, "stress-progress.json"), JsonSerializer.Serialize(new { Seconds = elapsed.Elapsed.TotalSeconds, Rounds = round + 1, Completed = actualCompleted, MemoryBytes = System.Diagnostics.Process.GetCurrentProcess().WorkingSet64 }, AppStore.JsonOptions), token);
                }
            }
            await File.WriteAllTextAsync(Path.Combine(output, "backup-verification.json"), JsonSerializer.Serialize(new { Success = true, BurstSaveRequests = 300, LatestSaveDurable = true, ViewModelBackup = true, EditFreeze = true, PortableRestore = true, AuxiliaryMaximum = mediaMaximum, AuxiliaryFailuresIsolated = mediaFailed, ActualMediaMaximum = actualMediaMaximum, ActualCompleted = actualCompleted, Rounds = rounds }, new JsonSerializerOptions { WriteIndented = true }), token);
            main.Close(); await Until(() => !main.IsVisible); Application.Current.Shutdown(0);
        }
        catch (Exception ex) { await File.WriteAllTextAsync(Path.Combine(output, "backup-verification-error.txt"), ex.ToString()); main?.Close(); Application.Current.Shutdown(1); }
    }
}
