using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Eluna.App.Models;
using Eluna.App.Services;

namespace Eluna.App.Controls;

public sealed class AgentMembersEditor : UserControl
{
    private readonly string _format = "Eluna.AgentMembers." + Guid.NewGuid().ToString("N");
    private readonly ObservableCollection<AgentAccount> _members = [];
    private readonly ListBox _available = new() { DisplayMemberPath = nameof(AgentAccount.Label) }, _selected = new() { DisplayMemberPath = nameof(AgentAccount.Label) };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 0), FontSize = 12 };
    private Point _start;
    private readonly int _maximum;
    private readonly bool _includeLocal;
    public IReadOnlyList<Guid> SelectedIds => _members.Select(a => a.Id).ToList();
    public event Action? Changed;
    public AgentMembersEditor(IEnumerable<AgentAccount> accounts, IEnumerable<Guid> selected, int maximum = AccountDefaults.MaximumAgents, bool includeLocal = true)
    {
        _maximum = maximum; _includeLocal = includeLocal;
        AgentAccount[] all = accounts.ToArray(); _available.ItemsSource = all.Where(a => a.Enabled).ToArray();
        foreach (Guid id in selected) { if (all.FirstOrDefault(a => a.Id == id) is { } account) { _members.Add(account); } }
        _selected.ItemsSource = _members;
        var grid = new Grid(); grid.ColumnDefinitions.Add(new()); grid.ColumnDefinitions.Add(new() { Width = new(74) }); grid.ColumnDefinitions.Add(new());
        grid.RowDefinitions.Add(new() { Height = GridLength.Auto }); grid.RowDefinitions.Add(new() { Height = new(190) }); grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
        grid.Children.Add(new TextBlock { Text = "アカウント", Margin = new(0, 0, 0, 8) });
        var heading = new TextBlock { Text = "参加者", ToolTip = "ドラッグで追加・並べ替え", Margin = new(0, 0, 0, 8), TextWrapping = TextWrapping.Wrap }; Grid.SetColumn(heading, 2); grid.Children.Add(heading);
        Grid.SetRow(_available, 1); grid.Children.Add(_available); Grid.SetRow(_selected, 1); Grid.SetColumn(_selected, 2); grid.Children.Add(_selected);
        System.Windows.Automation.AutomationProperties.SetName(_available, "参加できるエージェント"); System.Windows.Automation.AutomationProperties.SetName(_selected, "参加者と表示順");
        var actions = new StackPanel { VerticalAlignment = VerticalAlignment.Center }; Grid.SetRow(actions, 1); Grid.SetColumn(actions, 1); grid.Children.Add(actions);
        void Button(string text, Action action) { var b = new Button { Content = text, Padding = new(4), Margin = new(3), ToolTip = text }; b.Click += (_, _) => action(); actions.Children.Add(b); }
        Button("追加 →", () => { if (_available.SelectedItem is AgentAccount a) { Add(a, _members.Count); } });
        Button("← 外す", () => { if (_selected.SelectedItem is AgentAccount a) { _members.Remove(a); Update(); } });
        Button("↑", () => Move(-1)); Button("↓", () => Move(1));
        Grid.SetRow(_status, 2); Grid.SetColumnSpan(_status, 3); _status.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush"); grid.Children.Add(_status);
        Content = grid;
        foreach (ListBox list in new[] { _available, _selected })
        {
            list.AllowDrop = true; list.PreviewMouseLeftButtonDown += (_, e) => _start = e.GetPosition(list);
            list.MouseMove += (_, e) =>
            {
                Point p = e.GetPosition(list);
                if (e.LeftButton != MouseButtonState.Pressed || Math.Abs(p.X - _start.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(p.Y - _start.Y) < SystemParameters.MinimumVerticalDragDistance || list.SelectedItem is not AgentAccount account) { return; }
                var data = new DataObject(); data.SetData(_format, account.Id.ToString("N"), false); DragDrop.DoDragDrop(list, data, DragDropEffects.Move | DragDropEffects.Copy);
            };
            list.DragOver += (_, e) => { e.Effects = Read(e.Data, all) is null ? DragDropEffects.None : DragDropEffects.Move; e.Handled = true; };
            list.Drop += (_, e) =>
            {
                AgentAccount? account = Read(e.Data, all); if (account is null) { return; }
                if (list == _available) { _members.Remove(account); Update(); }
                else
                {
                    int index = _members.Count;
                    DependencyObject? hit = list.InputHitTest(e.GetPosition(list)) as DependencyObject;
                    while (hit is not null && hit is not ListBoxItem) { hit = VisualTreeHelper.GetParent(hit); }
                    if (hit is ListBoxItem item && item.DataContext is AgentAccount target) { index = _members.IndexOf(target) + (e.GetPosition(item).Y > item.ActualHeight / 2 ? 1 : 0); }
                    Add(account, index);
                }
                e.Handled = true;
            };
        }
        Update();
    }
    internal bool Accepts(IDataObject data) => data.GetDataPresent(_format, false);
    private AgentAccount? Read(IDataObject data, AgentAccount[] accounts) => data.GetDataPresent(_format, false) && data.GetData(_format, false) is string value && Guid.TryParse(value, out Guid id) ? accounts.FirstOrDefault(a => a.Id == id) : null;
    internal void Add(AgentAccount account, int index)
    {
        int old = _members.IndexOf(account);
        if (old >= 0) { _members.RemoveAt(old); if (old < index) { index--; } }
        else if (_members.Count >= _maximum || !account.Enabled) { _status.Text = _includeLocal ? "参加者は自分を含めて50名までです。" : "この議論の参加者は" + _maximum + "名までです。"; return; }
        _members.Insert(Math.Clamp(index, 0, _members.Count), account); _selected.SelectedItem = account; Update();
    }
    private void Move(int delta)
    { int index = _selected.SelectedIndex; if (index >= 0 && index + delta >= 0 && index + delta < _members.Count) { _members.Move(index, index + delta); Update(); } }
    private void Update() { _status.Text = $"{_members.Count + (_includeLocal ? 1 : 0)}/{_maximum + (_includeLocal ? 1 : 0)}名" + (_includeLocal ? "（自分を含む）" : "（議論の参加者）") + " · 左へドロップすると退出。↑↓でも並べ替えできます。"; Changed?.Invoke(); }
}
