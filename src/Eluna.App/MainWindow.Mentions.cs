using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using Eluna.App.Controls;
using Eluna.App.Models;
using Eluna.App.Services;

namespace Eluna.App;

public partial class MainWindow
{
    internal Popup MentionPopup { get; } = new() { AllowsTransparency = true, StaysOpen = true, Placement = PlacementMode.Top };
    internal ListBox MentionList { get; } = new() { MaxHeight = 260, Focusable = false };
    private bool _acceptingMention;
    private string? _dismissedMention;
    private MentionAdorner? _mentionAdorner;
    internal sealed record MemberSuggestion(AgentAccount? Account, string Name, string Detail, bool All = false);
    private void InitializeMentions()
    {
        MentionPopup.PlacementTarget = InputBox;
        InputBox.Loaded += (_, _) =>
        {
            if (_mentionAdorner is not null || AdornerLayer.GetAdornerLayer(InputBox) is not { } layer) { return; }
            _mentionAdorner = new MentionAdorner(InputBox, () => _viewModel.MentionAccounts, () => _composing) { IsHitTestVisible = false }; layer.Add(_mentionAdorner);
        };
        InputBox.TextChanged += (_, _) => RefreshMentionAdornment(); InputBox.SelectionChanged += (_, _) => RefreshMentionAdornment(); InputBox.SizeChanged += (_, _) => RefreshMentionAdornment();
        InputBox.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler((_, _) => RefreshMentionAdornment()));
        _viewModel.MentionAccounts.CollectionChanged += (_, _) => RefreshMentionAdornment();
        var border = new Border { BorderThickness = new(1), CornerRadius = new(6), Width = 330, Padding = new(6) }; border.SetResourceReference(Border.BackgroundProperty, "SidebarBrush"); border.SetResourceReference(Border.BorderBrushProperty, "StrokeBrush");
        var body = new DockPanel(); var hint = new TextBlock { Text = "↑↓ 選択  ·  Tab / Enter 確定  ·  Esc 閉じる", FontSize = 11, Margin = new(8, 8, 8, 4), TextWrapping = TextWrapping.Wrap }; hint.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush"); DockPanel.SetDock(hint, Dock.Bottom); body.Children.Add(hint); body.Children.Add(MentionList); border.Child = body; MentionPopup.Child = border;
        var template = new DataTemplate(); var panel = new FrameworkElementFactory(typeof(StackPanel)); panel.SetValue(MarginProperty, new Thickness(6, 4, 6, 4));
        var title = new FrameworkElementFactory(typeof(TextBlock)); title.SetBinding(TextBlock.TextProperty, new Binding(nameof(MemberSuggestion.Name))); title.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis); panel.AppendChild(title);
        var detail = new FrameworkElementFactory(typeof(TextBlock)); detail.SetBinding(TextBlock.TextProperty, new Binding(nameof(MemberSuggestion.Detail))); detail.SetValue(TextBlock.FontSizeProperty, 11.0); detail.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush"); detail.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap); panel.AppendChild(detail); template.VisualTree = panel; MentionList.ItemTemplate = template;
        InputBox.TextChanged += (_, _) => UpdateMentions(); InputBox.SelectionChanged += (_, _) => UpdateMentions();
        MentionList.PreviewMouseLeftButtonUp += async (_, e) => { if (MentionList.SelectedItem is MemberSuggestion selected) { e.Handled = true; await AcceptMentionAsync(selected); } };
        _viewModel.NavigationStarting += () => { MentionPopup.IsOpen = false; _dismissedMention = null; };
        PreviewMouseDown += (_, e) => { if (e.OriginalSource is DependencyObject d && !InputBox.IsAncestorOf(d)) { MentionPopup.IsOpen = false; } };
        Deactivated += (_, _) => MentionPopup.IsOpen = false; Closed += (_, _) => MentionPopup.IsOpen = false;
    }
    private void RefreshMentionAdornment() => _mentionAdorner?.InvalidateVisual();
    internal void UpdateMentions(bool requireFocus = true)
    {
        if (_acceptingMention || _composing || requireFocus && !InputBox.IsKeyboardFocusWithin || !_viewModel.IsReady || _viewModel.SelectedConversation is not { } room) { MentionPopup.IsOpen = false; return; }
        MentionQuery? query = MentionService.Completion(InputBox.Text, InputBox.CaretIndex);
        string key = query?.Start + ":" + query?.Query;
        if (query is null || key == _dismissedMention) { MentionPopup.IsOpen = false; return; }
        var state = _viewModel.CaptureState();
        var suggestions = _viewModel.MentionAccounts.Where(a => a.Enabled && a.Name.Contains(query.Query, StringComparison.OrdinalIgnoreCase)).OrderByDescending(a => a.IsLocal || room.Room?.Members.Contains(a.Id) == true).ThenBy(a => a.Name).Take(7).Select(a => new MemberSuggestion(a, a.Icon + " " + a.Name, a.IsLocal ? "この端末のメンバー" : room.Room?.Members.Contains(a.Id) != true ? "未参加 · 招待してメンション" : ChannelAccess.CanSpeak(state, room.Save(), a.Id) ? "参加中" : "閲覧のみ · 発言は制限されています")).ToList();
        if ("全員".Contains(query.Query, StringComparison.OrdinalIgnoreCase) || "all".Contains(query.Query, StringComparison.OrdinalIgnoreCase)) { suggestions.Add(new(null, "@全員", "発言できる全メンバーに呼びかける", true)); }
        MentionList.ItemsSource = suggestions; MentionList.SelectedIndex = 0; MentionPopup.IsOpen = suggestions.Count > 0;
    }
    private bool MentionKey(KeyEventArgs e)
    {
        if (_composing || !MentionPopup.IsOpen) { return false; }
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) && e.Key is Key.Tab or Key.Enter) { return false; }
        if (e.Key is Key.Up or Key.Down) { int count = MentionList.Items.Count; if (count > 0) { MentionList.SelectedIndex = (MentionList.SelectedIndex + (e.Key == Key.Down ? 1 : count - 1)) % count; MentionList.ScrollIntoView(MentionList.SelectedItem); } e.Handled = true; return true; }
        if (e.Key == Key.Escape) { MentionQuery? query = MentionService.Completion(InputBox.Text, InputBox.CaretIndex); _dismissedMention = query?.Start + ":" + query?.Query; MentionPopup.IsOpen = false; e.Handled = true; return true; }
        if (e.Key is Key.Enter or Key.Tab && MentionList.SelectedItem is MemberSuggestion suggestion) { e.Handled = true; _ = AcceptMentionAsync(suggestion); return true; }
        return false;
    }
    internal async Task AcceptMentionAsync(MemberSuggestion suggestion)
    {
        if (_acceptingMention || _viewModel.SelectedConversation is not { } room) { return; }
        MentionQuery? query = MentionService.Completion(InputBox.Text, InputBox.CaretIndex); if (query is null) { return; }
        string before = InputBox.Text; _acceptingMention = true; MentionPopup.IsOpen = false;
        try
        {
            if (suggestion.Account is { IsLocal: false } account && room.Room?.Members.Contains(account.Id) != true) { await _viewModel.InviteMembersAsync(room.Id, [account.Id]); }
            if (_viewModel.SelectedConversation != room || InputBox.Text != before) { return; }
            InputBox.Select(query.Start, query.Length); InputBox.SelectedText = suggestion.All ? "@全員 " : MentionService.Format(suggestion.Account!); InputBox.CaretIndex = query.Start + (suggestion.All ? 4 : MentionService.Format(suggestion.Account!).Length); InputBox.Focus();
        }
        catch (Exception ex) { _viewModel.Error = ex.Message; }
        finally { _acceptingMention = false; _dismissedMention = null; }
    }
}
