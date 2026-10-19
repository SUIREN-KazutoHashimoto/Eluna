using System.Collections.ObjectModel;
using Eluna.App.Models;

namespace Eluna.App.ViewModels;

public sealed partial class ChatViewModel
{
    public ObservableCollection<AgentAccount> RoomMembers { get; } = [];
    public ObservableCollection<MemberCard> RoomMemberCards { get; } = [];
    private string MemberStatus(AgentAccount account)
    {
        if (account.IsLocal) { return "会話に参加中"; }
        if (!account.Enabled) { return "無効"; }
        foreach (InferenceJob job in Inferences.Items.Where(j => j.State == "active"))
        {
            ChatMessage? answer = FindAnswer(job.Request); if (answer?.Work?.Stage is "debate-wait" or "plan" or "paused") { continue; }
            if (answer?.Team is { } team) { if (team.ActiveRoomId == SelectedConversation?.Id && team.ActiveAgentId == account.Id) { return "入力中…"; } continue; }
            if (job.Request.ConversationId == SelectedConversation?.Id && job.Request.BranchId == SelectedBranch?.Id && (answer?.Work?.Turns.LastOrDefault(t => t.Incomplete)?.AgentId ?? job.Request.Agent?.Id) == account.Id) { return "入力中…"; }
        }
        return Inferences.Items.Any(j => j.State is "queued" or "paused" && j.Request.ConversationId == SelectedConversation?.Id && j.Request.BranchId == SelectedBranch?.Id && j.Request.Agent?.Id == account.Id) ? "順番待ち" : "";
    }
    private void RefreshMemberCards()
    {
        for (int i = 0; i < RoomMembers.Count; i++)
        {
            AgentAccount account = RoomMembers[i]; MemberCard? card = RoomMemberCards.FirstOrDefault(c => c.Account.Id == account.Id);
            if (card is null) { card = new(account); RoomMemberCards.Insert(i, card); } else if (RoomMemberCards.IndexOf(card) != i) { RoomMemberCards.Move(RoomMemberCards.IndexOf(card), i); }
            card.Update(account, MemberStatus(account));
        }
        while (RoomMemberCards.Count > RoomMembers.Count) { RoomMemberCards.RemoveAt(RoomMemberCards.Count - 1); }
    }
    public string ServerTitle => SelectedProject?.Name ?? "ダイレクトメッセージ";
    public string ChannelTitle => SelectedConversation?.Title ?? "会話";
    public string ChannelSymbol => SelectedConversation?.Room?.Kind switch { "voice" => "◖", "dm" or "group" => "@", "thread" => "↳", _ => "#" };
    public string ChannelDescription => SelectedConversation?.Room?.Kind switch { "voice" => "ボイス・テキスト・画面共有", "dm" => "ダイレクトメッセージ", "group" => "グループメッセージ", "thread" => "スレッド", _ => SelectedProject?.Name ?? "テキストチャンネル" };
    public string ChannelPlaceholder => ChannelSymbol + " " + ChannelTitle + " へメッセージを送信";
    public string ChannelWelcome => ChannelSymbol == "#" ? ChannelTitle + "へようこそ！" : ChannelTitle;
    public string MemberHeading => "メンバー — " + RoomMembers.Count;
    public string ChannelListHeading => SelectedProject is null ? "ダイレクトメッセージ" : "チャンネル";
    public bool HasHistorySearch => HistorySearch.Length > 0;
    public string TypingNotice
    {
        get
        {
            InferenceJob[] active = Inferences.Items.Where(j => j.State == "active" && (j.Request.ConversationId == SelectedConversation?.Id && j.Request.BranchId == SelectedBranch?.Id || FindAnswer(j.Request)?.Team is { ActiveRoomId: Guid room } && room == SelectedConversation?.Id)).ToArray();
            string names = string.Join("、", active.Where(j => FindAnswer(j.Request)?.Work?.Stage is not ("debate-wait" or "plan" or "paused")).Select(j => FindAnswer(j.Request)?.Team is { } team ? team.ActiveRoomId == SelectedConversation?.Id ? Agents.FirstOrDefault(a => a.Id == team.ActiveAgentId)?.Name : null : FindAnswer(j.Request)?.Work?.Turns.LastOrDefault(t => t.Incomplete)?.Name ?? j.Request.Agent?.Name ?? "メンバー").Where(n => n is not null).Distinct());
            string drawing = string.Join("、", MediaJobs.Where(j => j.State == "active" && j.Request is { Kind: "image", ArtistGenerated: true } r && r.ConversationId == SelectedConversation?.Id && r.BranchId == SelectedBranch?.Id).Select(j => j.Request!.Agent?.Name).Where(n => n is not null).Distinct());
            return string.Join(" · ", new[] { names.Length > 0 ? names + " が入力中…" : "", drawing.Length > 0 ? drawing + " が描いています…" : "" }.Where(s => s.Length > 0));
        }
    }
    private void NotifyShell()
    {
        Notify(nameof(IsSocialChannel)); Notify(nameof(SocialStatus)); Notify(nameof(SocialStopped));
        RefreshMemberCards();
        NotifyVoice();
        Notify(nameof(ServerTitle)); Notify(nameof(ChannelTitle)); Notify(nameof(ChannelSymbol)); Notify(nameof(ChannelDescription)); Notify(nameof(ChannelPlaceholder)); Notify(nameof(ChannelWelcome)); Notify(nameof(MemberHeading)); Notify(nameof(TypingNotice)); Notify(nameof(ChannelListHeading));
    }
}
