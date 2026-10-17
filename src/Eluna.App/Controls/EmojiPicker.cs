using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using Eluna.App.Services;

namespace Eluna.App.Controls;

internal sealed record EmojiChoice(EmojiEntry Entry, bool Selected)
{
    public string Glyph => Entry.Glyph;
    public string Hint => Entry.Hint + (Selected ? "（追加済み。選ぶと取り消します）" : "");
}
internal sealed class EmojiPicker : Border
{
    internal TextBox Search { get; } = new() { MaxLength = 120, Padding = new(8, 7, 8, 7) };
    internal ListBox Choices { get; } = new() { SelectionMode = SelectionMode.Single };
    private readonly ComboBox _category = new() { DisplayMemberPath = "Value", SelectedValuePath = "Key", Margin = new(0, 8, 0, 8) };
    private readonly TextBlock _detail = new() { FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 0) }, _empty = new() { Text = "見つかりません。検索語やカテゴリを変えてください。", TextWrapping = TextWrapping.Wrap, Margin = new(10) };
    private readonly IReadOnlyList<string> _recent;
    private readonly IReadOnlySet<string> _selected;
    private bool _composing;
    internal bool CategoryOpen => _category.IsDropDownOpen;
    internal event Action<string>? Picked;
    internal event Action? CloseRequested;
    internal EmojiPicker(IReadOnlyList<string>? recent, IReadOnlySet<string>? selected = null)
    {
        _recent = recent?.ToArray() ?? []; _selected = selected ?? new HashSet<string>(); Width = 360; Height = 430; Padding = new(14); CornerRadius = new(8); BorderThickness = new(1); SetResourceReference(BackgroundProperty, "SidebarBrush"); SetResourceReference(BorderBrushProperty, "StrokeBrush");
        SetResourceReference(TextElement.ForegroundProperty, "TextBrush");
        KeyboardNavigation.SetTabNavigation(this, KeyboardNavigationMode.Cycle);
        var root = new DockPanel(); Child = root; var head = new StackPanel(); DockPanel.SetDock(head, Dock.Top); root.Children.Add(head);
        head.Children.Add(new TextBlock { Text = selected is null ? "絵文字を選ぶ" : "リアクションを選ぶ", FontSize = 17, FontWeight = FontWeights.SemiBold, Margin = new(0, 0, 0, 8) }); head.Children.Add(Search);
        _category.ItemsSource = EmojiCatalog.Categories; _category.SelectedValue = _recent.Count > 0 ? "recent" : "all"; head.Children.Add(_category);
        var footer = new StackPanel(); DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer); footer.Children.Add(_detail); var hint = new TextBlock { Text = "↑↓←→ 選択 · Enter 確定 · Esc 閉じる", FontSize = 10, Margin = new(0, 5, 0, 0), TextWrapping = TextWrapping.Wrap }; hint.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush"); footer.Children.Add(hint);
        var body = new Grid(); root.Children.Add(body); body.Children.Add(Choices); body.Children.Add(_empty);
        var items = new FrameworkElementFactory(typeof(WrapPanel)); items.SetValue(WrapPanel.ItemWidthProperty, 52.0); items.SetValue(WrapPanel.ItemHeightProperty, 52.0); Choices.ItemsPanel = new ItemsPanelTemplate(items);
        var style = new Style(typeof(ListBoxItem), (Style)Application.Current.FindResource(typeof(ListBoxItem))); style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(2))); style.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(1))); style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch)); style.Setters.Add(new Setter(FrameworkElement.ToolTipProperty, new Binding("Hint"))); style.Setters.Add(new Setter(System.Windows.Automation.AutomationProperties.NameProperty, new Binding("Hint"))); Choices.ItemContainerStyle = style;
        var template = new DataTemplate(); var grid = new FrameworkElementFactory(typeof(Grid)); var glyph = new FrameworkElementFactory(typeof(TextBlock)); glyph.SetBinding(TextBlock.TextProperty, new Binding("Glyph")); glyph.SetValue(TextBlock.FontSizeProperty, 24.0); glyph.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center); glyph.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center); grid.AppendChild(glyph);
        var check = new FrameworkElementFactory(typeof(TextBlock)); check.SetValue(TextBlock.TextProperty, "✓"); check.SetValue(TextBlock.FontSizeProperty, 10.0); check.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Right); check.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Bottom); check.SetBinding(VisibilityProperty, new Binding("Selected") { Converter = (IValueConverter)Application.Current.FindResource("BoolVisibility") }); grid.AppendChild(check); template.VisualTree = grid; Choices.ItemTemplate = template;
        System.Windows.Automation.AutomationProperties.SetName(Search, "絵文字を日本語・英語で検索"); Search.ToolTip = "絵文字を日本語・英語で検索"; System.Windows.Automation.AutomationProperties.SetName(_category, "絵文字のカテゴリ"); System.Windows.Automation.AutomationProperties.SetName(Choices, "絵文字の候補");
        Search.TextChanged += (_, _) => { if (Search.Text.Length > 0 && _category.SelectedValue is "recent") { _category.SelectedValue = "all"; } else { Refresh(); } }; _category.SelectionChanged += (_, _) => Refresh(); Choices.SelectionChanged += (_, _) => _detail.Text = (Choices.SelectedItem as EmojiChoice)?.Hint ?? "";
        Choices.PreviewMouseLeftButtonUp += (_, e) => { if (e.OriginalSource is DependencyObject source && ItemsControl.ContainerFromElement(Choices, source) is ListBoxItem { DataContext: EmojiChoice choice }) { e.Handled = true; Picked?.Invoke(choice.Glyph); } };
        TextCompositionManager.AddPreviewTextInputStartHandler(Search, (_, _) => _composing = true); TextCompositionManager.AddPreviewTextInputHandler(Search, (_, _) => _composing = false);
        PreviewKeyDown += (_, e) =>
        {
            if (_composing || e.Key == Key.ImeProcessed || _category.IsDropDownOpen) { return; }
            if (e.Key == Key.Escape) { e.Handled = true; CloseRequested?.Invoke(); }
            else if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None || e.Key == Key.Space && Choices.IsKeyboardFocusWithin && Keyboard.Modifiers == ModifierKeys.None) { e.Handled = true; ChooseSelected(); }
            else if (e.Key == Key.Down && Search.IsKeyboardFocusWithin && Choices.Items.Count > 0) { e.Handled = true; Choices.Focus(); Choices.SelectedIndex = Math.Max(0, Choices.SelectedIndex); }
        };
        UiMotion.SetFeedback(this, "entrance"); Loaded += (_, _) => Search.Focus(); Refresh();
    }
    private void Refresh()
    {
        string category = (string?)_category.SelectedValue ?? "all";
        Choices.ItemsSource = EmojiCatalog.Find(Search.Text, category, _recent).Select(e => new EmojiChoice(e, _selected.Contains(e.Glyph))).ToArray(); Choices.SelectedIndex = Choices.Items.Count > 0 ? 0 : -1; _empty.Visibility = Choices.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }
    internal void ChooseSelected() { if (Choices.SelectedItem is EmojiChoice choice) { Picked?.Invoke(choice.Glyph); } }
}
