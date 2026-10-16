using System.IO;
using Eluna.App.Models;

namespace Eluna.App.Services;

internal static class ChannelAccess
{
    internal static ChannelGrant Direct(SavedConversation room, Guid agent) => room.Room?.Permissions?.FirstOrDefault(p => p.AgentId == agent)
        ?? new(agent, room.Room?.Members.Contains(agent) == true || room.Room is null, room.Room?.Members.Contains(agent) == true || room.Room is null);
    internal static bool CanView(AppState state, SavedConversation room, Guid agent)
    {
        var visited = new HashSet<Guid>();
        while (true)
        {
            if (!visited.Add(room.Id) || !Direct(room, agent).View) { return false; }
            if (room.Room?.ParentId is not Guid parent) { return true; }
            SavedConversation? next = state.Conversations.FirstOrDefault(c => c.Id == parent);
            if (next is null) { return false; }
            room = next;
        }
    }
    internal static bool CanSpeak(AppState state, SavedConversation room, Guid agent) => state.Agents?.Any(a => a.Id == agent && a.Enabled) == true &&
        (room.Room is null || room.Room.Members.Contains(agent)) && Direct(room, agent).Speak && CanView(state, room, agent);
    internal static bool CanInvite(AppState state, SavedConversation room, Guid agent) => CanSpeak(state, room, agent) && Direct(room, agent).Invite;
    internal static bool CanOrganize(AppState state, SavedConversation room, Guid agent) => CanSpeak(state, room, agent) && Direct(room, agent).Organize;
    internal static bool CanReadFor(AppState state, SavedConversation destination, SavedConversation source, Guid? agent)
    {
        if (agent is Guid id) { return CanView(state, source, id); }
        // Shared debate context must be readable by every participating account.
        Guid[] readers = destination.Room?.Members.Where(id => CanSpeak(state, destination, id)).ToArray() ?? [];
        return readers.Length == 0 ? destination.Room is null : readers.All(id => CanView(state, source, id));
    }
    internal static void RequireSpeaker(AppState state, SavedConversation room, Guid? agent)
    {
        if (agent is Guid id && !CanSpeak(state, room, id)) { throw new InvalidOperationException("このアカウントには、このチャンネルの閲覧・発言権限がありません。"); }
        if (agent is null && room.Room is { } profile && !profile.Members.Any(id => CanSpeak(state, room, id))) { throw new InvalidOperationException("発言できるアカウントがいません。チャンネルのアクセス権を確認してください。"); }
    }
    internal static void Validate(List<ChannelGrant>? grants, IReadOnlySet<Guid> agents)
    {
        if (grants is null) { return; }
        if (grants.Count > 500 || grants.Any(g => g is null || !agents.Contains(g.AgentId) || !g.View && (g.Speak || g.Invite || g.Organize) || !g.Speak && (g.Invite || g.Organize)) || grants.Select(g => g.AgentId).Distinct().Count() != grants.Count)
        { throw new InvalidDataException("チャンネル権限の対象・組み合わせを確認してください。"); }
    }
}
