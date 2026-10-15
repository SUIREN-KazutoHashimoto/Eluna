namespace Eluna.App.Models;

public sealed record InferenceMode(string Id, string Name, string Description, string Icon, int ThinkingTokens, string Effort);
public sealed record GenerationLimits(int ThinkingTokens, int MaxTokens);
public sealed record GenerationUpdate(string Content = "", string Reasoning = "", string? FinishReason = null, int? OutputTokens = null, double? OutputMilliseconds = null);

public static class InferenceModes
{
    public const string DefaultId = "standard";
    public static IReadOnlyList<InferenceMode> All { get; } =
    [
        new("fast", "速度優先", "思考を省いて、すばやく回答", "↯", 0, "none"),
        new(DefaultId, "標準", "対応モデルで、ほどよく考える", "◉", 512, "medium"),
        new("thorough", "じっくり", "考える時間を増やして回答", "✧", 2048, "high")
    ];

    public static bool IsValid(string? id) => All.Any(mode => mode.Id == id);
    public static InferenceMode Find(string? id) => All.FirstOrDefault(mode => mode.Id == id) ?? All[1];

    public static GenerationLimits GetLimits(InferenceMode mode, int contextSize, int inputTokens, int maximumAnswer = 2048)
    {
        // Leave space for template closing tags and a useful final answer.
        int available = contextSize - inputTokens - 32;
        if (available < 128)
        {
            throw new InvalidOperationException("会話が長くなり、回答する余裕がありません。新しいチャットを始めるか、設定で会話を記憶する長さを増やしてください。");
        }
        int thinking = Math.Min(mode.ThinkingTokens, (available - 128) / 2);
        return new(thinking, Math.Min(available, maximumAnswer + thinking));
    }
    public static GenerationLimits LimitOutput(GenerationLimits limits, int maximum)
    {
        int total = Math.Min(limits.MaxTokens, maximum);
        return new(Math.Min(limits.ThinkingTokens, Math.Max(0, (total - 128) / 2)), total);
    }
}
