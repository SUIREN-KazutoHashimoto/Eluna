using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Eluna.App.Models;

namespace Eluna.App.Services;

internal static class AgentFeaturesVerification
{
    private static void Check(bool ok, string text) { if (!ok) { throw new InvalidOperationException(text); } }
    internal static async Task RunAsync(AppStore store, string output, string? metadata)
    {
        Directory.CreateDirectory(output); MainWindow? main = null;
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(10)); CancellationToken token = timeout.Token;
        async Task Until(Func<bool> ready) { while (!ready()) { await Task.Delay(30, token); } }
        async Task Progress(string stage) => await File.WriteAllTextAsync(Path.Combine(output, "progress.json"), JsonSerializer.Serialize(new { stage }), token);
        try
        {
            UnsavedChanges.Review = _ => false;
            LocalModel model = metadata is null ? new("agent-fixture", "検証用モデル", "fixture", "local", "fixture", "", 0) : JsonSerializer.Deserialize<LocalModel>(await File.ReadAllTextAsync(metadata, token), AppStore.JsonOptions)!;
            model = model with { Preferences = new(ContextSize: 8192, Temperature: .1, MaxOutputTokens: 384, DefaultMode: "fast") };
            if (metadata is not null) { string folder = Path.Combine(store.ModelsDirectory, model.Id); Directory.CreateDirectory(folder); await File.WriteAllTextAsync(Path.Combine(folder, "model.json"), JsonSerializer.Serialize(model, AppStore.JsonOptions), token); }
            main = new MainWindow(store) { WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = -20000 }; main.Show(); var chat = main.ViewModel;
            await Until(() => chat.IsReady || chat.HasError); Check(chat.IsReady, chat.Error);
            if (metadata is null) { chat.Models.Add(model); }
            chat.SelectedModel = model;
            await chat.ApplySettingsAsync(chat.Settings with { WorkMode = "offline", InferenceMode = "fast", ContextSize = 8192, MaxConcurrentInferences = 1, Compute = chat.ComputeChoices.Any(c => c.Id == "Vulkan0") ? "Vulkan0" : "cpu" });
            var friend = new AgentAccount(Guid.NewGuid(), "検証担当", "検", "日本語で短く、弱点と改善を示す。", ModelId: model.Id, Personality: new("critic"));
            var stranger = new AgentAccount(Guid.NewGuid(), "つながりのないアカウント", ModelId: model.Id);
            await chat.SaveAgentAsync(friend); await chat.SaveAgentAsync(stranger);
            var owner = new AgentAccount(Guid.NewGuid(), "進行担当", "進", "日本語で短く回答する。", ModelId: model.Id, Personality: new("analyst"), Connections: [friend.Id], Autonomy: new(true, true, true, true));
            await chat.SaveAgentAsync(owner);
            await chat.SaveProjectAsync(new(Guid.NewGuid(), "依頼元サーバー", "PRIVATE_SERVER_INSTRUCTION", "project-only", true, [new(Guid.NewGuid(), "非公開資料", "PRIVATE_DOCUMENT_TOKEN", DateTimeOffset.UtcNow)], []));
            Conversation source = await chat.CreateRoomAsync("自動編成を依頼する", "channel", [owner.Id, friend.Id]);
            source.Messages.Add(new() { Role = "user", Content = "PRIVATE_SOURCE_HISTORY_TOKEN" });
            await chat.SetChannelPermissionsAsync(source.Id, [new(owner.Id, true, true, true, true), new(friend.Id, false, false)]);
            var profile = new AgentProfileWindow(chat, owner.Id) { Owner = main, WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = -20000 }; profile.Show();
            profile.Behavior.Preset.SelectedValue = "roleplay"; profile.Behavior.Role.Text = "旅の案内人"; Check(await profile.SaveAsync(), "Personality profile did not save.");
            owner = chat.Agents.Single(a => a.Id == owner.Id); Check(owner.Personality?.Role == "旅の案内人" && owner.Connections!.SequenceEqual([friend.Id]) && owner.Autonomy!.Automatic, "Profile lost personality/contacts/autonomy.");
            var permissions = new RoomPermissionsWindow(chat, source) { Owner = main, WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = -20000 }; permissions.Show();
            permissions.Accounts.SelectedItem = friend; Check(!permissions.ViewBox.IsChecked!.Value, "Permission UI missed explicit denial."); permissions.ViewBox.IsChecked = true; Check(await permissions.SaveAsync(), "Permission save failed.");
            Check(ChannelAccess.CanView(chat.CaptureState(), source.Save(), friend.Id) && !ChannelAccess.CanSpeak(chat.CaptureState(), source.Save(), friend.Id), "Read-only UI grant allowed speech.");
            foreach (string theme in new[] { "dark", "light" })
            {
                Appearance.Apply(theme, 1.3); profile.Width = profile.MinWidth; profile.Height = profile.MinHeight; permissions.Width = permissions.MinWidth; permissions.Height = permissions.MinHeight;
                await PrototypePreview.RenderAsync(profile, Path.Combine(output, "profile-minimum-" + theme + ".png")); await PrototypePreview.RenderAsync(permissions, Path.Combine(output, "permissions-minimum-" + theme + ".png"));
                Check(profile.SaveButton.IsVisible && profile.SaveButton.ActualWidth > 100 && permissions.Accounts.ActualWidth > 200, "Profile/permissions clipped at minimum size.");
            }
            profile.Close(); permissions.Close(); Appearance.Apply("dark", 1);
            chat.SelectedRecipient = chat.Recipients.Single(r => r.Id == friend.Id); chat.Input = "この下書きは送信しない"; int jobs = chat.Inferences.Items.Count; chat.SendCommand.Execute(null); await Task.Delay(100, token);
            Check(chat.Input == "この下書きは送信しない" && chat.Inferences.Items.Count == jobs && chat.HasError, "Denied account consumed the draft or queued inference.");
            chat.Input = "@Computer 操作して"; chat.SendCommand.Execute(null); await Task.Delay(100, token); Check(chat.Input.Length > 0 && chat.ComputerActivities.Count == 0, "Computer Use bypassed speech permissions.");
            bool deniedArtifact = false; try { chat.CaptureArtifactContext("資料を作成"); } catch (InvalidOperationException) { deniedArtifact = true; }
            Check(deniedArtifact, "Document/image context bypassed permissions.");
            chat.SelectedRecipient = chat.Recipients.Single(r => r.Id == owner.Id); chat.Inferences.Suspended = true; chat.Input = "予約テスト"; chat.SendCommand.Execute(null); await Until(() => chat.Inferences.Items.Count > jobs && chat.Inferences.Items.Last().State == "queued" && chat.CanGenerate);
            InferenceJob queued = chat.Inferences.Items.Last(); Check(queued.State == "queued" && chat.TypingNotice.Length == 0, "Queued work incorrectly showed typing.");
            Check(chat.RoomMemberCards.Single(c => c.Account.Id == owner.Id).Status == "順番待ち" && source.NavigationStatus.Contains("順番待ち", StringComparison.Ordinal), "Queued member/channel status was missing.");
            await chat.SetChannelPermissionsAsync(source.Id, [new(owner.Id, true, false), new(friend.Id, false, false)]); Check(queued.State == "canceled", "Revoked queued work survived.");
            Check(chat.RoomMemberCards.All(c => !c.HasActivity) && !source.HasNavigationStatus, "Cancellation left stale member/channel activity.");
            await chat.SetChannelPermissionsAsync(source.Id, [new(owner.Id, true, true, true, true), new(friend.Id, false, false)]); chat.Inferences.Suspended = false;
            await Progress("ui-permissions-complete");
            if (metadata is not null)
            {
                int originalRooms = chat.Conversations.Count, originalServers = chat.Projects.Count;
                chat.SelectedConversation = source; chat.SelectedRecipient = chat.Recipients.Single(r => r.Id == owner.Id);
                chat.Input = "新しいサーバー『読書会チーム』と2つの作業チャンネルを作ってください。進行担当は読書会の進め方を3点、つながりのある検証担当は問題点と改善を3点にまとめ、最後にここへ報告してください。検索しないでください。";
                chat.SendCommand.Execute(null); await Until(() => chat.Inferences.Items.Last().Request.AnswerId != queued.Request.AnswerId);
                InferenceJob teamJob = chat.Inferences.Items.Last(); ChatMessage answer = source.Messages.Last();
                await Until(() => answer.Team is not null || teamJob.State is "failed" or "complete"); Check(answer.Team is not null, "Real model did not organize a requested team: " + teamJob.Detail + " / " + answer.Content);
                TeamRun team = answer.Team!; Check(team.Assignments.Any(a => a.AgentId == friend.Id) && team.Assignments.All(a => a.AgentId == friend.Id || a.AgentId == owner.Id), "Connected account was not invited or a stranger joined.");
                Check(chat.Projects.Count == originalServers + 1 && team.Assignments.Select(a => a.RoomId).Distinct().Count() == 2 && chat.ProjectChoices.Any(p => p.Id == team.ProjectId), "Server/channel navigation was not created.");
                Check(chat.SelectedConversation == source, "Autonomy switched the user's visible room.");
                await Progress("real-team-created");
                bool typingSeen = false, interrupted = false; Guid[] createdRooms = team.Assignments.Select(a => a.RoomId).Distinct().ToArray();
                while (teamJob.State == "active")
                {
                    token.ThrowIfCancellationRequested();
                    if (answer.Team is { ActiveRoomId: Guid room, ActiveAgentId: Guid actor } current && room != source.Id)
                    {
                        Conversation active = chat.Conversations.Single(c => c.Id == room); chat.SelectedConversation = active;
                        Check(chat.CanStopSelected, "The active worker channel did not expose its stop action.");
                        Check(chat.TypingNotice.Contains(chat.Agents.Single(a => a.Id == actor).Name, StringComparison.Ordinal), "Worker typing label was not in the actual channel."); typingSeen = true;
                        Check(chat.RoomMemberCards.Single(c => c.Account.Id == actor).Status == "入力中…" && chat.RoomMemberCards.Where(c => c.Account.Id != actor).All(c => c.Status != "入力中…"), "Member list attributed the running task to the wrong account.");
                        if (!File.Exists(Path.Combine(output, "typing.png"))) { main.Width = 1000; main.Height = 720; await PrototypePreview.RenderAsync(main, Path.Combine(output, "typing.png")); }
                        if (!interrupted && current.Assignments.Any(a => a.Complete) && active.Messages.Any(m => m.Identity?.AgentId == actor && m.IsGenerating))
                        {
                            interrupted = true; List<ChannelGrant> original = active.Room?.Permissions ?? [];
                            await chat.SetChannelPermissionsAsync(active.Id, [new(actor, false, false)]); await teamJob.Finished.WaitAsync(token);
                            Check(teamJob.State == "canceled" && answer.Team?.Stage == "paused" && chat.TypingNotice.Length == 0, "Active revocation did not stop the team/typing.");
                            Check(chat.RoomMemberCards.All(c => c.Status != "入力中…"), "Revocation left an animated member state.");
                            Guid[] completed = answer.Team!.Assignments.Where(a => a.Complete).Select(a => a.AnswerId).ToArray();
                            await chat.SetChannelPermissionsAsync(active.Id, original); await chat.ResumeTeamAsync(answer);
                            await Until(() => chat.Inferences.Items.Last().Request.AnswerId == answer.Id && chat.Inferences.Items.Last() != teamJob && chat.Inferences.Items.Last().State is "active" or "failed" or "complete");
                            teamJob = chat.Inferences.Items.Last();
                            Check(answer.Team!.Assignments.Where(a => a.Complete).Select(a => a.AnswerId).SequenceEqual(completed) && answer.Team.Assignments.Select(a => a.RoomId).Distinct().SequenceEqual(createdRooms), "Resume lost finished work or recreated rooms.");
                            await Progress("real-team-revoked-and-resumed");
                        }
                    }
                    await Task.Delay(40, token);
                }
                await teamJob.Finished.WaitAsync(token); Check(teamJob.State == "complete" && answer.Team!.Stage == "complete" && answer.Content.Length > 0, "Team execution/report failed: " + teamJob.Detail + " / " + answer.Team?.Detail);
                Check(!answer.Content.Contains("作成は不可能", StringComparison.Ordinal) && !answer.Content.Contains("チャンネル作成は完了していない", StringComparison.Ordinal), "The report contradicted the application's confirmed local operations.");
                Check(typingSeen && interrupted && chat.TypingNotice.Length == 0 && answer.Team!.Assignments.All(a => a.Complete), "Typing cleanup, active revocation or completed assignments failed.");
                Check(chat.RoomMemberCards.All(c => c.Status != "入力中…"), "Completion left stale member activity.");
                Check(chat.Inferences.Items.Count(j => j.Request.AnswerId == answer.Id) == 2 && chat.Conversations.Count == originalRooms + 2, "Recursive work or duplicate rooms were created.");
                foreach (Guid id in answer.Team!.Assignments.Select(a => a.RoomId).Distinct())
                {
                    Conversation worker = chat.Conversations.Single(c => c.Id == id); Check(worker.ProjectId != source.ProjectId && !worker.Messages.Any(m => m.Content.Contains("PRIVATE_SOURCE_HISTORY_TOKEN", StringComparison.Ordinal) || m.Knowledge.Any(k => k.Text.Contains("PRIVATE_DOCUMENT_TOKEN", StringComparison.Ordinal))), "Source history/documents were copied into a new server.");
                    Check(worker.Messages.Where(m => m.Role == "assistant").All(m => m.Content.Length > 0 && m.Identity?.AgentId is not null), "Worker result is missing its account.");
                }
                var teamWindow = new TeamWindow(chat, answer) { Owner = main, WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = -20000 }; teamWindow.Show(); await PrototypePreview.RenderAsync(teamWindow, Path.Combine(output, "team-result.png")); teamWindow.Close();
                chat.SelectedConversation = source; await PrototypePreview.RenderAsync(main, Path.Combine(output, "report.png"));
                await chat.PersistSafelyAsync(); AppState persisted = await store.LoadAsync(); AppStore.Validate(persisted); Check(persisted.Conversations.Single(c => c.Id == source.Id).Messages.Last().Team!.Stage == "complete", "Completed work was not persisted.");
                await File.WriteAllTextAsync(Path.Combine(output, "team.json"), JsonSerializer.Serialize(answer.Save(), AppStore.JsonOptions), token); await Progress("real-team-report-complete");
            }
            await File.WriteAllTextAsync(Path.Combine(output, "result.txt"), "Agent permissions/profile/personality/denied draft/computer/artifact/queue revocation/UI" + (metadata is null ? "" : "/real-model server+channels/invitation/worker replies/report/typing/isolation/persistence") + ": OK", token);
        }
        catch (Exception ex) { Environment.ExitCode = 1; await File.WriteAllTextAsync(Path.Combine(output, "result.txt"), ex.ToString()); }
        finally { if (main is not null) { foreach (Window child in main.OwnedWindows.Cast<Window>().ToArray()) { child.Close(); } await main.ViewModel.ShutdownAsync(); main.Close(); } Application.Current.Shutdown(); }
    }
}
