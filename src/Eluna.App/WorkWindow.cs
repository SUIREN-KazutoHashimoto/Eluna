using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Eluna.App.Controls;
using Eluna.App.Models;
using Eluna.App.Services;
using Eluna.App.ViewModels;
using Microsoft.Win32;

namespace Eluna.App;

public sealed class WorkWindow : Window
{
    public WorkWindow(ChatViewModel viewModel, ChatMessage answer)
    {
        WorkSession work = answer.Work ?? throw new ArgumentException("調査がありません。", nameof(answer));
        Title = "調査の活動と資料 · Eluna"; Width = 1040; Height = 760; MinWidth = 760; MinHeight = 540;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; Appearance.Attach(this); DataContext = work;
        var root = new Grid { Margin = new(24) }; root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        root.RowDefinitions.Add(new() { Height = GridLength.Auto }); root.RowDefinitions.Add(new()); root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var title = new TextBlock { FontSize = 22, FontWeight = FontWeights.SemiBold, Margin = new(0, 0, 0, 6) }; title.SetBinding(TextBlock.TextProperty, "Label"); root.Children.Add(title);
        var budget = new TextBlock { Margin = new(0, 0, 0, 18), TextWrapping = TextWrapping.Wrap }; budget.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush"); budget.SetBinding(TextBlock.TextProperty, "Budget"); Grid.SetRow(budget, 1); root.Children.Add(budget);
        var body = new Grid(); body.ColumnDefinitions.Add(new()); body.ColumnDefinitions.Add(new() { Width = new(360) }); Grid.SetRow(body, 2); root.Children.Add(body);
        var activities = new ListBox { Margin = new(0, 0, 16, 0), ItemsSource = work.Activities, ItemTemplate = CardTemplate("Label", "Detail") };
        activities.MouseDoubleClick += (_, _) => { if (activities.SelectedItem is WorkActivity activity) { ShowText(activity.Title, activity.Detail); } };
        var activityArea = new DockPanel(); var follow = new CheckBox { Content = "最新の活動を追う", IsChecked = true, Margin = new(0, 0, 0, 6) }; DockPanel.SetDock(follow, Dock.Top); activityArea.Children.Add(follow); activityArea.Children.Add(activities); body.Children.Add(activityArea);
        follow.Checked += (_, _) => { if (work.Activities.Count > 0) { activities.ScrollIntoView(work.Activities[^1]); } };
        activities.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler((_, args) => { if (args.ExtentHeightChange == 0 && args.OriginalSource is ScrollViewer scroll && args.VerticalChange != 0) { follow.IsChecked = scroll.ScrollableHeight - scroll.VerticalOffset < 40; } }));
        work.Activities.CollectionChanged += ActivityChanged;
        void ActivityChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        { if (follow.IsChecked == true && e.NewItems?.Count > 0) { activities.ScrollIntoView(e.NewItems[0]); } }
        Closed += (_, _) => work.Activities.CollectionChanged -= ActivityChanged;
        var details = new StackPanel(); Grid.SetColumn(details, 1);
        var planButton = new Button { Content = "調査計画を確認", HorizontalAlignment = HorizontalAlignment.Left, Margin = new(0, 0, 0, 12) };
        planButton.Click += (_, _) => ShowText("調査計画", work.Plan); details.Children.Add(planButton);
        details.Children.Add(new TextBlock { Text = "閲覧した資料", FontWeight = FontWeights.SemiBold, Margin = new(0, 0, 0, 8) });
        var sources = new ListBox { ItemsSource = work.Sources, ItemTemplate = CardTemplate("Label", "Detail"), MaxHeight = 290 };
        sources.MouseDoubleClick += (_, _) => { if (sources.SelectedItem is WorkSource source) { new SourceWindow(source) { Owner = this }.Show(); } };
        details.Children.Add(sources);
        var sourceButton = new Button { Content = "選択した資料の本文・日時", IsEnabled = false, ToolTip = "資料を選んでから開いてください。", HorizontalAlignment = HorizontalAlignment.Left, Margin = new(0, 6, 0, 16) };
        sources.SelectionChanged += (_, _) => sourceButton.IsEnabled = sources.SelectedItem is WorkSource;
        sourceButton.Click += (_, _) => { if (sources.SelectedItem is WorkSource source) { new SourceWindow(source) { Owner = this }.Show(); } }; details.Children.Add(sourceButton);
        details.Children.Add(new TextBlock { Text = "役割ごとの発言", FontWeight = FontWeights.SemiBold, Margin = new(0, 0, 0, 8) });
        var turns = new ListBox { ItemsSource = work.Turns, ItemTemplate = CardTemplate("Label", "Content"), MaxHeight = 240 };
        turns.MouseDoubleClick += (_, _) => { if (turns.SelectedItem is DebateTurn turn) { ShowText($"ラウンド{turn.Round} · {turn.Role}", turn.Content); } }; details.Children.Add(turns);
        var verification = new Button { Content = "根拠の照合結果", HorizontalAlignment = HorizontalAlignment.Left, Margin = new(0, 8, 0, 0) };
        verification.Click += (_, _) => ShowText("根拠の照合結果", work.Verification); details.Children.Add(verification);
        var scroll = new ScrollViewer { Content = details, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }; Grid.SetColumn(scroll, 1); body.Children.Add(scroll);
        var footer = new WrapPanel { Margin = new(0, 12, 0, 0) }; Grid.SetRow(footer, 3); root.Children.Add(footer);
        var close = new Button { Content = "閉じる" }; close.Click += (_, _) => Close(); DockPanel.SetDock(close, Dock.Right); footer.Children.Add(close);
        var stop = new Button { Content = "この作業を停止", Margin = new(0, 0, 8, 0) }; stop.Click += (_, _) => viewModel.StopAnswer(answer); footer.Children.Add(stop);
        var resume = new Button { Content = "途中から再開", Margin = new(0, 0, 8, 0) }; resume.SetBinding(IsEnabledProperty, "CanResume");
        resume.Click += async (_, _) => { if (viewModel.CanGenerate) { await viewModel.ResumeWorkAtSourceAsync(answer); } }; footer.Children.Add(resume);
        resume.ToolTip = "この回答の会話・分岐を選んで再開します。別の会話の処理中は予約できます。";
        void RefreshButtons(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        { resume.IsEnabled = work.CanResume && viewModel.CanResumeWorkAtSource(answer); stop.IsEnabled = work.IsRunning; }
        viewModel.PropertyChanged += RefreshButtons; work.PropertyChanged += RefreshButtons;
        Closed += (_, _) => { viewModel.PropertyChanged -= RefreshButtons; work.PropertyChanged -= RefreshButtons; }; RefreshButtons(null, new(null));
        var openChat = new Button { Content = "対象の会話を開く" }; openChat.Click += (_, _) => viewModel.OpenAnswer(answer); footer.Children.Add(openChat);
        var export = new Button { Content = "Markdownで書き出す", HorizontalAlignment = HorizontalAlignment.Left };
        export.Click += async (_, _) =>
        {
            var picker = new SaveFileDialog { Filter = "Markdown (*.md)|*.md", FileName = "Eluna-research.md" };
            if (picker.ShowDialog(this) == true)
            {
                try { await File.WriteAllTextAsync(picker.FileName, Export(work)); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { MessageBox.Show(this, "保存先の容量とアクセス権を確認してください。", "書き出せませんでした"); }
            }
        }; footer.Children.Add(export);
        var activityButton = new Button { Content = "活動の詳細", IsEnabled = false, ToolTip = "活動を選んでから開いてください。", HorizontalAlignment = HorizontalAlignment.Left };
        activities.SelectionChanged += (_, _) => activityButton.IsEnabled = activities.SelectedItem is WorkActivity;
        activityButton.Click += (_, _) => { if (activities.SelectedItem is WorkActivity activity) { ShowText(activity.Title, activity.Detail); } }; footer.Children.Add(activityButton); Content = root;
        void ShowText(string label, string text) { new WorkTextWindow(label, text.Length > 0 ? text : "まだありません。") { Owner = this }.Show(); }
    }
    internal static DataTemplate CardTemplate(string titlePath, string detailPath)
    {
        var template = new DataTemplate(); var panel = new FrameworkElementFactory(typeof(StackPanel));
        var title = new FrameworkElementFactory(typeof(TextBlock)); title.SetBinding(TextBlock.TextProperty, new Binding(titlePath)); title.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap); title.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold); panel.AppendChild(title);
        var detail = new FrameworkElementFactory(typeof(TextBlock)); detail.SetBinding(TextBlock.TextProperty, new Binding(detailPath)); detail.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap); detail.SetValue(TextBlock.MaxHeightProperty, 90.0); detail.SetValue(TextBlock.FontSizeProperty, 12.0); detail.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 6, 0, 0)); detail.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush"); panel.AppendChild(detail);
        template.VisualTree = panel; return template;
    }
    public static string Export(WorkSession work) => "# " + work.Question + "\n\n" + work.Label + "\n\n" + work.Budget + "\n\n" + work.Report +
        "\n\n## 調査計画\n\n" + work.Plan + "\n\n## 根拠の照合\n\n" + work.Verification +
        "\n\n## 議論\n\n" + string.Join("\n\n", work.Turns.Select(t => $"### ラウンド{t.Round} · {t.Role}\n\n{t.Content}")) +
        "\n\n## 活動\n\n" + string.Join("\n\n", work.Activities.Select(a => $"- {a.Time.ToLocalTime():HH:mm:ss} {a.Title}\n\n{a.Detail}"));
}

public sealed class WorkTextWindow : Window
{
    public WorkTextWindow(string label, string text)
    {
        Title = label + " · Eluna"; Width = 820; Height = 670; MinWidth = 600; MinHeight = 400; WindowStartupLocation = WindowStartupLocation.CenterOwner; Appearance.Attach(this);
        Content = new ScrollViewer { Content = new MarkdownView { Markdown = text, Margin = new(24) }, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }
}
public sealed class SourceWindow : Window
{
    public SourceWindow(WorkSource source)
    {
        Title = "資料の本文 · Eluna"; Width = 860; Height = 720; MinWidth = 600; MinHeight = 420; WindowStartupLocation = WindowStartupLocation.CenterOwner; Appearance.Attach(this);
        var root = new DockPanel { Margin = new(24) };
        var header = new StackPanel { Margin = new(0, 0, 0, 18) }; DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        header.Children.Add(new TextBlock { Text = source.Label, FontSize = 20, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        header.Children.Add(new TextBlock { Text = source.Detail, TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 0) });
        var link = new Button { Content = "配布元のページを開く ↗", HorizontalAlignment = HorizontalAlignment.Left, ToolTip = source.Url };
        link.Click += (_, _) => { try { Process.Start(new ProcessStartInfo(source.Url) { UseShellExecute = true }); } catch (System.ComponentModel.Win32Exception) { MessageBox.Show(this, "ブラウザーを起動できませんでした。"); } }; header.Children.Add(link);
        root.Children.Add(new TextBox { Text = source.Text, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, VerticalContentAlignment = VerticalAlignment.Top, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new(14) }); Content = root;
    }
}
