using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Eluna.App.Models;
using Eluna.App.Services;
using Eluna.App.ViewModels;
using Microsoft.Win32;

namespace Eluna.App;

public sealed class ComputerUseWindow : Window
{
    private readonly ChatViewModel _chat;
    private readonly ComputerDesktop _desktop = new();
    private readonly ListBox _windows = new() { SelectionMode = SelectionMode.Multiple, DisplayMemberPath = nameof(ComputerTarget.Label), Height = 172 };
    private readonly ComboBox _models = new() { DisplayMemberPath = nameof(LocalModel.DisplayName), MinWidth = 160 };
    private readonly ComboBox _limit = new() { ItemsSource = new[] { 10, 20, 40, 100 }, SelectedIndex = 1, Width = 72 };
    private readonly TextBox _task = new() { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 100, MaxHeight = 220, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxLength = 8000 };
    private readonly CheckBox _navigationAuto = new() { Content = "移動・スクロール・待機だけ自動", Margin = new(0, 8, 0, 8) };
    private readonly Image _image = new() { Stretch = Stretch.Uniform };
    private readonly Canvas _overlay = new() { IsHitTestVisible = false };
    private readonly Grid _viewport = new() { MinHeight = 100, Background = Brushes.Black };
    private readonly TextBox _elements = new() { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MinHeight = 100 };
    private readonly ObservableCollection<ComputerStep> _steps = [];
    private readonly ListBox _history = new() { MinHeight = 80 };
    private readonly TabControl _tabs = new() { Background = Brushes.Transparent, BorderThickness = new(0), Margin = new(0, 8, 0, 0) };
    private readonly RowDefinition _historyRow = new() { Height = new(2, GridUnitType.Star), MinHeight = 90 };
    private readonly TextBox _review = new() { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 135, MinHeight = 60 };
    private readonly TextBox _reviewText = new() { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MinHeight = 36, MaxHeight = 85, MaxLength = 8000, Visibility = Visibility.Collapsed };
    private readonly Border _reviewPanel = new() { Padding = new(12), CornerRadius = new(10), Margin = new(0, 10, 0, 0), Visibility = Visibility.Collapsed };
    private readonly TextBlock _status = new() { Text = "操作対象とモデルを選び、依頼を入力してください。", TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _modelHint = new() { TextWrapping = TextWrapping.Wrap, Margin = new(0, 6, 0, 10), FontSize = 11 };
    private readonly ProgressBar _progress = new() { Height = 3, Margin = new(0, 10, 0, 0), Visibility = Visibility.Collapsed };
    private readonly Button _start, _stop, _refresh, _preview, _approve, _deny, _export;
    private CancellationTokenSource? _cancellation;
    private Task _pending = Task.CompletedTask;
    private TaskCompletionSource<bool>? _approval;
    private TaskCompletionSource<bool>? _closing;
    private bool _working, _allowClose;
    private ComputerReview? _proposal;
    private ComputerObservation? _observation;
    internal Task<bool> PendingClose => _closing?.Task ?? Task.FromResult(!IsVisible);
    internal IReadOnlyList<ComputerStep> Steps => _steps;
    internal string OperationStatus => _status.Text;
    internal bool AwaitingApproval => _approval is not null;
    internal ComputerReview? PendingReview => _proposal;
    internal void DecideVerification(bool approve) { if (approve) { Approve(); } else { _approval?.TrySetResult(false); } }
    internal void PreviewVerification(LocalModel? model, ComputerTarget target, ComputerObservation observation)
    { _models.SelectedItem = model; _windows.ItemsSource = new[] { target }; _windows.SelectedItem = target; Observe(observation); }
    internal Task<bool> ReviewVerificationAsync(ComputerReview review, CancellationToken token) => ReviewAsync(review, token);
    internal Task StartVerificationAsync(LocalModel model, ComputerTarget target, string task)
    { _models.SelectedItem = model; _windows.ItemsSource = new[] { target }; _windows.SelectedItem = target; _task.Text = task; return StartAsync(); }

    public ComputerUseWindow(ChatViewModel chat)
    {
        _chat = chat; Title = "Computer Use · Eluna"; Width = 1180; Height = 830; MinWidth = 900; MinHeight = 640;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; Appearance.Attach(this);
        var root = new Grid { Margin = new(20) }; root.RowDefinitions.Add(new() { Height = GridLength.Auto }); root.RowDefinitions.Add(new()); root.RowDefinitions.Add(new() { Height = GridLength.Auto }); Content = root;
        var heading = new StackPanel { Margin = new(0, 0, 0, 14) }; root.Children.Add(heading);
        heading.Children.Add(new TextBlock { Text = "Computer Use", FontSize = 22, FontWeight = FontWeights.SemiBold });
        heading.Children.Add(Note("見て、判断して、操作する。選択したWindowsアプリをローカルモデルで操作します。"));
        var body = new Grid(); body.ColumnDefinitions.Add(new() { Width = new(290) }); body.ColumnDefinitions.Add(new() { Width = new(16) }); body.ColumnDefinitions.Add(new()); Grid.SetRow(body, 1); root.Children.Add(body);
        var setup = new StackPanel(); var setupScroll = new ScrollViewer { Content = setup, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }; body.Children.Add(setupScroll);
        setup.Children.Add(Label("操作するアプリ · 複数選択可")); setup.Children.Add(Note("選んだ対象だけを読み取り・操作します。対象は開始時に固定されます。"));
        AccessibleName(_windows, "操作を許可するアプリ"); _windows.ToolTip = "クリック、またはSpaceキーで対象を追加・解除できます。最大8アプリ。"; setup.Children.Add(_windows);
        var windowButtons = new WrapPanel(); setup.Children.Add(windowButtons);
        _refresh = Button(windowButtons, "一覧を更新", () => RunOperationAsync(RefreshWindowsAsync));
        _preview = Button(windowButtons, "画面を確認", () => RunOperationAsync(PreviewAsync));
        setup.Children.Add(Label("操作を考えるモデル")); _models.ItemsSource = chat.Models; _models.SelectedItem = chat.SelectedModel; setup.Children.Add(_models); AccessibleName(_models, "Computer Useのモデル");
        _models.SelectionChanged += (_, _) => ModelHint(); _modelHint.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush"); setup.Children.Add(_modelHint); ModelHint();
        setup.Children.Add(Label("依頼")); setup.Children.Add(_task); AccessibleName(_task, "Computer Useへの依頼");
        setup.Children.Add(Note("例: 選んだメモ帳に今日の作業リストを入力して。\nこの指示と対象画面だけを使い、会話・プロジェクト資料は自動で渡しません。"));
        var limits = new WrapPanel { Margin = new(0, 8, 0, 0) }; limits.Children.Add(new TextBlock { Text = "操作上限", VerticalAlignment = VerticalAlignment.Center, Margin = new(0, 0, 10, 0) }); limits.Children.Add(_limit); setup.Children.Add(limits); AccessibleName(_limit, "Computer Useの操作上限");
        setup.Children.Add(_navigationAuto); setup.Children.Add(Note("初期状態は各操作を確認。自動にしても、クリック・文字入力・送信につながるキーは毎回確認します。認証・購入・保護画面は利用者が操作してください。"));
        setup.Children.Add(Note("実行中はマウス・キーボードを共有します。対象を操作すると確認後の画面が変わり、操作を見送る場合があります。画面・操作履歴は自動保存しません。"));
        var right = new Grid(); right.RowDefinitions.Add(new() { Height = new(3, GridUnitType.Star) }); right.RowDefinitions.Add(_historyRow); Grid.SetColumn(right, 2); body.Children.Add(right);
        var frame = new Border { BorderThickness = new(1), CornerRadius = new(8), Padding = new(1), Child = _viewport }; frame.SetResourceReference(Border.BorderBrushProperty, "StrokeBrush"); right.Children.Add(frame);
        _viewport.Children.Add(_image); _viewport.Children.Add(_overlay); _viewport.SizeChanged += (_, _) => DrawProposal(); AccessibleName(_image, "操作前に取得した対象アプリの画面");
        _tabs.SetResourceReference(ForegroundProperty, "TextBrush"); Grid.SetRow(_tabs, 1); right.Children.Add(_tabs);
        var historyTemplate = new DataTemplate(); var row = new FrameworkElementFactory(typeof(TextBlock)); row.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap); row.SetBinding(TextBlock.TextProperty, new Binding { Converter = new StepTextConverter() }); historyTemplate.VisualTree = row;
        _history.ItemsSource = _steps; _history.ItemTemplate = historyTemplate; AccessibleName(_history, "Computer Useの実行履歴");
        _tabs.Items.Add(Tab("実行履歴", _history)); _tabs.Items.Add(Tab("読み取ったUI要素", _elements)); AccessibleName(_elements, "読み取ったUI要素");
        var footer = new StackPanel { Margin = new(0, 12, 0, 0) }; Grid.SetRow(footer, 2); root.Children.Add(footer);
        footer.Children.Add(_status); footer.Children.Add(_progress);
        _reviewPanel.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush"); footer.Children.Add(_reviewPanel);
        var reviewBody = new StackPanel(); _reviewPanel.Child = reviewBody; reviewBody.Children.Add(_review); AccessibleName(_review, "実行する操作の確認内容");
        reviewBody.Children.Add(_reviewText); AccessibleName(_reviewText, "入力する文字 · 実行前に修正できます");
        var approvals = new WrapPanel { Margin = new(0, 6, 0, 0) }; reviewBody.Children.Add(approvals);
        _approve = Button(approvals, "この操作を実行", () => { Approve(); return Task.CompletedTask; }); _approve.SetResourceReference(BackgroundProperty, "AccentBrush"); _approve.SetResourceReference(ForegroundProperty, "OnAccentBrush");
        _deny = Button(approvals, "実行せず終了", () => { _approval?.TrySetResult(false); return Task.CompletedTask; });
        var actions = new DockPanel { Margin = new(0, 10, 0, 0) }; footer.Children.Add(actions);
        var rightActions = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right }; DockPanel.SetDock(rightActions, Dock.Right); actions.Children.Add(rightActions);
        _export = Button(rightActions, "履歴を書き出す", ExportAsync); _stop = Button(rightActions, "停止 · Ctrl+Alt+Esc", () => { Stop(); return Task.CompletedTask; });
        _start = Button(rightActions, "操作を開始", StartAsync); _start.SetResourceReference(BackgroundProperty, "AccentBrush"); _start.SetResourceReference(ForegroundProperty, "OnAccentBrush");
        var close = Button(actions, "閉じる", () => { Close(); return Task.CompletedTask; }); close.HorizontalAlignment = HorizontalAlignment.Left;
        _windows.SelectionChanged += (_, _) => UpdateControls(); _task.TextChanged += (_, _) => UpdateControls();
        Loaded += async (_, _) => await RunOperationAsync(RefreshWindowsAsync); Closing += OnClosing;
        Closed += (_, _) => { if (_chat.ComputerActivity?.State != "active") { _chat.SetComputerActivity(null); } };
        UpdateControls();
    }

    private static TextBlock Label(string text) => new() { Text = text, FontWeight = FontWeights.SemiBold, Margin = new(0, 8, 0, 6) };
    private static TabItem Tab(string title, object content)
    {
        var item = new TabItem { Header = title, Content = content };
        item.SetResourceReference(StyleProperty, "WorkOptionsTab");
        return item;
    }
    private static TextBlock Note(string text) { var block = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 11, Margin = new(0, 4, 0, 8) }; block.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush"); return block; }
    private static void AccessibleName(DependencyObject element, string name) => AutomationProperties.SetName(element, name);
    private static Button Button(Panel parent, string text, Func<Task> action)
    { var button = new Button { Content = text, Margin = new(0, 0, 6, 0), Padding = new(9, 7, 9, 7) }; AccessibleName(button, text); button.Click += async (_, _) => { try { await action(); } catch (Exception ex) { MessageBox.Show(Window.GetWindow(button), ex.Message, "Computer Use"); } }; parent.Children.Add(button); return button; }
    private void ModelHint()
    { _modelHint.Text = _models.SelectedItem is LocalModel model ? model.Projector is not null ? "画像認識 + UI要素を使用。画像・文書画面でprojectorを変更できます。" : "UI要素を使用。画像対応モデルとprojectorを設定すると座標操作も使えます。" : "モデルを追加・選択してください。"; UpdateControls(); }
    private void UpdateControls()
    {
        if (_start is null) { return; }
        _windows.IsEnabled = _models.IsEnabled = _task.IsEnabled = _limit.IsEnabled = _navigationAuto.IsEnabled = !_working;
        _refresh.IsEnabled = !_working; _preview.IsEnabled = !_working && _windows.SelectedItems.Count > 0;
        _start.IsEnabled = !_working && _chat.CanStartComputer && _models.SelectedItem is LocalModel && _windows.SelectedItems.Count is > 0 and <= 8 && !string.IsNullOrWhiteSpace(_task.Text);
        _stop.IsEnabled = _working; _export.IsEnabled = !_working && _steps.Count > 0;
        _progress.Visibility = _working ? Visibility.Visible : Visibility.Collapsed; _progress.IsIndeterminate = _working;
    }
    private async Task RefreshWindowsAsync(CancellationToken token)
    {
        long[] selected = _windows.SelectedItems.Cast<ComputerTarget>().Select(w => w.Handle).ToArray();
        List<ComputerTarget> windows = await _desktop.WindowsAsync(token); _windows.ItemsSource = windows;
        foreach (ComputerTarget target in windows.Where(w => selected.Contains(w.Handle))) { _windows.SelectedItems.Add(target); }
        Status(windows.Count == 0 ? "操作できるアプリがありません。対象アプリを開き、一覧を更新してください。" : $"{windows.Count}個のアプリが見つかりました。操作する対象を選んでください。");
    }
    private async Task PreviewAsync(CancellationToken token)
    { if (_windows.SelectedItems.Cast<ComputerTarget>().FirstOrDefault() is { } target) { Observe(await _desktop.ObserveAsync(target, token)); Status("画面を取得しました。まだ操作していません。"); } }
    private Task RunOperationAsync(Func<CancellationToken, Task> operation)
    {
        if (_working) { return Task.CompletedTask; }
        _working = true; _cancellation = new(); UpdateControls(); _pending = RunCoreAsync(operation); return _pending;
    }
    private async Task RunCoreAsync(Func<CancellationToken, Task> operation)
    {
        try { await operation(_cancellation!.Token); }
        catch (OperationCanceledException) { Status("停止しました。実行済みの操作は取り消されません。"); }
        catch (Exception ex) { Status("停止 · " + ex.Message); }
        finally { _working = false; _cancellation?.Dispose(); _cancellation = null; UpdateControls(); }
    }
    private Task StartAsync()
    {
        if (_working || !_chat.CanStartComputer || _models.SelectedItem is not LocalModel model) { return Task.CompletedTask; }
        ComputerTarget[] allowed = _windows.SelectedItems.Cast<ComputerTarget>().ToArray(); string task = _task.Text; int max = (int)_limit.SelectedItem; bool auto = _navigationAuto.IsChecked == true;
        _steps.Clear(); _chat.ComputerCompletion = RunOperationAsync(async token =>
        {
            using var hotkey = new ComputerStopHotkey(this, Stop); using LlamaService engine = _chat.ComputerEngine();
            var activity = new ComputerActivity(task[..Math.Min(task.Length, 80)], Stop, () => { if (IsVisible) { WindowState = WindowState.Normal; Activate(); } });
            _chat.SetComputerActivity(activity);
            try
            {
                var planner = new ComputerPlanner(engine, model, _chat.Settings with { ContextSize = model.Options.ContextSize ?? _chat.Settings.ContextSize }, Status);
                var runner = new ComputerUseRunner(_desktop, planner);
                ComputerUseResult result = await runner.RunAsync(task, allowed, max, auto, Observe, step => { _steps.Add(step); _history.ScrollIntoView(step); }, Status, ReviewAsync, token);
                activity.State = result.State == "complete" ? "complete" : "paused";
                Status((result.State == "complete" ? "モデルの完了報告 · " : "停止 · ") + result.Message);
            }
            catch (OperationCanceledException) { activity.State = "canceled"; throw; }
            catch { activity.State = "failed"; throw; }
            finally { HideReview(); _chat.SetComputerActivity(activity); }
        });
        return _chat.ComputerCompletion;
    }
    private void Status(string text) { _status.Text = text; _chat.ComputerStatus(text); }
    internal void Stop() { _cancellation?.Cancel(); _approval?.TrySetCanceled(); }
    private async Task<bool> ReviewAsync(ComputerReview review, CancellationToken token)
    {
        _proposal = review; Observe(review.Observation); _approval = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using CancellationTokenRegistration registration = token.Register(() => _approval?.TrySetCanceled(token));
        ComputerElement? element = review.Observation.Elements.FirstOrDefault(e => e.Id == review.Action.Element);
        _review.Text = review.ConfirmationReason + "\n" + review.Observation.Title + "\n" + review.Action.Label +
            (element is not null ? $" · #{element.Id} {element.Type} {element.Name}" : $" · 座標 ({review.Action.X}, {review.Action.Y}) / 1000") +
            "\n" + review.Action.Reason + (review.Action.Type is "type_text" or "set_value" ? "\n入力する文字は下の欄で修正できます。" : "");
        _reviewText.Text = review.Action.Text; _reviewText.Visibility = review.Action.Type is "type_text" or "set_value" ? Visibility.Visible : Visibility.Collapsed;
        _reviewPanel.Visibility = Visibility.Visible; _tabs.Visibility = Visibility.Collapsed; _historyRow.MinHeight = 0; _historyRow.Height = new(0); DrawProposal(); Activate();
        try { return await _approval.Task; } finally { HideReview(); }
    }
    private void HideReview()
    { _approval = null; _proposal = null; _reviewPanel.Visibility = Visibility.Collapsed; _tabs.Visibility = Visibility.Visible; _historyRow.MinHeight = 90; _historyRow.Height = new(2, GridUnitType.Star); _overlay.Children.Clear(); }
    private void Approve()
    { if (_proposal is { } proposal && proposal.Action.Type is "type_text" or "set_value") { proposal.ApprovedAction = proposal.Action with { Text = _reviewText.Text }; } _approval?.TrySetResult(true); }
    private void Observe(ComputerObservation observation)
    {
        _observation = observation;
        using var stream = new MemoryStream(Convert.FromBase64String(observation.ImageBase64));
        var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.StreamSource = stream; bitmap.EndInit(); bitmap.Freeze(); _image.Source = bitmap;
        _elements.Text = observation.Title + "\n" + observation.Notice + "\n" + string.Join('\n', observation.Elements.Select(e => $"#{e.Id} {e.Type} {e.Name}" + (e.Value.Length > 0 ? "\n" + e.Value : "")));
        DrawProposal();
    }
    private void DrawProposal()
    {
        _overlay.Children.Clear();
        if (_proposal is not { } review || _observation is not { } observation || review.Action.Type is not ("click" or "double_click" or "right_click" or "drag" or "scroll")) { return; }
        double scale = Math.Min(_viewport.ActualWidth / observation.Bounds.Width, _viewport.ActualHeight / observation.Bounds.Height);
        double left = (_viewport.ActualWidth - observation.Bounds.Width * scale) / 2, top = (_viewport.ActualHeight - observation.Bounds.Height * scale) / 2;
        ComputerElement? element = observation.Elements.FirstOrDefault(e => e.Id == review.Action.Element);
        double x = element is null ? observation.Bounds.Width * review.Action.X / 1000d : element.Bounds.Left - observation.Bounds.Left + element.Bounds.Width / 2d;
        double y = element is null ? observation.Bounds.Height * review.Action.Y / 1000d : element.Bounds.Top - observation.Bounds.Top + element.Bounds.Height / 2d;
        var marker = new Ellipse { Width = 24, Height = 24, Stroke = Brushes.Orange, StrokeThickness = 3, Fill = new SolidColorBrush(Color.FromArgb(60, 255, 150, 0)) }; Canvas.SetLeft(marker, left + x * scale - 12); Canvas.SetTop(marker, top + y * scale - 12); _overlay.Children.Add(marker);
        if (review.Action.Type == "drag") { _overlay.Children.Add(new Line { X1 = left + x * scale, Y1 = top + y * scale, X2 = left + observation.Bounds.Width * review.Action.X2 / 1000d * scale, Y2 = top + observation.Bounds.Height * review.Action.Y2 / 1000d * scale, Stroke = Brushes.Orange, StrokeThickness = 2 }); }
    }
    private async Task ExportAsync()
    {
        var picker = new SaveFileDialog { Filter = "操作履歴JSON|*.json", FileName = "computer-use.json" };
        if (picker.ShowDialog(this) == true)
        { await File.WriteAllTextAsync(picker.FileName, JsonSerializer.Serialize(new { Task = _task.Text, Steps = _steps, Result = _status.Text }, ComputerJson.Options)); Status("操作履歴を書き出しました。入力文字・対象アプリ名を含みます。画面画像は含みません。"); }
    }
    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_allowClose || !_working) { return; }
        e.Cancel = true; if (_closing is not null) { return; }
        _closing = new(TaskCreationOptions.RunContinuationsAsynchronously); Stop();
        try { await _pending; } finally { _allowClose = true; Close(); _closing.TrySetResult(true); }
    }
    private sealed class StepTextConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => value is ComputerStep step ? $"{step.Number:00}  {step.At.ToLocalTime():HH:mm:ss}  {step.Action.Label}\n{step.Action.Reason}\n{step.Result}" : "";
        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => throw new NotSupportedException();
    }
}
