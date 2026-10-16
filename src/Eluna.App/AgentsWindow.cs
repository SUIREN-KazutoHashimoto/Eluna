using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Eluna.App.Controls;
using Eluna.App.Models;
using Eluna.App.Services;
using Eluna.App.ViewModels;

namespace Eluna.App;

public sealed class AgentsWindow : Window
{
    private readonly ChatViewModel _chat;
    internal ListBox AgentList { get; } = new() { DisplayMemberPath = nameof(AgentAccount.Label) };
    internal TextBox NameBox { get; } = new();
    internal TextBox IconBox { get; } = new();
    internal TextBox InstructionBox { get; } = new() { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 120, MaxHeight = 240, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly TextBox _specialty = new() { TextWrapping = TextWrapping.Wrap }, _query = new(), _historyText = new() { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, AcceptsReturn = true, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MinHeight = 90 };
    private readonly ComboBox _model = new() { DisplayMemberPath = "Name" }, _scope = new() { DisplayMemberPath = "Value", SelectedValuePath = "Key" }, _origin = new() { DisplayMemberPath = "Name" };
    private readonly CheckBox _enabled = new() { Content = "このエージェントを有効にする" }, _memoryEnabled = new() { Content = "この記憶を参照する", IsChecked = true };
    private readonly ListBox _history = new(), _memories = new() { DisplayMemberPath = nameof(AgentMemory.Text), MaxHeight = 140 };
    private readonly TextBox _memoryText = new() { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 90, MaxHeight = 190, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12 };
    internal Button SaveButton { get; } = new() { Content = "プロフィールを保存", Padding = new(18, 8, 18, 8) };
    private Guid _id, _memoryId = Guid.NewGuid(); private string _baseline = "", _memoryBaseline = ""; private bool _saving, _switching, _loadingMemory;
    private CancellationTokenSource? _historyCancel; private int _page;
    private sealed record Origin(Guid? Id, string Name, Guid? ProjectId);
    private sealed record Entry(Guid RoomId, Guid MessageId, string Place, string Speaker, string Content) { public string Label => Place + " · " + Speaker + "\n" + Content[..Math.Min(180, Content.Length)].Replace('\n', ' '); }
    public AgentsWindow(ChatViewModel chat)
    {
        _chat = chat; Title = "エージェント · Eluna"; Width = 1000; Height = 780; MinWidth = 650; MinHeight = 500; WindowStartupLocation = WindowStartupLocation.CenterOwner; Appearance.Attach(this);
        var root = new Grid { Margin = new(22) }; root.ColumnDefinitions.Add(new() { Width = new(190) }); root.ColumnDefinitions.Add(new() { Width = new(18) }); root.ColumnDefinitions.Add(new()); Content = root;
        var left = new DockPanel(); root.Children.Add(left); var add = new Button { Content = "＋ エージェントを作成", Margin = new(0, 0, 0, 12) }; DockPanel.SetDock(add, Dock.Top); left.Children.Add(add);
        AgentList.ItemsSource = chat.Agents; left.Children.Add(AgentList);
        var right = new DockPanel(); Grid.SetColumn(right, 2); root.Children.Add(right);
        var footer = new StackPanel { Margin = new(0, 12, 0, 0) }; DockPanel.SetDock(footer, Dock.Bottom); right.Children.Add(footer); footer.Children.Add(_status);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right }; footer.Children.Add(buttons);
        var close = new Button { Content = "閉じる", Margin = new(0, 0, 8, 0) }; close.Click += (_, _) => Close(); buttons.Children.Add(close); SaveButton.SetResourceReference(StyleProperty, "PrimaryButton"); buttons.Children.Add(SaveButton);
        var tabs = new TabControl { Background = System.Windows.Media.Brushes.Transparent, BorderThickness = new(0) }; right.Children.Add(tabs);
        StackPanel Tab(string name) { var panel = new StackPanel { Margin = new(0, 12, 8, 8) }; var tab = new TabItem { Header = name, Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } }; tab.SetResourceReference(StyleProperty, "WorkOptionsTab"); tabs.Items.Add(tab); return panel; }
        void Field(StackPanel panel, string label, FrameworkElement control) { panel.Children.Add(new TextBlock { Text = label, Margin = new(0, 10, 0, 6), TextWrapping = TextWrapping.Wrap }); panel.Children.Add(control); System.Windows.Automation.AutomationProperties.SetName(control, label); }
        StackPanel profile = Tab("プロフィール"); Field(profile, "名前", NameBox); Field(profile, "アイコン（文字・絵文字）", IconBox); Field(profile, "使用モデル", _model); Field(profile, "専門分野", _specialty); Field(profile, "基本指示・話し方", InstructionBox); _enabled.Margin = new(0, 14, 0, 0); profile.Children.Add(_enabled);
        profile.Children.Add(new TextBlock { Text = "モデルを変更してもアカウントと記憶は引き継ぎます。過去の発言の名前・モデルは保持します。無効化したエージェントの履歴も残ります。", TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new(0, 14, 0, 0) });
        StackPanel history = Tab("全会話履歴"); Field(history, "参加した会話を検索", _query); _history.DisplayMemberPath = nameof(Entry.Label); _history.MinHeight = 160; _history.MaxHeight = 260; _history.Margin = new(0, 12, 0, 8); history.Children.Add(_history); history.Children.Add(_historyText);
        var historyActions = new WrapPanel { Margin = new(0, 8, 0, 0) }; history.Children.Add(historyActions);
        var open = new Button { Content = "元の発言を開く" }; historyActions.Children.Add(open); open.Click += (_, _) => { if (_history.SelectedItem is Entry entry) { chat.OpenMessage(entry.RoomId, entry.MessageId); } };
        var more = new Button { Content = "次の200件", Margin = new(8, 0, 0, 0) }; historyActions.Children.Add(more); more.Click += async (_, _) => { _page++; await SearchHistory(); };
        _history.SelectionChanged += (_, _) => _historyText.Text = (_history.SelectedItem as Entry)?.Content ?? "";
        _query.TextChanged += async (_, _) => { _page = 0; await SearchHistory(); };
        StackPanel memory = Tab("保存メモリ"); memory.Children.Add(new TextBlock { Text = "履歴は自動で保存されます。ここでは明示的に覚えておく内容と共有範囲を管理します。", TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 10) }); memory.Children.Add(_memories);
        Field(memory, "記憶する内容（4,000文字まで）", _memoryText);
        _scope.ItemsSource = new[] { new KeyValuePair<string, string>("room", "この会話だけ"), new("server", "このサーバー内"), new("agent", "受け入れを許可した参加先へ持ち越す") }; _scope.SelectedValue = "room";
        Field(memory, "参照範囲", _scope); Field(memory, "作成元の会話", _origin); _memoryEnabled.Margin = new(0, 12, 0, 8); memory.Children.Add(_memoryEnabled);
        var memoryActions = new WrapPanel(); memory.Children.Add(memoryActions);
        var newMemory = new Button { Content = "新しい記憶" }; memoryActions.Children.Add(newMemory); newMemory.Click += async (_, _) => { if (await LeaveMemoryDraft()) { ClearMemory(); } };
        var saveMemory = new Button { Content = "記憶を保存", Margin = new(8, 0, 0, 0) }; memoryActions.Children.Add(saveMemory);
        saveMemory.Click += async (_, _) => await SaveMemory();
        var deleteMemory = new Button { Content = "選択した記憶を削除", Margin = new(8, 0, 0, 0) }; memoryActions.Children.Add(deleteMemory);
        deleteMemory.Click += async (_, _) => { if (_memories.SelectedItem is AgentMemory m && MessageBox.Show(this, "この保存メモリを削除します。過去の回答に引用済みの内容は残ります。", "記憶の削除", MessageBoxButton.YesNo) == MessageBoxResult.Yes) { await chat.DeleteAgentMemoryAsync(m); RefreshMemories(); ClearMemory(); } };
        _memories.SelectionChanged += async (_, _) =>
        {
            if (_loadingMemory || _memories.SelectedItem is not AgentMemory m) { return; }
            if (!await LeaveMemoryDraft()) { _loadingMemory = true; _memories.SelectedItem = chat.AgentMemories.FirstOrDefault(n => n.Id == _memoryId); _loadingMemory = false; return; }
            _memoryId = m.Id; _memoryText.Text = m.Text; _scope.SelectedValue = m.Scope; _memoryEnabled.IsChecked = m.Enabled; _origin.SelectedItem = _origin.Items.Cast<Origin>().FirstOrDefault(o => o.Id == m.SourceConversationId); _memoryBaseline = MemorySnapshot();
        };
        SaveButton.Click += async (_, _) => await SaveProfile();
        add.Click += async (_, _) => { if (_saving || !await LeaveDraft()) { return; } _switching = true; AgentList.SelectedItem = null; _switching = false; Fill(new(Guid.NewGuid(), "新しいエージェント")); NameBox.Focus(); };
        AgentList.SelectionChanged += async (_, _) =>
        {
            if (_switching || _saving || AgentList.SelectedItem is not AgentAccount next) { return; }
            if (!await LeaveDraft()) { _switching = true; AgentList.SelectedItem = chat.Agents.FirstOrDefault(a => a.Id == _id); _switching = false; return; }
            Fill(next); await SearchHistory();
        };
        UnsavedChanges.Protect(this, () => Snapshot() != _baseline || MemoryDirty, SaveAll);
        Closed += (_, _) => { _historyCancel?.Cancel(); _historyCancel?.Dispose(); };
        if (chat.Agents.FirstOrDefault() is { } first) { Fill(first); AgentList.SelectedItem = first; }
    }
    private string Snapshot() => JsonSerializer.Serialize(new { NameBox.Text, Icon = IconBox.Text, Instruction = InstructionBox.Text, Specialty = _specialty.Text, Model = (_model.SelectedItem as DebateModelChoice)?.Id, Enabled = _enabled.IsChecked });
    private void Fill(AgentAccount agent)
    {
        _id = agent.Id; NameBox.Text = agent.Name; IconBox.Text = agent.Icon; InstructionBox.Text = agent.Instruction; _specialty.Text = agent.Specialty; _enabled.IsChecked = agent.Enabled;
        var models = new List<DebateModelChoice> { new("", "未指定（既存のモデル設定を使用）", "") }; models.AddRange(_chat.Models.Select(m => new DebateModelChoice(m.Id, m.DisplayName, m.SizeLabel)));
        if (agent.ModelId is string missing && !models.Any(m => m.Id == missing)) { models.Add(new(missing, "未取得のモデル · " + missing, "モデルを再取得するか選び直してください", false)); }
        _model.ItemsSource = models; _model.SelectedItem = models.First(m => m.Id == (agent.ModelId ?? "")); _baseline = Snapshot(); _status.Text = "保存済み"; RefreshMemories();
        _origin.ItemsSource = new[] { new Origin(null, "作成場所を指定しない（持ち越し記憶用）", null) }.Concat(_chat.Conversations.Where(c => WorkspaceService.Participated(c.Save(), agent.Id)).Select(c => new Origin(c.Id, (_chat.Projects.FirstOrDefault(p => p.Id == c.ProjectId)?.Name ?? "DM・個人") + " / " + c.Title, c.ProjectId))).ToArray();
        _origin.SelectedItem = _origin.Items.Cast<Origin>().FirstOrDefault(o => o.Id == _chat.SelectedConversation?.Id) ?? _origin.Items[0];
        ClearMemory();
    }
    private void RefreshMemories() { _loadingMemory = true; _memories.ItemsSource = _chat.AgentMemories.Where(m => m.AgentId == _id).ToArray(); _loadingMemory = false; }
    private string MemorySnapshot() => JsonSerializer.Serialize(new { _memoryText.Text, _scope.SelectedValue, Source = (_origin.SelectedItem as Origin)?.Id, _memoryEnabled.IsChecked });
    private bool MemoryDirty => _memoryBaseline.Length > 0 && MemorySnapshot() != _memoryBaseline;
    private void ClearMemory()
    { _loadingMemory = true; _memories.SelectedItem = null; _memoryId = Guid.NewGuid(); _memoryText.Text = ""; _scope.SelectedValue = "room"; _memoryEnabled.IsChecked = true; _memoryBaseline = MemorySnapshot(); _loadingMemory = false; }
    private async Task<bool> SaveMemory()
    {
        try
        {
            if (!_chat.Agents.Any(a => a.Id == _id)) { throw new InvalidOperationException("先にプロフィールを保存してください。"); }
            Guid agentId = _id, memoryId = _memoryId; string savedSnapshot = MemorySnapshot();
            Origin? source = _origin.SelectedItem as Origin;
            await _chat.SaveAgentMemoryAsync(new(_memoryId, _id, _memoryText.Text.Trim(), (string)_scope.SelectedValue, source?.Id, source?.ProjectId, _memoryEnabled.IsChecked == true, DateTimeOffset.UtcNow));
            if (_id == agentId && _memoryId == memoryId) { _memoryBaseline = savedSnapshot; RefreshMemories(); _status.Text = "記憶を保存しました。参照設定を変更したため、関連する進行中の処理と予約を停止しました。"; }
            return true;
        }
        catch (Exception ex) { _status.Text = ex.Message; return false; }
    }
    private async Task<bool> LeaveMemoryDraft()
    { if (!MemoryDirty) { return true; } bool? choice = UnsavedChanges.Choose(this); return choice == false || choice == true && await SaveMemory(); }
    private async Task<bool> SaveAll() => (Snapshot() == _baseline || await SaveProfile()) && (!MemoryDirty || await SaveMemory());
    private async Task<bool> LeaveDraft()
    { if (_baseline.Length == 0 || Snapshot() == _baseline && !MemoryDirty) { return true; } bool? choice = UnsavedChanges.Choose(this); return choice == false || choice == true && await SaveAll(); }
    private async Task<bool> SaveProfile()
    {
        if (_saving) { return false; }
        _saving = true; SaveButton.IsEnabled = false; AgentList.IsEnabled = false; string savedSnapshot = Snapshot();
        try { string? model = (_model.SelectedItem as DebateModelChoice)?.Id; await _chat.SaveAgentAsync(new(_id, NameBox.Text.Trim(), IconBox.Text.Trim(), InstructionBox.Text, _specialty.Text, string.IsNullOrEmpty(model) ? null : model, _enabled.IsChecked == true)); _baseline = savedSnapshot; _status.Text = Snapshot() == _baseline ? "保存しました" : "保存しました。その後の編集は未保存です。"; return true; }
        catch (Exception ex) { _status.Text = ex.Message; return false; }
        finally { _saving = false; SaveButton.IsEnabled = true; AgentList.IsEnabled = true; }
    }
    private async Task SearchHistory()
    {
        _historyCancel?.Cancel(); _historyCancel?.Dispose(); _historyCancel = new(); CancellationToken token = _historyCancel.Token;
        Guid agent = _id; string query = _query.Text.Trim(); int page = _page; AppState state = _chat.CaptureState();
        try
        {
            await Task.Delay(120, token);
            Entry[] entries = await Task.Run(() =>
            {
                var results = new List<Entry>();
                foreach (SavedConversation source in state.Conversations.OrderByDescending(c => c.UpdatedAt))
                {
                    token.ThrowIfCancellationRequested(); if (!WorkspaceService.Participated(source, agent)) { continue; }
                    foreach (SavedMessage m in WorkspaceService.Messages(source).Reverse())
                    {
                        if (m.Content.Length > 0 && (query.Length == 0 || (source.Title + m.Content).Contains(query, StringComparison.OrdinalIgnoreCase))) { results.Add(new(source.Id, m.Id ?? Guid.Empty, source.Title, m.Identity?.Name ?? m.Role, m.Content)); }
                        foreach (DebateTurn turn in m.Work?.Turns ?? [])
                        { if (query.Length == 0 || turn.Content.Contains(query, StringComparison.OrdinalIgnoreCase)) { results.Add(new(source.Id, turn.Id ?? WorkspaceService.StableId(m.Work!.Id, turn.Round + ":" + turn.Role), source.Title, turn.Name ?? turn.Role, turn.Content)); } }
                    }
                }
                return results.Skip(page * 200).Take(200).ToArray();
            }, token);
            if (!token.IsCancellationRequested) { _history.ItemsSource = entries; }
        }
        catch (OperationCanceledException) { }
    }
}
