using System.Windows;
using System.Windows.Controls;
using Eluna.App.Models;
using Eluna.App.Services;

namespace Eluna.App;

public sealed class KnowledgeWindow : Window
{
    public KnowledgeWindow(IReadOnlyList<KnowledgeReference> references)
    {
        Title = "AIに渡したナレッジ · Eluna"; Width = 920; Height = 680; MinWidth = 620; MinHeight = 420; WindowStartupLocation = WindowStartupLocation.CenterOwner; Appearance.Attach(this);
        var root = new DockPanel { Margin = new(24) }; Content = root;
        var header = new TextBlock { Text = references.Count == 0 ? "AIに渡したナレッジの抜粋はありません。" : "生成時にAIへ渡した抜粋です。調査は最後の開始・再開時の記録です。現在の資料を変更しても記録は残ります。", TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 16) }; DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        var grid = new Grid(); grid.ColumnDefinitions.Add(new() { Width = new(220) }); grid.ColumnDefinitions.Add(new() { Width = new(16) }); grid.ColumnDefinitions.Add(new()); root.Children.Add(grid);
        var list = new ListBox { ItemsSource = references, ItemTemplate = (DataTemplate)FindResource("KnowledgeReferenceTemplate") };
        System.Windows.Automation.AutomationProperties.SetName(list, "AIに渡したナレッジの一覧"); grid.Children.Add(list);
        var body = new TextBox { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalContentAlignment = VerticalAlignment.Top, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        System.Windows.Automation.AutomationProperties.SetName(body, "生成時のナレッジの抜粋"); Grid.SetColumn(body, 2); grid.Children.Add(body);
        list.SelectionChanged += (_, _) => { body.Text = list.SelectedItem is KnowledgeReference item ? item.Label + "\n" + item.Kind + "\n\n" + item.Text : ""; body.ScrollToHome(); };
        list.SelectedIndex = references.Count > 0 ? 0 : -1;
    }
}
