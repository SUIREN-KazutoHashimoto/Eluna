using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using Eluna.App.Controls;
using Eluna.App.Models;
using Eluna.App.ViewModels;

namespace Eluna.App.Services;

internal static class UiUxPolishVerification
{
    private static void Check(bool success, string message) { if (!success) { throw new InvalidOperationException(message); } }
    private static async Task Until(Func<bool> condition)
    { for (int i = 0; i < 500; i++) { if (condition()) { return; } await Task.Delay(20); } throw new TimeoutException("UI operation did not settle."); }
    private static IEnumerable<T> Find<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        { DependencyObject child = VisualTreeHelper.GetChild(root, i); if (child is T typed) { yield return typed; } foreach (T nested in Find<T>(child)) { yield return nested; } }
    }
    private static T Offscreen<T>(T window) where T : Window
    { window.WindowStartupLocation = WindowStartupLocation.Manual; window.Left = -20000; window.Top = -20000; window.Show(); return window; }
    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
    internal static async Task RunAsync(MainWindow window, AppStore store, string output)
    {
        Directory.CreateDirectory(output); var report = new Dictionary<string, object>(); var fixtures = new List<Conversation>();
        var vm = window.ViewModel;
        try
        {
            await Until(() => vm.IsReady || vm.HasError); Check(vm.IsReady, vm.Error);
            var model = new LocalModel("ui-polish", "日本語モデル · 確認用", "audit", "local", "audit", "", 579615840,
                Profile: new("画面確認", "qwen35", 800000000, 262144, "Q4_K_M", true, 24, 1024, 2, 128, 128, "確認用", ["ja", "en"]));
            vm.Models.Add(model); vm.SelectedModel = model;
            var conversation = new Conversation { Title = "UI改善の確認" };
            conversation.Messages.Add(new() { Role = "user", Content = "表示と操作を確認してください。" });
            conversation.Messages.Add(new() { Role = "assistant", Content = "## 画面の確認\n\n本文を選択しながら、別のチャットでも作業できます。\n\n```csharp\nvar value = 42;\n```\n\n- 管理機能は設定に集約\n- 進捗と予約は処理リストへ\n- 会話を広く表示" });
            vm.Conversations.Insert(0, conversation); vm.SelectedConversation = conversation;
            for (int i = 0; i < 24; i++) { var c = new Conversation { Title = $"会話 {i + 1} · 長い名前を読みながら作業する" }; vm.Conversations.Add(c); fixtures.Add(c); }
            window.Width = 820; window.Height = 600;
            await PrototypePreview.RenderAsync(window, Path.Combine(output, "main-minimum.png"));
            Check(window.FindName("ModelSelector") is null && !Find<ComboBox>(window).Any(), "AI selectors remain in the conversation shell.");
            double messageHeight = ((ListBox)window.FindName("MessagesList")).ActualHeight;
            double historyHeight = ((ListBox)window.FindName("HistoryList")).ActualHeight;
            Check(messageHeight >= 300 && historyHeight >= 240, "The compact layout did not leave enough space for chat and history.");
            Check(!Find<Button>(window).Any(b => b.Content is "モデル管理" or "データ・バックアップ" or "ダウンロード管理"), "Management controls remain on the main sidebar.");
            report["density"] = new { minimumWidth = 820, minimumHeight = 600, messageHeight, historyHeight };

            var settings = Offscreen(new SettingsWindow(vm, 4) { Owner = window });
            await PrototypePreview.RenderAsync(settings, Path.Combine(output, "settings-management.png"));
            foreach (string label in new[] { "モデルを探す", "モデル管理", "データ・バックアップ", "ダウンロード管理" }) { Check(Find<Button>(settings).Any(b => b.Content is string s && s == label && b.IsVisible), "Missing management route: " + label); }
            await CheckComputerAwakeAsync(vm, store, output);
            report["computerUseKeepAwake"] = "default-on, unsaved protection, explicit off/on persistence, dark/light minimum size and 130% text";
            settings.SelectTab(0); ((TextBox)settings.FindName("InstructionBox")).Text += "\n未保存の変更";
            int reviews = 0; UnsavedChanges.Review = _ => { reviews++; return null; }; settings.Close();
            Check(settings.IsVisible && reviews == 1, "Settings discarded an unsaved edit without cancellation.");
            UnsavedChanges.Review = _ => false; settings.Close(); await Until(() => !settings.IsVisible);
            var project = Offscreen(new ProjectWindow(vm) { Owner = window }); ((TextBox)project.FindName("NameBox")).Text = "未保存のプロジェクト";
            UnsavedChanges.Review = _ => null; project.Close(); Check(project.IsVisible, "Project edits were discarded.");
            UnsavedChanges.Review = _ => false; project.Close(); await Until(() => !project.IsVisible);
            var manager = Offscreen(new ModelsWindow(store, vm) { Owner = window });
            await Task.Delay(100); TextBox modelName = Find<TextBox>(manager).Single(t => AutomationProperties.GetName(t) == "モデルの表示名"); modelName.Text = "保存前の変更";
            UnsavedChanges.Review = _ => null; manager.Close(); Check(manager.IsVisible, "Model edits were discarded.");
            UnsavedChanges.Review = _ => false; manager.Close(); await Until(() => !manager.IsVisible);
            report["unsavedChangesProtected"] = true;

            vm.Error = "添付に失敗しました"; int beforeBranches = conversation.Branches.Count; vm.RetryCommand.Execute(null); await Task.Delay(50);
            Check(conversation.Branches.Count == beforeBranches && vm.Inferences.Items.Count == 0 && !vm.CanRetry, "An unrelated error regenerated a response.");
            int retried = 0; vm.SetError("操作の失敗", () => { retried++; return Task.CompletedTask; }); vm.RetryCommand.Execute(null); await Until(() => retried == 1);
            report["retryKeepsOriginalOperation"] = true;

            var started = new List<string>(); var hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Task running = vm.QueueMediaOperationAsync("文書を作成 · 確認用", async token => { started.Add("first"); vm.ReportMediaProgress("本文を生成しています · 確認用"); await hold.Task.WaitAsync(token); }, CancellationToken.None);
            Task next = vm.QueueMediaOperationAsync("画像の予約 · 確認用", _ => { started.Add("second"); return Task.CompletedTask; }, CancellationToken.None);
            using var cancel = new CancellationTokenSource(); bool canceledRan = false;
            Task canceled = vm.QueueMediaOperationAsync("取り消す予約", _ => { canceledRan = true; return Task.CompletedTask; }, cancel.Token); cancel.Cancel();
            try { await canceled; } catch (OperationCanceledException) { }
            Check(!canceledRan, "Canceled media work was executed.");
            vm.Downloads.Items.Add(new() { Name = "表示確認用モデル.gguf", ImportPath = "fixture", Status = "active", Stage = "ダウンロード中", Received = 40L << 20, Total = 100L << 20, Speed = 4L << 20 });
            await Until(() => started.Count == 1);
            var processSettings = Offscreen(new SettingsWindow(vm, 9) { Owner = window });
            await PrototypePreview.RenderAsync(processSettings, Path.Combine(output, "process-settings.png"));
            ProcessListPanel panel = (ProcessListPanel)((ContentControl)processSettings.FindName("ProcessContent")).Content; panel.Refresh();
            Check(Find<TextBlock>(panel).Any(t => t.Text.Contains("本文を生成", StringComparison.Ordinal)) && Find<ProgressBar>(panel).Any(p => !p.IsIndeterminate && Math.Abs(p.Value - 40) < 0.01), "Process popup omitted live inference or download progress.");
            Check(Find<TextBlock>(panel).Any(t => t.Text.Contains("画像の予約", StringComparison.Ordinal)), "Process popup omitted queued work.");
            processSettings.Close(); hold.TrySetResult(); await running; await next; await Until(() => !vm.IsBusy);
            Check(started.SequenceEqual(new[] { "first", "second" }), "Media jobs did not run in order."); vm.Downloads.Items.Clear();
            report["processes"] = new { progress = 40, mediaOrder = started, canceledRan };

            var media1 = Offscreen(new MediaWindow(store, vm) { Owner = window });
            var media2 = Offscreen(new MediaWindow(store, vm) { Owner = window });
            TextBox doc1 = Find<TextBox>(media1).Single(t => AutomationProperties.GetName(t).StartsWith("文書本文", StringComparison.Ordinal));
            TextBox doc2 = Find<TextBox>(media2).Single(t => AutomationProperties.GetName(t).StartsWith("文書本文", StringComparison.Ordinal));
            doc1.Text = "別々の下書き A"; doc2.Text = "別々の下書き B"; await media1.SaveDraftAsync(); await media2.SaveDraftAsync();
            Check(media1.DraftPathForVerification != media2.DraftPathForVerification && await File.ReadAllTextAsync(media1.DraftPathForVerification) == doc1.Text && await File.ReadAllTextAsync(media2.DraftPathForVerification) == doc2.Text, "Two document windows overwrote the same draft.");
            await PrototypePreview.RenderAsync(media1, Path.Combine(output, "document-draft.png"));
            media1.Close(); media2.Close(); await Until(() => !media1.IsVisible && !media2.IsVisible); report["independentDrafts"] = true;

            ArtifactContext captured = vm.CaptureArtifactContext("確認用の文書"); vm.SelectedConversation = fixtures[0];
            Check(captured.Conversation.Id == conversation.Id && captured.Model.Id == model.Id, "Artifact context followed later navigation."); vm.SelectedConversation = conversation;
            report["artifactContextFrozen"] = true;
            string markdown = SkillFormat.WithLicenseAndCompatibility(SkillFormat.Create("audit-skill", "確認用の手順", "# 手順\n日本語で説明する。"), "MIT", "Windows / Eluna");
            SkillPackage package = SkillFormat.Parse(new() { ["SKILL.md"] = Encoding.UTF8.GetBytes(markdown) }); Check(package.License == "MIT" && package.Compatibility == "Windows / Eluna", "Skill metadata editing lost optional fields.");
            var skills = Offscreen(new SkillsWindow(vm) { Owner = window }); skills.Width = skills.MinWidth; skills.Height = skills.MinHeight;
            await PrototypePreview.RenderAsync(skills, Path.Combine(output, "skills-minimum.png"));
            Check(((TextBox)skills.FindName("InstructionBox")).ActualHeight >= 80, "Skill instructions were hidden below the metadata.");
            ((Expander)skills.FindName("MetadataExpander")).IsExpanded = true;
            await PrototypePreview.RenderAsync(skills, Path.Combine(output, "skills-expanded-minimum.png"));
            TextBox instructions = (TextBox)skills.FindName("InstructionBox"); Grid instructionLayout = (Grid)skills.FindName("InstructionLayout");
            Check(instructions.TranslatePoint(new(0, instructions.ActualHeight), instructionLayout).Y <= instructionLayout.ActualHeight + 1, "Expanded skill metadata clipped the instructions.");
            skills.Close(); await Until(() => !skills.IsVisible);
            report["skillEditor"] = true;

            using var http = new HttpClient(new ModelHandler()); using var catalog = new ModelCatalog(store, http);
            var browser = Offscreen(new ModelWindow(store, vm.Downloads, vm.ComputeChoices, vm.Settings.Compute, vm.EffectiveContext, vm, catalog) { Owner = window });
            await Until(() => ((ListBox)browser.FindName("FileList")).Items.Count == 2);
            ((ComboBox)browser.FindName("CapacityFilter")).SelectedIndex = 1;
            Check(((ListBox)browser.FindName("FileList")).Items.Count == 0, "The model size filter ignored its limit.");
            ((ComboBox)browser.FindName("CapacityFilter")).SelectedIndex = 0; ((ComboBox)browser.FindName("QuantizationFilter")).SelectedIndex = 1;
            Check(((ListBox)browser.FindName("FileList")).Items.Count == 1, "The quantization filter did not narrow the files.");
            Check(!((TextBlock)browser.FindName("StatusText")).Text.Contains("一致するファイルはありません", StringComparison.Ordinal), "The model filter kept an outdated empty-state message.");
            browser.Width = browser.MinWidth; browser.Height = browser.MinHeight;
            await PrototypePreview.RenderAsync(browser, Path.Combine(output, "models-filtered-minimum.png"));
            browser.Close(); report["modelFilters"] = true;

            AppSettings original = vm.Settings; await vm.ApplySettingsAsync(original with { UiScale = 1.3, Theme = "light" });
            var largeSettings = Offscreen(new SettingsWindow(vm, 4) { Owner = window }); largeSettings.Width = largeSettings.MinWidth; largeSettings.Height = largeSettings.MinHeight;
            await PrototypePreview.RenderAsync(largeSettings, Path.Combine(output, "settings-large-text.png")); largeSettings.Close();
            Appearance.Apply("dark", 1, true); await PrototypePreview.RenderAsync(window, Path.Combine(output, "high-contrast-simulation.png")); await vm.ApplySettingsAsync(original);
            report["themesAndTextScale"] = new { scale = 1.3, highContrast = "resource simulation; physical OS setting unchanged" };

            var longChat = new Conversation { Title = "読書位置の保持" };
            for (int i = 0; i < 30; i++) { longChat.Messages.Add(new() { Role = "user", Content = $"段落 {i}: " + new string('文', 120) }); }
            vm.Conversations.Add(longChat); vm.SelectedConversation = longChat;
            await PrototypePreview.RenderAsync(window, Path.Combine(output, "reading-position-before.png"));
            ScrollViewer reading = Find<ScrollViewer>((ListBox)window.FindName("MessagesList")).First(); reading.ScrollToVerticalOffset(150); await Task.Delay(100); double savedOffset = reading.VerticalOffset;
            vm.SelectedConversation = fixtures[0]; await Task.Delay(150); vm.SelectedConversation = longChat; await Until(() => !window.ReadingPositionPending);
            await File.WriteAllTextAsync(Path.Combine(output, "reading-position.json"), JsonSerializer.Serialize(new { savedOffset, restored = reading.VerticalOffset }));
            Check(Math.Abs(reading.VerticalOffset - savedOffset) < 2, "Conversation navigation lost its reading position.");
            for (int repeat = 0; repeat < 3; repeat++)
            {
                vm.SelectedConversation = fixtures[0]; vm.SelectedConversation = longChat; await Until(() => !window.ReadingPositionPending);
                await File.WriteAllTextAsync(Path.Combine(output, "rapid-reading-position.json"), JsonSerializer.Serialize(new { repeat, savedOffset, restored = reading.VerticalOffset, reading.ScrollableHeight, reading.ViewportHeight, count = ((ListBox)window.FindName("MessagesList")).Items.Count }));
                Check(Math.Abs(reading.VerticalOffset - savedOffset) < 2, $"Rapid channel navigation applied a stale scroll operation: repeat={repeat}, offset={reading.VerticalOffset}, expected={savedOffset}, extent={reading.ScrollableHeight}.");
            }
            report["readingPosition"] = new { savedOffset, restoredOffset = reading.VerticalOffset }; vm.SelectedConversation = conversation; vm.Conversations.Remove(longChat);

            var loads = new List<object>();
            foreach (int count in new[] { 1000, 10000 })
            {
                var added = new List<Conversation>(); string paragraph = new('あ', 1000);
                {
                    for (int i = 0; i < count; i++) { var c = new Conversation { Title = "検索負荷 " + i }; for (int j = 0; j < 10; j++) { c.Messages.Add(new() { Role = "assistant", Content = i == count - 1 && j == 9 ? paragraph + "UXBENCH987654" : paragraph }); } added.Add(c); fixtures.Add(c); }
                }
                vm.Conversations.AddRange(added);
                var clock = Stopwatch.StartNew(); vm.HistorySearch = "UXBENCH987654"; double setterMs = clock.Elapsed.TotalMilliseconds;
                int dispatcherProbe = 0; await Dispatcher.CurrentDispatcher.InvokeAsync(() => dispatcherProbe++, DispatcherPriority.Input);
                await vm.HistorySearchFinished; double totalMs = clock.Elapsed.TotalMilliseconds;
                Check(vm.HistoryView.Cast<Conversation>().Count() == 1 && dispatcherProbe == 1, "Large history search returned wrong results or blocked input dispatch.");
                loads.Add(new { conversations = count, messages = count * 10, setterMs, totalMs, dispatcherProbe });
                vm.HistorySearch = ""; vm.Conversations.RemoveRange(added);
            }
            report["historyLoad"] = loads;
            await File.WriteAllTextAsync(Path.Combine(output, "verification.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
            await File.WriteAllTextAsync(Path.Combine(output, "result.txt"), "UI polish: management routes, density, process progress/order/cancel, unsaved edits, retry, independent drafts, artifact scope, skill metadata, model filters, themes and 1k/10k history: OK.");
        }
        catch (Exception ex) { Environment.ExitCode = 1; await File.WriteAllTextAsync(Path.Combine(output, "result.txt"), ex.ToString()); }
        finally
        {
            UnsavedChanges.Review = _ => false;
            vm.Conversations.RemoveRange(fixtures);
            foreach (MediaJob job in vm.MediaJobs.Where(j => j.IsPending)) { job.Cancel(); }
            foreach (Window child in window.OwnedWindows.Cast<Window>().ToArray()) { child.Close(); }
            await Task.Delay(100); window.Close();
        }
    }
    private static async Task CheckComputerAwakeAsync(ChatViewModel vm, AppStore store, string output)
    {
        AppSettings original = vm.Settings;
        Check(original.ComputerUseKeepAwake, "The default-on setting was not loaded.");
        var settings = Offscreen(new SettingsWindow(vm, 6));
        var box = (CheckBox)settings.FindName("ComputerAwakeBox");
        Check(box.IsChecked == true, "The settings checkbox did not show the enabled value.");
        box.IsChecked = false; box.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        Check(((TextBlock)settings.FindName("SaveStateText")).Text.Contains("未保存", StringComparison.Ordinal), "The power checkbox did not mark settings dirty.");
        int reviews = 0; UnsavedChanges.Review = _ => { reviews++; return null; }; settings.Close();
        Check(settings.IsVisible && reviews == 1 && vm.Settings.ComputerUseKeepAwake, "An unsaved power change escaped the close protection or changed live settings.");
        Click((Button)settings.FindName("SaveButton")); await Until(() => !vm.Settings.ComputerUseKeepAwake && ((Button)settings.FindName("SaveButton")).IsEnabled);
        Check(!(await store.LoadAsync()).Settings.ComputerUseKeepAwake, "Saving the power opt-out did not persist.");
        settings.Close();
        settings = Offscreen(new SettingsWindow(vm, 6)); box = (CheckBox)settings.FindName("ComputerAwakeBox");
        Check(box.IsChecked == false, "Reopening settings lost the power opt-out.");
        box.IsChecked = true; box.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        Click((Button)settings.FindName("SaveButton")); await Until(() => vm.Settings.ComputerUseKeepAwake && ((Button)settings.FindName("SaveButton")).IsEnabled);
        Check((await store.LoadAsync()).Settings.ComputerUseKeepAwake, "Re-enabling the power setting did not persist.");
        foreach (string theme in new[] { "dark", "light" })
        {
            Appearance.Apply(theme); settings.Width = 860; settings.Height = 740;
            box.BringIntoView(); await PrototypePreview.RenderAsync(settings, Path.Combine(output, "computer-awake-" + theme + ".png"));
            settings.Width = settings.MinWidth; settings.Height = settings.MinHeight; Appearance.Apply(theme, 1.3);
            box.BringIntoView(); await PrototypePreview.RenderAsync(settings, Path.Combine(output, "computer-awake-" + theme + "-small.png"));
            var label = (TextBlock)box.Content;
            Check(box.IsVisible && box.ActualWidth > 150 && label.ActualHeight >= 10 && label.ActualWidth <= box.ActualWidth && ((Button)settings.FindName("SaveButton")).ActualWidth > 0, "The power option or Save button was clipped at the minimum settings size.");
        }
        settings.Close(); Appearance.Apply(original.Theme, original.UiScale); UnsavedChanges.Review = _ => false;
    }

    private sealed class ModelHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string data = JsonSerializer.Serialize(new { id = "audit/model", sha = new string('a', 40), cardData = new { license = "apache-2.0", language = new[] { "ja" } }, siblings = new[] { new { rfilename = "model-Q4_K_M.gguf", lfs = new { sha256 = new string('a', 64), size = 3L << 30 } }, new { rfilename = "model-Q8_0.gguf", lfs = new { sha256 = new string('b', 64), size = 6L << 30 } } } });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(data, Encoding.UTF8, "application/json") });
        }
    }
}
