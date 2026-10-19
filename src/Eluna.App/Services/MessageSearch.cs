using System.Text.RegularExpressions;

namespace Eluna.App.Services;

internal sealed record MessageSearchResult(IReadOnlyList<MessageLocation> Matches, int Total);
internal static class MessageSearch
{
    internal static MessageSearchResult Find(IReadOnlyList<MessageLocation> rows, string query, string mode, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        string[] terms = Regex.Matches(query, "(?:from:|in:)?\"[^\"]*\"|\\S+", RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(100)).Select(m => m.Value).ToArray();
        if (terms.Length > 30) { throw new InvalidOperationException("検索語は30個以内に絞ってください。引用符で囲むと一つのフレーズとして探せます。"); }
        IEnumerable<MessageLocation> selected = rows.Where(m => m.Text.Length > 0 && (mode switch { "unread" => m.Unread, "mentions" => m.Mention, "pins" => m.Pinned, _ => true }));
        var matches = new List<MessageLocation>();
        foreach (MessageLocation row in selected)
        {
            token.ThrowIfCancellationRequested(); bool Match(string term) => term.StartsWith("from:", StringComparison.OrdinalIgnoreCase) ? row.Speaker.Contains(term[5..].Trim('"'), StringComparison.OrdinalIgnoreCase) : term.StartsWith("in:", StringComparison.OrdinalIgnoreCase) ? row.Place.Contains(term[3..].Trim('"'), StringComparison.OrdinalIgnoreCase) : (row.Text + " " + row.Speaker + " " + row.Place).Contains(term.Trim('"'), StringComparison.OrdinalIgnoreCase);
            if (terms.All(Match)) { matches.Add(row); }
        }
        return new(matches.OrderByDescending(m => mode is "unread" or "mentions" ? m.Received ?? m.Time : m.Time).Take(200).ToArray(), matches.Count);
    }
}
