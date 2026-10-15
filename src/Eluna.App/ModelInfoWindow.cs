using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using Eluna.App.Controls;
using Eluna.App.Models;
using Eluna.App.Services;

namespace Eluna.App;

public sealed class ModelInfoWindow : Window
{
    private readonly ModelCatalog _catalog;
    private readonly CancellationTokenSource _cancel = new();
    public ModelInfoWindow(AppStore store, string repository, string revision, string title, string summary)
    {
        _catalog = new(store); Title = "モデル情報 · Eluna"; Width = 920; Height = 780; MinWidth = 640; MinHeight = 440; WindowStartupLocation = WindowStartupLocation.CenterOwner; Appearance.Attach(this);
        var root = new DockPanel { Margin = new(24) }; var head = new StackPanel { Margin = new(0, 0, 0, 18) }; DockPanel.SetDock(head, Dock.Top); root.Children.Add(head);
        head.Children.Add(new TextBlock { Text = title, FontSize = 22, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        head.Children.Add(new TextBlock { Text = summary, TextWrapping = TextWrapping.Wrap, Margin = new(0, 10, 0, 12) });
        var status = new TextBlock { Text = "配布元の説明を取得しています…", TextWrapping = TextWrapping.Wrap }; head.Children.Add(status);
        var body = new MarkdownView(); root.Children.Add(new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }); Content = root;
        Loaded += async (_, _) =>
        {
            if (repository == "local") { status.Text = "手元から追加したモデルです。能力や利用条件は元の配布元で確認してください。"; return; }
            try { body.Markdown = await _catalog.GetCardAsync(repository, revision, _cancel.Token); status.Text = "モデルカード（配布元の説明）。得意分野、日本語、思考対応の説明を確認してください。品質を保証する評価値ではありません。"; }
            catch (OperationCanceledException) { }
            catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or System.IO.InvalidDataException) { status.Text = "モデルカードを取得できませんでした。配布元のページでも確認できます。"; }
        };
        var page = new Button { Content = "配布元を開く ↗", HorizontalAlignment = HorizontalAlignment.Left }; head.Children.Add(page);
        page.Click += (_, _) => { if (repository != "local") { try { Process.Start(new ProcessStartInfo("https://huggingface.co/" + string.Join('/', repository.Split('/').Select(Uri.EscapeDataString))) { UseShellExecute = true }); } catch (System.ComponentModel.Win32Exception) { status.Text = "ブラウザーを開けませんでした。"; } } };
        Closed += (_, _) => { _cancel.Cancel(); _catalog.Dispose(); _cancel.Dispose(); };
    }
    public static string Summary(LocalModel model, int context, MemoryCapacity memory)
    {
        ModelProfile? p = model.Profile;
        return $"{model.SizeLabel} · {model.Files.Count}ファイル · {model.License}\n" + (p is null ? "GGUFの能力情報を読み取れませんでした。\n" :
            $"{p.Architecture} · {p.ParameterLabel} · {p.Quantization} · 学習時の記憶長 {p.TrainingContext?.ToString("N0") ?? "不明"}\n{p.ThinkingLabel} · {p.JapaneseLabel}\n") + HardwareAdvisor.Describe(model.Size, p, context, memory);
    }
}
