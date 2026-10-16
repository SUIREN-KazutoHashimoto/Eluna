using System.Collections.ObjectModel;
using Eluna.App.Models;
using Eluna.App.Services;

namespace Eluna.App.ViewModels;

public sealed partial class ChatViewModel
{
    private LocalMember _self = new();
    public LocalMember Self => _self;
    public AgentAccount SelfAccount => new(AgentAccount.LocalId, Self.Name, Self.Icon, Specialty: Self.About.Length > 0 ? Self.About : "会話に参加中");
    public ObservableCollection<AgentAccount> MentionAccounts { get; } = [];
    private MessageIdentity SelfIdentity(Guid? reply = null, string? detail = null) => new(null, Self.Name, Self.Icon, Time: DateTimeOffset.UtcNow, ReplyTo: reply, Detail: detail);
    internal void RefreshMentionAccounts()
    { MentionAccounts.Clear(); MentionAccounts.Add(SelfAccount); foreach (AgentAccount account in Agents) { MentionAccounts.Add(account); } }
    public async Task SaveSelfAsync(LocalMember profile)
    {
        profile = profile with { Name = profile.Name.Trim(), Icon = profile.Icon.Trim(), About = profile.About.Trim() };
        if (Agents.Any(a => a.Name.Equals(profile.Name, StringComparison.OrdinalIgnoreCase))) { throw new InvalidOperationException("他のメンバーと区別できる表示名を選んでください。"); }
        WorkspaceService.Validate(CaptureState() with { Self = profile }); _self = profile;
        Notify(nameof(Self)); RefreshRoom(); await PersistSafelyAsync();
    }
    public async Task OpenDirectMessageAsync(Guid id)
    {
        AgentAccount account = Agents.FirstOrDefault(a => a.Id == id && a.Enabled) ?? throw new InvalidOperationException("このメンバーは現在利用できません。");
        Conversation? existing = Conversations.FirstOrDefault(c => c.Room?.Kind == "dm" && c.Room.Members.SequenceEqual([id]) && !c.IsArchived);
        if (existing is not null) { SelectedConversation = existing; } else { await CreateRoomAsync(account.Name + "とのDM", "dm", [id]); }
    }
}
