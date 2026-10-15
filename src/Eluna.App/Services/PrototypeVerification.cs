using System.IO;
using System.Text;
using System.Text.Json;
using Eluna.App.Models;
using Eluna.App.ViewModels;

namespace Eluna.App.Services;

internal static class PrototypeVerification
{
    public static async Task RunAsync(AppStore store, string metadataPath, string resultPath, bool verifyWorkflows = false, bool workflowsOnly = false, bool modelManagement = false, string? debateMetadata = null, bool interruption = false, bool searchRouting = false)
    {
        var report = new Dictionary<string, object>();
        try
        {
            LocalModel model = JsonSerializer.Deserialize<LocalModel>(await File.ReadAllTextAsync(metadataPath), AppStore.JsonOptions)
                ?? throw new InvalidDataException("Model metadata is missing.");
            if (searchRouting)
            { using var routingTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(6)); report["searchRouting"] = await CheckSearchRoutingAsync(store, model, routingTimeout.Token); report["success"] = true; return; }
            if (interruption)
            { using var interruptionTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(4)); await CheckInterruptionAsync(store, model, interruptionTimeout.Token); report["success"] = true; return; }
            if (modelManagement)
            { using var modelTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(4)); report["modelManagement"] = await CheckModelManagementAsync(store, model, modelTimeout.Token); report["success"] = true; return; }
            if (workflowsOnly)
            {
                string modelDirectory = Path.Combine(store.ModelsDirectory, model.Id); Directory.CreateDirectory(modelDirectory);
                await File.WriteAllTextAsync(Path.Combine(modelDirectory, "model.json"), JsonSerializer.Serialize(model, AppStore.JsonOptions));
                using var workflowTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(8));
                LocalModel? secondary = debateMetadata is null ? null : JsonSerializer.Deserialize<LocalModel>(await File.ReadAllTextAsync(debateMetadata), AppStore.JsonOptions);
                if (secondary is not null) { string secondaryDirectory = Path.Combine(store.ModelsDirectory, secondary.Id); Directory.CreateDirectory(secondaryDirectory); await File.WriteAllTextAsync(Path.Combine(secondaryDirectory, "model.json"), JsonSerializer.Serialize(secondary, AppStore.JsonOptions)); }
                report["researchWorkflows"] = await CheckWorkAsync(store, model, workflowTimeout.Token, secondary); report["success"] = true; return;
            }
            using var llama = new LlamaService(store);
            await llama.InitializeAsync();
            report["runtimeDirectory"] = llama.BinaryDirectory!;
            report["devices"] = llama.Devices;
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(8));
            await llama.EnsureModelAsync(model, new(model.Id), _ => { }, timeout.Token);
            var answer = new StringBuilder();
            await foreach (GenerationUpdate text in llama.GenerateAsync([new("user", "2 + 3 の答えを数値だけ答えてください。")], InferenceModes.Find("fast"), timeout.Token))
            {
                answer.Append(text.Content);
            }
            if (answer.Length == 0) { throw new InvalidOperationException("Bundled engine returned an empty response."); }
            report["firstCompute"] = llama.ComputeLabel;
            report["answer"] = answer.ToString();
            var modes = new List<object>();
            foreach (InferenceMode mode in InferenceModes.All.Where(m => m.Id != "fast"))
            {
                int reasoningCharacters = 0;
                answer.Clear();
                await foreach (GenerationUpdate piece in llama.GenerateAsync([new("user", "17×23を計算し、答えの数値だけを書いてください。")], mode, timeout.Token))
                {
                    reasoningCharacters += piece.Reasoning.Length;
                    answer.Append(piece.Content);
                }
                if (reasoningCharacters == 0 || answer.Length == 0 || answer.ToString().Contains("<think>", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException($"Bundled {mode.Id} reasoning or answer separation failed.");
                }
                modes.Add(new { mode = mode.Id, reasoningCharacters, answer = answer.ToString() });
            }
            report["inferenceModes"] = modes;
            using var stop = new CancellationTokenSource();
            bool canceled = false;
            try
            {
                await foreach (GenerationUpdate piece in llama.GenerateAsync([new("user", "長い数学の問題を作り、よく考えて解いてください。")], InferenceModes.Find("thorough"), stop.Token))
                {
                    if (piece.Reasoning.Length > 0) { stop.Cancel(); }
                }
            }
            catch (OperationCanceledException) { canceled = true; }
            if (!canceled) { throw new InvalidOperationException("Bundled reasoning did not stop."); }
            report["thinkingCancellation"] = true;
            await llama.EnsureModelAsync(model, new(model.Id, "cpu"), _ => { }, timeout.Token);
            answer.Clear();
            await foreach (GenerationUpdate text in llama.GenerateAsync([new("user", "こんにちはとだけ答えてください。")], InferenceModes.Find("standard"), timeout.Token)) { answer.Append(text.Content); }
            if (answer.Length == 0 || llama.ComputeLabel != "CPU") { throw new InvalidOperationException("Bundled CPU engine failed."); }
            report["cpuAnswer"] = answer.ToString();
            llama.Stop();
            report["sessionKeysRemoved"] = !Directory.EnumerateFiles(Path.Combine(store.Root, "Sessions"), "*.key").Any();
            await CheckChatAsync(store, model, timeout.Token);
            report["chatStateAndModePersistence"] = true;
            if (verifyWorkflows) { report["researchWorkflows"] = await CheckWorkAsync(store, model, timeout.Token); }
            report["success"] = true;
        }
        catch (Exception ex)
        {
            report["success"] = false;
            report["error"] = ex.ToString();
            Environment.ExitCode = 1;
        }
        finally
        {
            await File.WriteAllTextAsync(resultPath, JsonSerializer.Serialize(report, AppStore.JsonOptions));
            System.Windows.Application.Current.Shutdown(Environment.ExitCode);
        }
    }

    private static async Task<object> CheckSearchRoutingAsync(AppStore store, LocalModel model, CancellationToken token)
    {
        string directory = Path.Combine(store.ModelsDirectory, model.Id); Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, "model.json"), JsonSerializer.Serialize(model, AppStore.JsonOptions), token);
        var decisions = new List<object>();
        using (var llama = new LlamaService(store))
        {
            await llama.InitializeAsync();
            await llama.EnsureModelAsync(model, new(model.Id), _ => { }, token);
            foreach ((string question, bool expected) in new[] { ("光合成の仕組みを初心者向けに説明して", false), ("短いおとぎ話を書いてください", false), ("めまいが続いています。どうすればいいですか？", true), ("初心者向けのノートパソコンをおすすめして", true), ("C# WPFでアプリを作る環境構築の手順を教えて", true) })
            {
                SearchDecision decision = await SearchRouter.DecideAsync("chat", [new("user", question)], async (input, cancellation) =>
                {
                    var result = new StringBuilder();
                    await foreach (GenerationUpdate piece in llama.GenerateAsync([new("user", input)], InferenceModes.Find("fast"), cancellation, SearchRouter.Instruction, 32, SearchRouter.Schema)) { result.Append(piece.Content); }
                    return result.ToString();
                }, token);
                decisions.Add(new { question, decision.Search, decision.Reason, expected });
                if (decision.Search != expected) { throw new InvalidOperationException("Actual model routed the sample incorrectly: " + question + " / " + decision.Reason); }
            }
        }
        using var chat = new ChatViewModel(store);
        await chat.InitializeAsync();
        await chat.ApplySettingsAsync(chat.Settings with { WorkMode = "chat", WorkOptions = new(SearchLimit: 1, PageLimit: 2, SecondsLimit: 110, TokenLimit: 4000) });
        chat.SelectedMode = InferenceModes.Find("fast");
        async Task<ChatMessage> Send(string question, bool stopAtSearch = false)
        {
            chat.NewChat(); chat.Input = question; chat.SendCommand.Execute(null);
            while (chat.IsBusy)
            {
                token.ThrowIfCancellationRequested();
                if (chat.SendCommand.CanExecute(null) || chat.CanChange) { throw new InvalidOperationException("Routing allowed duplicate send or mode changes."); }
                if (stopAtSearch && chat.SelectedConversation!.Messages.Last().Work?.SearchesUsed > 0) { chat.StopCommand.Execute(null); }
                await Task.Delay(20, token);
            }
            if (chat.HasError) { throw new InvalidOperationException(chat.Error); }
            return chat.SelectedConversation!.Messages.Last();
        }
        ChatMessage plain = await Send("2 + 3 の答えを数値だけ答えてください。");
        if (plain.Work is not null || plain.Content.Length == 0) { throw new InvalidOperationException("Ordinary arithmetic used the web or failed."); }
        ChatMessage searched = await Send("llama.cppのWindowsでのVulkan対応について、公式情報をもとに短く説明してください。");
        WorkSession work = searched.Work ?? throw new InvalidOperationException("Default chat did not automatically search.");
        if (!work.AutomaticSearch || work.SearchesUsed != 1 || work.Stage != "complete" || searched.Content.Length == 0 || chat.SelectedWorkMode.Id != "chat") { throw new InvalidOperationException("Automatic search did not complete or changed composer mode."); }
        // Start regeneration and stop after search begins. Routing, cancellation and saved work share one busy interval.
        Task regenerate = chat.RegenerateAsync(searched);
        while (chat.IsBusy)
        {
            token.ThrowIfCancellationRequested();
            if (chat.SelectedConversation!.Messages.Last().Work?.SearchesUsed > 0) { chat.StopCommand.Execute(null); }
            await Task.Delay(20, token);
        }
        await regenerate;
        if (chat.SelectedWorkMode.Id != "chat" || chat.SelectedConversation!.Messages.Last().Work is not { AutomaticSearch: true, Stage: "paused" }) { throw new InvalidOperationException("Automatic regeneration lost its mode or did not stop."); }
        ChatMessage explicitRequest = await Send("17×23の計算方法を検索してください。", true);
        if (explicitRequest.Work?.SearchesUsed != 1 || explicitRequest.Work.Stage != "paused") { throw new InvalidOperationException("Explicit request did not start a search or stop."); }
        chat.SelectedWorkMode = WorkModes.Find("search");
        ChatMessage forced = await Send("こんにちは", true);
        if (forced.Work?.SearchesUsed != 1 || forced.Work.AutomaticSearch) { throw new InvalidOperationException("Forced search mode depended on routing."); }
        chat.SelectedWorkMode = WorkModes.Find("offline");
        ChatMessage offline = await Send("最新の情報を検索して。短く回答してください。");
        if (offline.Work is not null || offline.Content.Length == 0) { throw new InvalidOperationException("No-search mode was bypassed."); }
        await chat.PersistSafelyAsync();
        AppState saved = await store.LoadAsync();
        if (!saved.Conversations.SelectMany(c => c.Branches?.SelectMany(b => b.Messages) ?? c.Messages).Any(m => m.Work?.AutomaticSearch == true) || saved.Settings.WorkMode != "offline")
        { throw new InvalidOperationException("Auto-search history or no-search preference was not saved."); }
        return new { decisions, autoSearch = new { work.SearchesUsed, work.PagesUsed, sources = work.Sources.Count, work.Stage }, ordinaryChat = plain.Content, forcedSearch = true, explicitRequest = true, stopAndRegenerate = true, offline = true, persistence = true };
    }

    private static async Task CheckChatAsync(AppStore store, LocalModel model, CancellationToken token)
    {
        string directory = Path.Combine(store.ModelsDirectory, model.Id);
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, "model.json"), JsonSerializer.Serialize(model, AppStore.JsonOptions), token);
        using var chat = new ChatViewModel(store);
        await chat.InitializeAsync();
        chat.SelectedWorkMode = WorkModes.Find("offline");
        chat.SelectedMode = InferenceModes.Find("standard");
        chat.Input = "17×23を計算し、答えの数値だけを書いてください。";
        chat.SendCommand.Execute(null);
        bool sawThinking = false;
        while (chat.IsBusy)
        {
            token.ThrowIfCancellationRequested();
            sawThinking |= chat.SelectedConversation!.Messages.Last().IsThinking;
            chat.SelectedMode = InferenceModes.Find("fast");
            if (chat.SelectedMode.Id != "fast" || chat.CanChange)
            {
                throw new InvalidOperationException("Future-request mode could not change independently of the active request.");
            }
            await Task.Delay(20, token);
        }
        ChatMessage answer = chat.SelectedConversation!.Messages.Last();
        if (chat.HasError || !sawThinking || answer.Mode != "standard" || answer.Content.Length == 0 || answer.ThinkingSeconds is null || answer.IsGenerating)
        {
            throw new InvalidOperationException("Chat thinking-to-answer state failed: " + chat.Error);
        }
        chat.SelectedMode = InferenceModes.Find("thorough");
        chat.Input = "長い数学の問題を作り、よく考えて解いてください。";
        chat.SendCommand.Execute(null);
        bool stopped = false;
        while (chat.IsBusy)
        {
            token.ThrowIfCancellationRequested();
            if (chat.SelectedConversation.Messages.Last().IsThinking)
            {
                chat.StopCommand.Execute(null);
                stopped = true;
            }
            await Task.Delay(20, token);
        }
        if (!stopped || chat.SelectedConversation.Messages.Last().Notice != "生成を停止しました")
        {
            throw new InvalidOperationException("Chat could not stop during thinking.");
        }
        chat.SelectedMode = InferenceModes.Find("fast");
        await chat.PersistSafelyAsync();
        Conversation conversation = chat.SelectedConversation;
        ConversationBranch original = conversation.ActiveBranch;
        string originalText = answer.Content;
        await chat.RegenerateAsync(answer);
        if (chat.HasError || conversation.Branches.Count < 2 || original.Messages[1].Content != originalText || conversation.Messages.Last().Content.Length == 0)
        {
            throw new InvalidOperationException("Real regenerate or branch preservation failed: " + chat.Error);
        }
        await chat.EditAndGenerateAsync(conversation.Messages[0], "2 + 3 の答えを数値だけ答えてください。");
        if (chat.HasError || conversation.Messages[0].Content.StartsWith("17", StringComparison.Ordinal) || original.Messages[0].Content.StartsWith("2", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Real question editing modified the original branch: " + chat.Error);
        }
        await chat.ContinueAsync(conversation.Messages.Last());
        if (chat.HasError || conversation.Messages.Last().Role != "assistant") { throw new InvalidOperationException("Real continuation failed: " + chat.Error); }
        chat.Input = "下書きの復元テスト";
        chat.NewChat();
        chat.SelectedConversation = conversation;
        if (chat.Input != "下書きの復元テスト") { throw new InvalidOperationException("Switching chats discarded the draft."); }
        chat.HistorySearch = "17×23";
        if (!chat.HistoryView.Cast<Conversation>().Contains(conversation)) { throw new InvalidOperationException("Full-text history search missed an old branch."); }
        chat.HistorySearch = "";
        await chat.TogglePinAsync(conversation);
        await chat.ToggleArchiveAsync(conversation);
        if (chat.HistoryView.Cast<Conversation>().Contains(conversation)) { throw new InvalidOperationException("Archive filter failed."); }
        chat.ShowArchived = true;
        if (!chat.HistoryView.Cast<Conversation>().Contains(conversation)) { throw new InvalidOperationException("Archived conversation could not be found."); }
        chat.ShowArchived = false;
        await chat.ToggleArchiveAsync(conversation);
        await chat.PersistSafelyAsync();
        AppState state = await store.LoadAsync();
        if (state.Settings.InferenceMode != "fast" || !state.Conversations.SelectMany(c => c.Branches?.SelectMany(b => b.Messages) ?? c.Messages).Any(m => m.Mode == "standard" && m.ThinkingSeconds is not null))
        {
            throw new InvalidOperationException("Chat mode or completed thought summary was not saved.");
        }
        chat.NewChat();
        Conversation longChat = chat.SelectedConversation!;
        string oldText = string.Concat(Enumerable.Repeat("要件はローカル処理、Windows専用、モデル名はエルナです。", 25));
        for (int i = 0; i < 8; i++) { longChat.Messages.Add(new() { Role = i % 2 == 0 ? "user" : "assistant", Content = oldText }); }
        chat.Input = "条件を短くまとめてください。";
        chat.SendCommand.Execute(null);
        while (chat.IsBusy) { token.ThrowIfCancellationRequested(); await Task.Delay(30, token); }
        if (chat.HasError || longChat.SummaryThrough == 0 || longChat.Summary.Length == 0 || longChat.Messages[0].Content != oldText || longChat.Messages.Count != 10)
        {
            throw new InvalidOperationException("Long-context compression changed history or failed: " + chat.Error);
        }
        chat.NewChat();
        chat.Input = "番号付きで長い物語を30段落書いてください。";
        chat.SendCommand.Execute(null);
        bool savedPartial = false;
        while (chat.IsBusy)
        {
            token.ThrowIfCancellationRequested();
            AppState checkpoint = await store.LoadAsync();
            SavedMessage? partial = checkpoint.Conversations.FirstOrDefault(c => c.Id == chat.SelectedConversation!.Id)?.Messages.LastOrDefault();
            if (partial is { Incomplete: true, Content.Length: > 0 })
            {
                if (!ChatMessage.Restore(partial).HasNotice) { throw new InvalidOperationException("Interrupted checkpoint was not recoverable."); }
                savedPartial = true; chat.StopCommand.Execute(null);
            }
            await Task.Delay(30, token);
        }
        if (!savedPartial) { throw new InvalidOperationException("Streaming output was not periodically saved."); }
        await chat.ShutdownAsync();
    }
    private static async Task<object> CheckWorkAsync(AppStore store, LocalModel model, CancellationToken token, LocalModel? secondary = null)
    {
        using var chat = new ChatViewModel(store); await chat.InitializeAsync(); chat.SelectedModel = chat.Models.First(m => m.Id == model.Id); chat.SelectedMode = InferenceModes.Find("standard");
        await chat.ApplySettingsAsync(chat.Settings with { ContextSize = 8192, WorkOptions = new(2, 3, 2, 120, 6000, 1) });
        if (secondary is not null)
        {
            await chat.ApplySettingsAsync(chat.Settings with
            {
                WorkOptions = new(3, 4, 2, 240, 10000, 1,
                [new("proposer", "提案役", "公式資料のWindows向けVulkanのビルド条件を挙げる。", model.Id), new("critic", "批判役", "前提条件の見落とし、資料にない断定、読み間違いを指摘する。", secondary.Id), new("verifier", "検証役", "他の役の発言を本文と照合し、事実と未確認事項を区別する。", model.Id)])
            });
        }
        chat.PlanReviewRequested += (work, token) => Task.FromResult(true);
        var results = new List<object>();
        foreach (string mode in secondary is null ? new[] { "search", "research", "debate" } : new[] { "debate" })
        {
            chat.NewChat(); chat.SelectedWorkMode = WorkModes.Find(mode); chat.Input = "llama.cppのWindowsでのVulkan対応について、公式情報をもとに短く説明してください。"; chat.SendCommand.Execute(null);
            while (chat.IsBusy) { token.ThrowIfCancellationRequested(); await Task.Delay(40, token); }
            ChatMessage answer = chat.SelectedConversation!.Messages.Last(); WorkSession work = answer.Work!;
            if (chat.HasError || work.Stage != "complete" || work.Sources.Count == 0 || answer.Content.Length < 20 || work.SearchesUsed > work.Options.SearchLimit || work.PagesUsed > work.Options.PageLimit || work.TokensUsed > work.Options.TokenLimit || mode == "debate" && work.Turns.Count < 2)
            { throw new InvalidOperationException($"Real {mode} failed: {chat.Error}; {work.Stage}; {work.Budget}; sources={work.Sources.Count}; turns={work.Turns.Count}"); }
            if (secondary is not null && (!work.Turns.Any(t => t.ModelId == secondary.Id) || !work.Turns.Any(t => t.ModelId == model.Id) || work.Turns.Count != 3)) { throw new InvalidOperationException("The assigned real debate models were not used for all roles."); }
            results.Add(new { mode, work.Stage, sources = work.Sources.Select(s => new { s.Number, s.Url }), work.SearchesUsed, work.PagesUsed, work.TokensUsed, work.SecondsUsed, turns = work.Turns.Count, roleModels = work.Turns.Select(t => new { t.Role, t.ModelId }), matchedQuotes = work.Claims.Count, answer = answer.Content });
        }
        await chat.ShutdownAsync(); return results;
    }
    private static async Task CheckInterruptionAsync(AppStore store, LocalModel model, CancellationToken token)
    {
        string directory = Path.Combine(store.ModelsDirectory, model.Id); Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, "model.json"), JsonSerializer.Serialize(model, AppStore.JsonOptions), token);
        using var chat = new ChatViewModel(store); await chat.InitializeAsync(); chat.SelectedMode = InferenceModes.Find("fast");
        chat.Input = "日本語で長い物語を100段落書いてください。各段落は番号付きで、登場人物の会話を詳しく描いてください。"; chat.SendCommand.Execute(null);
        while (chat.IsBusy) { await Task.Delay(50, token); }
        if (chat.HasError) { throw new InvalidOperationException(chat.Error); }
        await chat.ShutdownAsync();
    }
    public static async Task CheckRecoveryAsync(AppStore store)
    {
        var report = new Dictionary<string, object>();
        try
        {
            AppState before = await store.LoadAsync(); SavedMessage interrupted = before.Conversations.SelectMany(c => c.Messages).Last(m => m.Incomplete && m.Content.Length > 0);
            using var chat = new ChatViewModel(store); await chat.InitializeAsync();
            ChatMessage restored = chat.Conversations.SelectMany(c => c.Messages).Single(m => m.Id == interrupted.Id);
            if (restored.Content != interrupted.Content || !restored.HasNotice || restored.IsGenerating || !chat.IsReady) { throw new InvalidOperationException("Interrupted output was not restored on a fresh app startup."); }
            report["recoveredCharacters"] = restored.Content.Length; report["recoveryNotice"] = restored.Notice; report["readyForNextMessage"] = chat.IsReady; report["success"] = true; await chat.ShutdownAsync();
        }
        catch (Exception ex) { report["success"] = false; report["error"] = ex.ToString(); Environment.ExitCode = 1; }
        finally { await File.WriteAllTextAsync(Path.Combine(store.Root, "recovery.json"), JsonSerializer.Serialize(report, AppStore.JsonOptions)); System.Windows.Application.Current.Shutdown(Environment.ExitCode); }
    }
    private static async Task<object> CheckModelManagementAsync(AppStore store, LocalModel source, CancellationToken token)
    {
        using var catalog = new ModelCatalog(store); LocalModel imported = await catalog.ImportAsync(source.FilePath, null, token);
        if (imported.Profile is not { Parameters: > 0, Quantization: "Q4_K_M", SupportsThinking: true } || imported.Profile.Architecture.Length == 0)
        { throw new InvalidOperationException("Real GGUF metadata was not read correctly."); }
        var service = new ModelsService(store); var preferences = new ModelPreferences(8192, 0.2, 0.9, 20, 1.05, 512, "fast");
        await service.UpdateAsync(imported with { Preferences = preferences, IsFavorite = true, DisplayName = "移動の検証" }, token);
        using var chat = new ChatViewModel(store); await chat.InitializeAsync(); chat.SelectedMode = InferenceModes.Find("fast");
        async Task Generate()
        {
            chat.NewChat();
            chat.Input = "2 + 3の答えを数値だけ書いてください。"; chat.SendCommand.Execute(null);
            while (chat.IsBusy) { await Task.Delay(30, token); }
            if (chat.HasError || !chat.SelectedConversation!.Messages.Last().Content.Contains('5') || chat.EffectiveContext != 8192 || !chat.Metrics.Contains("8,192", StringComparison.Ordinal))
            { throw new InvalidOperationException("Model-specific context or generation failed: " + chat.Error + "; context=" + chat.EffectiveContext + "; metrics=" + chat.Metrics + "; answer=" + chat.SelectedConversation!.Messages.Last().Content); }
        }
        await Generate();
        int messages = chat.SelectedConversation!.Messages.Count;
        Task busy = chat.ManageModelsAsync(t => Task.Delay(150, t));
        chat.Input = "管理中の二重実行"; chat.SendCommand.Execute(null);
        if (!chat.IsBusy || chat.CanGenerate || chat.SelectedConversation.Messages.Count != messages) { throw new InvalidOperationException("Generation began during model management."); }
        await busy;
        string newLocation = Path.Combine(store.Root, "relocated-models");
        await chat.ManageModelsAsync(async t => { chat.ReleaseModel(); await service.MoveDirectoryAsync(newLocation, null, t); });
        if (chat.SelectedModel?.FilePath.StartsWith(newLocation, StringComparison.OrdinalIgnoreCase) != true || chat.SelectedModel.Options != preferences || !chat.SelectedModel.IsFavorite)
        { throw new InvalidOperationException("Relocation did not preserve the selected model or presets."); }
        await Generate(); await chat.ShutdownAsync();
        return new { profile = imported.Profile, preferences, relocated = true, generationAfterRelocation = true, managementBlocksGeneration = true };
    }
}
