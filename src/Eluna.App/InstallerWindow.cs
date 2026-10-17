using System.IO;
using System.Windows;
using System.Windows.Controls;
using Eluna.App.Services;

namespace Eluna.App;

internal sealed class InstallerWindow : Window
{
    internal InstallerWindow(bool uninstall = false)
    {
        Title = (uninstall ? "アンインストール" : "Elunaのインストール") + " · Eluna"; Width = 600; Height = 460; MinWidth = 480; MinHeight = 340; WindowStartupLocation = WindowStartupLocation.CenterScreen; Appearance.Attach(this);
        var root = new DockPanel { Margin = new(28) }; Content = root;
        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right }; DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        var close = new Button { Content = "閉じる", Margin = new(0, 0, 10, 0) }; close.Click += (_, _) => Close(); footer.Children.Add(close);
        var install = new Button { Content = uninstall ? "アンインストール" : "インストール" }; footer.Children.Add(install);
        var panel = new StackPanel(); var scroll = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }; root.Children.Add(scroll);
        panel.Children.Add(new TextBlock { Text = uninstall ? "Elunaをアンインストール" : "ElunaをこのPCに追加", FontSize = 24, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        var detail = new TextBlock { Text = uninstall ? "アプリ本体とElunaのショートカットを削除します。会話・モデル・生成物は保存先に残します。" : "利用者専用のフォルダーへ導入します。管理者権限や追加の環境構築は不要です。", TextWrapping = TextWrapping.Wrap, Margin = new(0, 16, 0, 16) }; panel.Children.Add(detail);
        panel.Children.Add(new TextBlock { Text = InstallerService.DefaultDirectory, TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 16) });
        var shortcuts = new CheckBox { Content = new TextBlock { Text = "スタートメニューとデスクトップへショートカットを作る", TextWrapping = TextWrapping.Wrap }, IsChecked = true, Visibility = uninstall ? Visibility.Collapsed : Visibility.Visible }; panel.Children.Add(shortcuts);
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new(0, 16, 0, 16) }; panel.Children.Add(status);
        var launch = new Button { Content = "Elunaを起動", Visibility = Visibility.Collapsed, HorizontalAlignment = HorizontalAlignment.Left }; panel.Children.Add(launch);
        launch.Click += (_, _) => { InstallerService.Start(Path.Combine(InstallerService.DefaultDirectory, "Eluna.exe")).Dispose(); Close(); };
        install.Click += async (_, _) =>
        {
            install.IsEnabled = false; close.IsEnabled = false; status.Text = uninstall ? "削除を準備しています…" : "アプリをコピーしています…";
            try
            {
                if (uninstall)
                {
                    var marker = InstallerService.Current ?? throw new IOException("インストール記録が見つかりません。");
                    string helper = Path.Combine(Path.GetTempPath(), "Eluna-uninstall-" + Guid.NewGuid().ToString("N") + ".exe"); File.Copy(InstallerService.Executable!, helper);
                    InstallerService.Start(helper, "--remove-install", marker.Directory, marker.Id.ToString("N"), Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture)).Dispose(); Application.Current.Shutdown(); return;
                }
                if (string.Equals(Path.GetDirectoryName(InstallerService.Executable), InstallerService.DefaultDirectory, StringComparison.OrdinalIgnoreCase)) { throw new IOException("すでにインストール先から起動しています。更新画面を利用してください。"); }
                using (Stream? bundle = typeof(InstallerWindow).Assembly.GetManifestResourceStream("Eluna.NativeRuntime.zip")) { if (bundle is null) { throw new IOException("インストールには配布用のEluna-Setup.exeを使用してください。"); } }
                await InstallerService.InstallAsync(InstallerService.Executable!, InstallerService.DefaultDirectory, shortcuts.IsChecked == true, true, CancellationToken.None);
                status.Text = "インストールしました。会話やモデルは従来の保存先を使用します。"; launch.Visibility = Visibility.Visible;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException) { status.Text = e.Message; install.IsEnabled = true; }
            finally { close.IsEnabled = true; }
        };
    }
}
