using System.Text;
using Eluna.App.Models;

namespace Eluna.App.Services;

public static class ContextCompressor
{
    internal static async Task<SavedMessage[]> PrepareAsync(Conversation conversation, LlamaService llama, AppSettings settings,
        Action<string> status, CancellationToken token, KnowledgeContext? knowledge = null, AgentAccount? agent = null)
    {
        SavedMessage[] history = conversation.Messages.Where(m => !m.Pending && !m.IsGenerating && (m.Content.Length > 0 || m.Attachments.Count > 0)).Select(m => m.Save()).ToArray();
        string instruction = knowledge?.Instruction ?? settings.SystemInstruction + "\n" + conversation.CustomInstruction;
        int through = string.IsNullOrWhiteSpace(conversation.Summary) ? 0 : Math.Clamp(conversation.SummaryThrough, 0, history.Length);
        SavedMessage[] Build() => (conversation.Summary.Length > 0 ? new[] { new SavedMessage("user", "以前の会話の引き継ぎメモ:\n" + conversation.Summary), new SavedMessage("assistant", "引き継ぎメモを参照します。") } : [])
            .Concat(history.Skip(through)).ToArray();
        SavedMessage[] WithKnowledge() { SavedMessage[] addressed = WorkspaceService.AddressAgent(Build(), agent); return knowledge?.Prepend(addressed) ?? addressed; }
        if (await llama.CountInputTokensAsync(WithKnowledge(), instruction, token) < settings.ContextSize * 0.7) { return WithKnowledge(); }
        int target = Math.Max(0, history.Length - 4);
        while (target < history.Length && history[target].Role != "user") { target++; }
        if (target <= through) { return WithKnowledge(); }
        status("長い会話を整理しています… 元の履歴は保存されています");
        const string summarizeInstruction = "会話を引き継ぐためのメモを日本語で500文字以内に要約してください。目的、確定した事実、条件、未解決事項を残し、推測を事実に変えないでください。メモ本文だけを出力してください。";
        async Task<string> Summarize(string previous, string addition)
        {
            var result = new StringBuilder();
            await foreach (GenerationUpdate piece in llama.GenerateAsync([new("user", "既存メモ:\n" + previous + "\n追加の会話:\n" + addition)], InferenceModes.Find("fast"), token, summarizeInstruction, 512)) { result.Append(piece.Content); }
            return result.Length > 0 ? result.ToString() : throw new InvalidOperationException("会話の要約を取得できませんでした。再試行してください。");
        }
        while (through < target)
        {
            int end = through;
            var batch = new StringBuilder();
            while (end < target)
            {
                string addition = history[end].Role + ": " + history[end].PromptText + "\n";
                if (batch.Length + addition.Length > 2400 && batch.Length > 0) { break; }
                SavedMessage[] request = [new("user", "既存メモ:\n" + conversation.Summary + "\n追加の会話:\n" + batch + addition)];
                if (await llama.CountInputTokensAsync(request, summarizeInstruction, token) >= settings.ContextSize - 600) { break; }
                batch.Append(addition); end++;
            }
            if (end == through)
            {
                // Split an oversized old message without cutting a UTF-16 surrogate pair.
                string text = history[through].PromptText;
                string localSummary = conversation.Summary;
                int offset = 0;
                while (offset < text.Length)
                {
                    int length = Math.Min(2000, text.Length - offset);
                    while (true)
                    {
                        if (offset + length < text.Length && char.IsHighSurrogate(text[offset + length - 1])) { length--; }
                        SavedMessage[] request = [new("user", "既存メモ:\n" + localSummary + "\n追加の会話:\n" + text.Substring(offset, length))];
                        if (await llama.CountInputTokensAsync(request, summarizeInstruction, token) < settings.ContextSize - 600) { break; }
                        length /= 2;
                        if (length < 16) { throw new InvalidOperationException("要約する容量が足りません。会話を記憶する長さを増やしてください。"); }
                    }
                    localSummary = await Summarize(localSummary, history[through].Role + ": " + text.Substring(offset, length));
                    offset += length;
                }
                conversation.Summary = localSummary; through++;
            }
            else
            {
                conversation.Summary = await Summarize(conversation.Summary, batch.ToString()); through = end;
            }
            conversation.SummaryThrough = through;
        }
        return WithKnowledge();
    }
}
