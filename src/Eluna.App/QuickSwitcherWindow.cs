using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using Eluna.App.Services;
using Eluna.App.ViewModels;

namespace Eluna.App;

public sealed class QuickSwitcherWindow : Window
{
    private readonly ChatViewModel _chat;
    internal TextBox Query { get; } = new() { Padding = new(14, 12, 14, 12), FontSize = 18 };
    internal ListBox Results { get; } = new() { HorizontalContentAlignment = HorizontalAlignment.Stretch };
    private readonly TextBlock _empty = new() { Text = "見つかりません。名前や検索の種類を変えてください。", TextWrapping = TextWrapping.Wrap, Margin = new(14, 18, 14, 18) };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Margin = new(2, 8, 2, 0), FontSize = 12 };
    private bool _opening, _composing;
    public QuickSwitcherWindow(ChatViewModel chat)
    {
        _chat = chat; Title = "会話へ移動 · Eluna"; Width = 600; Height = 480; MinWidth = 380; MinHeight = 380; WindowStartupLocation = WindowStartupLocation.CenterOwner; ShowInTaskbar = false; Appearance.Attach(this);
        var root = new DockPanel { Margin = new(18) }; Content = root;
        var heading = new StackPanel { Margin = new(0, 0, 0, 12) }; DockPanel.SetDock(heading, Dock.Top); root.Children.Add(heading);
        heading.Children.Add(new TextBlock { Text = "どこへ移動しますか？", FontSize = 21, FontWeight = FontWeights.SemiBold, Margin = new(2, 0, 0, 12) }); heading.Children.Add(Query);
        var hints = new WrapPanel { Margin = new(2, 10, 0, 0) }; heading.Children.Add(hints);
        foreach (string label in new[] { "# チャンネル", "@ DM・メンバー", "* サーバー", "! ボイス" }) { var hint = new TextBlock { Text = label, FontSize = 12, Margin = new(0, 0, 14, 3) }; hint.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush"); hints.Children.Add(hint); }
        var footer = new DockPanel { Margin = new(0, 10, 0, 0) }; DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        var cancel = new Button { Content = "閉じる · Esc", HorizontalAlignment = HorizontalAlignment.Right }; DockPanel.SetDock(cancel, Dock.Right); footer.Children.Add(cancel); cancel.Click += (_, _) => Close();
        footer.Children.Add(new TextBlock { Text = "↑↓ 選択    Enter 移動", FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
        DockPanel.SetDock(_status, Dock.Bottom); root.Children.Add(_status);
        var body = new Grid(); root.Children.Add(body); body.Children.Add(Results); body.Children.Add(_empty);
        var template = new DataTemplate(); var row = new FrameworkElementFactory(typeof(DockPanel));
        var icon = new FrameworkElementFactory(typeof(TextBlock)); icon.SetBinding(TextBlock.TextProperty, new Binding("Icon")); icon.SetValue(FrameworkElement.WidthProperty, 36.0); icon.SetValue(TextBlock.FontSizeProperty, 20.0); icon.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center); icon.SetValue(FrameworkElement.MarginProperty, new Thickness(8, 0, 0, 0)); icon.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush"); row.AppendChild(icon);
        var stack = new FrameworkElementFactory(typeof(StackPanel)); stack.SetValue(FrameworkElement.MarginProperty, new Thickness(10, 7, 10, 7));
        var name = new FrameworkElementFactory(typeof(TextBlock)); name.SetBinding(TextBlock.TextProperty, new Binding("Name")); name.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold); name.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis); stack.AppendChild(name);
        var detail = new FrameworkElementFactory(typeof(TextBlock)); detail.SetBinding(TextBlock.TextProperty, new Binding("Detail")); detail.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush"); detail.SetValue(TextBlock.FontSizeProperty, 11.0); detail.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 5, 0, 0)); detail.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis); stack.AppendChild(detail); row.AppendChild(stack); template.VisualTree = row; Results.ItemTemplate = template;
        System.Windows.Automation.AutomationProperties.SetName(Query, "移動先を検索"); System.Windows.Automation.AutomationProperties.SetName(Results, "移動先の候補");
        Query.TextChanged += (_, _) => Refresh(); Results.MouseDoubleClick += async (_, _) => await OpenSelectedAsync();
        TextCompositionManager.AddPreviewTextInputStartHandler(Query, (_, _) => _composing = true); TextCompositionManager.AddPreviewTextInputHandler(Query, (_, _) => _composing = false);
        PreviewKeyDown += async (_, e) =>
        {
            if (_composing || e.Key == Key.ImeProcessed) { return; }
            if (e.Key == Key.Escape) { e.Handled = true; Close(); }
            else if (e.Key is Key.Down or Key.Up) { e.Handled = true; if (Results.Items.Count > 0) { Results.SelectedIndex = Math.Clamp(Results.SelectedIndex + (e.Key == Key.Down ? 1 : -1), 0, Results.Items.Count - 1); Results.ScrollIntoView(Results.SelectedItem); } }
            else if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None) { e.Handled = true; await OpenSelectedAsync(); }
        };
        UiMotion.SetFeedback(root, "entrance"); Loaded += (_, _) => Query.Focus(); Refresh();
    }
    private void Refresh()
    { Results.ItemsSource = QuickNavigation.Find(_chat.Conversations, _chat.Projects, _chat.Agents, Query.Text, _chat.Settings.SelectedProjectId, _chat.SelectedConversation?.Id); Results.SelectedIndex = Results.Items.Count > 0 ? 0 : -1; _empty.Visibility = Results.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed; }
    internal async Task<bool> OpenSelectedAsync()
    {
        if (_opening || Results.SelectedItem is not NavigationTarget target) { return false; }
        _opening = true; Results.IsEnabled = Query.IsEnabled = false;
        try
        {
            if (!_chat.CanNavigate) { throw new InvalidOperationException("準備が終わってから移動してください。"); }
            if (target.Room is Guid id) { _chat.SelectedConversation = _chat.Conversations.FirstOrDefault(c => c.Id == id && !c.IsArchived) ?? throw new InvalidOperationException("この会話は移動または削除されています。"); }
            else if (target.Account is Guid account) { await _chat.OpenDirectMessageAsync(account); }
            else { if (!_chat.Projects.Any(p => p.Id == target.Server)) { throw new InvalidOperationException("このサーバーは削除されています。"); } _chat.SelectProject(target.Server); }
            Close(); return true;
        }
        catch (Exception ex) { _status.Text = ex.Message; Refresh(); return false; }
        finally { _opening = false; Results.IsEnabled = Query.IsEnabled = true; }
    }
}
