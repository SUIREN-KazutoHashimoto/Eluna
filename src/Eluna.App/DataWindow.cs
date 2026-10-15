using System.IO;
using System.Windows;
using System.Windows.Controls;
using Eluna.App.Services;
using Eluna.App.ViewModels;
using Microsoft.Win32;

namespace Eluna.App;

public sealed class DataWindow : Window
{
    public DataWindow(ChatViewModel chat)
    {
        Title = "データ・バックアップ · Eluna"; Width = 760; Height = 560; MinWidth = 620; MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Style = (Style)Application.Current.FindResource(typeof(Window)); Appearance.Attach(this);
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
        Content = grid;
    }
}
