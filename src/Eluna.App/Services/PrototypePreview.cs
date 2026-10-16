using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Eluna.App.Controls;
using Eluna.App.Models;

namespace Eluna.App.Services;

internal static class PrototypePreview
{
    public static async Task CaptureAsync(MainWindow window, AppStore store, string output)
    {
        Directory.CreateDirectory(output);
        try
        {
            for (int i = 0; i < 600 && !window.ViewModel.IsReady && !window.ViewModel.HasError; i++)
            {
                await Task.Delay(100);
            }
            if (!window.ViewModel.IsReady)
            {
                throw new InvalidOperationException(window.ViewModel.Error);
            }
            foreach (WorkMode mode in WorkModes.All)
            {
                window.ViewModel.SelectedWorkMode = mode; await window.ViewModel.PersistSafelyAsync();
                if ((await store.LoadAsync()).Settings.WorkMode != mode.Id || window.ViewModel.NetworkNotice.Length == 0) { throw new InvalidOperationException("Search mode or external-communication notice was not preserved."); }
            }
            window.ViewModel.SelectedWorkMode = WorkModes.Find("chat");
            await RenderAsync(window, Path.Combine(output, "welcome.png"));
            var example = new Conversation { Title = "文章を読みやすく整える" };
            example.Messages.Add(new() { Role = "user", Content = "日本語の文章を、もっと読みやすくしたいです。" });
            example.Messages.Add(new() { Role = "assistant", Mode = "thorough", Content = "もちろんです。まずは、伝えたいことを一つに絞ってみましょう。\n\n読みやすい文章を作るための、3つのポイントです。\n\n1. 一文を短くする\n長い文は、意味のまとまりごとに分けます。\n\n2. 結論を先に伝える\n読み手が知りたいことから始めると、流れが自然になります。\n\n3. 具体的な言葉を選ぶ\n抽象的な表現を、場面が浮かぶ言葉に置き換えます。\n\n文章を貼っていただければ、一緒に整えていきます。" });
            window.ViewModel.Conversations.Insert(0, example);
            window.ViewModel.SelectedConversation = example;
            var model = new LocalModel("preview", "Qwen3.5-0.8B · Q4_K_M", "preview", "local", "preview", "", 579615840,
                Profile: new("画面確認用", "qwen35", 800000000, 262144, "Q4_K_M", true, 24, 1024, 2, 128, 128, "表示サンプル", ["ja", "en"]));
            window.ViewModel.Models.Add(model);
            window.ViewModel.SelectedModel = model;
            var modeSettings = new SettingsWindow(window.ViewModel, 7) { Owner = window, WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = -20000 }; modeSettings.Show();
            var modeList = (ComboBox)modeSettings.FindName("ThinkingBox");
            foreach (InferenceMode mode in InferenceModes.All)
            {
                modeList.SelectedItem = mode;
                ((Button)modeSettings.FindName("SaveButton")).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                for (int attempt = 0; attempt < 300 && !((Button)modeSettings.FindName("SaveButton")).IsEnabled; attempt++) { await Task.Delay(20); }
                if (window.ViewModel.SelectedMode.Id != mode.Id || (await store.LoadAsync()).Settings.InferenceMode != mode.Id)
                {
                    throw new InvalidOperationException("Mode selection or persistence failed.");
                }
            }
            modeSettings.Close();
            example.Messages[1].ThinkingSeconds = 7;
            await RenderAsync(window, Path.Combine(output, "chat.png"));
            example.Messages[1].Content = "# 結果を整理しました\n\n**重要な点**を表とコードでまとめます。[出典を開く](https://example.com)。\n\n| 項目 | 状態 |\n| --- | --- |\n| 履歴 | 保存済み |\n| 推論 | ローカル |\n\n```csharp\n// 日本語のコメント\npublic int Add(int a, int b)\n{\n    return a + b;\n}\n```\n\n> 元の会話を残して、別の回答も試せます。";
            await RenderAsync(window, Path.Combine(output, "markdown.png"));
            example.Fork(1, "別の回答");
            example.Messages.Add(new() { Role = "assistant", Content = "## 別の回答\n\n- 会話を分岐しました。\n- 元の回答は残っています。", Mode = "fast" });
            await RenderAsync(window, Path.Combine(output, "branches.png"));
            window.ViewModel.SelectedBranch = example.Branches[0];
            await window.ViewModel.ApplySettingsAsync(window.ViewModel.Settings with { Theme = "light" });
            await RenderAsync(window, Path.Combine(output, "light.png"));
            await window.ViewModel.ApplySettingsAsync(window.ViewModel.Settings with { Theme = "dark" });
            var settings = new SettingsWindow(window.ViewModel) { WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = -20000 };
            settings.Show(); await RenderAsync(settings, Path.Combine(output, "settings.png"));
            await CheckLicensesAsync(settings, output); settings.Close();
            var closingSettings = new SettingsWindow(window.ViewModel) { WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = -20000 };
            closingSettings.Show(); ((TabControl)closingSettings.FindName("SettingsTabs")).SelectedIndex = 1;
            Task loadingLicenses = ((LicensesPanel)closingSettings.FindName("LicenseView")).LoadAsync(); closingSettings.Close(); await loadingLicenses;
            var data = new DataWindow(window.ViewModel) { WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = -20000 };
            data.Show(); await RenderAsync(data, Path.Combine(output, "data.png")); data.Close();
            var updateWindow = new UpdateWindow { WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = -20000 };
            updateWindow.Show(); await RenderAsync(updateWindow, Path.Combine(output, "updates.png")); updateWindow.Close();
            var manager = new ModelsWindow(store, window.ViewModel) { WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = -20000 };
            manager.Show(); await RenderAsync(manager, Path.Combine(output, "model-manager.png")); manager.Width = manager.MinWidth; manager.Height = manager.MinHeight; await RenderAsync(manager, Path.Combine(output, "model-manager-small.png")); manager.Close();
            window.ViewModel.Downloads.Items.Add(new() { Name = "Qwen3.5-4B-Q4_K_M.gguf", ImportPath = "preview", Status = "active", Stage = "ダウンロード中", Received = 900000000, Total = 3000000000, Speed = 24000000 });
            window.ViewModel.Downloads.Items.Add(new() { Name = "Qwen3.5-2B-Q4_K_M.gguf", ImportPath = "preview", Status = "paused", Received = 300000000, Total = 1400000000 });
            window.ViewModel.Downloads.Items.Add(new() { Name = "Qwen3.5-0.8B-Q4_K_M.gguf", ImportPath = "preview", Status = "complete", ModelId = "preview", Received = 579615840, Total = 579615840 });
            var downloads = new DownloadsWindow(window.ViewModel.Downloads, window.ViewModel) { WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = -20000 };
            downloads.Show(); await RenderAsync(downloads, Path.Combine(output, "download-queue.png"));
            Button use = Buttons(downloads).Single(b => b.Content is "使う" && b.IsEnabled);
            LocalModel alternative = model with { Id = "preview-alternative", DisplayName = "別モデル（操作確認用）" }; window.ViewModel.Models.Add(alternative); window.ViewModel.SelectedModel = alternative;
            use.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            if (window.ViewModel.SelectedModel?.Id != model.Id) { throw new InvalidOperationException("Using a completed download did not select its model."); }
            Task managing = window.ViewModel.ManageModelsAsync(t => Task.Delay(250, t)); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            if (use.IsEnabled) { throw new InvalidOperationException("The downloaded model could be selected during a model operation."); }
            await managing; window.ViewModel.Models.Add(model); window.ViewModel.SelectedModel = model;
            downloads.Width = downloads.MinWidth; downloads.Height = downloads.MinHeight; await RenderAsync(downloads, Path.Combine(output, "download-queue-small.png")); downloads.Close();
            var withoutSelection = new DownloadsWindow(window.ViewModel.Downloads) { Left = -20000, Top = -20000, WindowStartupLocation = WindowStartupLocation.Manual }; withoutSelection.Show(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            if (Buttons(withoutSelection).Any(b => b.Content is "使う")) { throw new InvalidOperationException("A downloads window without model selection exposed an inactive use action."); }
            withoutSelection.Close();
            window.ViewModel.Downloads.Items.Clear();
            var attachmentExample = new Conversation { Title = "添付した文書を読む" };
            attachmentExample.Messages.Add(new() { Role = "user", Content = "この文書を要約してください。", Attachments = [new("企画書.docx", "document", "# 新しい企画\n\nこれは表示確認用の添付文書です。外部へ送信しません。")] });
            attachmentExample.Messages.Add(new() { Role = "assistant", Content = "添付内容をローカルで読み取り、ここに回答を表示します。これは表示サンプルです。" });
            attachmentExample.DraftAttachments.Add(new("追加のメモ.txt", "document", "次の質問で参照する文書です。"));
            window.ViewModel.Conversations.Add(attachmentExample); window.ViewModel.SelectedConversation = attachmentExample;
            var pixels = Enumerable.Repeat((byte)180, 24 * 24 * 3).ToArray();
            BitmapSource pastedImage = BitmapSource.Create(24, 24, 96, 96, PixelFormats.Rgb24, null, pixels, 24 * 3); pastedImage.Freeze();
            var pasteData = new DataObject(DataFormats.Bitmap, pastedImage);
            var paste = new DataObjectPastingEventArgs(pasteData, false, DataFormats.Bitmap) { RoutedEvent = DataObject.PastingEvent };
            ((TextBox)window.FindName("InputBox")).RaiseEvent(paste);
            for (int i = 0; i < 200 && (window.ViewModel.IsBusy || window.ViewModel.PendingAttachments.Count < 2); i++) { await Task.Delay(20); }
            if (!paste.CommandCancelled || window.ViewModel.PendingAttachments.Count != 2 || window.ViewModel.PendingAttachments.Last().ImageBase64 is null) { throw new InvalidOperationException("Pasted image was not staged as an attachment."); }
            await RenderAsync(window, Path.Combine(output, "attachments.png"));
            foreach (string tab in new[] { "document", "image", "vision" })
            {
                var media = new MediaWindow(store, window.ViewModel, tab, "# 日本語の文書\n\n編集してDOCX・PDF・Markdownへ保存できます。") { WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = -20000 };
                media.Show(); await RenderAsync(media, Path.Combine(output, "media-" + tab + ".png")); media.Width = media.MinWidth; media.Height = media.MinHeight; await RenderAsync(media, Path.Combine(output, "media-" + tab + "-small.png")); media.Close();
            }
            var autoWork = new WorkSession { AutomaticSearch = true, Question = "llama.cppの最新情報は？", ModelId = model.Id, Stage = "complete", SearchesUsed = 1, PagesUsed = 1 };
            autoWork.Activities.Add(new(DateTimeOffset.UtcNow, "routing", "必要に応じてWeb検索", "新しい情報・出典の確認が必要です（表示確認用）。"));
            autoWork.Queries.Add("llama.cpp 最新情報");
            var autoAnswer = new ChatMessage { Role = "assistant", Work = autoWork, Content = "必要に応じて検索した回答には、取得した資料と活動を表示します。これは画面確認用のサンプルです。" };
            var autoExample = new Conversation { Title = autoWork.Question }; autoExample.Messages.Add(new() { Role = "user", Content = autoWork.Question }); autoExample.Messages.Add(autoAnswer);
            window.ViewModel.Conversations.Add(autoExample); window.ViewModel.SelectedConversation = autoExample;
            await RenderAsync(window, Path.Combine(output, "auto-search.png"));
            window.ViewModel.SelectedWorkMode = WorkModes.Find("offline");
            await RenderAsync(window, Path.Combine(output, "search-off.png"));
            window.ViewModel.SelectedWorkMode = WorkModes.Find("chat");
            var work = new WorkSession { Mode = "debate", Question = "ローカルAIの使い方を検討する", ModelId = model.Id, Stage = "paused", Plan = "公式資料から条件を調べ、利点と制約を比較します。", PlanApproved = true, SearchesUsed = 2, PagesUsed = 2, TokensUsed = 1480, SecondsUsed = 42 };
            work.Queries.Add("llama.cpp Windows Vulkan");
            work.Sources.Add(new(1, "llama.cpp 公式ドキュメント（画面確認用）", "https://github.com/ggml-org/llama.cpp", "これは表示確認用の資料本文です。実際の調査結果ではありません。", DateTimeOffset.UtcNow));
            work.Activities.Add(new(DateTimeOffset.Now, "searching", "検索: llama.cpp Windows Vulkan", "Bing・DuckDuckGo・Wikipediaへ問い合わせました（表示サンプル）。"));
            work.Activities.Add(new(DateTimeOffset.Now, "source", "本文取得: 公式資料", "資料の本文と閲覧日時を保存しました。"));
            work.Activities.Add(new(DateTimeOffset.Now, "warning", "停止しました", "取得済みの資料と完了した工程を保存しました。"));
            work.Turns.Add(new(1, "提案役", "ローカルで処理できる利点を検討します。これは画面確認用のサンプルです。"));
            var workAnswer = new ChatMessage { Role = "assistant", Work = work, Content = "## 調査の途中結果\n\n活動と資料を確認して、途中から再開できます。" };
            var workExample = new Conversation { Title = work.Question }; workExample.Messages.Add(new() { Role = "user", Content = work.Question }); workExample.Messages.Add(workAnswer);
            window.ViewModel.Conversations.Add(workExample); window.ViewModel.SelectedConversation = workExample;
            window.ViewModel.SelectedWorkMode = WorkModes.Find("debate");
            await RenderAsync(window, Path.Combine(output, "work-chat.png"));
            var workWindow = new WorkWindow(window.ViewModel, workAnswer) { WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = -20000 };
            workWindow.Show(); await RenderAsync(workWindow, Path.Combine(output, "work-activity.png")); workWindow.Width = workWindow.MinWidth; workWindow.Height = workWindow.MinHeight; await RenderAsync(workWindow, Path.Combine(output, "work-activity-small.png")); workWindow.Close();
            var sourceWindow = new SourceWindow(work.Sources[0]) { WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = -20000 };
            sourceWindow.Show(); await RenderAsync(sourceWindow, Path.Combine(output, "source.png")); sourceWindow.Close();
            await CheckParticipantsAsync(window, store, output, model);
            var planWindow = new WorkPlanWindow(work) { WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = -20000 };
            planWindow.Show(); await RenderAsync(planWindow, Path.Combine(output, "work-plan.png")); planWindow.Close();
            window.ViewModel.SelectedWorkMode = WorkModes.Find("chat"); window.ViewModel.SelectedConversation = example;
            var movedModes = new SettingsWindow(window.ViewModel, 7) { Owner = window, WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = -20000 }; movedModes.Show();
            await RenderAsync(movedModes, Path.Combine(output, "mode-settings.png")); movedModes.Close();
            var thinkingMessage = new ChatMessage { Role = "assistant", Mode = "thorough", IsGenerating = true, IsThinking = true };
            example.Messages.Add(thinkingMessage);
            await RenderAsync(window, Path.Combine(output, "thinking.png"));
            example.Messages.Remove(thinkingMessage);
            window.Width = 860;
            window.Height = 640;
            await RenderAsync(window, Path.Combine(output, "compact.png"));
            window.Width = window.MinWidth;
            window.Height = window.MinHeight;
            await RenderAsync(window, Path.Combine(output, "minimum.png"));
            var branchSettings = new SettingsWindow(window.ViewModel, 7) { Owner = window, WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = -20000 }; branchSettings.Show();
            if (((ComboBox)branchSettings.FindName("BranchSelector")).SelectedItem != window.ViewModel.SelectedBranch) { throw new InvalidOperationException("Branch selector lost its selection in channel settings."); }
            branchSettings.Close();
            var welcome = new Conversation();
            window.ViewModel.Conversations.Add(welcome);
            window.ViewModel.SelectedConversation = welcome;
            await RenderAsync(window, Path.Combine(output, "minimum-welcome.png"));
            window.ViewModel.SelectedModel = null;
            await RenderAsync(window, Path.Combine(output, "minimum-setup.png"));
            ((ScrollViewer)window.FindName("WelcomeScroll")).ScrollToEnd();
            await RenderAsync(window, Path.Combine(output, "minimum-setup-end.png"));
            var models = new ModelWindow(store) { WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = -20000 };
            models.Show();
            for (int i = 0; i < 100 && ((ListBox)models.FindName("FileList")).Items.Count == 0; i++)
            {
                await Task.Delay(100);
            }
            await RenderAsync(models, Path.Combine(output, "models.png"));
            models.SetBusy(true);
            models.UpdateProgress(new(0, 579615840, "ダウンロード中"));
            await RenderAsync(models, Path.Combine(output, "models-download-start.png"));
            models.UpdateProgress(new(275L * 1024 * 1024, 579615840, "ダウンロード中"));
            await RenderAsync(models, Path.Combine(output, "models-downloading.png"));
            models.Width = models.MinWidth;
            models.Height = models.MinHeight;
            await RenderAsync(models, Path.Combine(output, "models-downloading-compact.png"));
            models.UpdateProgress(new(579615840, 579615840, "SHA-256を確認中"));
            await RenderAsync(models, Path.Combine(output, "models-verifying.png"));
            models.SetBusy(false);
            ((TextBlock)models.FindName("StatusText")).Text = "中止しました。ダウンロードは次回、途中から再開できます。";
            await RenderAsync(models, Path.Combine(output, "models-cancelled.png"));
            models.Close();
            await CheckProjectsAsync(window, store, output);
            await File.WriteAllTextAsync(Path.Combine(output, "result.txt"), "UI rendering, auto/forced/offline search modes and notices, mode/settings persistence, download selection, debate participant drops/order/limits/persistence and chat rows: OK.");
        }
        catch (Exception ex)
        {
            await File.WriteAllTextAsync(Path.Combine(output, "result.txt"), ex.ToString());
            Environment.ExitCode = 1;
        }
        finally
        {
            window.Close();
        }
    }

    private static async Task CheckParticipantsAsync(MainWindow window, AppStore store, string output, LocalModel primary)
    {
        void Assert(bool condition, string message) { if (!condition) { throw new InvalidOperationException(message); } }
        AppSettings original = window.ViewModel.Settings;
        LocalModel secondary = primary with { Id = "preview-debate", DisplayName = "Qwen3.5-4B · Q4_K_M（表示サンプル）", Size = 2800000000 };
        window.ViewModel.Models.Add(secondary);
        var options = new WorkOptionsWindow(window.ViewModel) { WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = -20000 };
        options.Show(); Assert(options.OptionsTabs.SelectedIndex == 0, "Debate settings did not open directly on participants.");
        var channelSettings = new SettingsWindow(window.ViewModel, 7) { Owner = window, WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = -20000 }; channelSettings.Show();
        Assert(Buttons(channelSettings).Any(b => b.Content is "検索・調査・議論の設定"), "Channel settings did not expose debate settings."); channelSettings.Close();
        DebateParticipantsEditor editor = options.ParticipantsEditor;
        DebateRole[] before = editor.GetRoles().ToArray(); DebateParticipantDraft first = editor.Participants[0], last = editor.Participants[^1];
        Assert(editor.ApplyDrop(editor.ModelData(secondary.Id), first) && first.SelectedModel.Id == secondary.Id && first.Id == before[0].Id, "Replacing a participant's model changed its identity or failed.");
        Assert(editor.ApplyDrop(editor.ParticipantData(last.Id), first) && editor.Participants[0].Id == last.Id, "Moving a participant before another row failed.");
        Assert(editor.ApplyDrop(editor.ParticipantData(last.Id), first, after: true) && editor.Participants[1].Id == last.Id, "Moving a participant downward produced an incorrect insertion index.");
        Button up = Buttons(editor).Single(b => b.Content is "↑ 上へ"), down = Buttons(editor).Single(b => b.Content is "↓ 下へ");
        Assert(up.IsEnabled && down.IsEnabled, "The keyboard-accessible reorder actions did not follow the selected participant.");
        up.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent)); Assert(editor.Participants[0].Id == last.Id, "The move-up action failed.");
        down.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent)); Assert(editor.Participants[1].Id == last.Id, "The move-down action failed.");
        Assert(editor.ApplyDrop(editor.ModelData(secondary.Id), first), "Selecting the first participant failed.");
        editor.DetailsExpander.IsExpanded = true; await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        editor.ParticipantNameBox.Text = "提案役 · 公式資料を確認"; editor.ParticipantInstructionBox.Text = "一次資料を示し、前提条件と改善案を提案する。";
        Assert(first.Name == editor.ParticipantNameBox.Text && first.Instruction == editor.ParticipantInstructionBox.Text && Equals(editor.ParticipantModelBox.SelectedItem, first.SelectedModel), "The selected participant's editor bindings were disconnected.");
        await RenderAsync(options, Path.Combine(output, "work-roles-details.png")); editor.DetailsExpander.IsExpanded = false;
        await RenderAsync(options, Path.Combine(output, "work-roles.png"));
        options.Width = options.MinWidth; options.Height = options.MinHeight; await RenderAsync(options, Path.Combine(output, "work-roles-small.png"));
        Assert(editor.ApplyDrop(editor.ModelData(primary.Id)), "Dropping a model onto empty space did not add a participant.");
        while (editor.Participants.Count < 6) { Assert(editor.ApplyDrop(editor.ModelData(primary.Id)), "Adding up to six participants failed."); }
        Assert(!editor.CanAdd && !editor.ApplyDrop(editor.ModelData(primary.Id)) && editor.Participants.Count == 6, "More than six participants were accepted.");
        Assert(editor.ApplyDrop(editor.ModelData(primary.Id), first), "Replacing a model at the six-participant limit was incorrectly blocked.");
        var foreign = new DebateParticipantsEditor(before, [primary, secondary]);
        Assert(!editor.ApplyDrop(foreign.ModelData(primary.Id)) && !editor.ApplyDrop(new DataObject(DataFormats.Text, "model:" + primary.Id)) && !editor.ApplyDrop(editor.ModelData("unknown")), "Unrelated or unknown drag data changed the participants.");
        while (editor.Participants.Count > 2) { Assert(editor.ApplyDrop(editor.ParticipantData(editor.Participants[^1].Id), remove: true), "Dropping a participant onto the remove area failed."); }
        Assert(!editor.CanRemove && !editor.ApplyDrop(editor.ParticipantData(first.Id), remove: true) && editor.Participants.Count == 2, "A debate with fewer than two participants was accepted.");
        Assert(editor.ApplyDrop(editor.ModelData(secondary.Id), first), "Assigning the model for persistence verification failed.");
        WorkOptions configured = options.CreateOptions();
        Assert((window.ViewModel.Settings.WorkOptions?.Participants ?? WorkOptions.DefaultRoles).SequenceEqual(before), "Draft changes affected live settings before Save.");
        await window.ViewModel.ApplySettingsAsync(window.ViewModel.Settings with { WorkOptions = configured });
        WorkOptions restored = (await store.LoadAsync()).Settings.WorkOptions!;
        Assert(restored.Participants.SequenceEqual(configured.Participants) && restored.Participants[0].Name == first.Name && restored.Participants[0].Instruction == first.Instruction, "Participant order, names, instructions or models were not saved.");
        var missing = new DebateParticipantsEditor([before[0] with { ModelId = "missing-model" }, before[1]], [primary]);
        Assert(missing.GetRoles()[0].ModelId == "missing-model", "Opening settings silently replaced a missing model assignment.");
        options.OptionsTabs.SelectedIndex = 1; await RenderAsync(options, Path.Combine(output, "work-options.png")); options.Close();
        await window.ViewModel.ApplySettingsAsync(original); window.ViewModel.Models.Remove(secondary);
        await File.WriteAllTextAsync(Path.Combine(output, "participants-check.txt"), "Replace, move before/after, add/remove, 2-6 limits, isolated drag data, draft isolation, missing models and ordered settings persistence: OK.");
    }

    private static async Task CheckLicensesAsync(SettingsWindow settings, string output)
    {
        var tabs = (TabControl)settings.FindName("SettingsTabs");
        var panel = (LicensesPanel)settings.FindName("LicenseView");
        var notices = (ListBox)panel.FindName("NoticesList");
        var filter = (TextBox)panel.FindName("FilterBox");
        var text = (TextBox)panel.FindName("NoticeText");
        var save = (Button)settings.FindName("SaveButton");
        var instructions = (TextBox)settings.FindName("InstructionBox");
        instructions.Text = "保存前の編集をタブ切り替えで失わない確認";
        tabs.SelectedIndex = 1; await panel.LoadAsync();
        LicenseDocument[] documents = notices.Items.Cast<LicenseDocument>().ToArray();
        if (documents.Length < 4 || documents[1].Title != "llama.cpp" || documents[2].Title != "stable-diffusion.cpp" || !text.IsReadOnly || !save.IsVisible)
        { throw new InvalidOperationException("The license tab lost notices, read-only text or the global save action for pending changes."); }
        bool bundled = typeof(LicenseCatalog).Assembly.GetManifestResourceInfo("Eluna.NativeRuntime.zip") is not null;
        if (bundled && (!documents.Any(d => d.Title == "Vulkan") || !documents.Any(d => d.Title == ".NET ランタイム") || !documents.Any(d => d.FileName.StartsWith("vendor/", StringComparison.Ordinal))))
        { throw new InvalidOperationException("The portable license tab omitted native, runtime or vendor notices."); }
        filter.Text = "LLAMA.CPP-MIT";
        if (notices.Items.Count != 1 || text.Text != documents[1].Text) { throw new InvalidOperationException("Filtering licenses did not select the matching original text."); }
        text.SelectAll();
        if (!System.Windows.Input.ApplicationCommands.Copy.CanExecute(null, text)) { throw new InvalidOperationException("The original license text could not be selected for copying."); }
        filter.Text = "no-license-matches-this-filter";
        if (notices.Items.Count != 0 || text.Text.Length != 0) { throw new InvalidOperationException("An empty license filter retained stale text."); }
        filter.Clear(); notices.SelectedItem = documents[2];
        await RenderAsync(settings, Path.Combine(output, "licenses.png"));
        settings.Width = settings.MinWidth; settings.Height = settings.MinHeight;
        await RenderAsync(settings, Path.Combine(output, "licenses-small.png"));
        if (text.ActualWidth < 180 || text.ActualHeight < 70 || text.Text != documents[2].Text) { throw new InvalidOperationException("The license original became unreadable at the minimum settings size."); }
        Appearance.Apply("light"); await RenderAsync(settings, Path.Combine(output, "licenses-light-small.png"));
        settings.Width = 860; settings.Height = 740;
        await RenderAsync(settings, Path.Combine(output, "licenses-light.png"));
        Appearance.Apply("dark");
        LicenseDocument largest = documents.MaxBy(d => d.Text.Length)!;
        notices.SelectedItem = largest; text.ScrollToEnd(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        if (text.Text != largest.Text || (largest.Text.Length > 10000 && text.VerticalOffset == 0)) { throw new InvalidOperationException("A large bundled license was truncated or could not be scrolled."); }
        tabs.SelectedIndex = 0; await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        if (!save.IsVisible || instructions.Text != "保存前の編集をタブ切り替えで失わない確認") { throw new InvalidOperationException("Switching settings tabs discarded general settings or hid their Save action."); }
        tabs.SelectedIndex = 1; await panel.LoadAsync();
        if (!ReferenceEquals(notices.SelectedItem, largest)) { throw new InvalidOperationException("Returning to the license tab unnecessarily reloaded the viewer."); }
        await File.WriteAllTextAsync(Path.Combine(output, "licenses-check.txt"), $"{documents.Length} notices; portable bundle: {bundled}; primary notices, filtering/empty results, exact selectable originals, large text/scroll, theme/minimum-size rendering and unsaved settings preserved: OK.");
    }

    private static async Task CheckProjectsAsync(MainWindow window, AppStore store, string output)
    {
        void Require(bool value, string message) { if (!value) { throw new InvalidOperationException(message); } }
        var chat = window.ViewModel;
        AppState initial = chat.CaptureState(); Conversation? original = chat.SelectedConversation;
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var a = new KnowledgeProject(Guid.NewGuid(), "港町の企画 · 画面確認用", "日本語で、決定事項と未確認事項を分けて説明する。", "project-only", true,
            [new(Guid.NewGuid(), "企画の前提.md", "# 企画の前提\n\nこの文書は画面確認用です。\n港町の企画の識別コードはHARBOR-4826です。\n予算の上限は300万円。", now)], [new(Guid.NewGuid(), "作成する資料は社内向け。決定事項を最初に書く。", now)]);
        var b = new KnowledgeProject(Guid.NewGuid(), "別の企画 · 分離確認用", "別プロジェクトの情報", "project-only", true, [], []);
        await chat.SaveProjectAsync(a); chat.Input = "企画Aの下書き";
        await chat.SaveProjectAsync(b); chat.Input = "企画Bの下書き";
        chat.SelectProject(a.Id); Require(chat.Input == "企画Aの下書き", "Switching projects lost a conversation draft.");
        Require(chat.HistoryView.Cast<Conversation>().All(c => c.ProjectId == a.Id), "The sidebar exposed another project's chats.");
        chat.NewChat(); Require(chat.SelectedConversation!.ProjectId == a.Id, "A new project chat was created outside its project.");
        var example = new Conversation { ProjectId = a.Id, Title = "企画の前提を確認" }; example.Messages.Add(new() { Role = "user", Content = "この企画で決まっていることは？" });
        var knowledge = await chat.BuildKnowledgeAsync(example, "港町の企画 予算", 8192, default);
        example.Messages.Add(new() { Role = "assistant", Content = "## 決定事項\n\n企画の予算上限は300万円です。[K1]\n\nこれはナレッジ参照の画面確認用サンプルです。", Knowledge = knowledge.References });
        chat.Conversations.Add(example); chat.SelectedConversation = example;
        window.Width = 1280; window.Height = 900;
        await RenderAsync(window, Path.Combine(output, "project-chat.png"));
        window.Width = window.MinWidth; window.Height = window.MinHeight;
        await RenderAsync(window, Path.Combine(output, "project-chat-small.png"));
        Require(((ScrollViewer)window.FindName("WelcomeScroll")).Visibility == Visibility.Collapsed, "Project history was obscured by the welcome screen.");
        var project = new ProjectWindow(chat, a) { WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = -20000 };
        project.Show(); Require(!((ComboBox)project.FindName("ScopeBox")).IsEnabled, "An existing project's memory boundary could be changed without a new project.");
        await RenderAsync(project, Path.Combine(output, "project-overview.png"));
        string file = Path.Combine(output, "knowledge-import.txt"); await File.WriteAllTextAsync(file, "登録テスト資料。プロジェクト内の資料コードはPROJECT-7712です。");
        await project.ImportFilesAsync([file]);
        Require(project.CreateDraft().Documents.Count == 2 && chat.Projects.Single(p => p.Id == a.Id).Documents.Count == 1, "Staged project files modified live knowledge before Save.");
        ((TabControl)project.FindName("ProjectTabs")).SelectedIndex = 1;
        await RenderAsync(project, Path.Combine(output, "project-documents.png"));
        project.Width = project.MinWidth; project.Height = project.MinHeight;
        await RenderAsync(project, Path.Combine(output, "project-documents-small.png"));
        ((TabControl)project.FindName("ProjectTabs")).SelectedIndex = 2;
        await RenderAsync(project, Path.Combine(output, "project-memory-small.png"));
        Appearance.Apply("light"); await RenderAsync(project, Path.Combine(output, "project-memory-light-small.png")); Appearance.Apply("dark");
        await chat.SaveProjectAsync(project.CreateDraft()); project.Close();
        var create = new ProjectWindow(chat) { WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = -20000 };
        create.Show(); Require(create.CreateDraft().MemoryMode == "project-only", "New projects did not default to project-only memory.");
        await RenderAsync(create, Path.Combine(output, "project-create.png")); create.Close();
        Require(chat.Projects.Count == (initial.Projects?.Count ?? 0) + 2, "Canceling project creation saved an unwanted project.");
        var settings = new SettingsWindow(chat, 2) { WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = -20000 };
        settings.Show(); await RenderAsync(settings, Path.Combine(output, "common-memory.png")); settings.Width = settings.MinWidth; settings.Height = settings.MinHeight;
        await RenderAsync(settings, Path.Combine(output, "common-memory-small.png")); settings.Close();
        var sources = new KnowledgeWindow(knowledge.References) { WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = -20000 };
        sources.Show(); await RenderAsync(sources, Path.Combine(output, "knowledge-references.png")); sources.Close();
        Directory.CreateDirectory(Path.GetDirectoryName(store.DocumentDraftPath(a.Id))!);
        await File.WriteAllTextAsync(store.DocumentDraftPath(a.Id, chat.SelectedConversation!.Id), "A_ONLY_DOCUMENT_DRAFT");
        var mediaA = new MediaWindow(store, chat) { WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = -20000 };
        mediaA.Show(); Require(mediaA.DraftText == "A_ONLY_DOCUMENT_DRAFT" && mediaA.ScopeMatches, "Project document drafts were read from another scope.");
        chat.SelectProject(b.Id); Require(!mediaA.ScopeMatches, "An open document tool silently followed a different project.");
        Buttons(mediaA).Single(button => button.Content is "AIで作成").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        Require(!chat.IsBusy && mediaA.OperationStatus.Contains("開いたチャンネル・分岐へ戻す", StringComparison.Ordinal), "Document generation used a different project from the one shown in the tool.");
        await File.WriteAllTextAsync(store.DocumentDraftPath(b.Id, chat.SelectedConversation!.Id), "B_ONLY_DOCUMENT_DRAFT");
        var mediaB = new MediaWindow(store, chat) { WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = -20000 };
        mediaB.Show(); Require(mediaB.DraftText == "B_ONLY_DOCUMENT_DRAFT", "Document draft isolation failed after switching projects."); mediaB.Close(); mediaA.Close(); chat.SelectProject(a.Id); chat.SelectedConversation = example;
        var note = new MemoryNote(Guid.NewGuid(), "取り込み時の共通メモリ確認用 SAMPLE-COMMON-9913", now);
        await chat.ApplySettingsAsync(chat.Settings, [.. initial.CommonMemories ?? [], note]);
        string export = Path.Combine(output, "projects-export.json"); await chat.ExportHistoryAsync(export);
        await chat.ApplySettingsAsync(chat.Settings, initial.CommonMemories ?? []);
        Guid[] idsBeforeImport = chat.Projects.Select(p => p.Id).ToArray();
        await chat.ImportHistoryAsync(export);
        Require(chat.CommonMemories.All(m => m.Id != note.Id && m.Text != note.Text) && chat.Projects.Where(p => !idsBeforeImport.Contains(p.Id)).All(p => p.Id != a.Id && p.Id != b.Id), "History import merged project IDs or enabled shared memories without opt-in.");
        await chat.ImportHistoryAsync(export, includeCommonMemories: true);
        Require(chat.CommonMemories.Count(m => m.Text == note.Text) == 1, "Explicit shared-memory import failed or duplicated content.");
        AppState persisted = await store.LoadAsync();
        Require(persisted.Projects!.Single(p => p.Id == a.Id).Documents.Count == 2 && persisted.Conversations.All(c => c.ProjectId is null || persisted.Projects!.Any(p => p.Id == c.ProjectId)), "Imported or saved project memberships became orphaned.");
        using (var restarted = new ViewModels.ChatViewModel(store))
        {
            await restarted.InitializeAsync();
            Require(restarted.IsReady && restarted.SelectedProject?.Id == a.Id && restarted.CommonMemories.Any(m => m.Text == note.Text), "Project scope or shared memory was lost on restart.");
        }
        example.Summary = "移動前の引き継ぎメモ"; example.SummaryThrough = 1;
        example.Messages[^1].Work = new() { Stage = "paused", Question = "サンプル", ModelId = "preview" };
        await chat.MoveConversationAsync(example, b.Id);
        Require(chat.SelectedProject?.Id == b.Id && example.Branches.All(branch => branch.Summary.Length == 0 && branch.SummaryThrough == 0) && !example.Messages[^1].Work!.CanResume, "Moving a chat retained old summaries/research continuation or the wrong scope.");
        foreach (KnowledgeProject added in chat.Projects.Where(p => initial.Projects?.All(old => old.Id != p.Id) != false).ToArray()) { await chat.DeleteProjectAsync(added.Id); }
        HashSet<Guid> originalIds = initial.Conversations.Select(c => c.Id).ToHashSet();
        foreach (Conversation added in chat.Conversations.Where(c => !originalIds.Contains(c.Id)).ToArray()) { chat.Conversations.Remove(added); }
        chat.SelectProject(initial.Settings.SelectedProjectId); await chat.ApplySettingsAsync(initial.Settings, initial.CommonMemories ?? []);
        if (original is not null) { chat.SelectedConversation = original; }
        await chat.PersistSafelyAsync();
        Require(chat.Projects.Count == (initial.Projects?.Count ?? 0) && !chat.Conversations.Any(c => c.Id == example.Id), "Deleting a project did not remove its member chats or changed another project.");
        await File.WriteAllTextAsync(Path.Combine(output, "projects-check.txt"), "Project-only default; scoped history/new chats/drafts; staged file import; document/memory/reference views in dark/light/minimum sizes; project isolation on import; shared-memory opt-in; restart; chat move invalidation; project/chat deletion: OK.");
    }

    private static IEnumerable<Button> Buttons(DependencyObject parent)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(parent, i); if (child is Button button) { yield return button; }
            foreach (Button descendant in Buttons(child)) { yield return descendant; }
        }
    }

    internal static async Task RenderAsync(Window window, string path, System.Windows.Controls.Primitives.Popup? popup = null)
    {
        await Task.Delay(150);
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        var content = (FrameworkElement)VisualTreeHelper.GetChild(window, 0);
        content.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth), (int)Math.Ceiling(content.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        var visual = new DrawingVisual();
        using (DrawingContext drawing = visual.RenderOpen())
        {
            var bounds = new Rect(0, 0, content.ActualWidth, content.ActualHeight);
            drawing.DrawRectangle(window.Background, null, bounds);
            drawing.DrawRectangle(new VisualBrush(content) { ViewboxUnits = BrushMappingMode.Absolute, Viewbox = bounds }, null, bounds);
            if (popup is { Child: FrameworkElement menu, PlacementTarget: FrameworkElement button })
            {
                menu.UpdateLayout();
                double offset = popup.Placement == System.Windows.Controls.Primitives.PlacementMode.Top
                    ? -menu.ActualHeight + popup.VerticalOffset : button.ActualHeight + popup.VerticalOffset;
                Point anchor = popup.Placement == System.Windows.Controls.Primitives.PlacementMode.Right ? new Point(button.ActualWidth, 0) : new Point(0, offset);
                Point location = button.TransformToAncestor(content).Transform(anchor);
                location.Y = Math.Clamp(location.Y, 0, Math.Max(0, content.ActualHeight - menu.ActualHeight));
                location.X = Math.Clamp(location.X, 0, Math.Max(0, content.ActualWidth - menu.ActualWidth));
                drawing.DrawRectangle(new VisualBrush(menu), null, new Rect(location.X, location.Y, menu.ActualWidth, menu.ActualHeight));
            }
        }
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        await using var stream = File.Create(path);
        encoder.Save(stream);
    }
}
