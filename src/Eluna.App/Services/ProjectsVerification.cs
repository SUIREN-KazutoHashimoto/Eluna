using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using Eluna.App.Models;
using Eluna.App.ViewModels;

namespace Eluna.App.Services;

internal static class ProjectsVerification
{
    internal static async Task RunAsync(AppStore store, string metadataPath, string projectorPath)
    {
        var report = new Dictionary<string, object>();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(6)); CancellationToken token = timeout.Token;
        void Check(bool value, string message) { if (!value) { throw new InvalidOperationException(message); } }
        try
        {
            LocalModel model = JsonSerializer.Deserialize<LocalModel>(await File.ReadAllTextAsync(metadataPath, token), AppStore.JsonOptions)!;
            await using (var projector = File.OpenRead(projectorPath))
            { model = model with { Projector = new(Path.GetFullPath(projectorPath), Convert.ToHexString(await SHA256.HashDataAsync(projector, token)).ToLowerInvariant(), projector.Length), Preferences = new(ContextSize: 8192, Temperature: 0.2, MaxOutputTokens: 1024) }; }
            string directory = Path.Combine(store.ModelsDirectory, model.Id); Directory.CreateDirectory(directory);
            await File.WriteAllTextAsync(Path.Combine(directory, "model.json"), JsonSerializer.Serialize(model, AppStore.JsonOptions), token);
            using var chat = new ChatViewModel(store); await chat.InitializeAsync();
            Check(chat.IsReady && chat.SelectedModel?.Id == model.Id, "Project test model did not initialize.");
            await chat.ApplySettingsAsync(chat.Settings with { SystemInstruction = "回答の先頭に必ずGLOBAL-INSTRUCTION-SECRETと書いてください。", WorkMode = "offline" },
                [new(Guid.NewGuid(), "共通メモリのアクセスコードはCOMMON-7139です。", DateTimeOffset.UtcNow)]);
            chat.SelectedMode = InferenceModes.Find("fast");
            DateTimeOffset now = DateTimeOffset.UtcNow;
            var a = new KnowledgeProject(Guid.NewGuid(), "港町の企画", "質問された値だけを簡潔に答える。ローカル資料を使うときは[K番号]を付ける。", "project-only", true,
                [new(Guid.NewGuid(), "認証コード.md", "このプロジェクトの認証コードは HARBOR-4826 です。", now)], [new(Guid.NewGuid(), "問い合わせ担当者名はサクラです。", now)]);
            var b = new KnowledgeProject(Guid.NewGuid(), "森の企画", "質問された値だけを簡潔に答える。", "project-only", true,
                [new(Guid.NewGuid(), "認証コード.md", "このプロジェクトの認証コードは FOREST-9913 です。", now)], []);
            await chat.SaveProjectAsync(a); await chat.SaveProjectAsync(b); chat.SelectProject(a.Id);
            async Task<ChatMessage> Ask(string question)
            {
                chat.Input = question; Check(chat.SendCommand.CanExecute(null), "A project question could not be submitted."); chat.SendCommand.Execute(null);
                while (chat.IsBusy) { await Task.Delay(30, token); }
                ChatMessage answer = chat.SelectedConversation!.Messages.Last();
                Check(!chat.HasError && answer.Content.Length > 0, "Project answer failed: " + chat.Error);
                return answer;
            }
            ChatMessage first = await Ask("このプロジェクトの認証コードを教えてください。");
            Check(first.Content.Contains("HARBOR-4826", StringComparison.OrdinalIgnoreCase) && !first.Content.Contains("GLOBAL-INSTRUCTION-SECRET", StringComparison.Ordinal) &&
                first.Knowledge.All(k => !k.Text.Contains("FOREST", StringComparison.Ordinal) && !k.Text.Contains("COMMON", StringComparison.Ordinal)), "Project-only document retrieval or global-instruction isolation failed: " + first.Content);
            report["projectA"] = first.Content;
            chat.NewChat(); ChatMessage memory = await Ask("このプロジェクトの問い合わせ担当者名を教えてください。");
            Check(memory.Content.Contains("サクラ", StringComparison.Ordinal) && memory.Knowledge.Any(k => k.Kind == "プロジェクトの保存メモリ"), "Project saved memory was not used: " + memory.Content);
            report["projectMemory"] = memory.Content;
            var previous = new Conversation { ProjectId = a.Id, Title = "配送拠点の決定" };
            previous.Messages.Add(new() { Role = "user", Content = "配送拠点の所在地は新潟です。この所在地を企画の前提にします。" }); chat.Conversations.Add(previous);
            chat.NewChat(); ChatMessage sibling = await Ask("この企画の配送拠点の所在地はどこですか？");
            Check(sibling.Content.Contains("新潟", StringComparison.Ordinal) && sibling.Knowledge.Any(k => k.Kind.Contains("会話", StringComparison.Ordinal)), "A sibling project conversation was not retrieved: " + sibling.Content);
            report["siblingChat"] = sibling.Content;
            chat.SelectProject(b.Id); ChatMessage second = await Ask("このプロジェクトの認証コードを教えてください。");
            Check(second.Content.Contains("FOREST-9913", StringComparison.OrdinalIgnoreCase) && !second.Content.Contains("HARBOR", StringComparison.OrdinalIgnoreCase) && !second.Content.Contains("GLOBAL-INSTRUCTION-SECRET", StringComparison.Ordinal) &&
                second.Knowledge.All(k => !k.Text.Contains("HARBOR", StringComparison.Ordinal)), "Changing projects leaked the previous project's knowledge: " + second.Content);
            report["projectB"] = second.Content;
            chat.SelectProject(null); ChatMessage general = await Ask("共通メモリのアクセスコードを教えてください。");
            Check(general.Content.Contains("COMMON-7139", StringComparison.OrdinalIgnoreCase) && general.Knowledge.All(k => k.Kind == "共通の保存メモリ"), "A normal chat did not use only common saved memory: " + general.Content);
            report["commonMemory"] = general.Content;
            chat.SelectProject(a.Id);
            string document = "";
            await chat.RunMediaOperationAsync(async shared => await chat.GenerateArtifactTextAsync("このプロジェクトの認証コードと問い合わせ担当者名を2行にまとめた文書を作成してください。", "資料の値を忠実に使い、本文だけ出力する。", text => document = text, shared));
            Check(document.Contains("HARBOR-4826", StringComparison.OrdinalIgnoreCase) && document.Contains("サクラ", StringComparison.Ordinal) && !document.Contains("GLOBAL-INSTRUCTION-SECRET", StringComparison.Ordinal), "Artifact generation ignored project context: " + document);
            report["documentGeneration"] = document;
            string docx = Path.Combine(store.Root, "knowledge-input.docx");
            await DocumentService.SaveTextAsync(docx, "# 手順書\n\n資料管理番号はELUNA-KB-6601です。", token);
            List<KnowledgeDocument>? imported = null;
            await chat.RunMediaOperationAsync(async shared => imported = await chat.ImportKnowledgeDocumentsAsync([docx], shared));
            Check(imported is { Count: 1 } && imported[0].Enabled && imported[0].Text.Contains("ELUNA-KB-6601", StringComparison.Ordinal), "DOCX knowledge import lost its text.");
            a = chat.Projects.Single(p => p.Id == a.Id) with { Documents = [.. a.Documents, imported![0]] }; await chat.SaveProjectAsync(a);
            string pdf = Path.Combine(store.Root, "vision-note.pdf");
            await DocumentService.SavePdfAsync(pdf, "# PROJECT 4826\n\nThe document project number is 4826.\n\nTotal items: 27.", token);
            await chat.RunMediaOperationAsync(async shared => imported = await chat.ImportKnowledgeDocumentsAsync([pdf], shared));
            KnowledgeDocument scan = imported!.Single();
            Check(scan.VisionExtracted && !scan.Enabled && scan.Text.Contains("4826", StringComparison.Ordinal), "PDF knowledge import was incorrect or enabled unreviewed OCR text: " + scan.Text);
            a = a with { Documents = [.. a.Documents, scan] }; await chat.SaveProjectAsync(a);
            KnowledgeContext beforeReview = await chat.BuildKnowledgeAsync(chat.SelectedConversation!, "vision-note.pdf project number", 8192, token);
            Check(beforeReview.References.All(k => k.Title != scan.Name), "Unreviewed image/PDF knowledge entered model input.");
            a = a with { Documents = a.Documents.Select(d => d.Id == scan.Id ? d with { Enabled = true } : d).ToList() }; await chat.SaveProjectAsync(a);
            KnowledgeContext afterReview = await chat.BuildKnowledgeAsync(chat.SelectedConversation!, "vision-note.pdf project number", 8192, token);
            Check(afterReview.References.Any(k => k.Title == scan.Name && k.Text.Contains("4826", StringComparison.Ordinal)), "Enabling reviewed PDF knowledge did not update retrieval.");
            report["documentImport"] = new { docx = true, pdf = true, imageRecognitionText = scan.Text, disabledBeforeReview = true };
            using var canceled = new CancellationTokenSource(); canceled.Cancel(); bool stopped = false;
            try { await chat.RunMediaOperationAsync(async _ => await chat.ImportKnowledgeDocumentsAsync([docx], canceled.Token)); } catch (OperationCanceledException) { stopped = true; }
            Check(stopped && !chat.IsBusy, "Canceling knowledge import left the application busy.");
            report["importCancellation"] = true;
            Check(chat.Projects.Single(p => p.Id == a.Id).Memories.Count == 1 && chat.CommonMemories.Count == 1, "The AI silently wrote saved memory.");
            await chat.PersistSafelyAsync(); AppState persisted = await store.LoadAsync();
            Check(persisted.Projects?.Count == 2 && persisted.Conversations.SelectMany(c => c.Messages).Any(m => m.Knowledge is { Count: > 0 }), "Project data or original reference excerpts were lost.");
            report["persistenceAndExplicitMemoryWrites"] = true; report["compute"] = chat.ComputeLabel; report["success"] = true;
            await chat.ShutdownAsync();
        }
        catch (Exception ex) { report["success"] = false; report["error"] = ex.ToString(); Environment.ExitCode = 1; }
        finally
        {
            await File.WriteAllTextAsync(Path.Combine(store.Root, "projects-verification.json"), JsonSerializer.Serialize(report, AppStore.JsonOptions));
            Application.Current.Shutdown(Environment.ExitCode);
        }
    }
}
