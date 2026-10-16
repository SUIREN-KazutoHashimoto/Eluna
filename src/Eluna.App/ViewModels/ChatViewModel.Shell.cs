using System.Collections.ObjectModel;
using Eluna.App.Models;

namespace Eluna.App.ViewModels;

public sealed partial class ChatViewModel
{
    public ObservableCollection<AgentAccount> RoomMembers { get; } = [];
    public string ServerTitle => SelectedProject?.Name ?? "ダイレクトメッセージ";
    public string ChannelTitle => SelectedConversation?.Title ?? "会話";
    public string ChannelSymbol => SelectedConversation?.Room?.Kind switch { "dm" or "group" => "@", "thread" => "↳", _ => "#" };
    public string ChannelDescription => SelectedConversation?.Room?.Kind switch { "dm" => "ダイレクトメッセージ", "group" => "グループメッセージ", "thread" => "スレッド", _ => SelectedProject?.Name ?? "テキストチャンネル" };
    public string ChannelPlaceholder => ChannelSymbol + " " + ChannelTitle + " へメッセージを送信";
    public string ChannelWelcome => ChannelSymbol == "#" ? ChannelTitle + "へようこそ！" : ChannelTitle;
    public string MemberHeading => "メンバー — " + RoomMembers.Count;
    public string ChannelListHeading => SelectedProject is null ? "ダイレクトメッセージ" : "テキストチャンネル";
    public bool HasHistorySearch => HistorySearch.Length > 0;
    public string TypingNotice
    {
        get
        {
            InferenceJob[] active = Inferences.Items.Where(j => j.State == "active" && j.Request.ConversationId == SelectedConversation?.Id).ToArray();
            if (active.Any(j => FindAnswer(j.Request)?.Work?.Stage == "debate-wait")) { return "次の発言を待っています"; }
            if (active.Any(j => FindAnswer(j.Request)?.Work?.Stage == "plan")) { return "確認を待っています"; }
            string names = string.Join("、", active.Select(j => FindAnswer(j.Request)?.Work?.Turns.LastOrDefault(t => t.Incomplete)?.Name ?? j.Request.Agent?.Name ?? "メンバー").Distinct());
            return names.Length > 0 ? names + " が入力中…" : "";
        }
    }
    private void NotifyShell()
    {
        Notify(nameof(ServerTitle)); Notify(nameof(ChannelTitle)); Notify(nameof(ChannelSymbol)); Notify(nameof(ChannelDescription)); Notify(nameof(ChannelPlaceholder)); Notify(nameof(ChannelWelcome)); Notify(nameof(MemberHeading)); Notify(nameof(TypingNotice)); Notify(nameof(ChannelListHeading));
    }
}
