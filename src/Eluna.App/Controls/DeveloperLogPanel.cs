using System.IO;
using System.Windows;
using System.Windows.Controls;
using Eluna.App.Models;
using Eluna.App.Services;
using Eluna.App.ViewModels;
using Microsoft.Win32;

namespace Eluna.App.Controls;

internal sealed class DeveloperLogPanel : UserControl, IDisposable
{
    private readonly ChatViewModel _chat;
    private readonly ListBox _events = new() { DisplayMemberPath = nameof(DiagnosticEvent.Label), MinHeight = 110, MaxHeight = 260 };
    private readonly TextBox _detail = new() { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, AcceptsReturn = true, MinHeight = 100, MaxHeight = 300, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new(0, 8, 0, 8) };
    private readonly TextBox _filter = new();
    private CancellationTokenSource? _read;
    private IReadOnlyList<DiagnosticEvent> _rows = [];
    private HashSet<Guid>? _correlations;
    internal DeveloperLogPanel(ChatViewModel chat)
    {
        _chat = chat; var body = new StackPanel(); Content = body;
        body.Children.Add(new TextBlock { Text = "技術ログ", FontWeight = FontWeights.SemiBold, Margin = new(0, 12, 0, 8) });
        var actions = new WrapPanel(); body.Children.Add(actions);
        var refresh = new Button { Content = "更新", Margin = new(0, 0, 8, 8) }; refresh.Click += async (_, _) => await RefreshAsync(); actions.Children.Add(refresh);
        var export = new Button { Content = "ログ全体をZIPへ保存…", Margin = new(0, 0, 0, 8) }; actions.Children.Add(export);
        export.Click += async (_, _) =>
        {
            var picker = new SaveFileDialog { Filter = "診断ログ ZIP|*.zip", FileName = "Eluna-logs-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture) + ".zip" };
            if (picker.ShowDialog(Window.GetWindow(this)) != true) { return; }
            try { await _chat.Trace.ExportAsync(picker.FileName); _status.Text = "ログ全体を書き出しました。"; } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { _status.Text = ex.Message; }
        };
        _filter.ToolTip = "領域・イベント・詳細を絞り込む"; System.Windows.Automation.AutomationProperties.SetName(_filter, "技術ログの絞り込み");
        body.Children.Add(_filter); body.Children.Add(_status); body.Children.Add(_events); body.Children.Add(_detail);
        _filter.TextChanged += (_, _) => Filter();
        _events.SelectionChanged += (_, _) => _detail.Text = _events.SelectedItem is DiagnosticEvent row ? row.Label + "\n相関ID: " + row.Correlation + "\nチャンネル: " + row.Room + "\nアカウント: " + row.Agent + "\n\n" + row.Detail : "";
        Unloaded += (_, _) => _read?.Cancel();
    }
    internal void Show(ChatMessage? message = null)
    {
        Visibility = _chat.Settings.DeveloperMode ? Visibility.Visible : Visibility.Collapsed;
        _correlations = message is null ? null : new HashSet<Guid>();
        if (message?.DiagnosticsId is Guid correlation) { _correlations!.Add(correlation); }
        if (message?.Artifact is { } artifact)
        { _correlations!.Add(artifact.JobId); if (_chat.MediaJobs.FirstOrDefault(j => j.Id == artifact.JobId)?.Request?.ParentOperation is Guid parent) { _correlations.Add(parent); } }
        if (_chat.Settings.DeveloperMode) { _ = RefreshAsync(); }
    }
    internal async Task RefreshAsync()
    {
        _read?.Cancel(); _read?.Dispose(); _read = new(); CancellationToken token = _read.Token;
        try
        {
            IReadOnlyList<DiagnosticEvent> rows = await _chat.Trace.ReadAsync(token: token, correlations: _correlations);
            if (token.IsCancellationRequested) { return; }
            _rows = _correlations is null ? rows : rows.Where(r => r.Correlation is Guid id && _correlations.Contains(id)).ToArray();
            Filter(); _status.Text = (_chat.Settings.Logs ?? new()).Level + " · 関連 " + _rows.Count + "件（関連する最新5000件まで）" + (_chat.Trace.Dropped > 0 ? " · 保存できなかった記録 " + _chat.Trace.Dropped + "件" : "") + (_chat.Trace.LastFailure is string failure ? "\nログ保存エラー: " + failure : "");
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { _status.Text = ex.Message; }
    }
    private void Filter()
    { string query = _filter.Text.Trim(); _events.ItemsSource = _rows.Where(r => query.Length == 0 || (r.Area + " " + r.Event + " " + r.Detail).Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray(); }
    public void Dispose() { _read?.Cancel(); _read?.Dispose(); _read = null; }
}
