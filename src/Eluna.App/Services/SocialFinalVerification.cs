using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using Eluna.App.Models;

namespace Eluna.App.Services;

internal static class SocialFinalVerification
{
    private static void Check(bool ok, string message) { if (!ok) { throw new InvalidOperationException(message); } }
    internal static async Task RunAsync(AppStore store, string output, string metadata)
    {
        Directory.CreateDirectory(output); MainWindow? main = null; using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(14)); CancellationToken token = deadline.Token;
        var evidence = new List<string>();
        async Task Until(Func<bool> ready) { while (!ready()) { await Task.Delay(50, token); } }
        async Task Stage(string stage) => await File.WriteAllTextAsync(Path.Combine(output, "progress.json"), JsonSerializer.Serialize(new { stage, evidence }, AppStore.JsonOptions), token);
        try
        {
            UnsavedChanges.Review = _ => false;
            LocalModel model = JsonSerializer.Deserialize<LocalModel>(await File.ReadAllTextAsync(metadata, token), AppStore.JsonOptions)!;
            model = model with { Preferences = new(ContextSize: 8192, Temperature: .6, MaxOutputTokens: 1024, DefaultMode: "fast") };
            string folder = Path.Combine(store.ModelsDirectory, model.Id); Directory.CreateDirectory(folder); await File.WriteAllTextAsync(Path.Combine(folder, "model.json"), JsonSerializer.Serialize(model, AppStore.JsonOptions), token);
            main = new(store) { WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = -20000 }; main.Show(); var chat = main.ViewModel;
            await Until(() => chat.IsReady || chat.HasError); Check(chat.IsReady, chat.Error); chat.SelectedModel = model;
            await chat.ApplySettingsAsync(chat.Settings with { WorkMode = "offline", InferenceMode = "fast", ContextSize = 8192, Compute = chat.ComputeChoices.Any(c => c.Id == "Vulkan0") ? "Vulkan0" : "cpu", Logs = new("detailed", true, 14, 128), DeveloperMode = true, Updates = new("develop", false) });
            foreach (Conversation starter in chat.Conversations.Where(c => c.Room?.Social?.Spontaneous == true).ToArray()) { chat.StopSocialConversation(starter.Id); }
            await Stage("character"); CharacterDraft character = await chat.GenerateCharacterAsync("落ち着いて正確な天文学好きの友人。短い名前と控えめな温度設定。", model.Id, token);
            Check(character.Generation is { Temperature: >= .2 and <= 1.2, TopP: >= .8 and <= 1, MaxOutputTokens: >= 512 and <= 2048 }, "Character generation did not propose valid editable sampling preferences.");
            await File.WriteAllTextAsync(Path.Combine(output, "character.json"), JsonSerializer.Serialize(character, AppStore.JsonOptions), token); evidence.Add("Actual model generated character plus valid temperature/top-p/output suggestions.");
            var actor = new AgentAccount(Guid.NewGuid(), "シオ", "🌙", "月や星の小話を同じ場の友人として1〜2文で話す。誰の発言もない場でも、自分から今晩の月の話題を自然に切り出す。", "天文と短い会話", model.Id, Generation: new(.7, .95, 768), Social: new("月 星 天文", 1, true, 5)); await chat.SaveAgentAsync(actor);
            Conversation room = await chat.CreateRoomAsync("星空ラウンジ", "channel", [actor.Id]);
            await chat.UpdateRoomAsync(room, room.Title, room.Room! with { Social = new(true, true, true, 0, 0, 1, 1, 1024, 1), WorkMode = "offline", InferenceMode = "fast" }, false);
            chat.SocialDecisionObserved += (id, decision) => File.WriteAllText(Path.Combine(output, "decision-" + id.ToString("N") + ".json"), decision);
            await Stage("waiting-for-spontaneous-timer"); int count = chat.Inferences.Items.Count;
            await Until(() => chat.Inferences.Items.Skip(count).Any(j => j.Request.ConversationId == room.Id && j.Request.Social?.Initiation == true));
            InferenceJob spontaneous = chat.Inferences.Items.Last(j => j.Request.ConversationId == room.Id); await spontaneous.Finished.WaitAsync(token);
            Check(spontaneous.State == "complete" && room.Messages.Any(m => !m.Pending && m.Identity?.AgentId == actor.Id && m.Content.Length > 0) && room.Messages.All(m => m.Role != "user"), "The live timer did not initiate a completed account post without a human message: " + spontaneous.Detail);
            chat.StopSocialConversation(room.Id); await PrototypePreview.RenderAsync(main, Path.Combine(output, "spontaneous.png")); evidence.Add("Real 1-minute app timer initiated a completed peer post without a human message; saved stop prevents continuation.");
            chat.ResumeSocialConversation(room.Id); await chat.UpdateRoomAsync(room, room.Title, room.Room! with { Social = room.Room!.Social! with { Spontaneous = false } }, false);
            chat.SelectedRecipient = chat.Recipients.Single(r => r.Id == actor.Id); count = chat.Inferences.Items.Count; chat.Input = "@シオ 今の話に👍の短いリアクションだけ付けて。説明文は不要。"; chat.SendCommand.Execute(null); await Until(() => chat.Inferences.Items.Count > count); InferenceJob reaction = chat.Inferences.Items.Last(); await reaction.Finished.WaitAsync(token);
            Check(reaction.State == "complete" && (room.Experience.Reactions?.Any(r => r.Actor == actor.Id) == true || room.Messages.Any(m => !m.Pending && m.Identity?.AgentId == actor.Id && m.Content.Trim() is "👍" or "👍🏻")), "Model did not produce a short reaction: " + reaction.Detail); chat.StopSocialConversation(room.Id); evidence.Add("Actual model produced a short emoji reaction without an explanatory answer.");
            await Stage("web-knowledge"); Conversation research = await chat.CreateRoomAsync("資料から話す", "dm", [actor.Id]); await chat.UpdateRoomAsync(research, research.Title, research.Room! with { Social = new(false), WorkMode = "search", InferenceMode = "fast" }, false);
            await chat.ApplySettingsAsync(chat.Settings with { WorkMode = "search", WorkOptions = new(SearchLimit: 2, PageLimit: 3, ParallelPages: 2, SecondsLimit: 240, TokenLimit: 4000) });
            count = chat.Inferences.Items.Count; chat.Input = "富士山の成り立ちと最後の噴火についてWeb検索して、会話として短く説明して。"; chat.SendCommand.Execute(null); await Until(() => chat.Inferences.Items.Count > count); InferenceJob web = chat.Inferences.Items.Last(); await web.Finished.WaitAsync(token); ChatMessage answer = research.Messages.Single(m => m.Id == web.Request.AnswerId);
            await File.WriteAllTextAsync(Path.Combine(output, "web-message.json"), JsonSerializer.Serialize(answer.Save(), AppStore.JsonOptions), token);
            Check(web.State == "complete" && answer.Work?.Sources.Count > 0 && answer.Content.Length > 20, "Live web/model generation did not produce grounded knowledge: " + web.Detail);
            Check(!new[] { "資料に基づく説明", "本文に存在する引用", "出典の警告", "取得失敗:" }.Any(s => answer.Content.Contains(s, StringComparison.Ordinal)) && !Regex.IsMatch(answer.Content, @"\[\d+\]"), "Page footnotes or internal report/technical boilerplate leaked into the message.");
            await chat.Trace.FlushAsync(); var rows = await chat.Trace.ReadAsync(web.Request.Id);
            Check(rows.Any(r => r.Area == "model" && r.Event == "input") && rows.Any(r => r.Area == "model" && r.Event == "output") && rows.Any(r => r.Area == "web"), "Live model/tool I/O was not correlated with the completed message.");
            await chat.Trace.ExportAsync(Path.Combine(output, "live-operation-logs.zip"), token); await PrototypePreview.RenderAsync(main, Path.Combine(output, "web-conversation.png")); evidence.Add(answer.Content.Contains("[S", StringComparison.Ordinal) ? "Live search/source fetch and local synthesis used app citations without page footnotes/report boilerplate; model/tool diagnostics correlated and exported." : "Live search/source fetch completed; ungrounded model claims were withheld with concise uncertainty, without page footnotes/report boilerplate; model/tool diagnostics correlated and exported.");
            await chat.PersistSafelyAsync(); AppState state = await store.LoadAsync(); AppStore.Validate(state); Check(state.SchemaVersion == 5 && state.Settings.Updates?.Train == "develop", "Final settings/schema did not persist.");
            await File.WriteAllTextAsync(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new { success = true, evidence }, AppStore.JsonOptions), token);
        }
        catch (Exception e) { Environment.ExitCode = 1; await File.WriteAllTextAsync(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new { success = false, error = e.ToString(), evidence }, AppStore.JsonOptions)); }
        finally { if (main is not null) { await main.ViewModel.ShutdownAsync(); main.Close(); } Application.Current.Shutdown(); }
    }
}
