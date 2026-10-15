using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using Eluna.App.Controls;
using Eluna.App.Services;

namespace Eluna.App;

public sealed class UpdateWindow : Window
{
    private readonly UpdateService _service = new();
    private readonly CancellationTokenSource _cancel = new();
    public UpdateWindow()
    {
        Title = "バージョン・更新 · Eluna"; Width = 760; Height = 660; MinWidth = 540; MinHeight = 400; WindowStartupLocation = WindowStartupLocation.CenterOwner; Appearance.Attach(this);
        var root = new DockPanel { Margin = new(24) }; var header = new StackPanel(); DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        header.Children.Add(new TextBlock { Text = "Eluna " + UpdateService.CurrentVersion, FontSize = 24, FontWeight = FontWeights.SemiBold });
        header.Children.Add(new TextBlock { Text = "公式GitHubのリリースを確認します。", Margin = new(0, 10, 0, 18) });
        var status = new TextBlock { Text = "更新は自動でインストールされません。", TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 16) }; header.Children.Add(status);
        var footer = new StackPanel { Orientation = Orientation.Horizontal, Margin = new(0, 16, 0, 0) }; DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        var check = new Button { Content = "更新を確認" }; check.SetResourceReference(StyleProperty, "PrimaryButton"); footer.Children.Add(check);
        var open = new Button { Content = "リリースを開く ↗", IsEnabled = false, Margin = new(8, 0, 0, 0) }; footer.Children.Add(open);
        var notes = new MarkdownView(); root.Children.Add(new ScrollViewer { Content = notes, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        ReleaseInfo? release = null;
        check.Click += async (_, _) =>
        {
            check.IsEnabled = false; open.IsEnabled = false; status.Text = "公開された正式リリースを確認しています…";
            try
            {
                release = await _service.CheckAsync(_cancel.Token);
                if (release is null) { status.Text = "公開された正式リリースを取得できませんでした。リポジトリやリリースが未公開の場合も表示されます。"; notes.Markdown = ""; }
                else
                {
                    status.Text = (release.IsNewer switch { true => $"新しいバージョン {release.Tag} があります。", false => $"公開版は {release.Tag} です。これより新しい正式リリースはありません。", _ => $"公開版は {release.Tag} です。現在の版との自動比較はできませんでした。" }) +
                        (release.Published is null ? "" : $" 公開 {release.Published.Value.ToLocalTime():yyyy-MM-dd}");
                    notes.Markdown = "# " + release.Name + "\n\n" + release.Notes; open.IsEnabled = true;
                }
            }
            catch (OperationCanceledException) { if (!_cancel.IsCancellationRequested) { status.Text = "確認がタイムアウトしました。再試行できます。"; } }
            catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or System.Text.Json.JsonException) { status.Text = "更新を確認できませんでした。通信状態を確認して再試行してください。"; }
            finally { check.IsEnabled = true; }
        };
        open.Click += (_, _) =>
        {
            if (release is null) { return; }
            try { Process.Start(new ProcessStartInfo(release.Page.AbsoluteUri) { UseShellExecute = true }); }
            catch (System.ComponentModel.Win32Exception) { status.Text = "ブラウザーを起動できませんでした。"; }
        };
        Closed += (_, _) => { _cancel.Cancel(); _service.Dispose(); _cancel.Dispose(); }; Content = root;
    }
}
