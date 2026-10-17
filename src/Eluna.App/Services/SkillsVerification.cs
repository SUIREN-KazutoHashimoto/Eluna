using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Eluna.App.Models;
using Eluna.App.ViewModels;

namespace Eluna.App.Services;

internal static class SkillsVerification
{
    private static void Check(bool value, string message) { if (!value) { throw new InvalidOperationException(message); } }
    internal static async Task RunAsync(AppStore store, string metadata)
    {
        var report = new Dictionary<string, object>(); MainWindow? window = null;
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(8)); CancellationToken token = timeout.Token;
        int reviews = 0;
        async Task Until(Func<bool> condition)
        {
            while (!condition())
            {
                token.ThrowIfCancellationRequested();
                if (window is not null)
                {
                    foreach (SkillExecutionWindow review in window.OwnedWindows.OfType<SkillExecutionWindow>().ToArray())
                    {
                        Check(window.IsEnabled, "実行確認で主画面が操作不能になりました。");
                        if (reviews == 0) { await PrototypePreview.RenderAsync(review, Path.Combine(store.Root, "skill-execution-review.png")); }
                        reviews++;
                        Find<Button>(review).Single(b => b.Content is "許可して実行").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                    }
                }
                await Task.Delay(20, token);
            }
        }
        try
        {
            LocalModel model = JsonSerializer.Deserialize<LocalModel>(await File.ReadAllTextAsync(metadata, token), AppStore.JsonOptions)!;
            model = model with { Preferences = new(ContextSize: 16384, Temperature: 0.1, MaxOutputTokens: 1024) };
            string directory = Path.Combine(store.ModelsDirectory, model.Id); Directory.CreateDirectory(directory);
            await File.WriteAllTextAsync(Path.Combine(directory, "model.json"), JsonSerializer.Serialize(model, AppStore.JsonOptions), token);
            window = new MainWindow(store) { WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = -20000 }; window.Show();
            ChatViewModel chat = window.ViewModel; await Until(() => chat.IsReady || chat.HasError); Check(chat.IsReady, chat.Error);
            var plans = new List<string>(); chat.SkillTrace = plans.Add; report["planning"] = plans;
            await chat.ApplySettingsAsync(chat.Settings with { WorkMode = "offline", MaxConcurrentInferences = 2 }); chat.SelectedMode = InferenceModes.Find("fast");
            var a = new KnowledgeProject(Guid.NewGuid(), "スキル試験 A", "値は資料にある通りに簡潔に答える。", "project-only", false, [], []);
            var b = a with { Id = Guid.NewGuid(), Name = "スキル試験 B" };
            await chat.SaveProjectAsync(a); await chat.SaveProjectAsync(b);
            SkillPackage Brief(string value) => SkillFormat.Parse(new(StringComparer.OrdinalIgnoreCase)
            {
                ["SKILL.md"] = Encoding.UTF8.GetBytes(SkillFormat.Create("project-brief", "企画の識別コードを報告する専用の手順。プロジェクトの識別コードや企画の報告書を作る依頼で使う。",
                    "# 企画の報告\n\n必ずreferences/code.mdをreadで読み、そこにある識別コードだけを回答してください。資料にない値は作らない。")),
                ["references/code.md"] = Encoding.UTF8.GetBytes("企画の識別コードは " + value + " です。")
            });
            SkillRegistration skillA = await chat.SkillFiles.SaveAsync(Brief("HARBOR-4826"), a.Id, null, token); await chat.SaveSkillAsync(skillA);
            SkillRegistration skillB = await chat.SkillFiles.SaveAsync(Brief("FOREST-9913"), b.Id, null, token); await chat.SaveSkillAsync(skillB);
            SkillRegistration common = await chat.SkillFiles.SaveAsync(SkillSamples.All().First(), null, null, token); await chat.SaveSkillAsync(common);
            chat.SelectProject(a.Id); Check(chat.AvailableSkills.Count == 1 && chat.AvailableSkills[0].Id == skillA.Id, "限定プロジェクトの一覧が混ざりました。");
            async Task<InferenceJob> Send(string prompt)
            {
                var before = chat.Inferences.Items.Select(j => j.Request.Id).ToHashSet(); chat.Input = prompt;
                Check(chat.SendCommand.CanExecute(null), "スキルの依頼を送信できません。" + chat.Error); chat.SendCommand.Execute(null);
                await Until(() => chat.Inferences.Items.Any(j => !before.Contains(j.Request.Id)) || chat.HasError && !chat.IsBusy);
                Check(!chat.HasError, chat.Error); await Until(() => chat.CanGenerate);
                return chat.Inferences.Items.Last(j => !before.Contains(j.Request.Id));
            }
            ChatMessage Answer(Conversation conversation, InferenceJob job) => conversation.Branches.SelectMany(branch => branch.Messages).Single(m => m.Id == job.Request.AnswerId);

            chat.SkillMode = "auto"; InferenceJob automatic = await Send("このプロジェクトの識別コードを報告してください。"); Conversation autoChat = chat.SelectedConversation!;
            await Until(() => automatic.State is "complete" or "failed"); ChatMessage autoAnswer = Answer(autoChat, automatic);
            Check(automatic.State == "complete" && autoAnswer.Skills.Count == 1 && autoAnswer.Skills[0].Id == skillA.Id && autoAnswer.Skills[0].Resources.Any(r => r.Text.Contains("HARBOR-4826", StringComparison.Ordinal)) && autoAnswer.Content.Contains("HARBOR-4826", StringComparison.Ordinal),
                "実モデルで自動選択・資料読み込みができませんでした: " + automatic.Detail + autoAnswer.Content);
            report["automatic"] = new { autoAnswer.Content, name = autoAnswer.Skills[0].Name, reason = autoAnswer.Skills[0].Reason };
            await PrototypePreview.RenderAsync(window, Path.Combine(store.Root, "skill-chat.png"));
            var use = new SkillUseWindow(autoAnswer.Skills, store.Root) { WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = -20000 }; use.Show();
            await PrototypePreview.RenderAsync(use, Path.Combine(store.Root, "skill-use.png")); use.Close();

            chat.NewChat(); InferenceJob ordinary = await Send("2+3の答えを数字だけ書いてください。"); Conversation ordinaryChat = chat.SelectedConversation!;
            await Until(() => ordinary.State is "complete" or "failed"); Check(ordinary.State == "complete" && Answer(ordinaryChat, ordinary).Skills.Count == 0 && Answer(ordinaryChat, ordinary).Content.Contains('5'), "無関係な依頼にスキルが適用されました。");
            chat.NewChat(); chat.SkillMode = "manual"; chat.Inferences.Suspended = true;
            InferenceJob queuedA = await Send("/project-brief 識別コードを報告してください。"); Conversation conversationA = chat.SelectedConversation!;
            SkillRegistration updated = await chat.SkillFiles.SaveAsync(Brief("NEW-VERSION-0000"), a.Id, skillA, token); await chat.SaveSkillAsync(updated);
            chat.SelectProject(b.Id); chat.SkillMode = "manual";
            InferenceJob queuedB = await Send("/project-brief 識別コードを報告してください。"); Conversation conversationB = chat.SelectedConversation!;
            chat.SelectProject(null); chat.NewChat(); chat.Input = "スキル実行中の別チャットの下書き";
            chat.Inferences.Suspended = false;
            await Until(() => queuedA.State is "complete" or "failed" && queuedB.State is "complete" or "failed");
            ChatMessage answerA = Answer(conversationA, queuedA), answerB = Answer(conversationB, queuedB);
            Check(queuedA.State == "complete" && queuedB.State == "complete" && answerA.Content.Contains("HARBOR-4826", StringComparison.Ordinal) && answerB.Content.Contains("FOREST-9913", StringComparison.Ordinal) &&
                answerA.Skills.Single().Revision == skillA.Revision && answerB.Skills.Single().Id == skillB.Id && chat.Input.Contains("別チャット", StringComparison.Ordinal), "予約したスキルの版やプロジェクトが混ざりました。");
            report["scopedParallelAndVersions"] = new { answerA = answerA.Content, answerB = answerB.Content, capturedRevision = skillA.Revision };

            chat.SelectProject(a.Id); chat.NewChat(); chat.SkillMode = "manual";
            SkillRegistration numbers = await chat.SkillFiles.SaveAsync(SkillSamples.All().Single(p => p.Name == "number-summary"), a.Id, null, token); await chat.SaveSkillAsync(numbers);
            InferenceJob calculation = await Send("/number-summary 数値4、8、12の件数・合計・平均を計算し、CSVも作成してください。"); Conversation numberChat = chat.SelectedConversation!;
            await Until(() => calculation.State is "complete" or "failed"); ChatMessage numberAnswer = Answer(numberChat, calculation);
            Check(calculation.State == "complete" && reviews > 0 && numberAnswer.Skills.Single().Executions.Any(e => e.Status == "成功" && e.Output.Contains("24", StringComparison.Ordinal) && e.Artifacts.Any(File.Exists)),
                "実モデルによる補助コードの実行が完了しませんでした: " + calculation.Detail + "\n" + SkillRunner.Context(numberAnswer.Skills));
            report["powershell"] = new { reviews, answer = numberAnswer.Content, executions = numberAnswer.Skills.Single().Executions };

            var manager = new SkillsWindow(chat) { WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = -20000 }; manager.Show();
            ((TextBox)manager.FindName("NameBox")).Text = "ui-created"; ((TextBox)manager.FindName("DescriptionBox")).Text = "UIから保存する確認用スキル。";
            ((TextBox)manager.FindName("InstructionBox")).Text = "# UI確認\n\n依頼された文章を3点にまとめる。";
            await manager.SaveDraftAsync(); Check(chat.Skills.Any(s => s.Name == "ui-created" && s.ProjectId == a.Id), "GUIからのスキル作成が保存されませんでした。");
            await PrototypePreview.RenderAsync(manager, Path.Combine(store.Root, "skills-manager.png"));
            manager.Width = manager.MinWidth; manager.Height = manager.MinHeight; await PrototypePreview.RenderAsync(manager, Path.Combine(store.Root, "skills-manager-small.png")); manager.Close();
            var picker = new SkillPickerWindow(chat) { WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = -20000 }; picker.Show(); await PrototypePreview.RenderAsync(picker, Path.Combine(store.Root, "skills-picker.png")); picker.Close();
            await chat.ApplySettingsAsync(chat.Settings with { Theme = "light" });
            var light = new SkillsWindow(chat) { Width = 760, Height = 600, WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = -20000 }; light.Show();
            await PrototypePreview.RenderAsync(light, Path.Combine(store.Root, "skills-manager-light-small.png")); light.Close();
            chat.SelectedConversation = numberChat; await PrototypePreview.RenderAsync(window, Path.Combine(store.Root, "skills-result-light.png"));
            await chat.PersistSafelyAsync(); AppState saved = await store.LoadAsync();
            Check(saved.SchemaVersion == 5 && saved.Skills?.Count == chat.Skills.Count && saved.Conversations.SelectMany(c => c.Messages).Any(m => m.Skills?.Any(s => s.Executions.Count > 0) == true), "スキルと使用記録の保存に失敗しました。");
            string document = ""; await chat.SetConversationSkillsAsync([numbers.Id]);
            Task artifactTask = chat.RunMediaOperationAsync(async cancellation => await chat.GenerateArtifactTextAsync("数値2、3、5を集計した簡潔な報告文書を作ってください。", "見出しと表で書く。", text => document = text, cancellation));
            await Until(() => artifactTask.IsCompleted); await artifactTask;
            Check(chat.LastArtifactSkills.Any(s => s.Id == numbers.Id) && document.Length > 0, "文書生成で指定スキルが使われませんでした。"); report["artifactSkills"] = true;
            await chat.ShutdownAsync(); window.Close(); await Until(() => !window.IsVisible); window = null;
            using var restored = new ChatViewModel(store); await restored.InitializeAsync();
            Check(restored.Skills.Count >= 4 && restored.Conversations.SelectMany(c => c.Messages).Any(m => m.Skills.Any(s => s.Executions.Count > 0)), "再起動後にスキルか使用記録が失われました。");
            await restored.ShutdownAsync(); report["persistenceAndUi"] = true; report["success"] = true;
        }
        catch (Exception ex) { report["success"] = false; report["error"] = ex.ToString(); Environment.ExitCode = 1; }
        finally
        {
            if (window is not null) { await window.ViewModel.ShutdownAsync(); window.Close(); }
            await File.WriteAllTextAsync(Path.Combine(store.Root, "skills-verification.json"), JsonSerializer.Serialize(report, AppStore.JsonOptions));
            Application.Current.Shutdown(Environment.ExitCode);
        }
    }
    private static IEnumerable<T> Find<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        { DependencyObject child = VisualTreeHelper.GetChild(root, i); if (child is T typed) { yield return typed; } foreach (T nested in Find<T>(child)) { yield return nested; } }
    }
}
