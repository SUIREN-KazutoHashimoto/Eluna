using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Eluna.App.Models;
using Eluna.App.ViewModels;

namespace Eluna.App.Services;

internal static class ConcurrentVerification
{
    internal static async Task RunAsync(AppStore store, string metadata)
    {
        var report = new Dictionary<string, object>(); MainWindow? window = null;
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(6)); CancellationToken token = timeout.Token;
        using var http = new HttpClient(new TransferFixture());
        void Check(bool value, string message) { if (!value) { throw new InvalidOperationException(message); } }
        async Task Until(Func<bool> condition)
        { while (!condition()) { token.ThrowIfCancellationRequested(); await Task.Delay(15, token); } }
        try
        {
            LocalModel model = JsonSerializer.Deserialize<LocalModel>(await File.ReadAllTextAsync(metadata, token), AppStore.JsonOptions)!;
            model = model with { Preferences = new(ContextSize: 8192, Temperature: 0.2, MaxOutputTokens: 2048) };
            string directory = Path.Combine(store.ModelsDirectory, model.Id); Directory.CreateDirectory(directory);
            await File.WriteAllTextAsync(Path.Combine(directory, "model.json"), JsonSerializer.Serialize(model, AppStore.JsonOptions), token);
            var downloads = new DownloadQueue(store, new ModelCatalog(store, http));
            window = new MainWindow(store, downloads) { WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = -20000 };
            window.Show(); ChatViewModel chat = window.ViewModel;
            await Until(() => chat.IsReady || chat.HasError); Check(chat.IsReady, chat.Error);
            await chat.ApplySettingsAsync(chat.Settings with { WorkMode = "offline", MaxConcurrentInferences = 1 });
            var now = DateTimeOffset.UtcNow;
            var a = new KnowledgeProject(Guid.NewGuid(), "並行作業 A", "質問された値を簡潔に回答する。", "project-only", false,
                [new(Guid.NewGuid(), "識別情報", "このプロジェクトの識別コードはHARBOR-4826です。", now)], []);
            var b = new KnowledgeProject(Guid.NewGuid(), "並行作業 B", "質問された値を簡潔に回答する。", "project-only", false,
                [new(Guid.NewGuid(), "識別情報", "このプロジェクトの識別コードはFOREST-9913です。", now)], []);
            await chat.SaveProjectAsync(a); await chat.SaveProjectAsync(b); chat.SelectProject(a.Id);
            async Task<InferenceJob> Send(string prompt)
            {
                int count = chat.Inferences.Items.Count; chat.Input = prompt; Check(chat.SendCommand.CanExecute(null), "送信できません: " + chat.Error); chat.SendCommand.Execute(null);
                await Until(() => chat.Inferences.Items.Count > count || !chat.IsBusy && chat.HasError);
                Check(chat.Inferences.Items.Count > count, chat.Error);
                await Until(() => chat.CanGenerate);
                return chat.Inferences.Items.Last();
            }
            const string longPrompt = "1から1000までの整数を順に、1行に1つずつ、途中を省略せずに書いてください。説明は不要です。";
            chat.SelectedMode = InferenceModes.Find("fast");
            InferenceJob first = await Send(longPrompt); Conversation firstChat = chat.SelectedConversation!;
            InferenceJob sibling = await Send("予約の順番を確認する質問です。2+3の答えだけを書いてください。");
            Check(sibling.State == "queued", "同じ会話の後続が並列実行されました。");
            chat.SelectProject(b.Id);
            InferenceJob other = await Send("このプロジェクトの識別コードだけを書いてください。"); Conversation otherChat = chat.SelectedConversation!;
            Check(other.State == "queued" && chat.CanNavigate && chat.CanGenerate && chat.SelectedConversation != firstChat, "処理中の移動・予約ができません。");
            chat.Inferences.Cancel(sibling);
            chat.SelectedModel = null; chat.SelectProject(null); chat.Input = "別の会話で編集中の下書き";
            Conversation draftChat = chat.SelectedConversation!;
            await Until(() => firstChat.Messages.Any(m => m.Role == "assistant" && m.Content.Length > 0));
            Check(chat.Input == draftChat.Draft && chat.SelectedConversation == draftChat && firstChat.Messages.Last().Content.Length == 0,
                "背景出力で表示中の下書きが変化したか、キャンセル済み予約が実行されました。");
            await PrototypePreview.RenderAsync(window, Path.Combine(store.Root, "background-chat.png"));
            chat.Inferences.Cancel(first); await Until(() => other.State is "complete" or "failed");
            ChatMessage otherAnswer = otherChat.Messages.Last();
            Check(other.State == "complete" && otherAnswer.Content.Contains("FOREST-9913", StringComparison.OrdinalIgnoreCase) &&
                otherAnswer.Knowledge.All(k => !k.Text.Contains("HARBOR", StringComparison.Ordinal)), "予約したモデル・プロジェクトの参照が変わりました: " + other.Detail + otherAnswer.Content);
            report["navigationAndSnapshot"] = new { otherAnswer.Content, draft = draftChat.Draft, siblingCanceled = sibling.State == "canceled", modelChangedWhileQueued = true };

            chat.SelectedModel = chat.Models.First(m => m.Id == model.Id); chat.SelectProject(null); chat.NewChat();
            await chat.ApplySettingsAsync(chat.Settings with { MaxConcurrentInferences = 2 });
            DownloadItem transfer = downloads.Enqueue(new("verification/controlled-transfer", new string('a', 40), "background.gguf", new string('0', 64), 524288, "MIT"));
            await Until(() => transfer.Received > 0);
            InferenceJob left = await Send(longPrompt); Conversation leftChat = chat.SelectedConversation!;
            chat.NewChat(); InferenceJob right = await Send(longPrompt); Conversation rightChat = chat.SelectedConversation!;
            await Until(() => left.State != "active" || right.State != "active" ||
                leftChat.Messages.Last().Content.Length > 30 && rightChat.Messages.Last().Content.Length > 30);
            Check(left.State == "active" && right.State == "active" && chat.Inferences.ActiveCount == 2 && downloads.IsActive && transfer.Received > 0,
                "2件の実推論とダウンロードの進行を同時に確認できませんでした: " + left.Detail + " / " + right.Detail);
            report["compute"] = chat.ActiveComputeLabels;
            var settings = new SettingsWindow(chat) { WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = -20000 };
            settings.Show(); Check((int)((ComboBox)settings.FindName("ConcurrencyBox")).SelectedItem == 2, "設定UIに並列数が反映されません。");
            await PrototypePreview.RenderAsync(settings, Path.Combine(store.Root, "concurrency-settings.png")); settings.Close();
            var queueWindow = new InferenceQueueWindow(chat) { WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = -20000 };
            queueWindow.Show(); await PrototypePreview.RenderAsync(queueWindow, Path.Combine(store.Root, "inference-queue.png"));
            queueWindow.Width = queueWindow.MinWidth; queueWindow.Height = queueWindow.MinHeight;
            await PrototypePreview.RenderAsync(queueWindow, Path.Combine(store.Root, "inference-queue-small.png")); queueWindow.Close();
            await PrototypePreview.RenderAsync(window, Path.Combine(store.Root, "two-generations.png"));
            window.Width = window.MinWidth; window.Height = window.MinHeight;
            await PrototypePreview.RenderAsync(window, Path.Combine(store.Root, "two-generations-small.png"));
            chat.SelectedConversation = leftChat; chat.StopCommand.Execute(null); await Until(() => left.State == "canceled");
            Check(right.State == "active", "片方を停止するともう片方も停止しました。");
            report["parallel"] = new
            {
                actualStreamingJobs = 2,
                leftCharacters = leftChat.Messages.Last().Content.Length,
                rightCharacters = rightChat.Messages.Last().Content.Length,
                controlledHttpTransferBytes = transfer.Received,
                isolatedStop = true
            };
            await chat.ApplySettingsAsync(chat.Settings with { MaxConcurrentInferences = 1, Theme = "light" });
            chat.NewChat(); InferenceJob after = await Send("2+3の答えを数字だけで回答してください。");
            Check(after.State == "queued" && right.State == "active", "並列数を下げたときに実行中の処理を止めたか、新規処理が上限を超えました。");
            Conversation sequenceChat = chat.SelectedConversation!;
            InferenceJob followUp = await Send("直前の計算の答えに7を足すといくつですか。数字だけで回答してください。");
            await PrototypePreview.RenderAsync(window, Path.Combine(store.Root, "background-light-small.png"));
            var lightQueue = new InferenceQueueWindow(chat) { Width = 560, Height = 420, WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = -20000 };
            lightQueue.Show(); await PrototypePreview.RenderAsync(lightQueue, Path.Combine(store.Root, "inference-queue-light-small.png")); lightQueue.Close();
            chat.Inferences.Cancel(right); await Until(() => followUp.State is "complete" or "failed");
            ChatMessage firstResult = sequenceChat.Messages.First(m => m.Id == after.Request.AnswerId);
            ChatMessage secondResult = sequenceChat.Messages.First(m => m.Id == followUp.Request.AnswerId);
            Check(after.State == "complete" && firstResult.Content.Contains('5') && followUp.State == "complete" && secondResult.Content.Contains("12", StringComparison.Ordinal),
                "同じ会話の先行回答を後続に引き継げませんでした: " + firstResult.Content + " / " + secondResult.Content);
            report["sameChatSequence"] = new[] { firstResult.Content, secondResult.Content };
            downloads.Pause(transfer); await Until(() => !downloads.IsActive);

            chat.Inferences.Suspended = true; chat.NewChat();
            InferenceJob pending = await Send("6+7の答えを数字だけで回答してください。");
            await chat.ShutdownAsync();
            window.Close(); await Until(() => !window.IsVisible); window = null;
            using var restored = new ChatViewModel(store); await restored.InitializeAsync();
            Check(!restored.IsBusy && restored.Inferences.Items.All(j => j.State == "paused") && restored.Inferences.Items.Any(j => j.Request.Id == pending.Request.Id), "再起動時の予約が失われたか、自動実行されました。");
            InferenceJob resumed = restored.Inferences.Items.Single(j => j.Request.Id == pending.Request.Id); restored.Inferences.Resume(resumed);
            await Until(() => resumed.State is "complete" or "failed");
            restored.OpenInference(resumed);
            Check(resumed.State == "complete" && restored.SelectedConversation!.Messages.Last().Content.Contains("13", StringComparison.Ordinal), "保存した予約を再開できませんでした: " + resumed.Detail);
            await restored.ShutdownAsync(); report["restartAndResume"] = true; report["success"] = true;
        }
        catch (Exception ex) { report["success"] = false; report["error"] = ex.ToString(); Environment.ExitCode = 1; }
        finally
        {
            if (window is not null) { await window.ViewModel.ShutdownAsync(); window.Close(); }
            await File.WriteAllTextAsync(Path.Combine(store.Root, "concurrent-verification.json"), JsonSerializer.Serialize(report, AppStore.JsonOptions));
            Application.Current.Shutdown(Environment.ExitCode);
        }
    }

    // Exercises the real transfer/write/progress/cancellation path without downloading another model.
    private sealed class TransferFixture : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        { Content = new StreamContent(new BlockingStream()) });
    }
    private sealed class BlockingStream : MemoryStream
    {
        private bool _read;
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_read) { await Task.Delay(Timeout.Infinite, cancellationToken); return 0; }
            _read = true; int length = Math.Min(buffer.Length, 16384); buffer.Span[..length].Clear(); return length;
        }
    }
}
