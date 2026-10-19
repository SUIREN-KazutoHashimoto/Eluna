using Eluna.App.Models;

namespace Eluna.App.Services;

internal sealed record NavigationTarget(Guid? Room, Guid? Server, Guid? Account, string Name, string Detail, string Icon, DateTimeOffset Recent);
internal static class QuickNavigation
{
    internal static IReadOnlyList<NavigationTarget> Find(IEnumerable<Conversation> rooms, IEnumerable<KnowledgeProject> servers, IEnumerable<AgentAccount> accounts, string query, Guid? currentServer, Guid? currentRoom)
    {
        Dictionary<Guid, string> names = servers.ToDictionary(s => s.Id, s => s.Name);
        string text = query.Trim(); char prefix = text.Length > 0 && "#@*!".Contains(text[0]) ? text[0] : '\0';
        if (prefix != '\0') { text = text[1..].Trim(); }
        string[] terms = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        IEnumerable<NavigationTarget> targets = rooms.Where(r => !r.IsArchived && (prefix switch { '#' => r.Room?.Kind is "channel" or "thread", '@' => r.Room?.Kind is "dm" or "group", '!' => r.Room?.Kind == "voice", '*' => false, _ => true }))
            .Select(r => new NavigationTarget(r.Id, r.ProjectId, null, r.Title, (r.ProjectId is Guid id ? names.GetValueOrDefault(id, "サーバー") : "ダイレクトメッセージ") + " · " + r.HistoryGroup, r.Room?.Kind switch { "voice" => "◖", "dm" => "@", "group" => "◉", "thread" => "↳", _ => "#" }, r.UpdatedAt));
        if (prefix is '\0' or '*') { targets = targets.Concat(names.Select(s => new NavigationTarget(null, s.Key, null, s.Value, "サーバー", "▦", DateTimeOffset.MinValue))); }
        if (prefix is '\0' or '@') { targets = targets.Concat(accounts.Where(a => a.Enabled).Select(a => new NavigationTarget(null, null, a.Id, a.Name, "メンバー · DMを開く", a.Icon, DateTimeOffset.MinValue))); }
        return targets.Where(t => terms.All(term => (t.Name + " " + t.Detail).Contains(term, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(t => text.Length > 0 && t.Name.Equals(text, StringComparison.OrdinalIgnoreCase)).ThenByDescending(t => text.Length > 0 && t.Name.StartsWith(text, StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(t => t.Room is not null && t.Room != currentRoom).ThenByDescending(t => t.Server == currentServer).ThenByDescending(t => t.Recent).ThenBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase).Take(40).ToArray();
    }
}
