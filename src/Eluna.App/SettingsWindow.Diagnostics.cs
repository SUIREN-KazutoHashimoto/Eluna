using System.Windows;
using System.Windows.Controls;
using Eluna.App.Controls;
using Eluna.App.Models;

namespace Eluna.App;

public partial class SettingsWindow
{
    private CheckBox _developer = null!, _captureLogs = null!;
    private ComboBox _logLevel = null!, _logDays = null!, _logSize = null!;
    private DeveloperLogPanel _allLogs = null!, _messageLogs = null!;
    private void InitializeDiagnostics()
    {
        LogOptions options = _viewModel.Settings.Logs ?? new(); var body = new StackPanel { Margin = new(0, 0, 8, 0) };
        var tab = new TabItem { Header = "開発者・ログ", Content = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled } }; tab.SetResourceReference(StyleProperty, "SettingsCategory"); SettingsTabs.Items.Add(tab);
        body.Children.Add(new TextBlock { Text = "開発者モードとログ", FontSize = 22, FontWeight = FontWeights.Bold, Margin = new(0, 0, 0, 16) });
        _developer = new() { Content = "開発者モード（メッセージ詳細に技術ログを表示）", IsChecked = _viewModel.Settings.DeveloperMode }; body.Children.Add(_developer);
        body.Children.Add(new TextBlock { Text = "表示と収集は別の設定です。通常の会話本文へ技術ログを追加しません。", TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 16) });
        _logLevel = new() { DisplayMemberPath = "Value", SelectedValuePath = "Key", ItemsSource = new[] { new KeyValuePair<string, string>("off", "オフ"), new("normal", "標準 · 処理・警告・エラー"), new("detailed", "詳細 · 操作と各工程を追跡") }, SelectedValue = options.Level };
        _captureLogs = new() { Content = new TextBlock { Text = "モデル・道具の入出力本文も保存（会話・資料・画面のテキストを含む）", TextWrapping = TextWrapping.Wrap }, IsChecked = options.CaptureContents, IsEnabled = options.Level == "detailed", Margin = new(0, 12, 0, 8) };
        _logDays = new() { ItemsSource = new[] { 1, 7, 14, 30, 90, options.RetentionDays }.Distinct().Order().ToArray(), SelectedItem = options.RetentionDays };
        _logSize = new() { ItemsSource = new[] { 8, 16, 32, 64, 128, 256, 512, 1024, 2048, options.MaxMegabytes }.Distinct().Order().ToArray(), SelectedItem = options.MaxMegabytes };
        void Field(string title, FrameworkElement element) { body.Children.Add(new TextBlock { Text = title, TextWrapping = TextWrapping.Wrap, Margin = new(0, 12, 0, 6) }); body.Children.Add(element); System.Windows.Automation.AutomationProperties.SetName(element, title); }
        Field("ログの収集レベル", _logLevel); body.Children.Add(_captureLogs); Field("保持日数", _logDays); Field("保存上限（MiB）", _logSize);
        body.Children.Add(new TextBlock { Text = "ログはアプリの保存場所のLogsへ保存します。収集をオフにしても、既存の記録は保持期間・容量の範囲で残ります。認証ヘッダー・既知のトークン形式・パスワード項目は伏せます。", TextWrapping = TextWrapping.Wrap, Margin = new(0, 12, 0, 8) });
        _allLogs = new(_viewModel); body.Children.Add(_allLogs); _allLogs.Show();
        _messageLogs = new(_viewModel); ((StackPanel)MessageInfoBox.Parent).Children.Add(_messageLogs);
        _developer.Click += (_, _) => { if (_fieldsReady) { Edited(); } }; _captureLogs.Click += (_, _) => { if (_fieldsReady) { Edited(); } };
        _logLevel.SelectionChanged += (_, _) => { _captureLogs.IsEnabled = _logLevel.SelectedValue is "detailed"; if (_fieldsReady) { Edited(); } };
        Closed += (_, _) => { _allLogs.Dispose(); _messageLogs.Dispose(); };
    }
    private object DiagnosticsSnapshot() => new { DeveloperMode = _developer.IsChecked, Level = _logLevel.SelectedValue, Capture = _captureLogs.IsChecked, Days = _logDays.SelectedItem, Size = _logSize.SelectedItem };
    private LogOptions DiagnosticOptions() => new(_logLevel.SelectedValue as string ?? "normal", _captureLogs.IsChecked == true, _logDays.SelectedItem is int days ? days : 14, _logSize.SelectedItem is int size ? size : 64);
}
