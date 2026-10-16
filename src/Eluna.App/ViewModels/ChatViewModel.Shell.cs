using System.Collections.ObjectModel;
using Eluna.App.Models;

namespace Eluna.App.ViewModels;

public sealed partial class ChatViewModel
{
    public ObservableCollection<AgentAccount> RoomMembers { get; } = [];
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
            return names.Length > 0 ? names + " が入力中…" : "";
        }
    }
    private void NotifyShell()
    {
        NotifyVoice();
        Notify(nameof(ServerTitle)); Notify(nameof(ChannelTitle)); Notify(nameof(ChannelSymbol)); Notify(nameof(ChannelDescription)); Notify(nameof(ChannelPlaceholder)); Notify(nameof(ChannelWelcome)); Notify(nameof(MemberHeading)); Notify(nameof(TypingNotice)); Notify(nameof(ChannelListHeading));
    }
}
