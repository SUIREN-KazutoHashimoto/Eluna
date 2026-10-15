using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Eluna.App.Models;
using Eluna.App.Services;

namespace Eluna.App;

public sealed record ImageGalleryEntry(string Name, string Repository, string Description, string Resolution, bool Verified);
public sealed class ImageModelGalleryWindow : Window
{
    public static IReadOnlyList<ImageGalleryEntry> Entries { get; } = [
        new("Stable Diffusion 1.5", "second-state/stable-diffusion-v1-5-GGUF", "初めての画像生成に。比較的小さい一体型モデル。", "512 × 512", true),
        new("Stable Diffusion 2.1", "second-state/stable-diffusion-2-1-GGUF", "768px向けの一体型モデル。実生成は未確認。", "768 × 768", false),
        new("Stable Diffusion 1.4", "second-state/stable-diffusion-v-1-4-GGUF", "SD1系列の別候補。実生成は未確認。", "512 × 512", false)];
    public ImageModelGalleryWindow(AppStore store, DownloadQueue downloads, Action<string> select, ModelCatalog? catalog = null)
    {
        Title = "画像生成モデルのギャラリー · Eluna"; Width = 900; Height = 740; MinWidth = 660; MinHeight = 480; Appearance.Attach(this);
        var lifetime = new CancellationTokenSource(); var browser = catalog ?? new ModelCatalog(store);
        var root = new DockPanel { Margin = new(22) }; Content = root;
        var header = new TextBlock { Text = "画像生成モデルを選ぶ", FontSize = 23, Margin = new(0, 0, 0, 8) }; DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        var note = new TextBlock { Text = "対応する一体型GGUFの候補です。容量・量子化・利用条件を確認して取得してください。取得は処理リストで続きます。", TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 12) }; DockPanel.SetDock(note, Dock.Top); root.Children.Add(note);
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new(0, 10, 0, 0) }; DockPanel.SetDock(status, Dock.Bottom); root.Children.Add(status);
        var rows = new StackPanel(); root.Children.Add(new ScrollViewer { Content = rows, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        var updates = new List<Action>();
        foreach (ImageGalleryEntry entry in Entries)
        {
            var card = new Border { CornerRadius = new(12), Padding = new(16), Margin = new(0, 0, 0, 12) }; card.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush"); rows.Children.Add(card);
            var content = new StackPanel(); card.Child = content;
            content.Children.Add(new TextBlock { Text = entry.Name, FontSize = 18, FontWeight = FontWeights.SemiBold });
            content.Children.Add(new TextBlock { Text = entry.Description + "\n" + entry.Resolution + (entry.Verified ? " · SD 1.5 Q4_0の実生成を確認済み" : " · エンジンの対応形式"), TextWrapping = TextWrapping.Wrap, Margin = new(0, 6, 0, 10) });
            var files = new ComboBox { DisplayMemberPath = "Detail", Margin = new(0, 0, 0, 8) }; System.Windows.Automation.AutomationProperties.SetName(files, entry.Name + "の量子化と容量"); content.Children.Add(files);
            var buttons = new WrapPanel(); content.Children.Add(buttons);
            var inspect = new Button { Content = "容量とファイルを確認" }; var get = new Button { Content = "取得キューへ追加", IsEnabled = false }; var use = new Button { Content = "選択して使う", IsEnabled = false }; var info = new Button { Content = "配布元・利用条件" };
            foreach (Button button in new[] { inspect, get, use, info }) { buttons.Children.Add(button); }
            DownloadItem? item = null;
            inspect.Click += async (_, _) => { inspect.IsEnabled = false; try { var candidates = await browser.GetFilesAsync(entry.Repository, lifetime.Token); files.ItemsSource = candidates; files.SelectedItem = candidates.FirstOrDefault(f => f.Filename.Contains("Q4_0", StringComparison.OrdinalIgnoreCase)) ?? candidates.FirstOrDefault(); get.IsEnabled = files.SelectedItem is ModelFile; status.Text = "ファイルの容量・量子化・利用条件を確認してください。"; } catch (OperationCanceledException) { } catch (Exception ex) { status.Text = DiagnosticsService.Explain(ex); } finally { inspect.IsEnabled = true; } };
            files.SelectionChanged += (_, _) => { item = files.SelectedItem is ModelFile file ? downloads.Items.FirstOrDefault(i => i.AssetKind == "image" && i.File?.Repository == file.Repository && i.File.Filename == file.Filename && i.File.Revision == file.Revision) : null; get.IsEnabled = files.SelectedItem is ModelFile; };
            get.Click += (_, _) => { if (files.SelectedItem is ModelFile file) { item = downloads.EnqueueImage(file); status.Text = "処理リストへ追加しました。ギャラリーを閉じても取得を続けます。"; } };
            use.Click += (_, _) => { if (item is { Status: "complete", ResultPath: not null } && File.Exists(item.ResultPath)) { select(item.ResultPath); status.Text = "画像生成モデルを選択しました。"; } };
            info.Click += (_, _) => new ModelInfoWindow(store, entry.Repository, (files.SelectedItem as ModelFile)?.Revision ?? "main", entry.Name, "利用条件は配布元で確認してください。") { Owner = this }.Show();
            var progress = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 11, Margin = new(0, 8, 0, 0) }; content.Children.Add(progress);
            updates.Add(() => { use.IsEnabled = item is { Status: "complete", ResultPath: not null } && File.Exists(item.ResultPath); get.IsEnabled = files.SelectedItem is ModelFile && item?.Status is not ("active" or "queued"); progress.Text = item is null ? entry.Repository : item.Label + " · " + item.Detail; });
        }
        var timer = new DispatcherTimer(TimeSpan.FromMilliseconds(350), DispatcherPriority.Background, (_, _) => { foreach (var update in updates) { update(); } }, Dispatcher); timer.Start();
        Closed += (_, _) => { timer.Stop(); lifetime.Cancel(); browser.Dispose(); };
    }
}
