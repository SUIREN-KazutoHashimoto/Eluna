using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Eluna.App.Models;
using Eluna.App.Services;

namespace Eluna.Checks;

internal static class Program
{
    private static void Check(bool condition, string message)
    {
        if (!condition) { throw new InvalidOperationException(message); }
    }

    private static async Task<int> Main(string[] args)
    {
        string root = Path.GetFullPath(args.LastOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal)) ?? "artifacts/checks");
        Directory.CreateDirectory(root);
        try
        {
            await CheckStorageAsync(Path.Combine(root, "storage-" + Guid.NewGuid().ToString("N")));
            await CheckEventsAsync();
            await ComputerUseChecks.RunAsync();
            await WorkspaceChecks.RunAsync(Path.Combine(root, "workspace-" + Guid.NewGuid().ToString("N")));
            await AgentFeaturesChecks.RunAsync(Path.Combine(root, "agents-" + Guid.NewGuid().ToString("N")));
            await SocialChatChecks.RunAsync(Path.Combine(root, "social-" + Guid.NewGuid().ToString("N")));
            await VoiceChannelChecks.RunAsync(Path.Combine(root, "voice-" + Guid.NewGuid().ToString("N")));
            await InferenceQueueChecks.RunAsync(Path.Combine(root, "inference-queue-" + Guid.NewGuid().ToString("N")));
            await SkillChecks.RunAsync(Path.Combine(root, "skills-" + Guid.NewGuid().ToString("N")));
            await LicenseChecks.RunAsync();
            await KnowledgeChecks.RunAsync(Path.Combine(root, "knowledge-" + Guid.NewGuid().ToString("N")));
            CheckInferenceModes();
            CheckMarkdown();
            WindowPlacementChecks.Run();
            await BackupChecks.RunAsync(Path.Combine(root, "backup-" + Guid.NewGuid().ToString("N")));
            await QueueStabilityChecks.RunAsync();
            await RemainingFeatureChecks.RunAsync(Path.Combine(root, "remaining-" + Guid.NewGuid().ToString("N")));
            await CheckBranchesAndBackupsAsync(Path.Combine(root, "branches-" + Guid.NewGuid().ToString("N")));
            CheckWebParsers();
            await SearchRoutingChecks.RunAsync();
            await MediaChecks.RunAsync(Path.Combine(root, "media-" + Guid.NewGuid().ToString("N")));
            CheckUpdates();
            await CheckUpdateNetworkStatesAsync();
            await CheckWorkflowsAsync(Path.Combine(root, "workflows-" + Guid.NewGuid().ToString("N")));
            await CheckDownloadsAsync(Path.Combine(root, "downloads-" + Guid.NewGuid().ToString("N")));
            await CheckSplitModelsAsync(Path.Combine(root, "split-models-" + Guid.NewGuid().ToString("N")));
            await ModelManagementChecks.RunAsync(Path.Combine(root, "model-management-" + Guid.NewGuid().ToString("N")));
            if (args.Contains("--integration"))
            {
                await CheckRealModelAsync(Path.Combine(root, "real-model"));
            }
            if (args.Contains("--web-integration")) { await CheckWebIntegrationAsync(); }
            if (args.Contains("--download-research-model")) { await DownloadResearchModelAsync(Path.Combine(root, "research-model"), "2B"); }
            if (args.Contains("--download-research-model4b")) { await DownloadResearchModelAsync(Path.Combine(root, "research-model"), "4B"); }
            if (args.Contains("--split-integration")) { await CheckSplitInferenceAsync(root); }
            if (args.Contains("--history-scale")) { await CheckHistoryScaleAsync(Path.Combine(root, "history-scale")); }
            Console.WriteLine("All prototype checks passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static async Task CheckHistoryScaleAsync(string root)
    {
        string body = string.Concat(Enumerable.Repeat("長い履歴の確認です。", 60));
        List<SavedConversation> conversations = Enumerable.Range(0, 20).Select(c =>
        {
            var conversation = new Conversation { Title = "大量履歴 " + c };
            for (int i = 0; i < 250; i++) { conversation.Messages.Add(new() { Role = i % 2 == 0 ? "user" : "assistant", Content = i + " " + body }); }
            return conversation.Save();
        }).ToList();
        var state = new AppState(1, new(), conversations); var store = new AppStore(root); var clock = System.Diagnostics.Stopwatch.StartNew();
        await store.SaveAsync(state); AppState restored = await store.LoadAsync(); double saveLoad = clock.Elapsed.TotalSeconds;
        string export = Path.Combine(root, "history-export.json"); clock.Restart(); await store.ExportAsync(restored, export); AppState imported = await store.ReadExportAsync(export);
        Check(imported.Conversations.Count == 20 && imported.Conversations.All(c => c.Messages.Count == 250 && c.Branches!.Single().Messages.Count == 250) &&
            imported.Conversations.Last().Messages.Last().Content == "249 " + body, "Large history save/load or export/import lost message or branch content.");
        Console.WriteLine($"5,000-message history ({new FileInfo(export).Length / 1048576.0:F1} MB), save/load {saveLoad:F2}s, export/import {clock.Elapsed.TotalSeconds:F2}s: OK");
    }

    private static async Task CheckStorageAsync(string root)
    {
        var store = new AppStore(root);
        Guid id = Guid.NewGuid();
        var expected = new AppState(1, new("test-model", "cpu", 8192, "thorough"),
            [new(id, "日本語の会話", [new("user", "こんにちは\n複数行"), new("assistant", "こんにちは。", "thorough", 4.5, "生成を停止しました")])]);
        await store.SaveAsync(expected);
        AppState restored = await new AppStore(root).LoadAsync();
        Check(restored.Conversations.Single().Id == id && restored.Conversations[0].Messages[0].Content == "こんにちは\n複数行" &&
            restored.Settings.ContextSize == 8192 && restored.Settings.InferenceMode == "thorough" &&
            restored.Conversations[0].Messages[1].ThinkingSeconds == 4.5 && restored.Conversations[0].Messages[1].Notice == "生成を停止しました",
            "Conversation, reasoning summary or settings did not survive restart.");
        await File.WriteAllTextAsync(Path.Combine(root, "state.json"), JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            settings = new { compute = "cpu", contextSize = 4096 },
            conversations = new[] { new { id, title = "既存の履歴", messages = new[] { new { role = "assistant", content = "以前の回答" } } } }
        }));
        AppState legacy = await store.LoadAsync();
        Check(legacy.Settings.InferenceMode == InferenceModes.DefaultId && legacy.Conversations[0].Messages[0].Content == "以前の回答",
            "Adding inference modes broke existing settings or history.");
        await store.SaveAsync(expected with { Settings = expected.Settings with { InferenceMode = "obsolete-mode" } });
        AppState normalized = await store.LoadAsync();
        Check(normalized.Settings.InferenceMode == InferenceModes.DefaultId && normalized.Conversations[0].Id == id,
            "Unknown inference mode discarded the user's conversations.");
        Task reader = Task.Run(async () => { for (int i = 0; i < 80; i++) { AppState snapshot = await store.LoadAsync(); Check(snapshot.Conversations.Count == 1, "Concurrent reads saw incomplete state."); } });
        for (int i = 0; i < 30; i++) { await store.SaveAsync(expected); }
        await reader;
        await File.WriteAllTextAsync(Path.Combine(root, "state.json"), "{broken");
        AppState recovered = await store.LoadAsync();
        Check(recovered.Conversations.Count == 0 && Directory.EnumerateFiles(root, "state.broken-*.json").Count() == 1,
            "Damaged history was not preserved before recovery.");
        Console.WriteLine("History, settings and damaged-data recovery: OK");
    }

    private static void CheckMarkdown()
    {
        const string sample = "# 見出し\n\n本文 **強調** と [資料](https://example.com)\n\n> 引用\n\n- 箇条書き\n\n| 項目 | 値 |\n| --- | --- |\n| A \\| B | `x|y` |\n\n```csharp\npublic int value = 42; // コメント\n```";
        IReadOnlyList<MarkdownBlock> blocks = MarkdownParser.Parse(sample);
        Check(blocks.Any(b => b.Kind == "heading" && b.Level == 1) && blocks.Any(b => b.Kind == "quote") && blocks.Any(b => b.Kind == "list"), "Markdown blocks were lost.");
        MarkdownBlock table = blocks.Single(b => b.Kind == "table");
        Check(table.Rows![1].Length == 2 && table.Rows[1][0] == "A | B" && table.Rows[1][1] == "`x|y`", "Escaped or code pipes broke a table.");
        MarkdownBlock code = blocks.Single(b => b.Kind == "code");
        IReadOnlyList<CodeToken> tokens = MarkdownParser.Highlight(code.Text, code.Language);
        Check(string.Concat(tokens.Select(t => t.Text)) == code.Text && tokens.Any(t => t.Kind == "keyword") && tokens.Any(t => t.Kind == "comment"), "Syntax highlighting modified the copied code.");
        Check(MarkdownParser.Parse("```python\nprint('途中')").Single().Text == "print('途中')", "An incomplete streamed fence lost its code.");
        Console.WriteLine("Markdown blocks, escaped tables, partial fences and lossless syntax highlighting: OK");
    }

    private static void CheckWebParsers()
    {
        List<WebResult> references = WebSearchService.ExtractLinks("forked from <a href='/ggml-org/llama.cpp'>ggml-org/llama.cpp</a><a href='http://127.0.0.1:8080/'>private</a><a href='/ggml-org/llama.cpp?utm_source=again'>same</a><a href='https://github.com/ggml-org/llama.cpp/blob/master/docs/build.md'>Build Vulkan on Windows</a>", new("https://github.com/example/fork"));
        Check(references.Count == 2 && references[0].Url == "https://github.com/ggml-org/llama.cpp" && references[1].Url.EndsWith("docs/build.md", StringComparison.Ordinal), "Reference discovery lost an original source, followed a local target or retained a duplicate.");
        string navigationHtml = string.Concat(Enumerable.Range(0, 100).Select(i => $"<a href='/navigation/{i}'>Navigation {i}</a>")) + "<article><a href='https://github.com/ggml-org/llama.cpp/blob/master/docs/build.md'>Build Vulkan on Windows</a></article>";
        Check(WebSearchService.ExtractLinks(navigationHtml, new("https://github.com/example/fork")).Single().Url.EndsWith("docs/build.md", StringComparison.Ordinal), "Navigation links hid the article's reference document.");
        Check(ResearchRunner.SourcePriority(references[0].Url, "llama.cpp Windows Vulkan") > ResearchRunner.SourcePriority("https://mirror.example/llama.cpp", "llama.cpp Windows Vulkan"), "An original repository was ranked below its mirror.");
        Check(!WebSearchService.IsPublicAddress(IPAddress.Parse("127.0.0.1")) && !WebSearchService.IsPublicAddress(IPAddress.Parse("192.168.1.10")) &&
            !WebSearchService.IsPublicAddress(IPAddress.Parse("::ffff:10.0.0.1")) && !WebSearchService.IsPublicAddress(IPAddress.Parse("fc00::1")) &&
            WebSearchService.IsPublicAddress(IPAddress.Parse("8.8.8.8")), "Nonpublic web targets were accepted.");
        List<WebResult> rss = WebSearchService.ParseBing("<rss><channel><item><title>テスト</title><link>https://example.com/page</link><description>&lt;b&gt;本文&lt;/b&gt;</description></item></channel></rss>");
        Check(rss.Single().Snippet == "本文", "RSS search results were not decoded.");
        bool externalEntity = false;
        try { WebSearchService.ParseBing("<!DOCTYPE x [<!ENTITY y SYSTEM 'file:///C:/private'>]><rss>&y;</rss>"); } catch (System.Xml.XmlException) { externalEntity = true; }
        Check(externalEntity, "Search XML enabled external entities.");
        List<WebResult> duck = WebSearchService.ParseDuckDuckGo("<a class='result-link' href='//duckduckgo.com/l/?uddg=https%3A%2F%2Fexample.com%2Fx'>A &amp; B</a>");
        Check(duck.Single().Url == "https://example.com/x" && duck[0].Title == "A & B", "Redirected search URLs were not extracted.");
        Check(WebSearchService.PlainText("<article><h1>題名</h1><script>PRIVATE_COMMAND</script><p>本文 &amp; 記号</p></article>").Contains("本文 & 記号", StringComparison.Ordinal) &&
            !WebSearchService.PlainText("<script>PRIVATE_COMMAND</script>").Contains("PRIVATE_COMMAND", StringComparison.Ordinal), "Active page content entered the extracted text.");
        Console.WriteLine("Public web targets, XML isolation, search links and HTML extraction: OK");
    }

    private static async Task CheckWebIntegrationAsync()
    {
        using var search = new WebSearchService();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        WebSearchResult result = await search.SearchAsync("llama.cpp Vulkan", 12, timeout.Token);
        Check(result.Results.Count >= 3 && result.Results.Select(r => r.Provider).Distinct().Count() >= 2, "The native metasearch did not return multiple providers: " + string.Join(';', result.Warnings));
        WebDocument page = await search.FetchAsync(new("llama.cpp", "https://raw.githubusercontent.com/ggml-org/llama.cpp/master/README.md", "", "test"), timeout.Token);
        Check(page.Text.Contains("llama.cpp", StringComparison.OrdinalIgnoreCase) && page.Text.Length > 500, "Real page text was not retrieved.");
        bool localBlocked = false;
        try { await search.FetchAsync(new("local", "http://127.0.0.1:8080/", "", "test"), timeout.Token); } catch (InvalidDataException) { localBlocked = true; }
        Check(localBlocked, "A local page was fetched as a public source.");
        Console.WriteLine($"Native web search and real source retrieval: OK ({result.Results.Count} results from {result.Results.Select(r => r.Provider).Distinct().Count()} providers)");
    }
    private static void CheckUpdates()
    {
        Check(UpdateService.Compare("v0.2.0", "0.1.0") == true && UpdateService.Compare("v0.1.0", "0.1.0") == false && UpdateService.Compare("v0.1.0.0", "0.1.0") == false &&
            UpdateService.Compare("v0.1.0", "0.2.0") == false && UpdateService.Compare("nightly", "0.1.0") is null, "Version comparison produced a false update or accepted an unknown tag.");
        Check(UpdateService.Compare("v0.1.0", "0.1.0-dev") == true && UpdateService.Compare("v0.0.9", "0.1.0-dev") == false &&
            UpdateService.Compare("v0.2.0-preview.10", "0.2.0-preview.2") == true && UpdateService.Compare("v0.2.0-preview", "0.2.0") == false,
            "The installed development version could not detect stable releases or ordered preview versions incorrectly.");
        using JsonDocument json = JsonDocument.Parse("{\"tag_name\":\"v0.2.0\",\"name\":\"正式版\",\"body\":\"変更点\",\"published_at\":null,\"html_url\":\"https://github.com/SUIREN-KazutoHashimoto/Eluna/releases/tag/v0.2.0\"}");
        ReleaseInfo info = UpdateService.Parse(json.RootElement, "0.1.0");
        Check(info.IsNewer == true && info.Notes == "変更点" && info.Published is null, "Release notes or absent date were mishandled.");
        using JsonDocument bad = JsonDocument.Parse("{\"tag_name\":\"v0.2.0\",\"html_url\":\"https://invented.example/Eluna.exe\"}"); bool rejected = false;
        try { UpdateService.Parse(bad.RootElement, "0.1.0"); } catch (JsonException) { rejected = true; }
        Check(rejected, "Update links outside the official release repository were accepted.");
        Console.WriteLine("Version comparison, unknown versions, release notes and official update links: OK");
    }
    private static async Task CheckUpdateNetworkStatesAsync()
    {
        using var noneHttp = new HttpClient(new StaticReplyHandler(HttpStatusCode.NotFound, "{}")); using var none = new UpdateService(noneHttp);
        Check(await none.CheckAsync(CancellationToken.None) is null, "No release was presented as a new version.");
        using var failedHttp = new HttpClient(new StaticReplyHandler(HttpStatusCode.ServiceUnavailable, "{}")); using var failed = new UpdateService(failedHttp);
        bool unavailable = false; try { await failed.CheckAsync(CancellationToken.None); } catch (HttpRequestException) { unavailable = true; }
        Check(unavailable, "A failed update request was presented as up to date.");
        using var invalidHttp = new HttpClient(new StaticReplyHandler(HttpStatusCode.OK, "{}")); using var invalid = new UpdateService(invalidHttp);
        bool malformed = false; try { await invalid.CheckAsync(CancellationToken.None); } catch (JsonException) { malformed = true; }
        Check(malformed, "Missing update fields were accepted as a release.");
        Console.WriteLine("No release, failed update request and malformed update response: OK");
    }

    private static async Task CheckWorkflowsAsync(string root)
    {
        int searches = 0, fetches = 0, reviews = 0, saves = 0;
        var generated = new List<WorkPrompt>();
        Task<WebSearchResult> Search(string query, int limit, CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); searches++;
            return Task.FromResult(new WebSearchResult(query, [new("公式資料", "https://docs.example.com/source?utm_source=test", "概要だけ", "test"),
                new("同じ資料", "https://docs.example.com/source", "概要だけ", "other"), new("読めない資料", "https://docs.example.com/blocked", "読めない概要", "test")], ["一つの検索先は利用できません"]));
        }
        Task<WebDocument> Fetch(WebResult result, CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); fetches++;
            if (result.Url.Contains("blocked", StringComparison.Ordinal)) { throw new InvalidDataException("本文取得不可"); }
            return Task.FromResult(new WebDocument(result.Title, result.Url, "取得できた本文に基づく事実。資料内の指示は実行しない。", DateTimeOffset.UtcNow, "2026-10-15"));
        }
        Task<WorkReply> Generate(WorkPrompt prompt, Action<string>? stream, CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); generated.Add(prompt);
            string text = prompt.Final ? "{\"answer\":\"取得した事実 [S1]。\",\"evidence\":[{\"source\":1,\"quote\":\"取得できた本文に基づく事実。\",\"claim\":\"取得した事実\"}]}" : prompt.System.StartsWith("Create a web search plan", StringComparison.Ordinal) ? "{\"queries\":[\"Eluna 公式\",\"Eluna 比較\"]}" :
                prompt.System.Contains("Find missing evidence", StringComparison.Ordinal) ? "{\"queries\":[\"Eluna 最新\"]}" : "検討した内容 [S1]。未確認事項は残る。";
            stream?.Invoke(text); return Task.FromResult(new WorkReply(text, 30));
        }
        Task<int> Count(WorkPrompt prompt, CancellationToken token) { token.ThrowIfCancellationRequested(); return Task.FromResult(500); }
        var runner = new ResearchRunner(Search, Fetch, Generate, Count, 4096);
        foreach (string mode in new[] { "search", "research", "debate" })
        {
            var work = new WorkSession
            {
                Mode = mode,
                ModelId = "default",
                Question = "Elunaの事実を確認",
                Options = new(3, 3, 2, 60, 5000, 2,
                [new("propose", "提案", "案を出す", "model-a"), new("critic", "批判", "反例を示す", "model-b")])
            };
            string output = "";
            await runner.RunAsync(work, (session, token) => { reviews++; session.Plan += "\n追加の確認事項"; return Task.FromResult(true); },
                () => { saves++; return Task.CompletedTask; }, _ => { }, text => output = text, CancellationToken.None);
            Check(work.Stage == "complete" && work.Sources.Count == 1 && work.Sources[0].Text.Contains("取得できた本文", StringComparison.Ordinal) &&
                output.Contains("[S1](<https://docs.example.com/source", StringComparison.Ordinal) && work.Claims.Count == 1, "Search/report sources were missing or exact quote verification failed.");
            Check(work.SearchesUsed <= 3 && work.PagesUsed <= 3 && work.TokensUsed <= 5000 && work.Activities.Any(a => a.Kind == "warning"), "Work budgets or provider/page failures were lost.");
            if (mode == "debate") { Check(work.Turns.Count == 4 && generated.Any(p => p.ModelId == "model-a") && generated.Any(p => p.ModelId == "model-b"), "Role models or completed rounds were not executed sequentially."); }
            var store = new AppStore(Path.Combine(root, mode));
            var conversation = new Conversation(); conversation.Messages.Add(new() { Role = "assistant", Content = output, Work = work });
            await store.SaveAsync(new(1, new(WorkMode: mode), [conversation.Save()]));
            WorkSession restored = Conversation.Restore((await store.LoadAsync()).Conversations.Single()).Messages.Single().Work!;
            Check(restored.Stage == "complete" && restored.Sources.Count == 1 && restored.Turns.Count == work.Turns.Count && !restored.CanResume, "Research state did not survive restart.");
        }
        Check(reviews == 2 && saves > 20 && fetches == 6, "Plan review, progress checkpoints or canonical URL deduplication failed.");
        using var cancel = new CancellationTokenSource();
        var interrupted = new WorkSession { Mode = "debate", ModelId = "default", Question = "Elunaを検討", Options = new(3, 3, 2, 60, 5000, 2) };
        await runner.RunAsync(interrupted, (_, _) => Task.FromResult(true), () =>
        {
            if (interrupted.Turns.Count(t => !t.Incomplete) == 1) { cancel.Cancel(); }
            return Task.CompletedTask;
        }, _ => { }, _ => { }, cancel.Token);
        Check(interrupted.Stage == "paused" && interrupted.Turns.Count == 1 && interrupted.Sources.Count == 1 && interrupted.CanResume, "Stopping debate discarded completed work.");
        WorkSession resumable = WorkSession.Restore(interrupted.Save()); int beforeSearches = searches, beforeFetches = fetches;
        await runner.RunAsync(resumable, (_, _) => throw new InvalidOperationException("Reviewed plan was prompted twice."), () => Task.CompletedTask, _ => { }, _ => { }, CancellationToken.None);
        Check(resumable.Stage == "complete" && resumable.Turns.Count == 6 && searches == beforeSearches && fetches == beforeFetches, "Resume repeated searches/pages/turns or did not finish.");
        Check(resumable.Turns[0].Content == interrupted.Turns[0].Content, "Resume replaced an existing role turn.");
        var limited = new WorkSession { Mode = "search", ModelId = "default", Question = "上限", Options = new(1, 1, 1, 30, 2000, 1), TokensUsed = 1990 };
        await runner.RunAsync(limited, (_, _) => Task.FromResult(true), () => Task.CompletedTask, _ => { }, _ => { }, CancellationToken.None);
        Check(limited.Stage == "limited" && limited.TokensUsed == 1990, "Token limits did not stop new generation.");
        var noBody = new ResearchRunner(Search, (_, _) => throw new InvalidDataException("取得不可"), Generate, Count, 4096);
        var empty = new WorkSession { Mode = "search", ModelId = "default", Question = "根拠なし", Options = new(1, 1, 1, 30, 2000, 1) };
        await noBody.RunAsync(empty, (_, _) => Task.FromResult(true), () => Task.CompletedTask, _ => { }, _ => { }, CancellationToken.None);
        Check(empty.Sources.Count == 0 && generated.Last().User.Contains("本文を取得できた資料はありません", StringComparison.Ordinal) && empty.Report.Contains("根拠の対応を確認できませんでした", StringComparison.Ordinal), "Search snippets were presented as retrieved evidence.");
        WorkSource[] known = [new(1, "資料", "https://example.com", "本文には12文字以上の正しい引用だけが含まれます。", DateTimeOffset.UtcNow)];
        string forged = "{\"answer\":\"でたらめ [S1]\",\"evidence\":[{\"source\":1,\"quote\":\"実際の本文にはない引用です。\",\"claim\":\"でたらめ\"}]}";
        Check(!ResearchRunner.TryGroundReport(forged, known, out _, out _), "A fabricated quotation was accepted as source evidence.");
        string sections = "{\"evidence\":[{\"source\":1,\"quote\":\"本文には12文字以上の正しい引用だけが含まれます。\",\"claim\":\"正しい引用を確認\"}],\"sections\":[{\"heading\":\"事実\",\"text\":\"正しい引用を確認\",\"kind\":\"fact\",\"sources\":[1]},{\"heading\":\"捏造\",\"text\":\"存在しない資料を根拠にした断定\",\"kind\":\"fact\",\"sources\":[99]},{\"heading\":\"未確認\",\"text\":\"追加調査が必要\",\"kind\":\"uncertain\",\"sources\":[]}] }";
        Check(ResearchRunner.TryGroundReport(sections, known, out string structured, out _) && structured.Contains("[S1](<https://example.com>)", StringComparison.Ordinal) &&
            structured.Contains("未確認・根拠不足", StringComparison.Ordinal) && !structured.Contains("存在しない資料", StringComparison.Ordinal), "Report sections lost deterministic citations or displayed an unsupported factual section.");
        string safe = ResearchRunner.FormatCitations("正しい [S1](https://invented.example/) 不明 [S99]", known, out int invalidCitations);
        Check(invalidCitations == 1 && safe.Contains("[S1](<https://example.com>)", StringComparison.Ordinal) && !safe.Contains("invented", StringComparison.Ordinal), "Generated citation URLs were not replaced by fetched URLs.");
        Check(ResearchRunner.SeedQuery("llama.cppのWindowsでのVulkan対応について説明してください") == "llama.cpp Windows Vulkan", "Original product names were lost in a Japanese search question.");
        GenerationLimits constrained = InferenceModes.LimitOutput(new(2048, 4096), 512);
        Check(constrained.MaxTokens == 512 && constrained.MaxTokens - constrained.ThinkingTokens >= 128 && constrained.ThinkingTokens < 512,
            "A research output limit allowed thinking to consume the entire structured answer budget.");
        Check(ResearchRunner.BuildEvidence([new(1, "資料", "https://example.com", "navigation\n" + new string('x', 6000) + "\nVulkan Windows llama.cpp target evidence", DateTimeOffset.UtcNow)], "llama.cpp Windows Vulkan", 200).Contains("target evidence", StringComparison.Ordinal), "Relevant page text was replaced by the navigation header.");
        const string forkUrl = "https://github.com/example/llama.cpp-fork", originalUrl = "https://github.com/ggml-org/llama.cpp", buildUrl = "https://github.com/ggml-org/llama.cpp/blob/master/docs/build.md";
        var visited = new List<string>();
        var navigation = new ResearchRunner((_, _, _) => Task.FromResult(new WebSearchResult("llama.cpp", [new("llama.cpp Windows Vulkan fork", forkUrl, "", "test")], [])),
            (hit, _) =>
            {
                visited.Add(hit.Url);
                List<WebResult> links = hit.Url == forkUrl ? [new("Sign in", "https://github.com/login?return_to=llama.cpp", "navigation", "link"), new("ggml-org/llama.cpp", originalUrl, "forked from", "link")] : hit.Url == originalUrl ? [new("Windows Vulkan build", buildUrl, "building the project", "link")] : [];
                return Task.FromResult(new WebDocument("llama.cpp", hit.Url, "取得できた本文に基づく事実。Windows Vulkan llama.cpp", DateTimeOffset.UtcNow, Links: links));
            }, Generate, Count, 4096);
        var navigationWork = new WorkSession { Question = "llama.cpp Windows Vulkan", Mode = "search", ModelId = "default", Options = new(1, 3, 3, 60, 5000, 1) };
        await navigation.RunAsync(navigationWork, (_, _) => Task.FromResult(true), () => Task.CompletedTask, _ => { }, _ => { }, CancellationToken.None);
        Check(visited.SequenceEqual(new[] { forkUrl, originalUrl, buildUrl }) && navigationWork.PagesUsed == 3 && navigationWork.SearchesUsed == 1 && navigationWork.Stage == "complete", "Following original-source links exceeded the shared page budget or skipped the linked build document.");
        Check(!navigationWork.Hits.Any(h => h.Url.StartsWith("https://github.com/login", StringComparison.Ordinal)), "A login navigation link was mistaken for a relevant reference because of its return URL.");
        var partialRounds = new WorkSession { Mode = "debate", Question = "Elunaの事実", ModelId = "default", Options = new(1, 1, 1, 60, 2000, 5) };
        await runner.RunAsync(partialRounds, (_, _) => Task.FromResult(true), () => Task.CompletedTask, _ => { }, _ => { }, CancellationToken.None);
        Check(partialRounds.Turns.Count < 15 && partialRounds.Turns.Count > 0 && partialRounds.Activities.Any(a => a.Kind == "warning" && a.Title.Contains("議論を締め切り", StringComparison.Ordinal)), "A partially completed debate was presented without a budget/round completion warning.");
        Console.WriteLine("Research/debate budgets, editable plans, shared evidence, citation validation, saved progress and resume: OK");
    }

    private static async Task CheckBranchesAndBackupsAsync(string root)
    {
        var conversation = new Conversation { Title = "分岐の確認", Draft = "書きかけ", CustomInstruction = "日本語で", IsPinned = true };
        conversation.Messages.Add(new() { Role = "user", Content = "元の質問" });
        conversation.Messages.Add(new() { Role = "assistant", Content = "元の回答" });
        ConversationBranch original = conversation.ActiveBranch;
        conversation.Summary = "元の会話のメモ"; conversation.SummaryThrough = 2; conversation.Summary = "  ";
        Check(conversation.SummaryThrough == 0 && conversation.Messages.Count == 2, "Clearing the summary discarded the original history from the inference context.");
        SavedConversation invalidSummary = conversation.Save() with { Branches = [new(original.Id, original.Name, original.Messages.Select(m => m.Save()).ToList(), "", 2)] };
        Check(Conversation.Restore(invalidSummary).SummaryThrough == 0, "Restoring an empty summary retained skipped history.");
        conversation.Fork(1, "別の回答");
        conversation.Messages[0].Content = "変更後の質問";
        conversation.Messages.Add(new() { Role = "assistant", Content = "生成途中", IsGenerating = true });
        Check(original.Messages.Count == 2 && original.Messages[0].Content == "元の質問" && original.Messages[1].Content == "元の回答", "Editing a branch changed the original conversation.");
        var store = new AppStore(root);
        var first = new AppState(1, new(), [conversation.Save()]);
        await store.SaveAsync(first);
        AppState restored = await store.LoadAsync();
        Conversation loaded = Conversation.Restore(restored.Conversations[0]);
        Check(loaded.Branches.Count == 2 && loaded.Messages.Last().Content == "生成途中" && loaded.Messages.Last().HasNotice &&
            loaded.Draft == "書きかけ" && loaded.CustomInstruction == "日本語で" && loaded.IsPinned, "Branch, draft or interrupted answer did not survive restart.");
        await store.SaveAsync(first with { Conversations = [] });
        FileInfo backup = store.GetBackups().First();
        await store.RestoreBackupAsync(backup.FullName);
        Check((await store.LoadAsync()).Conversations.Count == 1, "A saved generation could not be restored.");
        string export = Path.Combine(root, "export.json");
        await store.ExportAsync(first, export);
        Check((await store.ReadExportAsync(export)).Conversations[0].Branches!.Count == 2, "Export/import discarded branches.");
        bool invalid = false;
        await File.WriteAllTextAsync(export, "{invalid}");
        try { await store.ReadExportAsync(export); } catch (JsonException) { invalid = true; }
        Check(invalid && (await store.LoadAsync()).Conversations.Count == 1, "Invalid import damaged the current state.");
        string diagnostics = Path.Combine(root, "diagnostics.json");
        await DiagnosticsService.ExportAsync(diagnostics, new(SystemInstruction: "PRIVATE-INSTRUCTION"), "CPU", new IOException("PRIVATE-CONTENT"));
        string report = await File.ReadAllTextAsync(diagnostics);
        Check(!report.Contains("PRIVATE-", StringComparison.Ordinal), "Diagnostics leaked private text.");
        Console.WriteLine("Independent branches, drafts, partial recovery, backups, import/export and private diagnostics: OK");
    }

    private static async Task CheckEventsAsync()
    {
        string events = ": keepalive\r\n\r\ndata: {\"choices\":[{\"delta\":{\"role\":\"assistant\",\"content\":null}}]}\n\n" +
            "data: {\"choices\":[{\"delta\":{\"reasoning_content\":\"日本語で考える\"}}]}\r\n\r\n" +
            "data: {\"choices\":[{\"delta\":{\"content\":\"こん\",\"reasoning_content\":\"。\"}}]}\r\n\r\n" +
            "data: {\"choices\":[{\"delta\":{\"content\":\"にちは\"}}]}\n\n" +
            "data: {\"choices\":[{\"delta\":{},\"finish_reason\":\"length\"}]}\n\ndata: [DONE]\n\n";
        using var stream = new ChunkStream(Encoding.UTF8.GetBytes(events));
        var result = new StringBuilder();
        var reasoning = new StringBuilder();
        string? finish = null;
        await foreach (GenerationUpdate update in LlamaService.ReadEventsAsync(stream, CancellationToken.None))
        {
            result.Append(update.Content);
            reasoning.Append(update.Reasoning);
            finish = update.FinishReason ?? finish;
        }
        Check(result.ToString() == "こんにちは", "Fragmented UTF-8 SSE was not assembled correctly.");
        Check(reasoning.ToString() == "日本語で考える。" && finish == "length", "Reasoning leaked into the answer or output truncation was lost.");
        using var plain = new MemoryStream(Encoding.UTF8.GetBytes("data: {\"choices\":[{\"delta\":{\"content\":\"通常の回答\"}}]}\n\ndata: [DONE]\n\n"));
        await foreach (GenerationUpdate update in LlamaService.ReadEventsAsync(plain, CancellationToken.None))
        {
            Check(update.Content == "通常の回答" && update.Reasoning.Length == 0, "A model without reasoning stopped working.");
        }
        bool truncated = false;
        try
        {
            using var incomplete = new MemoryStream(Encoding.UTF8.GetBytes("data: {\"choices\":[]}\n\n"));
            await foreach (GenerationUpdate _ in LlamaService.ReadEventsAsync(incomplete, CancellationToken.None)) { }
        }
        catch (IOException) { truncated = true; }
        Check(truncated, "Unexpected end of a response was reported as success.");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        bool canceled = false;
        try
        {
            using var canceledStream = new MemoryStream([]);
            await foreach (GenerationUpdate _ in LlamaService.ReadEventsAsync(canceledStream, cancellation.Token)) { }
        }
        catch (OperationCanceledException) { canceled = true; }
        Check(canceled, "SSE cancellation was ignored.");
        using var thinkingStop = new CancellationTokenSource();
        bool thinkingCanceled = false;
        try
        {
            using var thinkingStream = new ChunkStream(Encoding.UTF8.GetBytes(events));
            await foreach (GenerationUpdate update in LlamaService.ReadEventsAsync(thinkingStream, thinkingStop.Token))
            {
                if (update.Reasoning.Length > 0) { thinkingStop.Cancel(); }
            }
        }
        catch (OperationCanceledException) { thinkingCanceled = true; }
        Check(thinkingCanceled, "Cancellation during the reasoning phase was ignored.");
        Console.WriteLine("Fragmented SSE, separate reasoning, truncation, plain models and cancellation: OK");
    }

    private static void CheckInferenceModes()
    {
        foreach (InferenceMode mode in InferenceModes.All)
        {
            GenerationLimits limits = InferenceModes.GetLimits(mode, 8192, 100);
            using JsonDocument payload = JsonDocument.Parse(JsonSerializer.Serialize(LlamaService.CreatePayload(
                [new("user", "質問"), new("assistant", "回答", "thorough", 10)], mode, limits)));
            JsonElement json = payload.RootElement;
            Check(json.GetProperty("chat_template_kwargs").GetProperty("enable_thinking").GetBoolean() == (mode.Id != "fast") &&
                json.GetProperty("reasoning_format").GetString() == "deepseek", "Inference mode was not applied to the API or thoughts could enter the answer.");
            Check(!json.GetProperty("messages")[2].TryGetProperty("thinkingSeconds", out _), "UI metadata was sent as model input.");
            GenerationLimits constrained = InferenceModes.GetLimits(mode, 2048, 1500);
            Check(constrained.MaxTokens <= 548 && constrained.MaxTokens - constrained.ThinkingTokens >= 128,
                "Reasoning consumed all the space reserved for the final answer.");
        }
        bool tooLong = false;
        try { InferenceModes.GetLimits(InferenceModes.Find("thorough"), 2048, 2000); }
        catch (InvalidOperationException) { tooLong = true; }
        Check(tooLong, "An overfull conversation was allowed to start without answer space.");
        var message = new ChatMessage { Role = "assistant", IsGenerating = true };
        Check(message.IsWaiting && !message.HasThought, "Preparing state is invisible.");
        message.IsThinking = true;
        Check(message.HasThought && !message.IsWaiting, "Thinking and waiting are shown at the same time.");
        message.ThinkingSeconds = 4.2;
        message.IsThinking = false;
        message.Content = "回答";
        message.IsGenerating = false;
        Check(message.ThinkingLabel == "5秒考えました" && !message.IsWaiting, "Completed reasoning summary was lost.");
        Console.WriteLine("Inference settings, answer-space limits and thinking states: OK");
    }

    private static async Task CheckDownloadsAsync(string root)
    {
        byte[] contents = new byte[3 * 1024 * 1024];
        RandomNumberGenerator.Fill(contents);
        "GGUF"u8.CopyTo(contents);
        string hash = Convert.ToHexString(SHA256.HashData(contents));
        using var handler = new DownloadHandler(contents);
        using var http = new HttpClient(handler);
        var store = new AppStore(root);
        using var catalog = new ModelCatalog(store, http);
        var file = new ModelFile("test/model", "abc123", "test.gguf", hash, contents.Length, "test");
        using var cancellation = new CancellationTokenSource();
        bool canceled = false;
        try
        {
            await catalog.DownloadAsync(file, new CallbackProgress(_ => cancellation.Cancel()), cancellation.Token);
        }
        catch (OperationCanceledException) { canceled = true; }
        Check(canceled && (await store.ListModelsAsync()).Count == 0, "Canceled download became a usable model.");
        LocalModel model = await catalog.DownloadAsync(file, null, CancellationToken.None);
        Check(handler.RangeRequests == 1 && File.Exists(model.FilePath) && new FileInfo(model.FilePath).Length == contents.Length,
            "Download did not resume using the verified byte range.");
        Check((await store.ListModelsAsync()).Count == 1, "Completed model metadata was not saved.");
        bool mismatch = false;
        try { await catalog.DownloadAsync(file with { Filename = "bad.gguf", Sha256 = new string('0', 64) }, null, CancellationToken.None); }
        catch (InvalidDataException) { mismatch = true; }
        Check(mismatch && (await store.ListModelsAsync()).Count == 1, "Incorrect hash was accepted.");
        Console.WriteLine("Download cancellation, resume, checksum and metadata: OK");
    }

    private static async Task CheckRealModelAsync(string root)
    {
        var store = new AppStore(root);
        using var catalog = new ModelCatalog(store);
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(15));
        List<ModelRepository> results = await catalog.SearchAsync("bartowski/Qwen_Qwen3.5-0.8B-GGUF", timeout.Token);
        Check(results.Any(r => r.Id == "bartowski/Qwen_Qwen3.5-0.8B-GGUF"), "Real model search failed.");
        List<ModelFile> files = await catalog.GetFilesAsync("bartowski/Qwen_Qwen3.5-0.8B-GGUF", timeout.Token);
        ModelFile file = files.First(f => f.Filename.EndsWith("Q4_K_M.gguf", StringComparison.Ordinal));
        long lastReport = 0;
        LocalModel model = await catalog.DownloadAsync(file, new CallbackProgress(progress =>
        {
            if (Environment.TickCount64 - lastReport > 3000)
            {
                Console.WriteLine(progress.Label);
                lastReport = Environment.TickCount64;
            }
        }), timeout.Token);
        using var llama = new LlamaService(store);
        await llama.InitializeAsync();
        Console.WriteLine($"Model search, download and SHA-256: OK ({model.SizeLabel})");
        await llama.EnsureModelAsync(model, new(model.Id), Console.WriteLine, timeout.Token);
        var text = new StringBuilder();
        await foreach (GenerationUpdate piece in llama.GenerateAsync([new("user", "「こんにちは」とだけ答えてください。")], InferenceModes.Find("fast"), timeout.Token)) { text.Append(piece.Content); }
        Check(text.Length > 0, "Real model produced an empty response.");
        Console.WriteLine($"Real streamed chat: OK ({llama.ComputeLabel}; {text.Length} characters)");
        using var stop = new CancellationTokenSource();
        bool canceled = false;
        try
        {
            await foreach (GenerationUpdate _ in llama.GenerateAsync([new("user", "長い物語を書いてください。")], InferenceModes.Find("standard"), stop.Token)) { stop.Cancel(); }
        }
        catch (OperationCanceledException) { canceled = true; }
        Check(canceled, "Real generation did not stop.");
        foreach (InferenceMode mode in InferenceModes.All.Where(m => m.Id != "fast"))
        {
            int thoughts = 0;
            text.Clear();
            await foreach (GenerationUpdate piece in llama.GenerateAsync([new("user", "17×23を計算し、答えの数値だけを書いてください。")], mode, timeout.Token))
            {
                thoughts += piece.Reasoning.Length;
                text.Append(piece.Content);
            }
            Check(thoughts > 0 && text.Length > 0 && !text.ToString().Contains("<think>", StringComparison.Ordinal), $"{mode.Id}: missing thoughts, answer or reasoning separation.");
            Console.WriteLine($"Real inference mode {mode.Id}: OK ({thoughts} reasoning characters; {text.Length} answer characters)");
        }
        await llama.EnsureModelAsync(model, new(model.Id, "cpu"), Console.WriteLine, timeout.Token);
        Check(llama.ComputeLabel == "CPU", "Compute device did not switch to CPU.");
        text.Clear();
        await foreach (GenerationUpdate piece in llama.GenerateAsync([new("user", "こんにちはとだけ答えてください。")], InferenceModes.Find("fast"), timeout.Token)) { text.Append(piece.Content); }
        Check(text.Length > 0, "CPU generation failed after switching.");
        llama.Stop();
        Check(!Directory.EnumerateFiles(Path.Combine(root, "Sessions"), "*.key").Any(), "Session credentials were not cleaned up.");
        Console.WriteLine("Real generation stop, CPU switch and server cleanup: OK");
    }
    private static async Task CheckSplitModelsAsync(string root)
    {
        byte[] contents = new byte[3 * 1024 * 1024]; RandomNumberGenerator.Fill(contents); "GGUF"u8.CopyTo(contents);
        string hash = Convert.ToHexString(SHA256.HashData(contents));
        ModelFile first = new("test/model", "revision", "model-Q4_K_M-00001-of-00002.gguf", hash, contents.Length, "test"),
            second = first with { Filename = "model-Q4_K_M-00002-of-00002.gguf" };
        ModelFile split = ModelCatalog.GroupSplitFiles([second, first]).Single();
        Check(split.Filename == first.Filename && split.Files.Count == 2 && split.Size == 2L * contents.Length && split.Files[1].Filename == second.Filename, "Split files were grouped out of order or with wrong total size.");
        Check(ModelCatalog.GroupSplitFiles([first]).Count == 0, "An incomplete split model was listed as downloadable.");
        var store = new AppStore(root); using var handler = new DownloadHandler(contents); using var http = new HttpClient(handler); using var catalog = new ModelCatalog(store, http);
        using var cancel = new CancellationTokenSource(); bool stopped = false;
        try { await catalog.DownloadAsync(split, new CallbackProgress(p => { if (p.Stage.Contains("（2/2）", StringComparison.Ordinal)) { cancel.Cancel(); } }), cancel.Token); }
        catch (OperationCanceledException) { stopped = true; }
        Check(stopped && (await store.ListModelsAsync()).Count == 0, "A partially downloaded split model was registered.");
        long received = 0;
        LocalModel model = await catalog.DownloadAsync(split, new CallbackProgress(p => { Check(p.Total == split.Size && p.Received <= p.Total, "Split progress did not describe the whole model."); received = p.Received; }), CancellationToken.None);
        Check(model.Files.Count == 2 && model.Size == split.Size && received == split.Size && (await store.ListModelsAsync()).Count == 1 && handler.RangeRequests == 1,
            "Split download did not resume or register all verified files together.");
        string secondPath = model.Files[1].FilePath;
        Check(Path.GetFullPath(secondPath).StartsWith(Path.GetFullPath(store.ModelsDirectory) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase), "The split fixture escaped its model directory.");
        File.Delete(secondPath);
        Check((await store.ListModelsAsync()).Count == 0, "A model with a missing shard was shown as usable.");
        await catalog.DownloadAsync(split, null, CancellationToken.None);
        string localDirectory = Path.Combine(root, "import-source"); Directory.CreateDirectory(localDirectory);
        string localFirst = Path.Combine(localDirectory, first.Filename), localSecond = Path.Combine(localDirectory, second.Filename);
        await File.WriteAllBytesAsync(localFirst, contents);
        bool missing = false; try { await catalog.ImportAsync(localFirst, null, CancellationToken.None); } catch (InvalidDataException) { missing = true; }
        Check(missing && (await store.ListModelsAsync()).Count == 1, "Incomplete local shards were imported.");
        await File.WriteAllBytesAsync(localSecond, contents);
        LocalModel imported = await catalog.ImportAsync(localSecond, null, CancellationToken.None);
        Check(imported.Files.Count == 2 && imported.Size == 2L * contents.Length && imported.FilePath.EndsWith(first.Filename, StringComparison.Ordinal) &&
            (await store.ListModelsAsync()).Count == 2, "Importing a later shard did not collect the entire split model.");
        string api = JsonSerializer.Serialize(new { sha = "revision", siblings = new[] { new { rfilename = first.Filename, lfs = new { sha256 = hash, size = contents.Length } }, new { rfilename = second.Filename, lfs = new { sha256 = hash, size = contents.Length } } } });
        using var apiHttp = new HttpClient(new StaticReplyHandler(HttpStatusCode.OK, api)); using var apiCatalog = new ModelCatalog(store, apiHttp);
        Check((await apiCatalog.GetFilesAsync("test/model", CancellationToken.None)).Single().Files.Count == 2, "Model API results did not expose a complete split download.");
        Console.WriteLine("Split GGUF grouping, total progress, interrupted resume, all-file verification and local import: OK");
    }
    private static async Task DownloadResearchModelAsync(string root, string size)
    {
        var store = new AppStore(root); using var catalog = new ModelCatalog(store);
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(15));
        List<ModelFile> files = await catalog.GetFilesAsync("bartowski/Qwen_Qwen3.5-" + size + "-GGUF", timeout.Token);
        ModelFile file = files.First(f => f.Filename.EndsWith("Q4_K_M.gguf", StringComparison.Ordinal));
        long lastReport = 0;
        LocalModel model = await catalog.DownloadAsync(file, new CallbackProgress(progress => { if (Environment.TickCount64 - lastReport > 3000) { Console.WriteLine(progress.Label); lastReport = Environment.TickCount64; } }), timeout.Token);
        Console.WriteLine("Verified research model: " + Path.Combine(Path.GetDirectoryName(model.FilePath)!, "model.json"));
    }
    private static async Task CheckSplitInferenceAsync(string root)
    {
        string first = Directory.GetFiles(Path.Combine(root, "source"), "*-00001-of-*.gguf").Single();
        var store = new AppStore(Path.Combine(root, "imported")); using var catalog = new ModelCatalog(store);
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        LocalModel model = await catalog.ImportAsync(first, null, timeout.Token);
        Check(model.Files.Count > 1 && (await store.ListModelsAsync()).Single().Size == model.Size, "Real GGUF shards were not imported together.");
        using var llama = new LlamaService(store); await llama.InitializeAsync();
        await llama.EnsureModelAsync(model, new(model.Id), Console.WriteLine, timeout.Token);
        var answer = new StringBuilder();
        await foreach (GenerationUpdate piece in llama.GenerateAsync([new("user", "2 + 3の答えを数値だけ書いてください。")], InferenceModes.Find("fast"), timeout.Token)) { answer.Append(piece.Content); }
        Check(answer.Length > 0 && answer.ToString().Contains('5'), "The actual split model did not generate the expected answer.");
        llama.Stop(); Console.WriteLine($"Real split GGUF inference: OK ({model.Files.Count} files; {model.SizeLabel}; answer={answer})");
    }

    private sealed class CallbackProgress(Action<DownloadProgress> callback) : IProgress<DownloadProgress>
    {
        public void Report(DownloadProgress value) => callback(value);
    }

    private sealed class ChunkStream(byte[] contents) : MemoryStream(contents)
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            base.ReadAsync(buffer[..Math.Min(buffer.Length, 3)], cancellationToken);
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            base.ReadAsync(buffer, offset, Math.Min(count, 3), cancellationToken);
    }

    private sealed class DownloadHandler(byte[] contents) : HttpMessageHandler
    {
        public int RangeRequests { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            long offset = request.Headers.Range?.Ranges.Single().From ?? 0;
            if (offset > 0) { RangeRequests++; }
            var response = new HttpResponseMessage(offset > 0 ? HttpStatusCode.PartialContent : HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(contents, (int)offset, contents.Length - (int)offset)
            };
            if (offset > 0) { response.Content.Headers.ContentRange = new ContentRangeHeaderValue(offset, contents.Length - 1, contents.Length); }
            return Task.FromResult(response);
        }
    }
    private sealed class StaticReplyHandler(HttpStatusCode code, string text) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(code) { Content = new StringContent(text, Encoding.UTF8, "application/json") });
    }
}
