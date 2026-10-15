using System.IO;
using System.Windows;
using System.Windows.Controls;
using Eluna.App.Services;
using Eluna.App.ViewModels;
using Microsoft.Win32;

namespace Eluna.App;

public sealed class DataWindow : Window
{
    private CancellationTokenSource? _work;
    private readonly CancellationTokenSource _lifetime = new();
    private bool _closeAfterCancel;
    private readonly TaskCompletionSource<bool> _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal Task<bool> PendingClose => _closed.Task;
    public DataWindow(ChatViewModel chat)
    {
        Title = "データ・バックアップ · Eluna"; Width = 800; Height = 650; MinWidth = 620; MinHeight = 500;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Style = (Style)Application.Current.FindResource(typeof(Window)); Appearance.Attach(this);
        var tabs = new TabControl { Margin = new(20), BorderThickness = new(0), Padding = new(0) };
        tabs.SetResourceReference(Control.BackgroundProperty, "BackgroundBrush"); tabs.SetResourceReference(Control.ForegroundProperty, "TextBrush");
        tabs.Items.Add(new TabItem { Header = "全体バックアップ", Content = FullBackup(chat), Style = (Style)Application.Current.FindResource("WorkOptionsTab") });
        tabs.Items.Add(new TabItem { Header = "履歴JSON・診断", Content = History(chat), Style = (Style)Application.Current.FindResource("WorkOptionsTab") });
        Content = tabs;
        Closing += (_, e) => { if (_work is not null) { e.Cancel = true; _closeAfterCancel = true; _work.Cancel(); } };
        Closed += (_, _) => { _lifetime.Cancel(); _closed.TrySetResult(true); };
    }
    private UIElement FullBackup(ChatViewModel chat)
    {
        var grid = new Grid { Margin = new(12, 20, 12, 8) };
        grid.RowDefinitions.Add(new()); grid.RowDefinitions.Add(new() { Height = GridLength.Auto }); grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = "アプリ全体を保存・復元", FontSize = 22, FontWeight = FontWeights.SemiBold, Margin = new(0, 0, 0, 16) });
        panel.Children.Add(new TextBlock { Text = "会話・プロジェクト・資料・メモリ・スキル本体・添付・生成物・予約・設定を、1つのZIPへ保存します。", TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 12) });
        panel.Children.Add(new TextBlock { Text = "ダウンロードは一時停止します。推論や生成は停止・完了してから実行してください。予約は保存でき、復元後は再開待ちになります。", TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 12) });
        var models = new CheckBox { Content = "モデル本体・画像入力の補助モデルも含める", Margin = new(0, 4, 0, 12) }; panel.Children.Add(models);
        var summary = new TextBlock { Text = "保存対象の容量を確認しています…", TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 12) }; panel.Children.Add(summary);
        panel.Children.Add(new TextBlock { Text = "モデルを含めない場合も選択・お気に入り等の情報を保存します。別PCでモデルが見つからない場合は、モデル本体の取得・追加が必要です。実行エンジン・通信の一時鍵・取得途中のファイルは含めません。", TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 16) });
        var actions = new WrapPanel();
        var save = new Button { Content = "全体バックアップを作成", Margin = new(0, 0, 8, 8) };
        var restore = new Button { Content = "ZIPから全体を復元", Margin = new(0, 0, 8, 8) };
        actions.Children.Add(save); actions.Children.Add(restore);
        var scroll = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }; grid.Children.Add(scroll);
        var status = new TextBlock { Text = "復元前の全体バックアップを自動で退避します。復元するとアプリを再起動します。", TextWrapping = TextWrapping.Wrap, Margin = new(0, 12, 0, 8) };
        var progress = new ProgressBar { Height = 5, Margin = new(0, 4, 0, 12), Minimum = 0, Maximum = 100 };
        var footer = new StackPanel(); footer.Children.Add(actions); footer.Children.Add(status); footer.Children.Add(progress); Grid.SetRow(footer, 1); grid.Children.Add(footer);
        var bottom = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right };
        var folder = new Button { Content = "退避先を開く", Margin = new(0, 0, 8, 0) };
        folder.Click += (_, _) => { try { string path = Path.Combine(chat.DataDirectory, "Backups"); Directory.CreateDirectory(path); System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true })?.Dispose(); } catch (Exception ex) { status.Text = "退避先を開けませんでした。" + ex.Message; } }; bottom.Children.Add(folder);
        var cancel = new Button { Content = "中止", IsEnabled = false, Margin = new(0, 0, 8, 0) }; cancel.Click += (_, _) => _work?.Cancel(); bottom.Children.Add(cancel);
        var close = new Button { Content = "閉じる" }; close.Click += (_, _) => Close(); bottom.Children.Add(close); Grid.SetRow(bottom, 2); grid.Children.Add(bottom);
        async Task Preview()
        {
            bool include = models.IsChecked == true;
            try
            {
                BackupPreview info = await BackupArchive.PreviewAsync(new(chat.DataDirectory), include, _lifetime.Token);
                if (!_lifetime.IsCancellationRequested && models.IsChecked == include) { summary.Text = $"対象: {info.Files:N0}ファイル / {Eluna.App.Models.FileSize.Format(info.Bytes)}（圧縮前）"; }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { if (!_lifetime.IsCancellationRequested) { summary.Text = "容量を確認できませんでした。" + ex.Message; } }
        }
        async Task Run(Func<CancellationToken, IProgress<BackupProgress>, Task> operation)
        {
            if (_work is not null) { return; }
            _work = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token); tabsEnabled(false); cancel.IsEnabled = true; progress.Value = 0;
            var reporter = new Progress<BackupProgress>(p => { status.Text = p.Stage; progress.Value = p.Percent; });
            try { await operation(_work.Token, reporter); status.Text = "完了しました"; progress.Value = 100; }
            catch (OperationCanceledException) { status.Text = "中止しました。保存済みのバックアップと現在のデータを保持しています。"; }
            catch (Exception ex) { status.Text = "処理できませんでした。" + ex.Message; }
            finally { _work.Dispose(); _work = null; tabsEnabled(true); cancel.IsEnabled = false; if (_closeAfterCancel) { Close(); } }
        }
        void tabsEnabled(bool enabled) { panel.IsEnabled = actions.IsEnabled = enabled; if (Content is TabControl tabs && tabs.Items[1] is TabItem legacy) { legacy.IsEnabled = enabled; } }
        save.Click += async (_, _) =>
        {
            var picker = new SaveFileDialog { Filter = "Eluna全体バックアップ (*.zip)|*.zip", FileName = "Eluna-backup-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".zip" };
            if (picker.ShowDialog(this) != true) { return; }
            bool include = models.IsChecked == true;
            await Run(async (token, reporter) => await chat.WithBackupAsync(async (store, ct) => { await BackupArchive.CreateAsync(store, picker.FileName, include, reporter, ct); return true; }, token));
        };
        restore.Click += async (_, _) =>
        {
            var picker = new OpenFileDialog { Filter = "Eluna全体バックアップ (*.zip)|*.zip", InitialDirectory = Path.Combine(chat.DataDirectory, "Backups") };
            if (picker.ShowDialog(this) != true) { return; }
            PreparedBackup? prepared = null;
            bool accepted = false;
            await Run(async (token, reporter) =>
            {
                BackupManifest info = await BackupArchive.InspectAsync(picker.FileName, token);
                if (MessageBox.Show(this, $"{info.CreatedAt.LocalDateTime:g} / {info.Files.Count:N0}ファイル / {Eluna.App.Models.FileSize.Format(info.Files.Sum(f => f.Length))}\n現在のデータを退避して、このバックアップに置き換えます。アプリを再起動してよいですか？", "全体バックアップの復元", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) { throw new OperationCanceledException(); }
                prepared = await chat.WithBackupAsync((store, ct) => BackupArchive.PrepareAsync(store, picker.FileName, reporter, ct), token);
                token.ThrowIfCancellationRequested(); accepted = true;
            });
            if (prepared is not null)
            {
                if (!accepted || _closeAfterCancel || _lifetime.IsCancellationRequested) { await Task.Run(() => BackupArchive.DeleteStage(prepared.Directory, Path.Combine(chat.DataDirectory, "Backups"))); return; }
                if (Owner is MainWindow main) { Close(); main.RestorePreparedBackup(prepared); }
                else { BackupArchive.DeleteStage(prepared.Directory, Path.Combine(chat.DataDirectory, "Backups")); status.Text = "主画面の設定から復元を実行してください。"; }
            }
        };
        Loaded += async (_, _) => { await chat.PersistSafelyAsync(); await Preview(); }; models.Checked += async (_, _) => await Preview(); models.Unchecked += async (_, _) => await Preview();
        return grid;
    }
    private UIElement History(ChatViewModel chat)
    {
        var grid = new Grid { Margin = new(26) };
        grid.RowDefinitions.Add(new() { Height = GridLength.Auto }); grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new()); grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
        grid.Children.Add(new TextBlock { Text = "履歴の保存・復元", FontSize = 24, Margin = new(0, 0, 0, 20) });
        var actions = new WrapPanel { Margin = new(0, 0, 0, 20) };
        var backups = new ListBox { ItemsSource = chat.GetBackups(), DisplayMemberPath = "Name" };
        var status = new TextBlock { Text = "自動バックアップは最大20世代保存します。復元前の履歴もバックアップします。", TextWrapping = TextWrapping.Wrap, Margin = new(0, 14, 0, 0) };
        async Task Run(Func<Task> work)
        {
            if (!actions.IsEnabled) { return; }
            actions.IsEnabled = backups.IsEnabled = false;
            try { await work(); backups.ItemsSource = chat.GetBackups(); status.Text = "完了しました"; }
            catch (Exception ex) { status.Text = "処理できませんでした。" + ex.Message; }
            finally { actions.IsEnabled = backups.IsEnabled = true; }
        }
        Button Action(string label, RoutedEventHandler handler)
        {
            var button = new Button { Content = label, Margin = new(0, 0, 8, 8) }; button.Click += handler; actions.Children.Add(button); return button;
        }
        Action("履歴・プロジェクトを書き出す", async (_, _) =>
        {
            var picker = new SaveFileDialog { Filter = "履歴JSON (*.json)|*.json", FileName = "Eluna-history.json" };
            if (picker.ShowDialog(this) == true) { await Run(() => chat.ExportHistoryAsync(picker.FileName)); }
        });
        var includeMemories = new CheckBox { Content = "取り込み時に共通メモリも追加する", IsChecked = false, Margin = new(0, 0, 8, 8), VerticalAlignment = VerticalAlignment.Center };
        actions.Children.Add(includeMemories);
        Action("履歴・プロジェクトを取り込む", async (_, _) =>
        {
            var picker = new OpenFileDialog { Filter = "履歴JSON (*.json)|*.json" };
            if (picker.ShowDialog(this) == true) { await Run(() => chat.ImportHistoryAsync(picker.FileName, includeMemories.IsChecked == true)); }
        });
        Action("選択した世代を復元", async (_, _) =>
        {
            if (backups.SelectedItem is FileInfo backup && MessageBox.Show(this, "現在の履歴をバックアップして、この世代に復元しますか？", "履歴の復元", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
            {
                await Run(() => chat.RestoreBackupAsync(backup.FullName));
            }
        });
        Action("診断情報を書き出す", async (_, _) =>
        {
            var picker = new SaveFileDialog { Filter = "診断JSON (*.json)|*.json", FileName = "Eluna-diagnostics.json" };
            if (picker.ShowDialog(this) == true) { await Run(() => chat.ExportDiagnosticsAsync(picker.FileName)); }
        });
        Grid.SetRow(actions, 1); grid.Children.Add(actions); Grid.SetRow(backups, 2); grid.Children.Add(backups); Grid.SetRow(status, 3); grid.Children.Add(status);
        return grid;
    }
}
