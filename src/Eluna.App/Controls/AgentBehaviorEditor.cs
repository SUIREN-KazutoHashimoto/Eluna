using System.Windows;
using System.Windows.Controls;
using Eluna.App.Models;
using Eluna.App.Services;

namespace Eluna.App.Controls;

internal sealed class AgentBehaviorEditor : StackPanel
{
    internal ComboBox Preset { get; } = new() { ItemsSource = AgentPersonas.All, DisplayMemberPath = "Name", SelectedValuePath = "Id" };
    internal TextBox Role { get; } = new() { MaxLength = 500, TextWrapping = TextWrapping.Wrap };
    private readonly TextBox _setting = new() { MaxLength = 2000, TextWrapping = TextWrapping.Wrap, AcceptsReturn = true, MinHeight = 58, MaxHeight = 130, VerticalScrollBarVisibility = ScrollBarVisibility.Auto },
        _traits = new() { MaxLength = 2000, TextWrapping = TextWrapping.Wrap, MinHeight = 40, MaxHeight = 100, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly StackPanel _connections = new();
    internal CheckBox Automatic { get; } = new() { Content = "依頼に応じてチームと作業場所を自動編成する" };
    private readonly CheckBox _servers = new() { Content = "サーバー作成を許可" }, _channels = new() { Content = "チャンネル作成を許可" },
        _invite = new() { Content = "つながりのあるアカウントを招待" }, _accept = new() { Content = "他のアカウントからの自動招待を受ける" };
    internal AgentBehaviorEditor()
    {
        void Field(string label, FrameworkElement control) { Children.Add(new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap, Margin = new(0, 12, 0, 6) }); Children.Add(control); System.Windows.Automation.AutomationProperties.SetName(control, label); }
        Field("性格", Preset); Field("役柄・キャラクター名（任意）", Role); Field("世界観・背景（任意）", _setting); Field("口調・性格の補足（任意）", _traits);
        var details = new StackPanel(); var expander = new Expander { Header = "つながり・自動編成", Content = details, Margin = new(0, 16, 0, 0) }; Children.Add(expander);
        details.Children.Add(new TextBlock { Text = "招待先には依頼文と担当の結果を共有します。元の履歴・資料はコピーしません。実行には依頼元チャンネルの「招待」「作業場所の作成」権限も必要です。最大3チャンネル・6担当で終了します。", TextWrapping = TextWrapping.Wrap, Margin = new(0, 10, 0, 8), FontSize = 12 });
        foreach (CheckBox box in new[] { Automatic, _servers, _channels, _invite, _accept }) { box.Content = new TextBlock { Text = (string)box.Content, TextWrapping = TextWrapping.Wrap }; box.Margin = new(0, 6, 0, 6); details.Children.Add(box); }
        details.Children.Add(new TextBlock { Text = "つながり（招待候補）", Margin = new(0, 12, 0, 6) }); details.Children.Add(new ScrollViewer { Content = _connections, MaxHeight = 180, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
    }
    internal void Load(AgentAccount account, IEnumerable<AgentAccount> agents)
    {
        AgentPersonality p = account.Personality ?? new(); Preset.SelectedValue = p.Preset; Role.Text = p.Role; _setting.Text = p.Setting; _traits.Text = p.Traits;
        AgentAutonomy a = account.Autonomy ?? new(); Automatic.IsChecked = a.Automatic; _servers.IsChecked = a.CreateServers; _channels.IsChecked = a.CreateChannels; _invite.IsChecked = a.InviteConnections; _accept.IsChecked = a.AcceptInvites;
        _connections.Children.Clear();
        foreach (AgentAccount other in agents.Where(a => a.Id != account.Id)) { _connections.Children.Add(new CheckBox { Content = new TextBlock { Text = other.Label, TextWrapping = TextWrapping.Wrap }, Tag = other.Id, IsChecked = account.Connections?.Contains(other.Id) == true, Margin = new(0, 5, 0, 5) }); }
    }
    internal AgentPersonality Personality => new(Preset.SelectedValue as string ?? "default", Role.Text.Trim(), _setting.Text.Trim(), _traits.Text.Trim());
    internal void ApplyPersonality(AgentPersonality p) { Preset.SelectedValue = p.Preset; Role.Text = p.Role; _setting.Text = p.Setting; _traits.Text = p.Traits; }
    internal AgentAutonomy Autonomy => new(Automatic.IsChecked == true, _servers.IsChecked == true, _channels.IsChecked == true, _invite.IsChecked == true, _accept.IsChecked == true);
    internal List<Guid> Connections => _connections.Children.OfType<CheckBox>().Where(b => b.IsChecked == true).Select(b => (Guid)b.Tag).ToList();
}
