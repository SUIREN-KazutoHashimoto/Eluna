using System.Windows;
using System.Windows.Controls;
using Eluna.App.Controls;
using Eluna.App.Models;
using Eluna.App.Services;
using Eluna.App.ViewModels;

namespace Eluna.App;

public sealed class UpdateWindow : Window
{
    private readonly UpdateCoordinator? _updates;
    public UpdateWindow(ChatViewModel? chat = null)
    {
        _updates = Application.Current.Windows.OfType<MainWindow>().FirstOrDefault(w => ReferenceEquals(w.ViewModel, chat))?.Updates;
        Title = "バージョン・更新 · Eluna"; Width = 760; Height = 660; MinWidth = 540; MinHeight = 400; WindowStartupLocation = WindowStartupLocation.CenterOwner; Appearance.Attach(this);
        var root = new DockPanel { Margin = new(24) }; Content = new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }; var header = new StackPanel(); DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        header.Children.Add(new TextBlock { Text = "Eluna " + UpdateService.CurrentVersion, FontSize = 24, FontWeight = FontWeights.SemiBold });
        var train = new ComboBox { ItemsSource = new[] { new KeyValuePair<string, string>("stable", "正式版 · main"), new("develop", "開発版 · develop") }, DisplayMemberPath = "Value", SelectedValuePath = "Key", SelectedValue = (chat?.Settings.Updates ?? new()).Train, Margin = new(0, 16, 0, 12), IsEnabled = chat is not null };
        header.Children.Add(train); var auto = new CheckBox { Content = new TextBlock { Text = "更新を自動で取得し、作業終了後・画面を使っていないときに適用する", TextWrapping = TextWrapping.Wrap }, IsChecked = (chat?.Settings.Updates ?? new()).Automatic, IsEnabled = chat is not null, Margin = new(0, 0, 0, 12) }; header.Children.Add(auto);
        header.Children.Add(new TextBlock { Text = InstallerService.Current is null ? "単体exe版です。インストールすると自動更新を利用できます。トレインを変えても古い版へ自動で戻しません。" : "インストール版です。推論・ダウンロード・VC中は更新を保留します。起動に失敗すると前のexeへ戻します。", TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 12) });
        var status = new TextBlock { Text = "更新画面は設定から開けます。", TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 12) }; header.Children.Add(status);
        var actions = new WrapPanel(); header.Children.Add(actions);
        var check = new Button { Content = "更新を確認", Margin = new(0, 0, 8, 8) }; var download = new Button { Content = "取得", Margin = new(0, 0, 8, 8) }; var restart = new Button { Content = "再起動して適用", Margin = new(0, 0, 8, 8) }; var cancel = new Button { Content = "取得を中止", Margin = new(0, 0, 8, 8) }; var install = new Button { Content = "インストール…", Margin = new(0, 0, 8, 8), Visibility = InstallerService.Current is null ? Visibility.Visible : Visibility.Collapsed };
        foreach (Button button in new[] { check, download, restart, cancel, install }) { actions.Children.Add(button); }
        var notes = new MarkdownView { TextSize = 14, TextLineHeight = 22 }; root.Children.Add(new ScrollViewer { Content = notes, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        async Task SaveOptions() { if (chat is not null) { try { await chat.ApplySettingsAsync(chat.Settings with { Updates = new((string)train.SelectedValue, auto.IsChecked == true) }); } catch (Exception e) when (e is System.IO.IOException or UnauthorizedAccessException or InvalidOperationException) { status.Text = "更新設定を保存できませんでした: " + e.Message; } } }
        train.SelectionChanged += async (_, _) => await SaveOptions(); auto.Click += async (_, _) => await SaveOptions();
        check.Click += async (_, _) => { if (_updates is not null) { await _updates.CheckAsync(); } }; download.Click += async (_, _) => { if (_updates is not null) { await _updates.DownloadAsync(); } }; cancel.Click += (_, _) => _updates?.Cancel();
        restart.Click += async (_, _) => { if (_updates is null || chat?.CanApplyUpdate != true) { status.Text = "処理とVCを終了してから適用してください。"; return; } Close(); await _updates.ApplyAsync(false); };
        install.Click += (_, _) => new InstallerWindow { Owner = this }.Show();
        void Refresh()
        {
            if (_updates is null) { check.IsEnabled = download.IsEnabled = restart.IsEnabled = cancel.IsEnabled = false; return; }
            status.Text = _updates.Status; check.IsEnabled = !_updates.Busy; download.IsEnabled = !_updates.Busy && _updates.Release?.IsNewer == true; restart.IsEnabled = _updates.Ready && InstallerService.Current is not null; cancel.IsEnabled = _updates.Busy; notes.Markdown = _updates.Notes;
        }
        System.ComponentModel.PropertyChangedEventHandler changed = (_, _) => Refresh(); if (_updates is not null) { _updates.PropertyChanged += changed; }
        Closed += (_, _) => { if (_updates is not null) { _updates.PropertyChanged -= changed; } }; Refresh();
    }
}
