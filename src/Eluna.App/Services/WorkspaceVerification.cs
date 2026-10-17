using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Eluna.App.Controls;
using Eluna.App.Models;
using Eluna.App.ViewModels;

namespace Eluna.App.Services;

internal static class WorkspaceVerification
{
    private static void Check(bool value, string message) { if (!value) { throw new InvalidOperationException(message); } }
    private static IEnumerable<T> Find<T>(DependencyObject root) where T : DependencyObject
    { for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) { DependencyObject child = VisualTreeHelper.GetChild(root, i); if (child is T item) { yield return item; } foreach (T nested in Find<T>(child)) { yield return nested; } } }
    internal static async Task RunAsync(AppStore store, string output, string? metadata)
    {
        Directory.CreateDirectory(output); MainWindow? main = null; var report = new Dictionary<string, object>();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(12)); CancellationToken token = timeout.Token;
        async Task Until(Func<bool> ready)
        {
            while (!ready())
            {
                token.ThrowIfCancellationRequested();
                // Exercise this application's actual plan dialog against fixture-only/offline tasks.
                if (main is not null)
                {
                    foreach (WorkPlanWindow plan in main.OwnedWindows.OfType<WorkPlanWindow>().ToArray())
                    { Button? start = Find<Button>(plan).FirstOrDefault(b => b.Content is "この計画で開始"); start?.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); }
                }
                await Task.Delay(30, token);
            }
        }
        async Task Capture(Window window, string name)
        { await PrototypePreview.RenderAsync(window, Path.Combine(output, name + ".png")); }
        async Task Progress(string stage, object? value = null) => await File.WriteAllTextAsync(Path.Combine(output, "progress.json"), JsonSerializer.Serialize(new { stage, value }, AppStore.JsonOptions), token);
        try
        {
            UnsavedChanges.Review = _ => false;
            LocalModel? model = null;
            if (metadata is not null)
            {
                model = JsonSerializer.Deserialize<LocalModel>(await File.ReadAllTextAsync(metadata, token), AppStore.JsonOptions)!;
                model = model with { Preferences = new(ContextSize: 8192, Temperature: 0.1, MaxOutputTokens: 768, DefaultMode: "fast") };
                string folder = Path.Combine(store.ModelsDirectory, model.Id); Directory.CreateDirectory(folder);
                await File.WriteAllTextAsync(Path.Combine(folder, "model.json"), JsonSerializer.Serialize(model, AppStore.JsonOptions), token);
            }
            main = new MainWindow(store) { WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = -20000 }; main.Show(); ChatViewModel chat = main.ViewModel;
            await Until(() => chat.IsReady || chat.HasError); Check(chat.IsReady, chat.Error);
            if (model is null) { model = new("workspace-ui", "画面確認用モデル", "fixture", "local", "fixture", "", 0); chat.Models.Add(model); chat.SelectedModel = model; }
            await chat.ApplySettingsAsync(chat.Settings with
            {
                WorkMode = "offline",
                InferenceMode = "fast",
                ContextSize = 8192,
                MaxConcurrentInferences = 1,
                Compute = chat.ComputeChoices.Any(c => c.Id == "Vulkan0") ? "Vulkan0" : "cpu"
            });
            var design = new AgentAccount(Guid.NewGuid(), "設計担当", "設", "日本語で短く具体的な案を述べる。記憶にない値は不明と答える。", "実用的なUI設計", model.Id);
            var critic = new AgentAccount(Guid.NewGuid(), "検証担当", "検", "前提・反例・使いにくい点を具体的に指摘する。説明は短くする。", "操作性と検証", model.Id);
            await chat.SaveAgentAsync(design); await chat.SaveAgentAsync(critic);
            var server = new KnowledgeProject(Guid.NewGuid(), "開発チーム", "発言は短く、根拠と提案を区別する。", "project-only", false, [], []);
            await chat.SaveProjectAsync(server);
            Conversation channel = await chat.CreateRoomAsync("設計相談", "channel", [design.Id, critic.Id]);
            Conversation dm = await chat.CreateRoomAsync("設計担当とのDM", "dm", [design.Id]);
            dm.Messages.Add(new() { Role = "user", Content = "私の識別語はMAPLE-7392です。", Identity = new(null, "あなた", "あ", Time: DateTimeOffset.UtcNow) });
            await chat.SaveAgentMemoryAsync(new(Guid.NewGuid(), design.Id, "利用者の識別語はMAPLE-7392です。", "agent", dm.Id));
            chat.SelectedConversation = channel;
            channel.Messages.Add(new() { Role = "user", Content = "設定画面の項目を整理したい。", Identity = new(null, "あなた", "あ", Time: DateTimeOffset.UtcNow) });
            var work = new WorkSession
            {
                Mode = "debate",
                Question = "設定画面の設計",
                ModelId = model.Id,
                Options = new(Roles: [new("a", design.Name, "案を出す", model.Id, design.Id, design.Icon), new("b", critic.Name, "反例を示す", model.Id, critic.Id, critic.Icon)])
            };
            var card = new ChatMessage
            {
                Role = "assistant",
                Content = "司会のまとめ: 項目を用途ごとに分ける案を検討します。",
                Work = work,
                Identity = new(design.Id, design.Name, design.Icon, model.Id, model.DisplayName, DateTimeOffset.UtcNow, Detail: "司会")
            };
            channel.Messages.Add(card);
            var first = new DebateTurn(1, "a", "利用頻度の高い設定を一般へまとめる案です。", model.Id, design.Name, Guid.NewGuid(), design.Id, design.Icon, Time: DateTimeOffset.UtcNow, ModelName: model.DisplayName);
            work.Turns.Add(first); work.Turns.Add(new(1, "b", "頻度だけでは初回設定の項目を見落としませんか。", model.Id, critic.Name, Guid.NewGuid(), critic.Id, critic.Icon, "question", first.Id, DateTimeOffset.UtcNow, ModelName: model.DisplayName));
            Check(chat.Timeline.Count == 4 && chat.Timeline.Count(m => m.IsProjection) == 2, "Debate turns did not appear exactly once in the channel.");
            ChatMessage firstRow = chat.Timeline.Single(m => m.Id == first.Id); work.Turns[0] = first with { Content = first.Content + "初回設定は専用の案内も使います。" };
            Check(ReferenceEquals(firstRow, chat.Timeline.Single(m => m.Id == first.Id)), "Streaming recreated the row and would lose selection.");
            await Capture(main, "channel-rounds"); main.Width = 820; main.Height = 600; await Capture(main, "channel-minimum");
            main.Width = 1280; main.Height = 900;
            Conversation thread = await chat.CreateRoomAsync("初回設定を深掘り", "thread", [design.Id, critic.Id], channel, firstRow);
            Check(thread.ProjectId == channel.ProjectId && thread.Room!.OriginMessageId == first.Id, "Thread lost its origin."); chat.SelectedConversation = channel;
            var members = new AgentMembersEditor(chat.Agents, [design.Id]); members.Add(critic, 1); members.Add(design, 2);
            Check(members.SelectedIds.SequenceEqual(new[] { critic.Id, design.Id }) && !members.Accepts(new DataObject(DataFormats.Text, "outside")), "Member editor lost order, duplicated an agent or accepted external drag data.");
            var agents = new AgentsWindow(chat) { WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = -20000, Owner = main }; agents.Show(); agents.AgentList.SelectedItem = design;
            agents.NameBox.Text = "設計担当・更新"; agents.SaveButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); await Until(() => chat.Agents.Any(a => a.Id == design.Id && a.Name == "設計担当・更新"));
            agents.NameBox.Text = "保存中に追加した未保存の名前"; await Until(() => agents.SaveButton.IsEnabled); UnsavedChanges.Review = _ => null; agents.AgentList.SelectedItem = critic;
            await Task.Delay(60, token); Check((agents.AgentList.SelectedItem as AgentAccount)?.Id == design.Id && agents.NameBox.Text == "保存中に追加した未保存の名前", "An edit made during save was incorrectly marked as saved.");
            UnsavedChanges.Review = _ => false; agents.NameBox.Text = "設計担当・更新";
            Check(firstRow.Speaker == "設計担当", "Editing a profile rewrote a historical speaker."); await Capture(agents, "agents"); agents.Width = agents.MinWidth; agents.Height = agents.MinHeight; await Capture(agents, "agents-minimum");
            Find<TabControl>(agents).Single().SelectedIndex = 2; await Task.Delay(60, token);
            TextBox note = Find<TextBox>(agents).Single(t => System.Windows.Automation.AutomationProperties.GetName(t).StartsWith("記憶する内容", StringComparison.Ordinal));
            note.Text = "未保存の記憶"; UnsavedChanges.Review = _ => null; agents.AgentList.SelectedItem = critic;
            await Task.Delay(60, token); Check((agents.AgentList.SelectedItem as AgentAccount)?.Id == design.Id && note.Text == "未保存の記憶", "Canceling an account switch lost the memory draft.");
            UnsavedChanges.Review = _ => false; agents.AgentList.SelectedItem = critic; await Task.Delay(60, token);
            Check(note.Text.Length == 0 && agents.NameBox.Text == critic.Name, "Changing accounts retained another agent's memory draft."); agents.Close();
            chat.SelectedConversation = channel; channel.Fork(1, "表示切替の確認"); Check(chat.Timeline.Count == 1 && chat.Timeline[0].Id == channel.Messages[0].Id, "Forking did not rebind the channel timeline.");
            chat.SelectedBranch = channel.Branches[0]; Check(chat.Timeline.Count == 4, "Returning to the original branch lost debate rows.");
            foreach (string theme in new[] { "dark", "light" })
            {
                Appearance.Apply(theme, 1.3);
                var roomEditor = new RoomWindow(chat, channel) { Owner = main, WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = -20000 }; roomEditor.Show();
                await Capture(roomEditor, "room-" + theme); roomEditor.Width = roomEditor.MinWidth; roomEditor.Height = roomEditor.MinHeight; await Capture(roomEditor, "room-" + theme + "-minimum"); roomEditor.Close();
            }
            Appearance.Apply("dark"); await chat.PersistSafelyAsync();
            AppState persisted = await store.LoadAsync(); AppStore.Validate(persisted);
            Check(persisted.Agents?.Count == chat.Agents.Count && persisted.Conversations.Single(c => c.Id == thread.Id).Room?.ParentId == channel.Id, "Workspace persistence broke accounts or thread links.");
            string backup = Path.Combine(output, "workspace-backup.zip"); await BackupArchive.CreateAsync(store, backup, false, null, token);
            var backupStore = new AppStore(Path.Combine(output, "restored")); PreparedBackup prepared = await BackupArchive.PrepareAsync(backupStore, backup, token: token); await BackupArchive.ApplyAsync(backupStore, prepared);
            AppState fromZip = await backupStore.LoadAsync(); Check(fromZip.Agents!.Count == chat.Agents.Count && fromZip.Conversations.Single(c => c.Id == thread.Id).Room!.ParentId == channel.Id, "ZIP restore broke account or thread identity.");
            report["ui"] = new { memberEditor = true, historicalIdentity = true, timeline = 4, thread = true, minimum = "820x600 / room 580x500 / agents 650x500", themes = "dark/light at 130%", backup = File.Exists(backup) };
            await Progress("ui-complete", report);
            if (metadata is not null)
            {
                async Task<InferenceJob> Send(string text)
                {
                    int before = chat.Inferences.Items.Count; chat.Input = text; Check(chat.SendCommand.CanExecute(null), "送信できません: " + chat.Error); chat.SendCommand.Execute(null);
                    await Until(() => chat.Inferences.Items.Count > before || chat.HasError); Check(chat.Inferences.Items.Count > before, chat.Error);
                    await Until(() => chat.CanGenerate); return chat.Inferences.Items.Last();
                }
                Conversation privateRoom = await chat.CreateRoomAsync("限定の確認", "channel", [design.Id]);
                InferenceJob hidden = await Send("保存メモリにある私の識別語を英数字のまま答えてください。分からなければ不明とだけ答えてください。"); await hidden.Finished;
                ChatMessage hiddenAnswer = privateRoom.Messages.Last(); Check(hidden.State == "complete" && !hiddenAnswer.Content.Contains("MAPLE-7392", StringComparison.Ordinal), "Private memory leaked in a real model response: " + hiddenAnswer.Content);
                await chat.UpdateRoomAsync(privateRoom, privateRoom.Title, privateRoom.Room! with { AcceptAgentMemory = true }, true);
                InferenceJob visible = await Send("保存メモリの識別語を、英数字を変えずに一語だけ答えてください。"); await visible.Finished;
                ChatMessage visibleAnswer = privateRoom.Messages.Last(); Check(visible.State == "complete" && visibleAnswer.Knowledge.Any(k => k.AgentId == design.Id) && visibleAnswer.Content.Contains("MAPLE-7392", StringComparison.Ordinal), "Allowed memory was not used by the real model: " + visible.Detail + " / " + visibleAnswer.Content);
                report["recall"] = new { hidden = hiddenAnswer.Content, allowed = visibleAnswer.Content, provenance = visibleAnswer.Knowledge }; await Progress("recall-complete", report);
                chat.SelectedConversation = channel;
                await chat.ApplySettingsAsync(chat.Settings with { WorkMode = "debate", WorkOptions = new(TokenLimit: 9000, SecondsLimit: 300, DebateStyle: "free", AutomaticDebate: false, MaxTurns: 4, DebateSearch: "off") });
                InferenceJob debate = await Send("社内AIツールの設定画面で、初心者が迷わない構成を議論してください。短く具体的に発言してください。");
                await Until(() => channel.Messages.Last().Work?.Stage == "debate-wait" || !debate.IsPending);
                WorkSession live = channel.Messages.Last().Work ?? throw new InvalidOperationException("議論が作られませんでした: " + debate.Detail);
                Check(live.Stage == "debate-wait", "手動開始で待機できませんでした: " + debate.Detail);
                await chat.ControlDebateAsync(live, "next", critic.Id.ToString("N")); await Until(() => live.Turns.Any(t => !t.Incomplete) && live.Stage == "debate-wait" || !debate.IsPending);
                Check(live.Turns.Any(t => t.AgentId == critic.Id && !t.Incomplete), "指名した参加者が発言しませんでした: " + debate.Detail);
                chat.Input = "@設計担当・更新 初回に必要な設定は何ですか？この質問を優先してください。"; chat.SendCommand.Execute(null);
                await Until(() => live.Turns.Count(t => t.Kind != "user" && !t.Incomplete) >= 2 || !debate.IsPending);
                await chat.SetDebateParticipantsAsync(live, [critic.Id, design.Id]);
                await chat.ControlDebateAsync(live, "organize");
                await Until(() => live.Turns.Count(t => t.Kind != "user" && !t.Incomplete) >= 3 || !debate.IsPending);
                await chat.ControlDebateAsync(live, "finish"); await debate.Finished;
                Check(debate.State == "complete" && live.Stage == "complete" && live.Turns.Any(t => t.Kind == "user") && live.Sources.Count == 0, "自由形式の介入・終了に失敗: " + debate.Detail);
                Check(chat.Timeline.Where(m => m.IsProjection && m.Identity?.WorkId == live.Id).Select(m => m.Id).Distinct().Count() == live.Turns.Count, "実議論の発言が重複しています。");
                await Capture(main, "live-free-debate");
                report["debate"] = new { live.Stage, live.TokensUsed, live.SecondsUsed, Turns = live.Turns, live.Report, model = model.DisplayName, compute = chat.Settings.Compute };
                await Progress("debate-complete", report);
                await chat.ApplySettingsAsync(chat.Settings with { WorkMode = "offline" });
                int allBefore = chat.Inferences.Items.Count;
                for (int attempt = 0; attempt < 2; attempt++)
                {
                    int before = chat.Inferences.Items.Count; chat.Input = "@全員 2 + 3 の答えを数字一文字で答えてください。"; chat.SendCommand.Execute(null);
                    await Until(() => chat.Inferences.Items.Count >= before + 2 || chat.HasError); Check(chat.Inferences.Items.Count >= before + 2, chat.Error);
                    InferenceJob[] current = chat.Inferences.Items.Skip(before).ToArray(); await Task.WhenAll(current.Select(j => j.Finished)).WaitAsync(token);
                    Check(current.All(j => j.State == "complete") && current.Select(j => j.Request.Agent!.Id).Distinct().Count() == 2, "Broadcast did not produce separate agent replies: " + string.Join(" / ", current.Select(j => j.Request.Agent?.Name + ": " + j.State + " " + j.Detail)));
                }
                InferenceJob[] allJobs = chat.Inferences.Items.Skip(allBefore).ToArray();
                Conversation threadRoom = chat.Conversations.Single(c => c.Id == thread.Id); chat.SelectedConversation = threadRoom;
                Check(chat.SelectedWorkMode.Id == "offline", "The thread lost its saved work mode.");
                InferenceJob threadJob = await Send("親の発言で提案した、一般へまとめる設定はどのような設定ですか。一文で答えてください。"); await threadJob.Finished;
                Check(threadJob.State == "complete" && threadRoom.Messages.Last().Content.Length > 0, "The real thread could not answer with parent context.");
                report["broadcastAndThread"] = new { agents = allJobs.Select(j => j.Request.Agent!.Name).ToArray(), replies = allJobs.Select(j => channel.Messages.Single(m => m.Id == j.Request.AnswerId).Content).ToArray(), threadReply = threadRoom.Messages.Last().Content };
                await Progress("broadcast-thread-complete", report);
                chat.SelectedConversation = channel;
                var round = new WorkSession
                {
                    Mode = "debate",
                    Stage = "paused",
                    ModelId = model.Id,
                    Question = "設定画面を用途ごとに分ける利点と注意点を議論してください。",
                    Plan = "固定の確認用資料を使って一巡だけ議論する。",
                    PlanApproved = true,
                    Expanded = true,
                    WebEnabled = true,
                    SearchesUsed = 1,
                    PagesUsed = 1,
                    Options = new(SearchLimit: 1, PageLimit: 1, Rounds: 1, TokenLimit: 8000, Roles: [new("a", design.Name, "具体案を示す", model.Id, design.Id), new("b", critic.Name, "反例を示す", model.Id, critic.Id)])
                };
                round.Queries.Add("fixture settings"); round.SearchedQueries.Add("fixture settings");
                round.Sources.Add(new(1, "確認用の設計資料", "https://example.invalid/settings-fixture", "設定は一般、モデル、データの三つに分類する。初回の設定にはモデルの取得が必要。設定名には利用者にわかる言葉を使う。", DateTimeOffset.UtcNow));
                channel.Messages.Add(new() { Role = "assistant", Work = round, Identity = new(design.Id, design.Name, design.Icon, model.Id, model.DisplayName, DateTimeOffset.UtcNow, Detail: "司会") });
                await chat.ControlDebateAsync(round, "resume"); await Until(() => round.Stage is "complete" or "failed" or "limited");
                Check(round.Stage == "complete" && round.Turns.Count == 2 && round.Turns.All(t => !t.Incomplete) && chat.Timeline.Count(m => m.Identity?.WorkId == round.Id && m.IsProjection) == 2, "Real round debate failed or duplicated speeches.");
                report["rounds"] = new { round.Stage, Turns = round.Turns, round.Report, fixedSources = true }; await Capture(main, "live-rounds"); await Progress("rounds-complete", report);
                chat.Inferences.Suspended = true;
                chat.SelectedConversation = privateRoom; InferenceJob related = await Send("参照許可を変更する前の予約です。");
                Conversation separate = await chat.CreateRoomAsync("無関係な検証担当DM", "dm", [critic.Id]); InferenceJob unrelated = await Send("2 + 3 の答えを数字一文字で答えてください。");
                await chat.UpdateRoomAsync(privateRoom, privateRoom.Title, privateRoom.Room! with { AcceptAgentMemory = false }, true);
                Check(!related.IsPending && unrelated.IsPending, "Revoking room memory canceled an unrelated agent/room request.");
                chat.Inferences.Suspended = false; await unrelated.Finished.WaitAsync(token); Check(unrelated.State == "complete", unrelated.Detail);
                report["scopedCancellation"] = true;
                chat.SelectedConversation = channel;
                await chat.ApplySettingsAsync(chat.Settings with { WorkMode = "debate", WorkOptions = new(TokenLimit: 10000, SecondsLimit: 300, DebateStyle: "free", AutomaticDebate: true, MaxTurns: 4, DebateSearch: "off") });
                InferenceJob interruptedJob = await Send("設定画面の改善案を100項目、各項目に具体的な理由を付けて議論してください。");
                await Until(() => channel.Messages.Last().Work?.Turns.Any(t => t.Incomplete && t.Content.Length > 30) == true || !interruptedJob.IsPending);
                WorkSession interrupted = channel.Messages.Last().Work ?? throw new InvalidOperationException(interruptedJob.Detail);
                Check(interrupted.IsRunning, "Pause scenario finished before streaming could be interrupted.");
                await chat.ControlDebateAsync(interrupted, "pause"); await interruptedJob.Finished;
                Check(interrupted.Stage == "paused" && interrupted.Turns.Any(t => t.Incomplete && t.Content.Length > 0), "Pausing lost the partial speech.");
                LocalModel lateModel = model with { Id = model.Id + "-late", DisplayName = "議論開始後に追加したモデル" };
                string lateFolder = Path.Combine(store.ModelsDirectory, lateModel.Id); Directory.CreateDirectory(lateFolder); await File.WriteAllTextAsync(Path.Combine(lateFolder, "model.json"), JsonSerializer.Serialize(lateModel, AppStore.JsonOptions), token); chat.Models.Add(lateModel);
                var operations = new AgentAccount(Guid.NewGuid(), "運用担当", "運", "利用者の最新の質問に短く答える。", "日常の運用", model.Id);
                await chat.SaveAgentAsync(operations); await chat.SetDebateParticipantsAsync(interrupted, [design.Id, critic.Id, operations.Id]);
                interrupted.Discussion = interrupted.Discussion with { Automatic = false, RequestedSpeaker = operations.Id.ToString("N") };
                await chat.ControlDebateAsync(interrupted, "resume");
                await Until(() => interrupted.Stage == "debate-wait" || interrupted.Stage is "failed" or "limited");
                await chat.SaveAgentAsync(operations with { ModelId = lateModel.Id });
                await chat.ControlDebateAsync(interrupted, "next", operations.Id.ToString("N"));
                await Until(() => interrupted.Turns.Any(t => t.AgentId == operations.Id && t.Incomplete && t.Content.Length > 30) || !interrupted.IsRunning);
                Check(interrupted.IsRunning, "The new participant failed before its speech.");
                chat.Input = "@運用担当 今の発言を中断し、初回設定で最も重要な点だけを一文で答えてください。";
                await chat.InterruptDebateAsync(interrupted);
                await Until(() => interrupted.Turns.Any(t => t.AgentId == operations.Id && !t.Incomplete) || interrupted.Stage is "failed" or "limited" or "complete");
                Check(interrupted.Turns.Any(t => t.AgentId == operations.Id && !t.Incomplete && t.ModelId == lateModel.Id), "The newly joined agent did not answer with its newly added model after interruption.");
                await chat.ControlDebateAsync(interrupted, "finish"); await Until(() => !interrupted.IsRunning && interrupted.Stage == "complete");
                Guid[] partialIds = interrupted.Turns.Where(t => t.Incomplete).Select(t => t.Id!.Value).ToArray(); Check(partialIds.Length >= 2, "Pause and interrupt did not preserve both partial speeches.");
                await chat.PersistSafelyAsync(); AppState afterStop = await store.LoadAsync();
                SavedWork savedInterrupted = afterStop.Conversations.Single(c => c.Id == channel.Id).Messages.Last(m => m.Work?.Id == interrupted.Id).Work!;
                Check(savedInterrupted.Turns.Where(t => t.Incomplete).Select(t => t.Id!.Value).SequenceEqual(partialIds), "Saving lost interrupted speech identities.");
                report["interruption"] = new { interrupted.Stage, partialIds, completed = interrupted.Turns.Count(t => !t.Incomplete), joined = operations.Id };
                await Capture(main, "live-interruption"); await Progress("interruption-complete", report);
                Conversation researchRoom = await chat.CreateRoomAsync("議論中の追加調査", "channel", [design.Id, critic.Id]);
                await chat.ApplySettingsAsync(chat.Settings with { WorkMode = "debate", WorkOptions = new(SearchLimit: 1, PageLimit: 1, SecondsLimit: 180, TokenLimit: 8000, DebateStyle: "free", AutomaticDebate: false, MaxTurns: 2, DebateSearch: "auto") });
                InferenceJob researchJob = await Send("設定画面の分類案を短く相談します。今は検索しないでください。");
                await Until(() => researchRoom.Messages.Last().Work?.Stage == "debate-wait" || !researchJob.IsPending);
                WorkSession research = researchRoom.Messages.Last().Work ?? throw new InvalidOperationException(researchJob.Detail);
                Check(research.SearchesUsed == 0 && research.Stage == "debate-wait", "The initial no-search instruction was ignored.");
                chat.Input = "@検証担当 llama.cpp の公式ドキュメントを検索して、モデル設定の分類で参考になる点を短く確認してください。"; chat.SendCommand.Execute(null);
                await Until(() => research.Stage == "debate-wait" && research.Turns.Any(t => t.Kind != "user" && !t.Incomplete) || research.Stage is "failed" or "limited");
                Check(research.SearchesUsed == 1 && research.SearchedQueries.Count == 1 && research.Turns.Any(t => t.AgentId == critic.Id && !t.Incomplete), "A live discussion could not add a requested web search: " + researchJob.Detail);
                await chat.ControlDebateAsync(research, "finish"); await researchJob.Finished.WaitAsync(token);
                Check(research.Stage == "complete", "The researched discussion failed to finish: " + researchJob.Detail);
                report["additionalResearch"] = new { research.SearchesUsed, research.PagesUsed, queries = research.SearchedQueries, sources = research.Sources.Select(s => s.Url).ToArray(), warnings = research.Activities.Where(a => a.Kind == "warning").ToArray() }; await Capture(main, "live-research"); await Progress("research-complete", report);
                chat.SelectedConversation = channel;
                await chat.ApplySettingsAsync(chat.Settings with { WorkMode = "offline" }); chat.SelectedRecipient = chat.Recipients.First(r => r.Id == design.Id);
                Guid mediaRoom = channel.Id, mediaBranch = channel.ActiveBranch.Id; ArtifactContext artifactContext = chat.CaptureArtifactContext("短い設計メモを日本語で作成してください。");
                MediaJob document = await chat.EnqueueMediaAsync("設計メモの作成", new("document", "設定画面の設計メモを二文で書いてください。", "日本語で短く答える。", channel.ProjectId, chat.NewMediaOutput(".md"), artifactContext));
                chat.SelectedConversation = separate; await document.Finished.WaitAsync(token);
                ChatMessage result = channel.Branches.Single(b => b.Id == mediaBranch).Messages.Single(m => m.Artifact?.JobId == document.Id);
                Check(document.State == "complete" && result.Identity?.AgentId == design.Id && File.Exists(result.Artifact!.ResultPath) && !separate.Messages.Any(m => m.Artifact?.JobId == document.Id), "Document completion was attached to the wrong room/account.");
                await chat.PersistSafelyAsync(); AppState savedDocument = await store.LoadAsync(); Check(savedDocument.Conversations.Single(c => c.Id == mediaRoom).Branches!.SelectMany(b => b.Messages).Any(m => m.Artifact?.JobId == document.Id), "Generated result link did not survive storage.");
                report["document"] = new { room = mediaRoom, branch = mediaBranch, job = document.Id, result.Artifact!.ResultPath, correctDestination = true }; await Progress("document-complete", report);
            }
            await File.WriteAllTextAsync(Path.Combine(output, "verification.json"), JsonSerializer.Serialize(report, AppStore.JsonOptions), token);
            await File.WriteAllTextAsync(Path.Combine(output, "result.txt"), "Workspace migration/accounts/channels/DM/thread/timeline/scopes/UI/backup" + (metadata is null ? " (UI fixtures)" : " and real model recall/free debate") + ": OK", token);
        }
        catch (Exception ex) { Environment.ExitCode = 1; await File.WriteAllTextAsync(Path.Combine(output, "result.txt"), ex.ToString()); }
        finally
        {
            if (main is not null) { foreach (Window child in main.OwnedWindows.Cast<Window>().ToArray()) { child.Close(); } await main.ViewModel.ShutdownAsync(); main.Close(); }
            Application.Current.Shutdown();
        }
    }
}
