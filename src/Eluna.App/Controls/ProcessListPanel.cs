using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using Eluna.App.Models;
using Eluna.App.Services;
using Eluna.App.ViewModels;

namespace Eluna.App.Controls;

public sealed class ProcessListPanel : UserControl
{
    private readonly ChatViewModel _chat;
    private readonly ObservableCollection<ProcessRow> _rows = [];
    private readonly DispatcherTimer _timer;
    private readonly TextBlock _empty;
    private readonly TextBlock _resources;
    public ProcessListPanel(ChatViewModel chat)
    {
        _chat = chat;
        var root = new DockPanel { Margin = new(14) };
        var head = new StackPanel { Margin = new(0, 0, 0, 10) }; DockPanel.SetDock(head, Dock.Top); root.Children.Add(head);
        head.Children.Add(new TextBlock { Text = "処理リスト", FontSize = 16, FontWeight = FontWeights.SemiBold });
        var hint = new TextBlock { Text = "実行中 → 予約順。推論とダウンロードは並行して進みます。", TextWrapping = TextWrapping.Wrap, FontSize = 11, Margin = new(0, 5, 0, 0) }; hint.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush"); head.Children.Add(hint);
        var footer = new WrapPanel { Margin = new(0, 8, 0, 0) }; DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        _resources = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 11, Margin = new(0, 8, 0, 0) }; _resources.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        var resources = new Expander { Header = "メモリと予約の見込み", Content = new ScrollViewer { Content = _resources, MaxHeight = 110, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }, Margin = new(0, 6, 0, 0) }; DockPanel.SetDock(resources, Dock.Bottom); root.Children.Add(resources);
        var history = new Button { Content = "推論の履歴", Padding = new(8, 5, 8, 5) }; history.Click += (_, _) => new InferenceQueueWindow(chat) { Owner = Window.GetWindow(this) ?? Application.Current.MainWindow }.Show(); footer.Children.Add(history);
        var clear = new Button { Content = "完了を片付ける", Padding = new(8, 5, 8, 5) }; clear.Click += async (_, _) => { chat.Inferences.ClearFinished(); foreach (DownloadItem item in chat.Downloads.Items.Where(i => i.Status == "complete").ToArray()) { chat.Downloads.Remove(item); } await chat.ClearFinishedMediaAsync(); Refresh(); }; footer.Children.Add(clear);
        var grid = new Grid(); root.Children.Add(grid);
        var list = new ListBox { ItemsSource = _rows, MaxHeight = Math.Max(160, Math.Min(360, SystemParameters.WorkArea.Height * 0.45)) };
        System.Windows.Automation.AutomationProperties.SetName(list, "実行中の処理と予約順");
        var template = new DataTemplate(); var panel = new FrameworkElementFactory(typeof(StackPanel));
        void Text(string path, double size, bool muted = false)
        {
            var text = new FrameworkElementFactory(typeof(TextBlock)); text.SetBinding(TextBlock.TextProperty, new Binding(path)); text.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap); text.SetValue(TextBlock.FontSizeProperty, size); text.SetValue(MarginProperty, new Thickness(0, 0, 0, 4)); if (muted) { text.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush"); }
            panel.AppendChild(text);
        }
        Text(nameof(ProcessRow.Title), 13); Text(nameof(ProcessRow.StateLabel), 11, true); Text(nameof(ProcessRow.Detail), 11, true);
        var progress = new FrameworkElementFactory(typeof(ProgressBar)); progress.SetValue(HeightProperty, 3.0); progress.SetValue(MarginProperty, new Thickness(0, 2, 0, 6)); progress.SetBinding(System.Windows.Controls.Primitives.RangeBase.ValueProperty, new Binding(nameof(ProcessRow.Percent)) { Mode = BindingMode.OneWay }); progress.SetBinding(ProgressBar.IsIndeterminateProperty, new Binding(nameof(ProcessRow.Indeterminate))); progress.SetBinding(VisibilityProperty, new Binding(nameof(ProcessRow.Active)) { Converter = new BooleanToVisibilityConverter() }); panel.AppendChild(progress);
        var actions = new FrameworkElementFactory(typeof(WrapPanel));
        void Action(string label, string visible, Action<ProcessRow> action)
        {
            var button = new FrameworkElementFactory(typeof(Button)); button.SetValue(ContentControl.ContentProperty, label); button.SetValue(Control.PaddingProperty, new Thickness(8, 4, 8, 4)); button.SetBinding(VisibilityProperty, new Binding(visible) { Converter = new BooleanToVisibilityConverter() }); button.AddHandler(System.Windows.Controls.Primitives.ButtonBase.ClickEvent, new RoutedEventHandler((sender, _) => { if (sender is FrameworkElement { DataContext: ProcessRow row }) { action(row); Refresh(); } })); actions.AppendChild(button);
        }
        Action("開く", nameof(ProcessRow.CanOpen), row => row.Open()); Action("停止・取消", nameof(ProcessRow.CanCancel), row => row.Cancel()); Action("再開・再試行", nameof(ProcessRow.CanResume), row => row.Resume());
        panel.AppendChild(actions); template.VisualTree = panel; list.ItemTemplate = template; grid.Children.Add(list);
        _empty = new TextBlock { Text = "処理はありません。\nメッセージを送るか、設定からモデルを取得できます。", Margin = new(6, 18, 6, 24), TextWrapping = TextWrapping.Wrap }; _empty.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush"); grid.Children.Add(_empty);
        Content = root;
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(250), DispatcherPriority.Background, (_, _) => Refresh(), Dispatcher); _timer.Stop();
        Loaded += (_, _) => { Refresh(); _timer.Start(); }; Unloaded += (_, _) => _timer.Stop();
    }
    internal void Refresh()
    {
        object[] jobs = [.. _chat.Inferences.Items.Cast<object>(), .. _chat.Downloads.Items.Cast<object>(), .. _chat.MediaJobs.Cast<object>()];
        foreach (object job in jobs)
        { if (!_rows.Any(r => ReferenceEquals(r.Job, job))) { _rows.Add(new(_chat, job)); } }
        foreach (ProcessRow row in _rows.ToArray()) { if (!jobs.Contains(row.Job)) { _rows.Remove(row); } else { row.Refresh(); } }
        ProcessRow[] ordered = _rows.OrderBy(r => r.Rank).ThenBy(r => r.Created).ToArray();
        for (int i = 0; i < ordered.Length; i++) { int old = _rows.IndexOf(ordered[i]); if (old != i) { _rows.Move(old, i); } }
        _empty.Visibility = _rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        _resources.Text = _chat.ResourceForecast;
    }
    private sealed class ProcessRow(ChatViewModel chat, object job) : ObservableObject
    {
        public object Job => job;
        public DateTimeOffset Created => job switch { InferenceJob i => i.Request.CreatedAt, DownloadItem d => d.CreatedAt, MediaJob m => m.CreatedAt, _ => DateTimeOffset.MinValue };
        private string State => job switch { InferenceJob i => i.State, DownloadItem d => d.Status, MediaJob m => m.State, _ => "complete" };
        public int Rank => State == "active" ? 0 : State == "queued" ? 1 : State is "paused" or "failed" ? 2 : 3;
        public bool Active => State == "active";
        public bool Indeterminate => Active && job is not DownloadItem;
        public double Percent => job is DownloadItem d ? d.Percent : 0;
        public string Title => job switch { InferenceJob i => "推論 · " + i.Title, DownloadItem d => "取得 · " + d.Name, MediaJob m => m.Title, _ => "" };
        public string StateLabel => (State switch { "active" => "実行中", "queued" => "予約中", "failed" => "失敗", "paused" => "再開待ち", "canceled" => "取消済み", _ => "完了" }) + (job is InferenceJob i ? " · " + (chat.Projects.FirstOrDefault(p => p.Id == i.Request.Settings.SelectedProjectId)?.Name ?? "通常チャット") : "");
        public string Detail => job switch { InferenceJob i => i.Detail + (i.Metrics.Length > 0 ? "\n" + i.Metrics : ""), DownloadItem d => d.Detail, MediaJob m => m.Detail, _ => "" };
        public bool CanCancel => job switch { InferenceJob i => i.CanCancel, DownloadItem d => d.CanPause, MediaJob m => m.IsPending || m.CanResume, _ => false };
        public bool CanResume => job switch { InferenceJob i => i.CanResume, DownloadItem d => d.CanResume, MediaJob m => m.CanResume, _ => false };
        public bool CanOpen => job is InferenceJob || job is DownloadItem { CanUse: true } || job is MediaJob { Request: not null };
        public void Cancel() { if (job is InferenceJob i) { chat.Inferences.Cancel(i); } else if (job is DownloadItem d) { chat.Downloads.Pause(d); } else if (job is MediaJob m) { m.Cancel(); } }
        public async void Resume() { try { if (job is InferenceJob i) { chat.Inferences.Resume(i); } else if (job is DownloadItem d) { chat.Downloads.Resume(d); } else if (job is MediaJob m) { await chat.ResumeMediaAsync(m); } } catch (Exception ex) { chat.Error = DiagnosticsService.Explain(ex); } }
        public void Open() { if (job is InferenceJob i) { chat.OpenInference(i); Application.Current.MainWindow?.Activate(); } else if (job is DownloadItem d && chat.Models.FirstOrDefault(m => m.Id == d.ModelId) is { } model) { chat.SelectedModel = model; } else if (job is MediaJob m) { new MediaResultWindow(m, chat.DataDirectory) { Owner = Application.Current.MainWindow }.Show(); } }
        public void Refresh() { foreach (string property in new[] { nameof(Title), nameof(StateLabel), nameof(Detail), nameof(Active), nameof(Percent), nameof(Indeterminate), nameof(CanCancel), nameof(CanResume), nameof(CanOpen) }) { Notify(property); } }
    }
}
