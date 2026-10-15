using Eluna.App.Models;

namespace Eluna.App.ViewModels;

public sealed partial class ChatViewModel
{
    private HashSet<Guid>? _historyMatches;
    private CancellationTokenSource? _historySearchCancellation;
    internal Task HistorySearchFinished { get; private set; } = Task.CompletedTask;
    private sealed record SearchDocument(Guid Id, string Title, string[] Texts);
    private void HighlightCurrentSearch()
    {
        string query = HistorySearch.Trim();
        foreach (ChatMessage message in SelectedConversation?.Messages ?? []) { message.SearchHighlighted = query.Length > 0 && message.Content.Contains(query, StringComparison.OrdinalIgnoreCase); }
    }
    private void RefreshHistorySearch()
    {
        _historySearchCancellation?.Cancel(); _historySearchCancellation?.Dispose(); _historySearchCancellation = new();
        string query = HistorySearch.Trim();
        Notify(nameof(HistoryEmptyHint));
        if (query.Length == 0)
        { _historyMatches = null; foreach (Conversation conversation in Conversations) { conversation.SearchExcerpt = ""; } HistoryView.Refresh(); HighlightCurrentSearch(); return; }
        SearchDocument[] documents = Conversations.Select(c => new SearchDocument(c.Id, c.Title, c.Branches.SelectMany(b => b.Messages).Select(m => m.Content).ToArray())).ToArray();
        Dictionary<Guid, string> Find(CancellationToken token)
        {
            var results = new Dictionary<Guid, string>();
            foreach (SearchDocument document in documents)
            {
                token.ThrowIfCancellationRequested();
                string? text = document.Texts.FirstOrDefault(t => t.Contains(query, StringComparison.OrdinalIgnoreCase));
                if (text is not null)
                { int start = Math.Max(0, text.IndexOf(query, StringComparison.OrdinalIgnoreCase) - 24); results[document.Id] = (start > 0 ? "…" : "") + text.Substring(start, Math.Min(110, text.Length - start)).Replace('\n', ' '); }
                else if (document.Title.Contains(query, StringComparison.OrdinalIgnoreCase)) { results[document.Id] = "タイトルが一致"; }
            }
            return results;
        }
        void Apply(Dictionary<Guid, string> results)
        {
            _historyMatches = results.Keys.ToHashSet();
            foreach (Conversation conversation in Conversations) { conversation.SearchExcerpt = results.GetValueOrDefault(conversation.Id, ""); }
            HistoryView.Refresh(); HighlightCurrentSearch(); Notify(nameof(HistoryEmptyHint));
        }
        if (Conversations.Count < 500) { Apply(Find(CancellationToken.None)); HistorySearchFinished = Task.CompletedTask; }
        else { HistorySearchFinished = SearchAsync(); }
        async Task SearchAsync()
        {
            CancellationToken token = _historySearchCancellation.Token;
            try { await Task.Delay(120, token); Dictionary<Guid, string> results = await Task.Run(() => Find(token), token); if (!token.IsCancellationRequested) { Apply(results); } }
            catch (OperationCanceledException) { }
        }
    }
}
