using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Eluna.App.Services;
using Eluna.App.ViewModels;

namespace Eluna.App;

public sealed class DownloadsWindow : Window
{
    public DownloadsWindow(DownloadQueue queue, ChatViewModel? viewModel = null)
    {
        Title = "ダウンロード管理 · Eluna"; Width = 860; Height = 660; MinWidth = 620; MinHeight = 420; WindowStartupLocation = WindowStartupLocation.CenterOwner; Appearance.Attach(this);
        var root = new DockPanel { Margin = new(24) }; var top = new StackPanel { Margin = new(0, 0, 0, 18) }; DockPanel.SetDock(top, Dock.Top); root.Children.Add(top);
        top.Children.Add(new TextBlock { Text = "ダウンロード管理", FontSize = 24, FontWeight = FontWeights.SemiBold });
        top.Children.Add(new TextBlock { Text = "この画面を閉じても取得を続けます。アプリ終了時は一時停止し、次の起動で再開できます。", TextWrapping = TextWrapping.Wrap, Margin = new(0, 10, 0, 0) });
        var notice = new TextBlock { Foreground = System.Windows.Media.Brushes.IndianRed, TextWrapping = TextWrapping.Wrap }; notice.SetBinding(TextBlock.TextProperty, new Binding("RecoveryNotice") { Source = queue }); top.Children.Add(notice);
        var list = new ListBox { ItemsSource = queue.Items };
        var template = new DataTemplate(); var panel = new FrameworkElementFactory(typeof(StackPanel));
        var name = new FrameworkElementFactory(typeof(TextBlock)); name.SetBinding(TextBlock.TextProperty, new Binding("Name")); name.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap); name.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold); panel.AppendChild(name);
        var label = new FrameworkElementFactory(typeof(TextBlock)); label.SetBinding(TextBlock.TextProperty, new Binding("Label")); label.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 8, 0, 6)); panel.AppendChild(label);
        var progress = new FrameworkElementFactory(typeof(ProgressBar)); progress.SetValue(FrameworkElement.HeightProperty, 5.0); progress.SetBinding(System.Windows.Controls.Primitives.RangeBase.ValueProperty, new Binding("Percent") { Mode = BindingMode.OneWay }); panel.AppendChild(progress);
        var detail = new FrameworkElementFactory(typeof(TextBlock)); detail.SetBinding(TextBlock.TextProperty, new Binding("Detail")); detail.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap); detail.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 8, 0, 8)); detail.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush"); panel.AppendChild(detail);
        var actions = new FrameworkElementFactory(typeof(StackPanel)); actions.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
        void Button(string text, string? enabled, RoutedEventHandler handler)
        {
            var button = new FrameworkElementFactory(typeof(Button)); button.SetValue(ContentControl.ContentProperty, text); button.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 8, 0));
            if (enabled is not null) { button.SetBinding(IsEnabledProperty, new Binding(enabled)); }
            button.AddHandler(System.Windows.Controls.Primitives.ButtonBase.ClickEvent, handler); actions.AppendChild(button);
        }
        Button("一時停止", "CanPause", (sender, _) => { if (sender is FrameworkElement { DataContext: DownloadItem item }) { queue.Pause(item); } });
        Button("再開・再試行", "CanResume", (sender, _) => { if (sender is FrameworkElement { DataContext: DownloadItem item }) { queue.Resume(item); } });
        if (viewModel is not null)
        {
            var use = new FrameworkElementFactory(typeof(Button)); use.SetValue(ContentControl.ContentProperty, "使う"); use.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 8, 0));
            var enabled = new MultiBinding { Converter = new DownloadUseConverter() }; enabled.Bindings.Add(new Binding("CanUse")); enabled.Bindings.Add(new Binding("CanCompose") { Source = viewModel }); use.SetBinding(IsEnabledProperty, enabled);
            use.AddHandler(System.Windows.Controls.Primitives.ButtonBase.ClickEvent, new RoutedEventHandler((sender, _) => { if (sender is FrameworkElement { DataContext: DownloadItem { ModelId: not null, Status: "complete" } item } && viewModel.CanCompose && viewModel.Models.FirstOrDefault(m => m.Id == item.ModelId) is { } model) { viewModel.SelectedModel = model; } })); actions.AppendChild(use);
        }
        Button("一覧から外す", "CanRemove", (sender, _) => { if (sender is FrameworkElement { DataContext: DownloadItem item }) { queue.Remove(item); } }); panel.AppendChild(actions); template.VisualTree = panel; list.ItemTemplate = template;
        var area = new Grid(); area.Children.Add(list); var empty = new TextBlock { Text = "ダウンロードはありません。設定の「モデルを探す」から追加できます。", Margin = new(10), TextWrapping = TextWrapping.Wrap }; var emptyStyle = new Style(typeof(TextBlock)); emptyStyle.Setters.Add(new Setter(VisibilityProperty, Visibility.Collapsed)); var emptyTrigger = new DataTrigger { Binding = new Binding("HasItems") { Source = list }, Value = false }; emptyTrigger.Setters.Add(new Setter(VisibilityProperty, Visibility.Visible)); emptyStyle.Triggers.Add(emptyTrigger); empty.Style = emptyStyle; area.Children.Add(empty); root.Children.Add(area); Content = root;
    }
    private sealed class DownloadUseConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, System.Globalization.CultureInfo culture) => values.All(value => value is true);
        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, System.Globalization.CultureInfo culture) => throw new NotSupportedException();
    }
}
