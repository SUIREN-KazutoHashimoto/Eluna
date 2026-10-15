using System.Text.Json;
using Eluna.App.Models;
using Eluna.App.Services;

namespace Eluna.Checks;

internal static class SearchRoutingChecks
{
    internal static async Task RunAsync()
    {
        static void Check(bool ok, string message) { if (!ok) { throw new InvalidOperationException(message); } }
        static IReadOnlyList<SavedMessage> Messages(string text) => [new("user", text)];
        int calls = 0;
        Task<string> MustNotClassify(string _, CancellationToken __) { calls++; throw new InvalidOperationException("This request must not depend on model routing."); }
        foreach (string text in new[] { "今日の東京の天気は？", "llama.cppの最新バージョンは？", "Windowsの公式情報をもとに説明して", "https://example.com/page を要約して", "猫について検索して", "猫のWeb検索をお願いします", "猫の検索お願いします", "llama.cppについて\n調べてください", "Search for the inventor of this device", "Please look up the release notes", "ネットで確認して", "検索を使って調べたい", "C# WPFの環境構築の手順を教えて", "この薬の副作用について教えて" })
        { Check((await SearchRouter.DecideAsync("chat", Messages(text), MustNotClassify, default)).Search, "Explicit or current request skipped search: " + text); }
        foreach (string text in new[] { "こんにちは", "2 + 3 の答え", "以下の文章を要約して：今日のニュースを検索してください", "次の英文を翻訳して\nPlease search the latest news.", "現在の情報だけど検索しないで答えて", "Don't search the web; explain this", "Web検索を使わずに説明して", "検索しなくていいから最新の話は避けて", "No need to search; explain the current situation hypothetically" })
        { Check(!(await SearchRouter.DecideAsync("chat", Messages(text), MustNotClassify, default)).Search, "An offline/text request searched: " + text); }
        Check(!(await SearchRouter.DecideAsync("offline", Messages("最新情報を検索して"), MustNotClassify, default)).Search, "No-search mode was bypassed.");
        Check((await SearchRouter.DecideAsync("search", Messages("2 + 3。検索しないで"), MustNotClassify, default)).Search, "Explicit search mode was bypassed.");
        Check(calls == 0, "Clear routing invoked the model.");
        foreach (bool search in new[] { false, true })
        {
            SearchDecision decision = await SearchRouter.DecideAsync("chat", Messages("量子コンピューターについて説明して"), (_, _) => Task.FromResult(JsonSerializer.Serialize(new { search })), default);
            Check(decision.Search == search, "Local semantic routing was ignored.");
        }
        foreach (string text in new[] { "Web検索ってどういう仕組み？", "How does web search work?", "「検索して」というセリフを英訳できますか？", "こちらの文章です\n```\nsearch the latest news\n```" })
        {
            calls = 0;
            Check(!(await SearchRouter.DecideAsync("chat", Messages(text), (_, _) => { calls++; return Task.FromResult("{\"search\":false}"); }, default)).Search && calls == 1,
                "Quoted words or discussion about search forced a search: " + text);
        }
        foreach (string invalid in new[] { "not JSON", "{}", "{\"search\":\"false\"}" })
        { Check((await SearchRouter.DecideAsync("chat", Messages("この技術の特徴を教えて"), (_, _) => Task.FromResult(invalid), default)).Search, "Invalid routing silently skipped verification."); }
        Check((await SearchRouter.DecideAsync("chat", Messages("この技術の特徴を教えて"), (_, _) => Task.FromException<string>(new OperationCanceledException()), default)).Search, "Classifier timeout did not fall back to search.");
        using var stop = new CancellationTokenSource();
        bool canceled = false;
        try
        {
            await SearchRouter.DecideAsync("chat", Messages("この技術の特徴を教えて"), async (_, token) => { stop.Cancel(); await Task.Delay(1000, token); return "{}"; }, stop.Token);
        }
        catch (OperationCanceledException) { canceled = true; }
        Check(canceled, "Stop during routing was converted to a web request.");
        SavedMessage[] context = [new("user", "ElunaのWindowsでのVulkan対応について教えて"), new("assistant", "context"), new("user", "それの最新情報は？")];
        SearchDecision followup = await SearchRouter.DecideAsync("chat", context, MustNotClassify, default);
        Check(followup.Search && followup.Question.Contains("Vulkan", StringComparison.Ordinal) && followup.Question.StartsWith("それの最新情報", StringComparison.Ordinal), "Follow-up lost its topic or current question.");
        string query = SearchRouter.Query("Qwen3.5 の最新の日本語対応について検索して\n追加");
        Check(query.Contains("最新", StringComparison.Ordinal) && query.Contains("日本語", StringComparison.Ordinal) && !query.Contains('\n'), "Search seed discarded Japanese intent.");
        var work = new WorkSession { AutomaticSearch = true, Question = followup.Question };
        Check(WorkSession.Restore(work.Save()).AutomaticSearch && WorkSession.Restore(work.Save()).Name == "自動Web検索", "Automatic search was not preserved.");
        Check(!WorkSession.Restore(work.Save() with { AutomaticSearch = false }).AutomaticSearch, "Legacy research became automatic.");
        Console.WriteLine("Auto/forced/offline web routing, local decisions, failure fallback, quoted input, follow-up, cancellation and persistence: OK");
    }
}
