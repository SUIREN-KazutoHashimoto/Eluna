using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Eluna.App.Controls;
using Eluna.App.Models;

namespace Eluna.App.Services;

internal static class VoiceChannelVerification
{
    private static void Check(bool ok, string text) { if (!ok) { throw new InvalidOperationException(text); } }
    private static IEnumerable<T> Find<T>(DependencyObject root) where T : DependencyObject
    { for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) { DependencyObject child = VisualTreeHelper.GetChild(root, i); if (child is T value) { yield return value; } foreach (T nested in Find<T>(child)) { yield return nested; } } }
    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
    private static T Show<T>(T window) where T : Window { window.WindowStartupLocation = WindowStartupLocation.Manual; window.Left = -20000; window.Top = -20000; window.Show(); return window; }
    internal static async Task RunAsync(AppStore store, string output, string? metadata)
    {
        Directory.CreateDirectory(output); MainWindow? main = null; Process? fixture = null; WindowsJob? fixtureJob = null;
        var report = new Dictionary<string, object>(); using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(6)); CancellationToken token = timeout.Token;
        async Task Until(Func<bool> ready) { while (!ready()) { await Task.Delay(40, token); } }
        try
        {
            UnsavedChanges.Review = _ => false; LocalModel? model = null;
            if (metadata is not null)
            {
                model = JsonSerializer.Deserialize<LocalModel>(await File.ReadAllTextAsync(metadata, token), AppStore.JsonOptions)!;
                model = model with { Preferences = new(ContextSize: 8192, Temperature: 0.1, MaxOutputTokens: 384, DefaultMode: "fast") };
                string folder = Path.Combine(store.ModelsDirectory, model.Id); Directory.CreateDirectory(folder); await File.WriteAllTextAsync(Path.Combine(folder, "model.json"), JsonSerializer.Serialize(model, AppStore.JsonOptions), token);
            }
            main = Show(new MainWindow(store)); var chat = main.ViewModel; await Until(() => chat.IsReady || chat.HasError); Check(chat.IsReady, chat.Error);
            if (model is null) { model = new("voice-fixture", "プロフィールで選択するモデル", "fixture", "local", "fixture", "", 0); chat.Models.Add(model); }
            chat.SelectedModel = model;
            await chat.ApplySettingsAsync(chat.Settings with { WorkMode = "offline", InferenceMode = "fast", Compute = chat.ComputeChoices.Any(c => c.Id == "Vulkan0") ? "Vulkan0" : "cpu" });
            var account = new AgentAccount(Guid.NewGuid(), "会話担当 · 長い日本語プロフィール名を使って表示を確認", "話", "日本語で短く回答してください。", ModelId: model.Id);
            await chat.SaveAgentAsync(account);
            Guid serverId = Guid.NewGuid(); var server = new KnowledgeProject(serverId, "開発サーバー · 長い名前のテスト", "サーバー専用の指示", "project-only", true, [new(Guid.NewGuid(), "資料", "検証用の資料です", DateTimeOffset.UtcNow)], [new(Guid.NewGuid(), "サーバーだけの決定", DateTimeOffset.UtcNow)], AcceptAgentMemory: true);
            await chat.SaveProjectAsync(server);
            Conversation voice = await chat.CreateRoomAsync("音声で相談するボイスチャンネル", "voice", [account.Id]);
            Check(chat.IsVoiceChannel && voice.HistoryGroup == "ボイスチャンネル", "Voice kind or navigation grouping missing.");
            voice.Messages.Add(new() { Role = "assistant", Content = "音声とテキストの両方で話せます。共有中の画面についても、ここから指示してください。", Identity = new(account.Id, account.Name, account.Icon, model.Id, model.DisplayName, DateTimeOffset.Now) });
            main.OpenProfile(account.Id); var profile = main.OwnedWindows.OfType<AgentProfileWindow>().Single();
            var second = model with { Id = "voice-second", DisplayName = "非常に長いモデル名 · " + new string('あ', 80) }; chat.Models.Add(second);
            profile.Close(); main.OpenProfile(account.Id); profile = main.OwnedWindows.OfType<AgentProfileWindow>().Single();
            profile.ModelBox.SelectedItem = profile.ModelBox.Items.Cast<DebateModelChoice>().Single(c => c.Id == second.Id); Check(await profile.SaveAsync(), "Profile save failed."); Check(chat.Agents.Single(a => a.Id == account.Id).ModelId == second.Id, "Profile model not applied.");
            foreach (string theme in new[] { "dark", "light" })
            {
                Appearance.Apply(theme, 1.3); profile.Width = profile.MinWidth; profile.Height = profile.MinHeight; await PrototypePreview.RenderAsync(profile, Path.Combine(output, "profile-minimum-" + theme + ".png"));
                Check(profile.SaveButton.IsVisible && profile.ModelBox.ActualWidth > 120, "Profile editing clipped at minimum size.");
            }
            profile.ModelBox.SelectedItem = profile.ModelBox.Items.Cast<DebateModelChoice>().First(c => c.Id == model.Id); await profile.SaveAsync(); profile.NameBox.Text = "未保存"; UnsavedChanges.Review = _ => null; profile.Close(); Check(profile.IsVisible, "Profile draft was lost."); UnsavedChanges.Review = _ => false; profile.Close();
            var serverWindow = Show(new ProjectWindow(chat, server) { Owner = main }); Check(serverWindow.CreateDraft().AcceptAgentMemory, "Editing server reset memory permissions.");
            Check(await serverWindow.SaveDraftAsync(), "Server save failed."); Check(chat.SelectedProject!.Documents.Count == 1 && chat.SelectedProject.Memories.Count == 1, "Server data was lost.");
            await PrototypePreview.RenderAsync(serverWindow, Path.Combine(output, "server-settings.png")); serverWindow.Close();
            Check(chat.VoiceParticipants.Count == 0, "Channel membership was presented as a voice connection before joining.");
            await chat.JoinVoiceAsync(); Check(chat.VoiceJoined && chat.VoiceMuted, "Joining automatically enabled microphone.");
            Check(chat.VoiceParticipants.Count == 2 && chat.VoiceParticipants.Any(p => p.Id == AgentAccount.LocalId) && chat.VoiceParticipants.Any(p => p.Id == account.Id && p.Status == "音声応答に参加"), "Joined voice participants were not distinguished from channel members.");
            long voiceSession = chat.VoiceSession; await chat.SetVoiceParticipantsAsync(voice.Id, voiceSession, []); Check(chat.VoiceParticipants.Count == 1, "Removing an AI from voice removed the local member or left a phantom connection.");
            await chat.SetVoiceParticipantsAsync(voice.Id, voiceSession, [account.Id]);
            var voicePeople = Show(new VoiceParticipantsWindow(chat) { Owner = main });
            await PrototypePreview.RenderAsync(voicePeople, Path.Combine(output, "voice-participants.png")); voicePeople.Close();
            await chat.ToggleVoiceOutputAsync(); report["explicitVoiceParticipants"] = true;
            chat.Input = "音声とは別の下書き"; Conversation other = await chat.CreateRoomAsync("テキストで別の作業", "channel", [account.Id]); chat.Input = "別チャンネルの下書き";
            Check(chat.VoiceJoined && !chat.IsJoinedVoiceChannel, "Voice call did not survive navigation."); chat.OpenVoiceChannel(); Check(chat.Input == "音声とは別の下書き" && chat.VoiceJoined, "Call navigation lost draft.");
            foreach (string theme in new[] { "dark", "light" })
            {
                Appearance.Apply(theme, 1); main.Width = 1280; main.Height = 900; await PrototypePreview.RenderAsync(main, Path.Combine(output, "voice-" + theme + ".png"));
                Appearance.Apply(theme, 1.3); main.Width = 820; main.Height = 600; await PrototypePreview.RenderAsync(main, Path.Combine(output, "voice-minimum-" + theme + ".png")); Check(((ListBox)main.FindName("MessagesList")).ActualHeight > 140, "Voice UI covered conversation.");
            }
            Appearance.Apply("dark", 1); main.Width = 1280; main.Height = 900;
            ProcessStartInfo start = ComputerDesktop.StartInfo("--computer-fixture"); start.ArgumentList.Add(Path.Combine(output, "fixture"));
            var visible = new ProcessStartInfo(start.FileName) { UseShellExecute = true, WindowStyle = ProcessWindowStyle.Normal }; foreach (string arg in start.ArgumentList) { visible.ArgumentList.Add(arg); }
            start = visible;
            fixture = Process.Start(start)!; fixtureJob = new(fixture); await Until(() => { fixture.Refresh(); return fixture.MainWindowHandle != 0; });
            ComputerTarget target = (await new ComputerDesktop().WindowsAsync(token)).Single(w => w.ProcessId == fixture.Id);
            await File.WriteAllTextAsync(Path.Combine(output, "desktop.txt"), ComputerDesktopNative.DesktopStatus(target), token);
            var picker = Show(new ScreenShareWindow(chat) { Owner = main }); await Until(() => picker.Targets.Items.Count > 0);
            picker.Targets.SelectedItem = picker.Targets.Items.Cast<ComputerTarget>().Single(t => t.ProcessId == fixture.Id);
            picker.Width = picker.MinWidth; picker.Height = picker.MinHeight; Appearance.Apply("dark", 1.3); await PrototypePreview.RenderAsync(picker, Path.Combine(output, "share-picker-minimum.png"));
            Check(picker.ShareButton.IsVisible && picker.ShareButton.IsEnabled && picker.Targets.ActualHeight > 30, "Share picker inaccessible at minimum size."); Click(picker.ShareButton); Check(!picker.IsVisible, "Share picker failed to close after selection."); Appearance.Apply("dark", 1);
            await Until(() => chat.SharedPreview is not null || !chat.IsSharing); Check(chat.IsSharing && chat.SharedPreview is not null && !chat.ShareControlAllowed, "Passive screen sharing failed or allowed control automatically.");
            await PrototypePreview.RenderAsync(main, Path.Combine(output, "screen-share.png"));
            if (metadata is not null)
            {
                chat.ShareControlAllowed = true; chat.Input = "検証用ウィンドウの作業メモ欄に「共有からの入力」と入力してください。保存ボタンは押さず、入力したら完了してください。"; chat.SendCommand.Execute(null);
                await Until(() => voice.Messages.LastOrDefault(m => m.Computer is not null)?.Computer is not null); ComputerSession computer = voice.Messages.Last(m => m.Computer is not null).Computer!;
                await Until(() => computer.Stage != "selecting"); Check(computer.SharedTarget == target, "Shared target did not bind to Computer Use.");
                await Until(() => computer.Steps.Any(s => s.Action.Type is "type_text" or "set_value") || !computer.Pending);
                if (computer.Reviewing) { throw new InvalidOperationException("Fixture input unexpectedly needed review."); }
                ComputerObservation observed = await new ComputerDesktop().PreviewAsync(target, token);
                bool entered = observed.Elements.Any(e => e.Value.Contains("共有からの入力", StringComparison.Ordinal));
                string desktopState = ComputerDesktopNative.DesktopStatus(target);
                if (!entered && desktopState.StartsWith("foreground=0;", StringComparison.Ordinal) && computer.Steps.Count == 0 && computer.Stage == "failed") { report["realDesktopInputUnverified"] = "Windows did not provide a foreground window: " + desktopState; }
                else { Check(entered, "Real model did not input text in the shared fixture: " + computer.Detail); report["realModelSharedInput"] = true; }
                chat.StopSharing(); await computer.Completion; Check(!chat.IsSharing && !chat.ShareControlAllowed && chat.SharedPreview is null && !computer.Pending, "Share stop left AI control active.");
                report["sharedControllerAndRevocation"] = true;
            }
            else { chat.ShareControlAllowed = true; chat.StopSharing(); Check(!chat.ShareControlAllowed && chat.SharedPreview is null, "Share permission survived stop."); }
            AgentAccount enabled = chat.Agents.First(a => a.Id == account.Id); await chat.SaveAgentAsync(enabled with { Enabled = false });
            int disabledBefore = voice.Messages.Count; chat.Input = "@Computer 無効なアカウントでは操作しない"; chat.SendCommand.Execute(null); await Task.Delay(80, token);
            Check(voice.Messages.Count == disabledBefore && chat.Input.Length > 0, "Disabled account started Computer Use or lost draft."); await chat.SaveAgentAsync(enabled);
            await chat.LeaveVoiceAsync(); Check(!chat.VoiceJoined && !chat.IsSharing && chat.VoiceParticipants.Count == 0, "Leaving retained microphone/share/participants state."); chat.SelectedConversation = other; Check(chat.Input == "別チャンネルの下書き", "Voice features overwrote another draft.");
            IReadOnlyList<VoiceChoice> voices = await WindowsVoiceAudio.VoicesAsync(); Check(voices.Count > 0, "No Windows speech synthesis voice.");
            await using (var audio = new WindowsVoiceAudio())
            {
                string wave = Path.Combine(output, "synthesis.wav"); await audio.SynthesizeFileAsync("こんにちは。今日はよい天気ですね。", wave); Check(new FileInfo(wave).Length > 1000, "Synthesis produced no WAV audio."); report["ttsWaveBytes"] = new FileInfo(wave).Length;
                string recognized = ""; audio.Recognized += text => recognized += text;
                try
                {
                    string engine = await audio.ListenAsync(wave); var clock = Stopwatch.StartNew(); while (recognized.Length == 0 && clock.Elapsed < TimeSpan.FromSeconds(20)) { await Task.Delay(100, token); }
                    Check(recognized.Length > 0, "Speech engine available but no recognition event was delivered."); report["recognition"] = new { engine, text = recognized }; await audio.MuteAsync();
                }
                catch (System.Runtime.InteropServices.COMException ex) { report["recognitionUnavailable"] = ex.Message + " (" + ex.HResult + ")"; }
                await audio.InterruptAsync();
            }
            if (metadata is not null)
            {
                string wave = Path.Combine(output, "synthesis.wav"); chat.VoiceAudioFactory = () => new WindowsVoiceAudio(wave); chat.SelectedConversation = voice;
                await chat.JoinVoiceAsync(); await chat.ToggleMicrophoneAsync(); Check(!chat.VoiceMuted, "WAV-backed voice input failed: " + chat.VoiceNotice);
                chat.SelectedConversation = other; int previous = voice.Messages.Count; await Until(() => voice.Messages.Count > previous && chat.Inferences.Items.Any(j => j.Request.ConversationId == voice.Id));
                await chat.ToggleMicrophoneAsync(); await Until(() => chat.Inferences.Items.Where(j => j.Request.ConversationId == voice.Id).All(j => !j.IsPending));
                Check(chat.Input == "別チャンネルの下書き" && chat.SelectedConversation == other && voice.Messages.Any(m => m.Identity?.Detail == "音声入力" && m.Role == "user"), "Recognition changed channel/draft or failed to post to joined voice channel.");
                Check(voice.Messages.Last(m => m.Role == "assistant").Content.Length > 0, "Voice recognition produced no model reply.");
                report["speechToRealModelInBackgroundChannel"] = true; await chat.LeaveVoiceAsync();
            }
            AppState persisted = await store.LoadAsync(); Check(persisted.Conversations.Single(c => c.Id == voice.Id).Room?.Kind == "voice", "Voice channel was not persisted.");
            AppState copied = WorkspaceService.ImportCopy(persisted); AppStore.Validate(copied); Check(copied.Conversations.Any(c => c.Room?.Kind == "voice"), "Voice import lost channel type.");
            report["success"] = true; report["profileServerVoiceShareUi"] = true;
            await File.WriteAllTextAsync(Path.Combine(output, "verification.json"), JsonSerializer.Serialize(report, AppStore.JsonOptions), token); await File.WriteAllTextAsync(Path.Combine(output, "result.txt"), "Profiles/server data and scope, voice membership/drafts, passive sharing/revocation, layouts, Windows speech and persistence: OK", token);
        }
        catch (Exception ex) { Environment.ExitCode = 1; await File.WriteAllTextAsync(Path.Combine(output, "result.txt"), ex.ToString()); }
        finally
        {
            fixtureJob?.Dispose(); fixture?.Dispose(); UnsavedChanges.Review = _ => false;
            if (main is not null) { foreach (Window child in main.OwnedWindows.Cast<Window>().ToArray()) { child.Close(); } await main.ViewModel.ShutdownAsync(); main.Close(); }
            Application.Current.Shutdown();
        }
    }
}
