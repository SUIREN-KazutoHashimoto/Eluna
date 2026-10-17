using System.Windows;
using System.Windows.Controls;
using Eluna.App.Models;
using Eluna.App.Services;
using Eluna.App.ViewModels;

namespace Eluna.App;

public sealed class InviteMembersWindow : Window
{
    private readonly ChatViewModel _chat; private readonly Guid _room;
    internal TextBox SearchBox { get; } = new();
    internal ListBox Candidates { get; } = new() { SelectionMode = SelectionMode.Multiple, DisplayMemberPath = nameof(AgentAccount.Label), MinHeight = 100 };
    internal Button InviteButton { get; } = new() { Content = "選んだメンバーを招待" };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 8) };
    private readonly HashSet<Guid> _selected = []; private bool _refreshing, _saving;
    public InviteMembersWindow(ChatViewModel chat, Conversation room)
    {
        _chat = chat; _room = room.Id; Title = "# " + room.Title + " に招待"; Width = 530; Height = 590; MinWidth = 380; MinHeight = 400; WindowStartupLocation = WindowStartupLocation.CenterOwner; Appearance.Attach(this);
        var root = new DockPanel { Margin = new(22) }; Content = root;
        var footer = new StackPanel(); DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer); footer.Children.Add(_status);
        var buttons = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right }; footer.Children.Add(buttons);
        var create = new Button { Content = "新しいアカウント", Margin = new(0, 0, 8, 8) }; buttons.Children.Add(create); create.Click += (_, _) => { var manager = new AgentsWindow(chat) { Owner = this }; manager.BeginCreate(); manager.Show(); manager.Closed += (_, _) => Refresh(); };
        InviteButton.SetResourceReference(StyleProperty, "PrimaryButton"); InviteButton.Margin = new(0, 0, 0, 8); buttons.Children.Add(InviteButton); InviteButton.Click += async (_, _) => await InviteAsync();
        var header = new StackPanel(); DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        header.Children.Add(new TextBlock { Text = "メンバーを招待", FontSize = 22 }); header.Children.Add(new TextBlock { Text = "参加後は名前にメンションして話しかけられます。選んだメンバーにはこのチャンネルの閲覧・発言を許可します。", TextWrapping = TextWrapping.Wrap, Margin = new(0, 12, 0, 14) });
        SearchBox.Margin = new(0, 0, 0, 10); System.Windows.Automation.AutomationProperties.SetName(SearchBox, "招待するアカウントを検索"); header.Children.Add(SearchBox); root.Children.Add(Candidates);
        SearchBox.TextChanged += (_, _) => Refresh(); Candidates.SelectionChanged += (_, e) => { if (_refreshing) { return; } foreach (AgentAccount a in e.AddedItems) { _selected.Add(a.Id); } foreach (AgentAccount a in e.RemovedItems) { _selected.Remove(a.Id); } Count(); };
        chat.Agents.CollectionChanged += AccountsChanged; room.PropertyChanged += RoomChanged; Closed += (_, _) => { chat.Agents.CollectionChanged -= AccountsChanged; room.PropertyChanged -= RoomChanged; }; Refresh();
    }
    private void AccountsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) => Refresh();
    private void RoomChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) { if (e.PropertyName == nameof(Conversation.Room)) { Refresh(); } }
    private void Count() { int members = _chat.Conversations.FirstOrDefault(c => c.Id == _room)?.Room?.Members.Count ?? 0; _status.Text = $"選択 {_selected.Count} 人 · 参加中 {members}/6 人" + (Candidates.Items.Count == 0 ? "\n候補がありません。検索を変えるか、新しいアカウントを作成してください。" : ""); InviteButton.IsEnabled = !_saving && _selected.Count > 0 && members + _selected.Count <= 6; }
    private void Refresh()
    {
        Conversation? room = _chat.Conversations.FirstOrDefault(c => c.Id == _room); _refreshing = true;
        _selected.RemoveWhere(id => room?.Room?.Members.Contains(id) == true || !_chat.Agents.Any(a => a.Id == id && a.Enabled));
        Candidates.ItemsSource = _chat.Agents.Where(a => a.Enabled && room?.Room?.Members.Contains(a.Id) != true && (a.Name + a.Specialty).Contains(SearchBox.Text.Trim(), StringComparison.OrdinalIgnoreCase)).ToArray();
        foreach (AgentAccount a in Candidates.Items) { if (_selected.Contains(a.Id)) { Candidates.SelectedItems.Add(a); } }
        _refreshing = false; Count();
    }
    internal async Task<bool> InviteAsync()
    {
        if (_saving) { return false; }
        _saving = true; Count();
        try { await _chat.InviteMembersAsync(_room, _selected.ToArray()); _selected.Clear(); Refresh(); _status.Text = "招待しました。メンバー一覧からプロフィールを開けます。"; return true; }
        catch (Exception ex) { _status.Text = ex.Message; return false; }
        finally { _saving = false; InviteButton.IsEnabled = _selected.Count > 0; }
    }
}
