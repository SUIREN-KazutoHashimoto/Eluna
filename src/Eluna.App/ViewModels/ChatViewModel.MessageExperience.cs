using Eluna.App.Models;
using Eluna.App.Services;

namespace Eluna.App.ViewModels;

public sealed partial class ChatViewModel
{
    private IncomingMessages? _incoming;
    private bool _readingMessages;
    private readonly HashSet<(Guid Room, Guid Branch)> _manualUnread = [];
    public event Action? MessageExperienceChanged;
    public int InboxCount => Conversations.Where(c => !c.IsArchived && !c.Experience.Muted).Sum(c => c.UnreadCount);
    public string InboxLabel => "受信トレイ · 未読 " + InboxCount + " 件";
    public bool HasSelectedUnread => SelectedConversation?.HasUnread == true;
    public string SelectedUnreadLabel => SelectedConversation?.UnreadHint ?? "";
    internal void UpdateReadingState(bool reading)
    {
        _readingMessages = reading;
        if (reading && SelectedConversation is { } room && !_manualUnread.Contains((room.Id, room.ActiveBranch.Id))) { MarkRead(room.Id, room.ActiveBranch.Id); }
    }
    private void Incoming(Conversation room, Guid branch, Guid message, string text)
    {
        if (!_ready || room.IsArchived || _readingMessages && room == SelectedConversation && branch == room.ActiveBranch.Id && !_manualUnread.Contains((room.Id, branch))) { return; }
        var unread = room.Experience.Unread ?? [];
        if (unread.Any(u => u.Branch == branch && u.Message == message)) { return; }
        bool mention = MentionService.Scan(text, [SelfAccount]).Any(m => m.All || m.Accounts.Contains(AgentAccount.LocalId));
        room.Experience = room.Experience with { Unread = [.. unread.TakeLast(999), new(branch, message, DateTimeOffset.UtcNow, mention)] };
        ExperienceChanged(); ScheduleSave();
    }
    public void MarkRead(Guid roomId, Guid? branch = null)
    {
        Conversation? room = Conversations.FirstOrDefault(c => c.Id == roomId); if (room is null) { return; }
        _manualUnread.RemoveWhere(p => p.Room == roomId && (branch is null || p.Branch == branch));
        if (room.Experience.Unread?.Any(u => branch is null || u.Branch == branch) != true) { return; }
        room.Experience = room.Experience with { Unread = (room.Experience.Unread ?? []).Where(u => branch is not null && u.Branch != branch).ToList() }; ExperienceChanged(); if (_ready) { ScheduleSave(); }
    }
    internal void MarkReadMessage(Guid roomId, Guid branchId, Guid messageId)
    {
        Conversation? room = Conversations.FirstOrDefault(c => c.Id == roomId); if (room?.Experience.Unread?.Any(u => u.Branch == branchId && u.Message == messageId) != true) { return; }
        room.Experience = room.Experience with { Unread = room.Experience.Unread.Where(u => u.Branch != branchId || u.Message != messageId).ToList() }; ExperienceChanged(); ScheduleSave();
    }
    public void MarkUnread(Guid roomId, Guid branchId, Guid messageId)
    {
        Conversation room = Conversations.First(c => c.Id == roomId); MessageLocation row = MessageLocations.In(room).FirstOrDefault(m => m.Branch == branchId && m.Message == messageId) ?? throw new InvalidOperationException("この発言は移動または削除されています。");
        _manualUnread.Add((roomId, branchId)); var unread = room.Experience.Unread ?? [];
        if (!unread.Any(u => u.Branch == branchId && u.Message == messageId)) { room.Experience = room.Experience with { Unread = [.. unread.TakeLast(999), new(branchId, messageId, DateTimeOffset.UtcNow)] }; ExperienceChanged(); ScheduleSave(); }
    }
    public Task SetMutedAsync(Guid roomId, bool muted)
    { Conversation room = Conversations.First(c => c.Id == roomId); room.Experience = room.Experience with { Muted = muted }; ExperienceChanged(); return PersistSafelyAsync(); }
    public async Task ToggleMessagePinAsync(Guid roomId, Guid branchId, Guid messageId)
    {
        Conversation room = RequireExperienceMessage(roomId, branchId, messageId); var pins = (room.Experience.Pins ?? []).ToList(); var pointer = new MessagePointer(branchId, messageId);
        if (!pins.Remove(pointer)) { if (pins.Count >= 200) { throw new InvalidOperationException("発言のピン留めはチャンネルごとに200件までです。"); } pins.Add(pointer); }
        room.Experience = room.Experience with { Pins = pins }; ApplyTimelineExperience(); ExperienceChanged(); await PersistSafelyAsync();
    }
    public async Task ToggleReactionAsync(Guid roomId, Guid branchId, Guid messageId, string emoji)
    {
        if (!MessageLocations.ReactionEmoji.Contains(emoji, StringComparer.Ordinal)) { throw new InvalidOperationException("このリアクションには対応していません。"); }
        Conversation room = RequireExperienceMessage(roomId, branchId, messageId); var reactions = (room.Experience.Reactions ?? []).ToList(); var reaction = new LocalReaction(branchId, messageId, emoji);
        if (!reactions.Remove(reaction)) { if (reactions.Count >= 1000) { throw new InvalidOperationException("リアクションはチャンネルごとに1000件までです。"); } reactions.Add(reaction); }
        room.Experience = room.Experience with { Reactions = reactions }; ApplyTimelineExperience(); ExperienceChanged(); await PersistSafelyAsync();
    }
    private Conversation RequireExperienceMessage(Guid roomId, Guid branchId, Guid messageId)
    {
        Conversation room = Conversations.FirstOrDefault(c => c.Id == roomId) ?? throw new InvalidOperationException("この会話は削除されています。");
        if (!MessageLocations.In(room).Any(m => m.Branch == branchId && m.Message == messageId)) { throw new InvalidOperationException("この発言は移動または削除されています。"); }
        return room;
    }
    private void ApplyTimelineExperience()
    {
        RoomExperience e = SelectedConversation?.Experience ?? new(); Guid? branch = SelectedBranch?.Id;
        var pins = (e.Pins ?? []).Where(p => p.Branch == branch).Select(p => p.Message).ToHashSet(); var reactions = (e.Reactions ?? []).Where(r => r.Branch == branch).ToLookup(r => r.Message);
        foreach (ChatMessage row in Timeline) { row.PinnedMessage = pins.Contains(row.Id); string[] emoji = reactions[row.Id].Select(r => r.Emoji).ToArray(); if (!row.Reactions.Select(r => r.Emoji).SequenceEqual(emoji)) { row.Reactions = emoji.Select(x => new ReactionBadge(x, x + " 1 ✓", x + " · " + Self.Name + " のリアクション（選択済み）。もう一度押すと取り消します。")).ToArray(); } }
    }
    private void ExperienceChanged() { Notify(nameof(InboxCount)); Notify(nameof(InboxLabel)); Notify(nameof(HasSelectedUnread)); Notify(nameof(SelectedUnreadLabel)); MessageExperienceChanged?.Invoke(); }
}
