using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Eluna.App.Controls;
using Eluna.App.Models;
using Eluna.App.Services;
using Eluna.App.ViewModels;

namespace Eluna.App;

public sealed class RoomWindow : Window
{
    internal AgentMembersEditor MembersEditor { get; }
    internal TextBox NameBox { get; } = new();
    internal ComboBox KindBox { get; } = new() { DisplayMemberPath = "Value", SelectedValuePath = "Key" };
    private readonly CheckBox _siblings = new() { Content = "同じサーバーの別チャンネルも参照する" }, _share = new() { Content = "参加したエージェントがこの履歴を持ち越すことを許可する" },
        _accept = new() { Content = "参加エージェントの持ち越し記憶を参照する" }, _serverAccept = new() { Content = "このサーバーで持ち越し記憶の受け入れを許可する" };
    internal Button SaveButton { get; } = new() { Content = "保存", Padding = new(24, 8, 24, 8) };
    private string _baseline = ""; private bool _saving;
    public RoomWindow(ChatViewModel chat, Conversation? room = null, WorkSession? discussion = null, ChatMessage? origin = null)
    {
        Title = discussion is not null ? "議論の参加者 · Eluna" : room is null ? "チャンネル・DMを作成 · Eluna" : "チャンネルの設定 · Eluna";
        Guid? capturedProject = room?.ProjectId ?? chat.SelectedProject?.Id;
        Width = 800; Height = 780; MinWidth = 580; MinHeight = 500; WindowStartupLocation = WindowStartupLocation.CenterOwner; Appearance.Attach(this);
        var root = new DockPanel { Margin = new(22) }; Content = root;
        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new(0, 12, 0, 0) }; DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        var cancel = new Button { Content = "閉じる", Margin = new(0, 0, 8, 0) }; cancel.Click += (_, _) => Close(); footer.Children.Add(cancel); SaveButton.SetResourceReference(StyleProperty, "PrimaryButton"); footer.Children.Add(SaveButton);
        var body = new StackPanel(); root.Children.Add(new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        body.Children.Add(new TextBlock { Text = discussion is null ? "会話の場所と参加者" : "次の発言から参加者を変更", FontSize = 22, Margin = new(0, 0, 0, 16) });
        KindBox.ItemsSource = new[] { new KeyValuePair<string, string>("channel", "チャンネル"), new("dm", "DM · AI一人"), new("group", "グループDM") };
        KindBox.SelectedValue = room?.Room?.Kind is "dm" or "group" ? room.Room.Kind : "channel"; KindBox.IsEnabled = room is null && discussion is null && origin is null;
        body.Children.Add(KindBox); NameBox.Text = room?.Title ?? (origin is null ? "新しいチャンネル" : "発言の深掘り"); NameBox.Margin = new(0, 10, 0, 16); body.Children.Add(NameBox);
        System.Windows.Automation.AutomationProperties.SetName(NameBox, "チャンネルまたはDMの名前");
        IEnumerable<Guid> members = discussion?.Discussion.PendingParticipants?.Where(r => r.AgentId is not null).Select(r => r.AgentId!.Value) ?? room?.Room?.Members ?? chat.SelectedConversation?.Room?.Members ?? [];
        MembersEditor = new(chat.Agents, members); body.Children.Add(MembersEditor);
        foreach (CheckBox box in new[] { _siblings, _share, _accept, _serverAccept })
        { box.Margin = new(0, 12, 0, 0); if (box.Content is string text) { box.Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap }; } body.Children.Add(box); }
        _siblings.IsChecked = room?.Room?.ReferenceSiblingChats == true; _share.IsChecked = room?.Room?.ShareHistory == true; _accept.IsChecked = room?.Room?.AcceptAgentMemory == true;
        _siblings.ToolTip = "サーバー管理で別会話の参照を許可している場合に利用できます。";
        _serverAccept.IsChecked = chat.Projects.FirstOrDefault(p => p.Id == (room?.ProjectId ?? chat.SelectedProject?.Id))?.AcceptAgentMemory == true;
        _serverAccept.Visibility = (room?.ProjectId ?? chat.SelectedProject?.Id) is null ? Visibility.Collapsed : Visibility.Visible;
        body.Children.Add(new TextBlock { Text = "限定サーバーの初期値は持ち越し記憶を使いません。履歴を共有すると、この場所で話した内容が許可先の会話で使われます。DMは一人、議論はAI二人以上です。", TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new(0, 14, 0, 0) });
        if (discussion is not null) { KindBox.Visibility = NameBox.Visibility = _siblings.Visibility = _share.Visibility = _accept.Visibility = _serverAccept.Visibility = Visibility.Collapsed; }
        if (origin is not null) { body.Children.Add(new TextBlock { Text = "このスレッドは、親の返信先までの発言を参照します。", TextWrapping = TextWrapping.Wrap, Margin = new(0, 10, 0, 0) }); }
        string Snapshot() => JsonSerializer.Serialize(new { NameBox.Text, KindBox.SelectedValue, Members = MembersEditor.SelectedIds, Siblings = _siblings.IsChecked, Share = _share.IsChecked, Accept = _accept.IsChecked, ServerAccept = _serverAccept.IsChecked });
        _baseline = Snapshot(); UnsavedChanges.Protect(this, () => !_saving && Snapshot() != _baseline, Save);
        SaveButton.Click += async (_, _) => await Save();
        async Task<bool> Save()
        {
            if (_saving) { return false; }
            _saving = true; SaveButton.IsEnabled = false;
            try
            {
                if (discussion is not null) { await chat.SetDebateParticipantsAsync(discussion, MembersEditor.SelectedIds); }
                else if (room is null || origin is not null)
                {
                    if (origin is null && KindBox.SelectedValue is "channel" && chat.SelectedProject?.Id != capturedProject) { throw new InvalidOperationException("作成先のサーバーへ戻って保存するか、画面を開き直してください。"); }
                    Conversation created = await chat.CreateRoomAsync(NameBox.Text, origin is null ? (string)KindBox.SelectedValue : "thread", MembersEditor.SelectedIds, origin is null ? null : room, origin);
                    await chat.UpdateRoomAsync(created, created.Title, created.Room! with { ReferenceSiblingChats = _siblings.IsChecked == true, ShareHistory = _share.IsChecked == true, AcceptAgentMemory = _accept.IsChecked == true }, _serverAccept.IsChecked == true);
                }
                else { await chat.UpdateRoomAsync(room, NameBox.Text.Trim(), room.Room! with { Members = MembersEditor.SelectedIds.ToList(), DefaultAgentId = MembersEditor.SelectedIds.FirstOrDefault(), ReferenceSiblingChats = _siblings.IsChecked == true, ShareHistory = _share.IsChecked == true, AcceptAgentMemory = _accept.IsChecked == true }, _serverAccept.IsChecked == true); }
                _baseline = Snapshot(); Close(); return true;
            }
            catch (Exception ex) { chat.Error = ex.Message; MessageBox.Show(this, ex.Message, "保存できませんでした"); return false; }
            finally { _saving = false; SaveButton.IsEnabled = true; }
        }
    }
}
