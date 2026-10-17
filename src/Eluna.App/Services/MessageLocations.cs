using System.IO;
using Eluna.App.Models;

namespace Eluna.App.Services;

internal sealed record MessageLocation(Guid Room, Guid Branch, Guid Message, string Speaker, string Icon, string Text, DateTimeOffset? Time, bool Complete, string Place, bool Unread, bool Mention, bool Pinned, DateTimeOffset? Received = null)
{
    public string Preview => Text.Length > 280 ? Text[..(char.IsHighSurrogate(Text[279]) ? 279 : 280)] + "…" : Text;
    public string Heading => Icon + " " + Speaker + (Time is { } time ? " · " + time.ToLocalTime().ToString("M/d HH:mm", System.Globalization.CultureInfo.InvariantCulture) : "") + (Complete ? "" : " · 未完了");
}
internal static class MessageLocations
{
    internal static readonly string[] ReactionEmoji = ["👍", "❤️", "🎉", "🤔", "✅", "👀", "🙏", "😂"];
    internal static bool Finished(ChatMessage message) => !message.IsGenerating && !message.WasInterrupted && (message.Identity?.Detail is not ("画像生成" or "文書生成") || message.Artifact is not null);
    internal static IEnumerable<MessageLocation> In(Conversation room, IReadOnlyDictionary<Guid, string>? serverNames = null)
    {
        var unread = (room.Experience.Unread ?? []).ToDictionary(u => (u.Branch, u.Message));
        var pins = (room.Experience.Pins ?? []).Select(p => (p.Branch, p.Message)).ToHashSet();
        string place = (room.ProjectId is Guid server ? serverNames?.GetValueOrDefault(server, "サーバー") ?? "サーバー" : "DM") + " / " + room.Title;
        foreach (ConversationBranch branch in room.Branches.ToArray())
        {
            MessageLocation Row(Guid id, string speaker, string icon, string text, DateTimeOffset? time, bool complete)
            { unread.TryGetValue((branch.Id, id), out UnreadMarker? marker); return new(room.Id, branch.Id, id, speaker, icon, text, time, complete, place + (room.Branches.Count > 1 ? " / " + branch.Name : ""), marker is not null, marker?.Mention == true, pins.Contains((branch.Id, id)), marker?.Received); }
            foreach (ChatMessage m in branch.Messages.ToArray())
            {
                if (m.Work is { Mode: "debate" } work)
                { foreach (DebateTurn t in work.Turns.ToArray()) { yield return Row(t.Id ?? WorkspaceService.StableId(work.Id, t.Round + ":" + t.Role), t.Name ?? t.Role, t.Icon, t.Content, t.Time, !t.Incomplete); } }
                if (m.Content.Length > 0) { yield return Row(m.Id, m.Speaker, m.Avatar, m.Content, m.Identity?.Time, Finished(m)); }
            }
        }
    }
    internal static void Validate(SavedConversation room)
    {
        if (room.Experience is not { } e) { return; }
        if (e.Unread?.Count > 1000 || e.Pins?.Count > 200 || e.Reactions?.Count > 1000 ||
            e.Unread?.Any(u => u is null) == true || e.Pins?.Any(p => p is null) == true || e.Reactions?.Any(r => r is null || !ReactionEmoji.Contains(r.Emoji, StringComparer.Ordinal)) == true ||
            e.Unread?.DistinctBy(u => (u.Branch, u.Message)).Count() != e.Unread?.Count || e.Pins?.Distinct().Count() != e.Pins?.Count || e.Reactions?.Distinct().Count() != e.Reactions?.Count)
        { throw new InvalidDataException("未読・ピン留め・リアクションの形式または件数が不正です。"); }
        var references = (e.Unread ?? []).Select(u => (u.Branch, u.Message)).Concat((e.Pins ?? []).Select(p => (p.Branch, p.Message))).Concat((e.Reactions ?? []).Select(r => (r.Branch, r.Message))).ToHashSet();
        if (references.Count == 0) { return; }
        foreach (SavedBranch b in room.Branches ?? [])
        { foreach (SavedMessage m in b.Messages) { if (m.Id is Guid id) { references.Remove((b.Id, id)); } if (m.Work is { } w) { foreach (DebateTurn t in w.Turns) { references.Remove((b.Id, t.Id ?? WorkspaceService.StableId(w.Id, t.Round + ":" + t.Role))); } } } }
        if (references.Count > 0) { throw new InvalidDataException("未読・ピン留め・リアクションの参照先がありません。"); }
    }
}
