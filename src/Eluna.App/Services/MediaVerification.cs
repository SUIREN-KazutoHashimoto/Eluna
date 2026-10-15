using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Eluna.App.Models;
using Eluna.App.ViewModels;

namespace Eluna.App.Services;

internal static class MediaVerification
{
    internal static async Task RunAsync(AppStore store, string modelMetadata, string projectorPath, string imageModel)
    {
        var report = new Dictionary<string, object>();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(8)); CancellationToken token = timeout.Token;
        void Check(bool value, string message) { if (!value) { throw new InvalidOperationException(message); } }
        try
        {
            LocalModel original = JsonSerializer.Deserialize<LocalModel>(await File.ReadAllTextAsync(modelMetadata), AppStore.JsonOptions)!;
            string directory = Path.Combine(store.ModelsDirectory, original.Id); Directory.CreateDirectory(directory);
            await File.WriteAllTextAsync(Path.Combine(directory, "model.json"), JsonSerializer.Serialize(original, AppStore.JsonOptions));
            await new ModelsService(store).ImportProjectorAsync(original, projectorPath, token);
            LocalModel model = (await store.ListModelsAsync()).Single();
            Check(model.Projector is not null && File.Exists(model.Projector.FilePath), "Projector import failed.");
            string picture = Path.Combine(store.Root, "fixture.png");
            var visual = new DrawingVisual(); using (DrawingContext drawing = visual.RenderOpen()) { drawing.DrawRectangle(Brushes.White, null, new Rect(0, 0, 384, 384)); drawing.DrawRectangle(Brushes.Red, null, new Rect(50, 50, 284, 284)); }
            var bitmap = new RenderTargetBitmap(384, 384, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); await using (var stream = File.Create(picture)) { encoder.Save(stream); }
            List<ChatAttachment> images = await AttachmentService.ReadAsync(picture, token);
            using (var llama = new LlamaService(store))
            {
                await llama.InitializeAsync(); await llama.EnsureModelAsync(model, new(model.Id, ContextSize: 8192), _ => { }, token);
                async Task<string> Ask(string question, List<ChatAttachment> attachments)
                {
                    var text = new StringBuilder();
                    await foreach (GenerationUpdate piece in llama.GenerateAsync([new("user", question, Attachments: attachments)], InferenceModes.Find("fast"), token, "Answer the question about the attached image accurately. Output only the requested answer.", 128)) { text.Append(piece.Content); }
                    return text.ToString();
                }
                string answer = await Ask("What color is the large square? Answer with one color word.", images);
                report["imageRecognition"] = new { compute = llama.ComputeLabel, answer };
                Check(answer.Contains("red", StringComparison.OrdinalIgnoreCase) || answer.Contains("赤", StringComparison.Ordinal), "Vision model did not identify the image: " + answer);
                string pdf = Path.Combine(store.Root, "fixture.pdf");
                await DocumentService.SavePdfAsync(pdf, "# PROJECT 4826\n\nThis document is a test. The project number is 4826.\n\n# TOTAL 27\n\nTotal items: 27.", token);
                List<ChatAttachment> pages = await AttachmentService.ReadAsync(pdf, token);
                Check(pages.Count == 1 && pages[0].ImageBase64 is not null, "Windows PDF rendering failed.");
                string read = await Ask("Read the four digit project number in this document. Output only the number.", pages);
                report["pdfRecognition"] = new { pages = pages.Count, answer = read };
                Check(read.Contains("4826", StringComparison.Ordinal), "PDF text was not recognized: " + read);
            }
            using (var chat = new ChatViewModel(store))
            {
                await chat.InitializeAsync(); await chat.ApplySettingsAsync(chat.Settings with { ContextSize = 8192 }); chat.SelectedMode = InferenceModes.Find("fast");
                string docx = Path.Combine(store.Root, "input.docx");
                await DocumentService.SaveTextAsync(docx, "# Project\n\nThe project code is ORBIT-4826. The owner is Sora.", token);
                await chat.AttachFilesAsync([docx, picture]);
                Check(chat.PendingAttachments.Count == 2 && chat.NetworkNotice.Contains("ローカル", StringComparison.Ordinal), "Attachments were not staged locally.");
                chat.Input = "What is the project code in the attached document? Output only the code."; chat.SendCommand.Execute(null);
                while (chat.IsBusy) { await Task.Delay(30, token); }
                ChatMessage response = chat.SelectedConversation!.Messages.Last();
                Check(!chat.HasError && response.Work is null && response.Content.Contains("4826", StringComparison.Ordinal), "Local document recognition failed: " + chat.Error + response.Content);
                report["documentRecognition"] = response.Content;
                await chat.ApplySettingsAsync(chat.Settings with { WorkOptions = new(SearchLimit: 1, PageLimit: 1, SecondsLimit: 90, TokenLimit: 3000) });
                chat.SelectedWorkMode = WorkModes.Find("search");
                chat.Input = "llama.cppのWindowsでのVulkan対応を検索してください。添付は参考資料です。"; chat.SendCommand.Execute(null);
                while (chat.IsBusy)
                {
                    if (chat.SelectedConversation.Messages.Last().Work?.SearchedQueries.Count > 0) { chat.StopCommand.Execute(null); }
                    await Task.Delay(30, token);
                }
                WorkSession combined = chat.SelectedConversation.Messages.Last().Work ?? throw new InvalidOperationException("Attachments blocked explicit search.");
                Check(combined.SearchesUsed == 1 && combined.SearchedQueries.Count == 1 && combined.Question.Contains("添付の読み取り結果", StringComparison.Ordinal), "Combined attachment reading and explicit web search failed: " + chat.Error);
                report["attachmentsAndWebSearch"] = true;
                chat.SelectedWorkMode = WorkModes.Find("chat");
                string document = "";
                await chat.RunMediaOperationAsync(t => chat.GenerateArtifactTextAsync("Create a short project brief for a local AI chat app. Include a title and three points.", "Write the document in Markdown. Output only the document.", text => document = text, t));
                Check(document.Length > 50, "Document generation returned no content.");
                await DocumentService.SaveTextAsync(Path.Combine(store.Root, "generated.docx"), document, token);
                await DocumentService.SavePdfAsync(Path.Combine(store.Root, "generated.pdf"), document, token);
                Check((await AttachmentService.ReadAsync(Path.Combine(store.Root, "generated.docx"), token))[0].Text.Length > 30, "Generated DOCX could not be read.");
                report["documentGeneration"] = new { characters = document.Length, docx = true, pdf = true };
                await chat.PersistSafelyAsync(); AppState saved = await store.LoadAsync();
                Check(saved.Conversations.SelectMany(c => c.Messages).Any(m => m.Attachments?.Count == 2 && m.Attachments.Any(a => a.ImageBase64 is not null)), "Document/image attachment history was not saved.");
                report["imageAndDocumentHistory"] = true;
                List<ChatAttachment> rendered = await AttachmentService.ReadAsync(Path.Combine(store.Root, "generated.pdf"), token);
                await File.WriteAllBytesAsync(Path.Combine(store.Root, "document-preview.jpg"), Convert.FromBase64String(rendered[0].ImageBase64!), token);
            }
            foreach (string compute in new[] { "Vulkan0", "cpu" })
            {
                string output = Path.Combine(store.Root, "generated-" + compute + ".png");
                await DiffusionService.GenerateAsync(store, new(imageModel, "a red apple on a white background", Width: 256, Height: 256, Steps: 1, Seed: 42, Compute: compute), output, _ => { }, token);
                Check(new FileInfo(output).Length > 1000 && MediaWindow.LoadImage(output).PixelWidth == 256, "Image generation returned no valid PNG.");
                report["imageGeneration-" + compute] = new FileInfo(output).Length;
            }
            using (var cancel = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                bool canceled = false; string output = Path.Combine(store.Root, "canceled.png");
                try { await DiffusionService.GenerateAsync(store, new(imageModel, "a small house", Steps: 40), output, _ => cancel.Cancel(), cancel.Token); }
                catch (OperationCanceledException) { canceled = true; }
                Check(canceled && !File.Exists(output), "Image generation cancellation retained a partial output."); report["imageCancellation"] = true;
            }
            report["success"] = true;
        }
        catch (Exception ex) { report["success"] = false; report["error"] = ex.ToString(); Environment.ExitCode = 1; }
        finally { await File.WriteAllTextAsync(Path.Combine(store.Root, "media-verification.json"), JsonSerializer.Serialize(report, AppStore.JsonOptions)); Application.Current.Shutdown(Environment.ExitCode); }
    }
}
