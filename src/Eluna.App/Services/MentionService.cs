using System.Text.RegularExpressions;
using Eluna.App.Models;

namespace Eluna.App.Services;

internal sealed record MentionToken(int Start, int Length, string Text, List<Guid> Accounts, bool All = false);
internal sealed record MentionQuery(int Start, int Length, string Query);
internal static class MentionService
{
    internal static string Format(AgentAccount account) => account.Name.Any(c => char.IsWhiteSpace(c) || c is '{' or '}') ? "@{" + account.Name.Replace("}", "", StringComparison.Ordinal) + "} " : "@" + account.Name + " ";
    internal static List<MentionToken> Scan(string text, IEnumerable<AgentAccount> accounts)
    {
        AgentAccount[] catalog = accounts.ToArray(); var found = new List<MentionToken>();
        (int Index, int End)[] ignored;
        try { ignored = Regex.Matches(text, @"```[\s\S]*?(?:```|\z)|`[^`\n]*`", RegexOptions.None, TimeSpan.FromMilliseconds(100)).Select(m => (m.Index, End: m.Index + m.Length)).ToArray(); }
        catch (RegexMatchTimeoutException) { return []; }
        for (int start = text.IndexOf('@'); start >= 0 && start < text.Length; start = text.IndexOf('@', start + 1))
        {
            if (ignored.Any(r => start >= r.Index && start < r.End) || start > 0 && (char.IsLetterOrDigit(text[start - 1]) || text[start - 1] is '_' or '@')) { continue; }
            int end = start + 1; string? name = null;
            if (end < text.Length && text[end] == '{') { int close = text.IndexOf('}', end + 1); if (close >= 0 && close - start <= 85 && !text.AsSpan(end, close - end).Contains('\n')) { name = text[(end + 1)..close]; end = close + 1; } }
            else
            {
                name = catalog.Select(a => a.Name).Append("全員").Append("all").Distinct().OrderByDescending(n => n.Length).FirstOrDefault(n => text.AsSpan(end).StartsWith(n, StringComparison.OrdinalIgnoreCase) && Boundary(text, end + n.Length));
                if (name is not null) { end += name.Length; }
            }
            if (name is null) { continue; }
            bool all = name is "全員" || name.Equals("all", StringComparison.OrdinalIgnoreCase);
            List<Guid> ids = all ? [] : catalog.Where(a => a.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).Select(a => a.Id).Distinct().ToList();
            if (all || ids.Count > 0) { found.Add(new(start, end - start, text[start..end], ids, all)); start = end - 1; }
        }
        return found;
    }
    private static bool Boundary(string text, int at) => at == text.Length || char.IsWhiteSpace(text[at]) || char.IsPunctuation(text[at]) || "をにもとへがは".Contains(text[at]) || text.AsSpan(at).StartsWith("さん", StringComparison.Ordinal);
    internal static MentionQuery? Completion(string text, int caret)
    {
        if (text.Length > 65536) { return null; }
        caret = Math.Clamp(caret, 0, text.Length); int start = caret == 0 ? -1 : text.LastIndexOf('@', caret - 1);
        if (start < 0 || start > 0 && (char.IsLetterOrDigit(text[start - 1]) || text[start - 1] is '_' or '@')) { return null; }
        string query = text[(start + 1)..caret]; if (query.StartsWith('{')) { query = query[1..]; }
        if (query.Length > 80 || query.Any(c => c is '\n' or '\r' or '}' || char.IsWhiteSpace(c))) { return null; }
        string before = text[..start]; if (Regex.Matches(before, "```", RegexOptions.None, TimeSpan.FromMilliseconds(100)).Count % 2 == 1 || before.Split('\n')[^1].Count(c => c == '`') % 2 == 1) { return null; }
        return new(start, caret - start, query);
    }
}
