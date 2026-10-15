using System.IO;
using System.Text.Json;
using System.Windows;
using Eluna.App.Models;

namespace Eluna.App.Services;

internal static class RestoreCycleVerification
{
    internal static async Task RunAsync(AppStore store, string output, bool prepare)
    {
        Directory.CreateDirectory(output); MainWindow? window = null;
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2)); CancellationToken token = timeout.Token;
        async Task Until(Func<bool> ready) { while (!ready()) { await Task.Delay(25, token); } }
        void Check(bool value, string message) { if (!value) { throw new InvalidOperationException(message); } }
        try
        {
            PreparedBackup? staged = null;
            if (prepare)
            {
                await store.SaveAsync(new(1, new(), [new(Guid.NewGuid(), "復元前の会話", [])]));
                var source = new AppStore(Path.Combine(output, "source")); await source.SaveAsync(new(1, new(), [new(Guid.NewGuid(), "復元サイクルの会話", [], Draft: "復元する下書き")]));
                Directory.CreateDirectory(Path.Combine(source.Root, "Generated", "Jobs")); await File.WriteAllTextAsync(Path.Combine(source.Root, "Generated", "document-draft.md"), "保存した生成文書", token);
                var request = new MediaRequest("image", "保存した依頼", "", null, Path.Combine(source.Root, "Generated", "Jobs", "result.png"), Image: new("missing-model.gguf", "保存した依頼"));
                await File.WriteAllTextAsync(Path.Combine(source.Root, "media-jobs.json"), JsonSerializer.Serialize(new[] { new SavedMediaJob(Guid.NewGuid(), "保存した画像予約", DateTimeOffset.UtcNow, "active", request) }, AppStore.JsonOptions), token);
                string archive = Path.Combine(output, "restore-cycle.zip"); await BackupArchive.CreateAsync(source, archive, false, token: token); staged = await BackupArchive.PrepareAsync(store, archive, token: token);
            }
            window = new MainWindow(store) { WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = -20000 }; window.Show();
            await Until(() => window.ViewModel.IsReady || window.ViewModel.HasError); Check(window.ViewModel.IsReady, window.ViewModel.Error);
            if (prepare) { window.RestorePreparedBackup(staged!, output); await Until(() => !window.IsVisible); Application.Current.Shutdown(0); return; }
            Check(window.ViewModel.Conversations.Single().Title == "復元サイクルの会話" && window.ViewModel.Conversations[0].Draft == "復元する下書き", "Restored state was overwritten by the previous application's shutdown.");
            Check(window.ViewModel.MediaJobs.Single() is { State: "paused", CanResume: true } && window.ViewModel.Inferences.ActiveCount == 0, "Restored jobs started without explicit resumption.");
            Check(await File.ReadAllTextAsync(Path.Combine(store.Root, "Generated", "document-draft.md"), token) == "保存した生成文書", "Generated documents were not restored.");
            Check(window.ViewModel.MediaJobs[0].Request!.OutputPath.StartsWith(Path.Combine(store.Root, "Generated") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase), "The resumed job retained its original data-directory output path.");
            string safety = Directory.GetFiles(store.BackupsDirectory, "before-restore-*.zip").Single(); Check((await BackupArchive.InspectAsync(safety, token)).Files.Any(f => f.Path == "state.json"), "The pre-restore backup is missing.");
            await PrototypePreview.RenderAsync(window, Path.Combine(output, "restored-main.png"));
            await File.WriteAllTextAsync(Path.Combine(output, "restart-verification.json"), JsonSerializer.Serialize(new { Success = true, NewProcessId = Environment.ProcessId, RestoredChat = true, RestoredDraft = true, GeneratedDocument = true, PausedRequest = true, RebasedOutput = true, SafetyBackup = safety }, new JsonSerializerOptions { WriteIndented = true }), token);
            window.Close(); await Until(() => !window.IsVisible); Application.Current.Shutdown(0);
        }
        catch (Exception ex) { await File.WriteAllTextAsync(Path.Combine(output, "restore-cycle-error.txt"), ex.ToString()); window?.Close(); Application.Current.Shutdown(1); }
    }
}
