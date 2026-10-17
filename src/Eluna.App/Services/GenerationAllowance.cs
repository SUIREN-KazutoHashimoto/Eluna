namespace Eluna.App.Services;

internal sealed class ConversationBudgetException : Exception
{
    internal ConversationBudgetException() : base("この会話の生成予算を使い切りました。会話の進め方の設定で上限を変更できます。") { }
}
internal sealed class GenerationAllowance(int limit)
{
    private readonly int _limit = limit;
    internal int Remaining { get; private set; } = limit;
    internal int Used => _limit - Remaining;
    internal int Limit(int requested)
    { if (Remaining < 128) { throw new ConversationBudgetException(); } return Math.Min(requested, Remaining); }
    internal void Spend(int actual, int reserved)
    { Remaining = Math.Max(0, Remaining - (actual > 0 ? actual : reserved)); }
}
