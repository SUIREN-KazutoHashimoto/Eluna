using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Eluna.App.Controls;
using Eluna.App.Models;
using Eluna.App.Services;
using Eluna.App.ViewModels;

namespace Eluna.App;

public sealed class RoomPermissionsWindow : Window
{
    private readonly ChatViewModel _chat;
    private readonly Guid _room;
    private readonly Dictionary<Guid, ChannelGrant> _grants;
    internal ListBox Accounts { get; } = new() { DisplayMemberPath = nameof(AgentAccount.Label), MaxHeight = 170 };
    internal CheckBox ViewBox { get; } = new() { Content = "閲覧・過去の会話の参照" };
    internal CheckBox SpeakBox { get; } = new() { Content = "発言・推論の実行（メンバーのみ）" };
    internal CheckBox InviteBox { get; } = new() { Content = "つながりのあるアカウントを招待" };
    internal CheckBox OrganizeBox { get; } = new() { Content = "サーバー・チャンネルの作成" };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 8) };
    private Guid? _editing; private bool _loading, _saving; private string _baseline;
    public RoomPermissionsWindow(ChatViewModel chat, Conversation room)
    {
        _chat = chat; _room = room.Id; _grants = (room.Room?.Permissions ?? []).ToDictionary(p => p.AgentId); _baseline = Snapshot();
        Title = room.Title + " · アクセス権"; Width = 590; Height = 720; MinWidth = 400; MinHeight = 450; WindowStartupLocation = WindowStartupLocation.CenterOwner; Appearance.Attach(this);
        var root = new DockPanel { Margin = new(24) }; Content = root;
        var footer = new StackPanel(); DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer); footer.Children.Add(_status);
        var save = new Button { Content = "アクセス権を保存", HorizontalAlignment = HorizontalAlignment.Right }; save.SetResourceReference(StyleProperty, "PrimaryButton"); footer.Children.Add(save); save.Click += async (_, _) => await SaveAsync();
        var body = new StackPanel(); root.Children.Add(new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        body.Children.Add(new TextBlock { Text = "アカウントのアクセス権", FontSize = 22, TextWrapping = TextWrapping.Wrap });
        body.Children.Add(new TextBlock { Text = "AIごとにこの場所の参照・発言を設定します。利用者本人は管理者として全履歴を開けます。メンバーは初期状態で閲覧・発言可能です。スレッドには親の閲覧権も必要です。", TextWrapping = TextWrapping.Wrap, Margin = new(0, 12, 0, 14) });
        var search = new TextBox { Margin = new(0, 0, 0, 8) }; System.Windows.Automation.AutomationProperties.SetName(search, "アカウントを検索"); body.Children.Add(search); body.Children.Add(Accounts);
        void Filter() => Accounts.ItemsSource = chat.Agents.Where(a => a.Name.Contains(search.Text, StringComparison.OrdinalIgnoreCase)).ToArray(); Filter(); search.TextChanged += (_, _) => Filter();
        var presets = new WrapPanel { Margin = new(0, 12, 0, 4) }; body.Children.Add(presets);
        void Preset(string label, bool view, bool speak, bool extra = false) { var b = new Button { Content = label, Margin = new(0, 0, 6, 6) }; presets.Children.Add(b); b.Click += (_, _) => { ViewBox.IsChecked = view; SpeakBox.IsChecked = speak; InviteBox.IsChecked = OrganizeBox.IsChecked = extra; Commit(); }; }
        Preset("閲覧・発言", true, true); Preset("閲覧のみ", true, false); Preset("アクセス不可", false, false); Preset("作業を任せる", true, true, true);
        foreach (CheckBox box in new[] { ViewBox, SpeakBox, InviteBox, OrganizeBox }) { box.Content = new TextBlock { Text = (string)box.Content, TextWrapping = TextWrapping.Wrap }; box.Margin = new(0, 7, 0, 7); body.Children.Add(box); box.Checked += Changed; box.Unchecked += Changed; }
        body.Children.Add(new TextBlock { Text = "変更すると関連する実行・予約を停止します。既に投稿・引用された情報は履歴に残ります。発言を許可しても参加者には自動追加されません。", TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new(0, 14, 0, 0) });
        Accounts.SelectionChanged += (_, _) => { _editing = (Accounts.SelectedItem as AgentAccount)?.Id; _loading = true; ChannelGrant p = _editing is Guid id ? _grants.GetValueOrDefault(id) ?? ChannelAccess.Direct(room.Save(), id) : new(Guid.Empty, false, false); ViewBox.IsChecked = p.View; SpeakBox.IsChecked = p.Speak; InviteBox.IsChecked = p.Invite; OrganizeBox.IsChecked = p.Organize; _loading = false; };
        Accounts.SelectedIndex = 0; UnsavedChanges.Protect(this, () => Snapshot() != _baseline, SaveAsync);
    }
    private void Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) { return; }
        _loading = true;
        if (sender == ViewBox && ViewBox.IsChecked != true) { SpeakBox.IsChecked = false; }
        if ((sender == InviteBox && InviteBox.IsChecked == true) || (sender == OrganizeBox && OrganizeBox.IsChecked == true)) { SpeakBox.IsChecked = true; }
        if (SpeakBox.IsChecked == true) { ViewBox.IsChecked = true; } else { InviteBox.IsChecked = OrganizeBox.IsChecked = false; }
        _loading = false; Commit();
    }
    private void Commit() { if (!_loading && _editing is Guid id) { _grants[id] = new(id, ViewBox.IsChecked == true, SpeakBox.IsChecked == true, InviteBox.IsChecked == true, OrganizeBox.IsChecked == true); } }
    private string Snapshot() => JsonSerializer.Serialize(_grants.Values.OrderBy(p => p.AgentId));
    internal async Task<bool> SaveAsync()
    {
        if (_saving) { return false; }
        _saving = true; string snapshot = Snapshot();
        try
        {
            Conversation? current = _chat.Conversations.FirstOrDefault(c => c.Id == _room);
            if (current is null || JsonSerializer.Serialize((current.Room?.Permissions ?? []).OrderBy(p => p.AgentId)) != _baseline) { throw new InvalidOperationException("別の画面でアクセス権が変更されています。編集内容を確認し、開き直してください。"); }
            await _chat.SetChannelPermissionsAsync(_room, _grants.Values.ToArray()); _baseline = snapshot; _status.Text = "アクセス権を保存しました。関連する処理を停止しました。"; return true;
        }
        catch (Exception ex) { _status.Text = ex.Message; return false; }
        finally { _saving = false; }
    }
}
