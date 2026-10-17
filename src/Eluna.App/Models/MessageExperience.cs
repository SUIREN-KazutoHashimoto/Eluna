namespace Eluna.App.Models;

public sealed record MessagePointer(Guid Branch, Guid Message);
public sealed record UnreadMarker(Guid Branch, Guid Message, DateTimeOffset Received, bool Mention = false);
public sealed record LocalReaction(Guid Branch, Guid Message, string Emoji);
public sealed record RoomExperience(List<UnreadMarker>? Unread = null, List<MessagePointer>? Pins = null, List<LocalReaction>? Reactions = null, bool Muted = false);
public sealed record ReactionBadge(string Emoji, string Label, string Hint);
