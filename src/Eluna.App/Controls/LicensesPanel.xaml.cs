using System.Windows;
using System.Windows.Controls;
using Eluna.App.Services;

namespace Eluna.App.Controls;

public partial class LicensesPanel : UserControl, IDisposable
{
    private readonly CancellationTokenSource _lifetime = new();
    private IReadOnlyList<LicenseDocument> _documents = [];
    private Task? _loading;
    private bool _compact, _disposed;

    public LicensesPanel()
    {
        InitializeComponent();
        Loaded += async (_, _) => await LoadAsync();
        SizeChanged += (_, _) =>
        {
            bool compact = ActualHeight < 330;
            if (_compact == compact) { return; }
            _compact = compact;
            OverviewText.Visibility = NoticeDescription.Visibility = NoticeFile.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
            if (NoticesList.SelectedItem is not null) { NoticesList.ScrollIntoView(NoticesList.SelectedItem); }
        };
    }

    internal Task LoadAsync() => _disposed ? Task.CompletedTask : _loading ??= LoadCoreAsync();

    private async Task LoadCoreAsync()
    {
        try
        {
            CancellationToken token = _lifetime.Token;
            _documents = await Task.Run(() => LicenseCatalog.LoadAsync(token), token);
            token.ThrowIfCancellationRequested();
            FilterBox.IsEnabled = true;
            ApplyFilter();
        }
        catch (OperationCanceledException) { }
        catch (Exception)
        {
            if (!_disposed) { LoadStatus.Text = "ライセンスを読み込めませんでした。設定を開き直してください。配布版の通知は保存先の Runtime 内の Licenses フォルダーでも確認できます。"; }
        }
    }

    private void Filter_Changed(object sender, TextChangedEventArgs e)
    {
        if (NoticesList is not null && FilterBox.IsEnabled) { ApplyFilter(); }
    }

    private void ApplyFilter()
    {
        string filter = FilterBox.Text.Trim();
        LicenseDocument? selected = NoticesList.SelectedItem as LicenseDocument;
        LicenseDocument[] matches = _documents.Where(d => (d.Title + " " + d.Description + " " + d.FileName).Contains(filter, StringComparison.OrdinalIgnoreCase)).ToArray();
        NoticesList.ItemsSource = matches;
        NoticesList.SelectedItem = selected is not null && matches.Contains(selected) ? selected : matches.FirstOrDefault();
        LoadStatus.Text = matches.Length == 0 ? "一致する通知がありません。検索語を変更してください。" : $"{matches.Length} 件の通知 · 原文は選択してコピーできます（Ctrl+A / Ctrl+C）。";
    }

    private void Notice_Changed(object sender, SelectionChangedEventArgs e)
    {
        LicenseDocument? selected = NoticesList.SelectedItem as LicenseDocument;
        NoticeTitle.Text = selected?.Title ?? "ライセンス";
        NoticeTitle.ToolTip = selected?.FileName;
        NoticeDescription.Text = selected?.Description ?? "";
        NoticeFile.Text = selected?.FileName ?? "";
        NoticeFile.ToolTip = selected?.FileName;
        NoticeText.Text = selected?.Text ?? "";
        NoticeText.ScrollToHome();
        NoticeText.CaretIndex = 0;
    }

    public void Dispose()
    {
        if (_disposed) { return; }
        _disposed = true;
        _lifetime.Cancel();
        _lifetime.Dispose();
        GC.SuppressFinalize(this);
    }
}
