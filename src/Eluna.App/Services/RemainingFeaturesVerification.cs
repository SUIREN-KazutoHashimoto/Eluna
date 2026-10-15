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
using Eluna.App.Models;
using Eluna.App.ViewModels;

namespace Eluna.App.Services;

internal static class RemainingFeaturesVerification
{
    private static void Check(bool condition, string message) { if (!condition) { throw new InvalidOperationException(message); } }
    private static IEnumerable<T> Find<T>(DependencyObject root) where T : DependencyObject
    { for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) { DependencyObject child = VisualTreeHelper.GetChild(root, i); if (child is T t) { yield return t; } foreach (T nested in Find<T>(child)) { yield return nested; } } }
    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
    internal static async Task RunAsync(MainWindow window, AppStore store, string output, string metadata, string imageModel)
    {
        Application.Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(8)); CancellationToken token = timeout.Token;
        var report = new Dictionary<string, object>(); var current = window;
        async Task Until(Func<bool> condition) { while (!condition()) { await Task.Delay(20, token); } }
        T Offscreen<T>(T child) where T : Window { child.Owner = current; child.WindowStartupLocation = WindowStartupLocation.Manual; child.Left = -20000; child.Top = -20000; child.Show(); return child; }
        try
        {
            Directory.CreateDirectory(output); ChatViewModel vm = current.ViewModel; await Until(() => vm.IsReady || vm.HasError); Check(vm.IsReady, vm.Error);
            LocalModel model = JsonSerializer.Deserialize<LocalModel>(await File.ReadAllTextAsync(metadata, token), AppStore.JsonOptions)! with { Preferences = new(ContextSize: 4096, Temperature: 0.1, MaxOutputTokens: 2048, DefaultMode: "fast") };
            string directory = Path.Combine(store.ModelsDirectory, model.Id); Directory.CreateDirectory(directory); await File.WriteAllTextAsync(Path.Combine(directory, "model.json"), JsonSerializer.Serialize(model, AppStore.JsonOptions), token);
            await vm.RefreshModelsAsync(model); await vm.ApplySettingsAsync(vm.Settings with { WorkMode = "offline", SkillMode = "off", MaxConcurrentMedia = 2, MaxConcurrentDownloads = 2 });
            var gpu = await GpuMemory.RefreshAsync(); report["gpuMemory"] = gpu;
            Check(gpu.Any(g => g.Total > 0 && g.Available is >= 0 && g.Available <= g.Total), "No adapter-wide free VRAM measurement was obtained.");
            var settings = Offscreen(new SettingsWindow(vm)); await PrototypePreview.RenderAsync(settings, Path.Combine(output, "concurrency-settings.png")); Check((int)((ComboBox)settings.FindName("MediaConcurrencyBox")).SelectedItem == 2 && (int)((ComboBox)settings.FindName("DownloadConcurrencyBox")).SelectedItem == 2, "Parallel limits did not reach settings UI."); settings.Close();
            var diff = Offscreen(new SkillDiffWindow(new Dictionary<string, byte[]> { ["SKILL.md"] = Encoding.UTF8.GetBytes("# 手順\n変更前"), ["old.txt"] = [1, 2, 3] }, new Dictionary<string, byte[]> { ["SKILL.md"] = Encoding.UTF8.GetBytes("# 手順\n変更後"), ["assets/new.txt"] = Encoding.UTF8.GetBytes("追加資料") }));
            await PrototypePreview.RenderAsync(diff, Path.Combine(output, "skill-diff.png")); Check(Find<TextBox>(diff).Any(t => t.Text.Contains("変更後", StringComparison.Ordinal)), "Skill diff did not show the changed content."); diff.Close();
            using var galleryHttp = new HttpClient(new GalleryFixture()); using var galleryCatalog = new ModelCatalog(store, galleryHttp);
            var gallery = Offscreen(new ImageModelGalleryWindow(store, vm.Downloads, _ => { }, galleryCatalog));
            foreach (Button inspect in Find<Button>(gallery).Where(b => b.Content is "容量とファイルを確認").ToArray()) { Click(inspect); }
            await Until(() => Find<ComboBox>(gallery).All(b => b.Items.Count == 1));
            await PrototypePreview.RenderAsync(gallery, Path.Combine(output, "image-model-gallery.png")); gallery.Width = gallery.MinWidth; gallery.Height = gallery.MinHeight;
            await PrototypePreview.RenderAsync(gallery, Path.Combine(output, "image-model-gallery-minimum.png")); gallery.Close();

            var media = Offscreen(new MediaWindow(store, vm)); TextBox prompt = Find<TextBox>(media).Single(t => AutomationProperties.GetName(t) == "作成する内容・目的・読み手");
            Button generate = Find<Button>(media).Single(b => b.Content is "AIで作成");
            prompt.Text = "先頭にALPHA-4826と書き、その後1から1000までの整数を改行区切りで、途中を省略せずに書いてください。説明は不要です。"; Click(generate);
            await Until(() => vm.MediaJobs.Count == 1); MediaJob first = vm.MediaJobs[0];
            prompt.Text = "先頭にBETA-7391と書き、その後1から80までの整数を改行区切りで書いてください。説明は不要です。"; Click(generate);
            await Until(() => vm.MediaJobs.Count == 2); MediaJob second = vm.MediaJobs[1];
            await Until(() => first.Text.Length > 20 && second.Text.Length > 20 || first.State == "failed" || second.State == "failed");
            Check(first.State == "active" && second.State == "active", "Two document engines did not stream concurrently: " + first.Detail + " / " + second.Detail);
            Check(first.Request!.OutputPath != second.Request!.OutputPath && first.Request.Prompt.Contains("ALPHA", StringComparison.Ordinal) && second.Request.Prompt.Contains("BETA", StringComparison.Ordinal), "Parallel jobs shared their captured inputs or output paths.");
            var popup = (Popup)current.FindName("ProcessPopup"); popup.IsOpen = true; await PrototypePreview.RenderAsync(current, Path.Combine(output, "parallel-processes.png"), popup); popup.IsOpen = false;
            var result = Offscreen(new MediaResultWindow(second, store.Root)); await PrototypePreview.RenderAsync(result, Path.Combine(output, "streaming-result.png")); result.Close();
            report["parallelDocuments"] = new { firstCharacters = first.Text.Length, secondCharacters = second.Text.Length, differentOutputs = true };
            report["mediaCompute"] = vm.ActiveMediaComputeLabels;
            MediaJob image = await vm.EnqueueMediaAsync("画像の再開確認", new("image", "a red circle on a white background", "", null, vm.NewMediaOutput(".png"), Image: new(imageModel, "a red circle on a white background", Width: 512, Height: 512, Steps: 20, Seed: 42)));
            Check(image.State == "queued", "Media concurrency exceeded its limit."); first.Cancel(); await Until(() => first.State == "canceled"); Check(second.State == "active", "Stopping one document stopped another document.");
            var longChat = new Conversation { Title = "再起動後の読書位置" }; for (int i = 0; i < 30; i++) { longChat.Messages.Add(new() { Role = "user", Content = "段落 " + i + " " + new string('文', 120) }); }
            vm.Conversations.Insert(0, longChat); vm.SelectedConversation = longChat; await vm.PersistSafelyAsync();
            await PrototypePreview.RenderAsync(current, Path.Combine(output, "reading-before-restart.png")); ScrollViewer reading = Find<ScrollViewer>((ListBox)current.FindName("MessagesList")).First(); reading.ScrollToVerticalOffset(150); await Task.Delay(100, token); double offset = reading.VerticalOffset;
            current.Close(); await Until(() => !current.IsVisible);
            current = new MainWindow(store) { WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = -20000 }; Application.Current.MainWindow = current; current.Show(); vm = current.ViewModel; await Until(() => vm.IsReady || vm.HasError); Check(vm.IsReady, vm.Error);
            await PrototypePreview.RenderAsync(current, Path.Combine(output, "reading-after-restart.png"));
            var restoredReading = Find<ScrollViewer>((ListBox)current.FindName("MessagesList")).First(); Check(Math.Abs(restoredReading.VerticalOffset - offset) < 2, "Reading position did not survive app restart.");
            Check(vm.MediaJobs.Count(j => j.State == "active") == 0, "Restored media executed without a resume action.");
            MediaJob restoredDoc = vm.MediaJobs.Single(j => j.Id == second.Id), restoredImage = vm.MediaJobs.Single(j => j.Id == image.Id);
            Check(restoredDoc.State == "paused" && restoredImage.State == "paused", "Unfinished document or image was not restored as paused.");
            await vm.ResumeMediaAsync(restoredDoc); await vm.ResumeMediaAsync(restoredImage);
            Check(restoredDoc.State == "active" && restoredImage.State == "active", "Document and image did not resume in independent slots.");
            await vm.ApplySettingsAsync(vm.Settings with { MaxConcurrentMedia = 1 }); Check(restoredDoc.State == "active" && restoredImage.State == "active", "Reducing the limit stopped a running job.");
            await Until(() => !restoredDoc.IsPending && !restoredImage.IsPending);
            Check(restoredDoc.State == "complete" && restoredImage.State == "complete", "Restored generation failed: " + restoredDoc.Detail + " / " + restoredImage.Detail);
            Check(File.Exists(restoredDoc.Request!.OutputPath) && File.ReadAllText(restoredDoc.Request.OutputPath) == restoredDoc.Text && restoredDoc.Text.Contains("BETA-7391", StringComparison.Ordinal) && !restoredDoc.Text.Contains("ALPHA-4826", StringComparison.Ordinal) && new FileInfo(restoredImage.Request!.OutputPath).Length > 100, "Resumed outputs were missing or mixed between jobs.");
            var finalResult = Offscreen(new MediaResultWindow(restoredImage, store.Root)); await PrototypePreview.RenderAsync(finalResult, Path.Combine(output, "resumed-image.png")); finalResult.Close();
            report["restart"] = new { readingBefore = offset, readingAfter = restoredReading.VerticalOffset, automaticallyExecuted = false, documentCharacters = restoredDoc.Text.Length, imageBytes = new FileInfo(restoredImage.Request!.OutputPath).Length, independentStop = true, reducedLimitPreservedActive = true };
            report["success"] = true;
        }
        catch (Exception ex) { report["success"] = false; report["error"] = ex.ToString(); Environment.ExitCode = 1; }
        finally
        {
            foreach (Window child in current.OwnedWindows.Cast<Window>().ToArray()) { child.Close(); }
            await current.ViewModel.ShutdownAsync(); current.Close(); await Task.Delay(100);
            await File.WriteAllTextAsync(Path.Combine(output, "remaining-verification.json"), JsonSerializer.Serialize(report, AppStore.JsonOptions)); Application.Current.Shutdown(Environment.ExitCode);
        }
    }
    private sealed class GalleryFixture : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { id = "gallery/fixture", sha = new string('a', 40), cardData = new { license = "fixture" }, siblings = new[] { new { rfilename = "image-Q4_0.gguf", lfs = new { sha256 = new string('a', 64), size = 3L << 30 } } } }), Encoding.UTF8, "application/json") });
    }
}
