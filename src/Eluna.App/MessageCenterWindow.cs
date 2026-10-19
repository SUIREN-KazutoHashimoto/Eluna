using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using Eluna.App.Models;
using Eluna.App.Services;
using Eluna.App.ViewModels;

namespace Eluna.App;

public sealed class MessageCenterWindow : Window
{
    private readonly ChatViewModel _chat;
    private readonly Guid? _sourceRoom, _sourceServer;
    private readonly ObservableCollection<MessageLocation> _rows = [];
    private readonly ComboBox _mode = new() { DisplayMemberPath = "Value", SelectedValuePath = "Key", MinWidth = 100 }, _scope = new() { DisplayMemberPath = "Value", SelectedValuePath = "Key", MinWidth = 100 };
    internal TextBox Query { get; } = new() { MaxLength = 2000, Padding = new(10, 9, 10, 9) };
    internal ListBox Results { get; } = new() { HorizontalContentAlignment = HorizontalAlignment.Stretch };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 8), FontSize = 12 }, _empty = new() { TextWrapping = TextWrapping.Wrap, Margin = new(14), Text = "一致する発言はありません。検索語や表示範囲を変えてください。" };
    private readonly Button _read = new() { Content = "選択を既読にする", Margin = new(0, 0, 8, 8) }, _unpin = new() { Content = "選択のピンを解除", Margin = new(0, 0, 8, 8) };
    private readonly Button _go = new() { Content = "発言へ移動", Margin = new(0, 0, 0, 8), IsEnabled = false };
    private CancellationTokenSource? _search;
    private bool _ready, _composing, _closed, _hasValidResults;
    internal Task SearchFinished { get; private set; } = Task.CompletedTask;
    public MessageCenterWindow(ChatViewModel chat, string mode = "unread", string query = "", bool all = false)
    {
        _chat = chat; _sourceRoom = chat.SelectedConversation?.Id; _sourceServer = chat.Settings.SelectedProjectId;
        Title = "メッセージを探す · Eluna"; Width = 720; Height = 640; MinWidth = 430; MinHeight = 500; WindowStartupLocation = WindowStartupLocation.CenterOwner; Appearance.Attach(this);
        var root = new DockPanel { Margin = new(20) }; Content = root;
        var heading = new StackPanel(); DockPanel.SetDock(heading, Dock.Top); root.Children.Add(heading); heading.Children.Add(new TextBlock { Text = "メッセージ", FontSize = 24, FontWeight = FontWeights.SemiBold, Margin = new(0, 0, 0, 12) });
        var choices = new WrapPanel { Margin = new(0, 0, 0, 10) }; heading.Children.Add(choices); choices.Children.Add(_mode); _mode.Margin = new(0, 0, 10, 6); choices.Children.Add(_scope); _scope.Margin = new(0, 0, 0, 6);
        _mode.ItemsSource = new[] { new KeyValuePair<string, string>("unread", "未読"), new("mentions", "メンション"), new("search", "検索"), new("pins", "ピン留め") }; _mode.SelectedValue = mode;
        _scope.ItemsSource = new[] { new KeyValuePair<string, string>("room", "開いた会話"), new("server", _sourceServer is null ? "DM全体" : "開いたサーバー"), new("all", "すべての場所") }; _scope.SelectedValue = all || mode is "unread" or "mentions" ? "all" : "room";
        Query.Text = query; heading.Children.Add(Query); heading.Children.Add(new TextBlock { Text = "本文・名前・場所で検索。例: from:星乃 in:相談  /  Enterで移動", TextWrapping = TextWrapping.Wrap, FontSize = 11, Margin = new(0, 7, 0, 2) }); heading.Children.Add(_status);
        var footer = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = new(0, 12, 0, 0) }; DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer); footer.Children.Add(_read); footer.Children.Add(_unpin);
        _go.SetResourceReference(StyleProperty, "PrimaryButton"); footer.Children.Add(_go); _go.Click += (_, _) => OpenSelected();
        _read.Click += (_, _) => { if (Results.SelectedItem is MessageLocation row) { chat.MarkReadMessage(row.Room, row.Branch, row.Message); } };
        _unpin.Click += async (_, _) => { if (Results.SelectedItem is MessageLocation row && row.Pinned) { try { await chat.ToggleMessagePinAsync(row.Room, row.Branch, row.Message); } catch (Exception ex) { _status.Text = ex.Message; } } };
        var body = new Grid(); root.Children.Add(body); Results.ItemsSource = _rows; body.Children.Add(Results); body.Children.Add(_empty);
        var template = new DataTemplate(); var panel = new FrameworkElementFactory(typeof(StackPanel)); panel.SetValue(FrameworkElement.MarginProperty, new Thickness(10, 9, 10, 9));
        void Line(string property, double size, bool muted)
        { var line = new FrameworkElementFactory(typeof(TextBlock)); line.SetBinding(TextBlock.TextProperty, new Binding(property)); line.SetValue(TextBlock.FontSizeProperty, size); line.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap); line.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 0, 5)); if (muted) { line.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush"); } panel.AppendChild(line); }
        Line("Heading", 13, false); Line("Place", 11, true); Line("Preview", 13, false); template.VisualTree = panel; Results.ItemTemplate = template;
        System.Windows.Automation.AutomationProperties.SetName(Query, "発言の検索語"); System.Windows.Automation.AutomationProperties.SetName(Results, "メッセージの結果"); System.Windows.Automation.AutomationProperties.SetName(_mode, "メッセージの表示種類"); System.Windows.Automation.AutomationProperties.SetName(_scope, "メッセージの表示範囲");
        _mode.SelectionChanged += (_, _) => Refresh(); _scope.SelectionChanged += (_, _) => Refresh(); Query.TextChanged += (_, _) => Refresh(); Results.SelectionChanged += (_, _) => UpdateActions(); Results.MouseDoubleClick += (_, _) => OpenSelected();
        TextCompositionManager.AddPreviewTextInputStartHandler(Query, (_, _) => _composing = true); TextCompositionManager.AddPreviewTextInputHandler(Query, (_, _) => _composing = false);
        PreviewKeyDown += (_, e) => { if (_composing || e.Key == Key.ImeProcessed) { return; } if (e.Key == Key.Escape) { e.Handled = true; Close(); } else if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None) { e.Handled = true; OpenSelected(); } else if (Query.IsKeyboardFocusWithin && e.Key == Key.Down && Results.Items.Count > 0) { e.Handled = true; Results.Focus(); Results.SelectedIndex = Math.Max(0, Results.SelectedIndex); } };
        chat.MessageExperienceChanged += ExperienceChanged; Closed += (_, _) => { _closed = true; chat.MessageExperienceChanged -= ExperienceChanged; _search?.Cancel(); _search?.Dispose(); };
        Loaded += (_, _) => Query.Focus(); UiMotion.SetFeedback(root, "entrance"); _ready = true; Refresh();
    }
    private void ExperienceChanged() => Refresh();
    private void UpdateActions() { _read.IsEnabled = _hasValidResults && Results.SelectedItem is MessageLocation { Unread: true }; _unpin.IsEnabled = _hasValidResults && Results.SelectedItem is MessageLocation { Pinned: true }; _go.IsEnabled = _hasValidResults && Results.SelectedItem is MessageLocation; _read.Visibility = _mode.SelectedValue is "unread" or "mentions" ? Visibility.Visible : Visibility.Collapsed; _unpin.Visibility = _mode.SelectedValue is "pins" ? Visibility.Visible : Visibility.Collapsed; }
    private void Refresh() { if (_ready && !_closed) { _search?.Cancel(); _search?.Dispose(); _search = new(); _hasValidResults = false; UpdateActions(); if (_rows.Count == 0) { _empty.Visibility = Visibility.Collapsed; } _status.Text = "探しています…"; SearchFinished = RefreshAsync(_search.Token); } }
    private async Task RefreshAsync(CancellationToken token)
    {
        string mode = (string?)_mode.SelectedValue ?? "search", scope = (string?)_scope.SelectedValue ?? "room", query = Query.Text;
        try
        {
            await Task.Delay(100, token); _status.Text = "探しています…";
            var names = _chat.Projects.ToDictionary(p => p.Id, p => p.Name); var snapshot = new List<MessageLocation>(); int count = 0;
            foreach (Conversation room in _chat.Conversations.Where(c => !c.IsArchived && (scope == "all" || scope == "server" && c.ProjectId == _sourceServer || scope == "room" && c.Id == _sourceRoom)).ToArray())
            {
                if (mode is "unread" or "mentions" && room.Experience.Muted) { continue; }
                foreach (MessageLocation row in MessageLocations.In(room, names)) { token.ThrowIfCancellationRequested(); snapshot.Add(row); if (++count % 500 == 0) { await Task.Yield(); } }
            }
            MessageSearchResult found = await Task.Run(() => MessageSearch.Find(snapshot, query, mode, token), token); token.ThrowIfCancellationRequested();
            MessageLocation? selected = Results.SelectedItem as MessageLocation;
            for (int i = 0; i < found.Matches.Count; i++)
            {
                MessageLocation value = found.Matches[i]; int old = -1; for (int j = i; j < _rows.Count; j++) { if ((_rows[j].Room, _rows[j].Branch, _rows[j].Message) == (value.Room, value.Branch, value.Message)) { old = j; break; } }
                if (old >= 0) { if (old != i) { _rows.Move(old, i); } if (_rows[i] != value) { _rows[i] = value; } } else { _rows.Insert(i, value); }
            }
            while (_rows.Count > found.Matches.Count) { _rows.RemoveAt(_rows.Count - 1); }
            _hasValidResults = true;
            Results.SelectedItem = selected is null ? _rows.FirstOrDefault() : _rows.FirstOrDefault(r => (r.Room, r.Branch, r.Message) == (selected.Room, selected.Branch, selected.Message)) ?? _rows.FirstOrDefault();
            _empty.Visibility = _rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed; _status.Text = found.Total + " 件" + (found.Total > 200 ? " · 新しい順に200件を表示。検索を絞ってください。" : "") + " · " + ((KeyValuePair<string, string>)_scope.SelectedItem).Value; UpdateActions();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or System.Text.RegularExpressions.RegexMatchTimeoutException) { if (!token.IsCancellationRequested) { _status.Text = ex.Message; } }
    }
    internal bool OpenSelected()
    {
        if (!_hasValidResults || Results.SelectedItem is not MessageLocation row) { return false; }
        if (!_chat.OpenMessage(row.Room, row.Branch, row.Message)) { _status.Text = "この発言は移動または削除されています。"; Refresh(); return false; }
        _chat.MarkReadMessage(row.Room, row.Branch, row.Message); Close(); return true;
    }
}
