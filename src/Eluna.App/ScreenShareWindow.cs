using System.Windows;
using System.Windows.Controls;
using Eluna.App.Models;
using Eluna.App.Services;
using Eluna.App.ViewModels;

namespace Eluna.App;

public sealed class ScreenShareWindow : Window
{
    private readonly CancellationTokenSource _cancel = new();
    internal ListBox Targets { get; } = new();
    internal Button ShareButton { get; } = new() { Content = "このウィンドウを共有", IsEnabled = false };
    public ScreenShareWindow(ChatViewModel chat)
    {
        Title = "画面を共有 · Eluna"; Width = 660; Height = 510; MinWidth = 440; MinHeight = 360; WindowStartupLocation = WindowStartupLocation.CenterOwner; Appearance.Attach(this);
        var root = new DockPanel { Margin = new(22) }; Content = root;
        var heading = new TextBlock { Text = "共有するウィンドウ", FontSize = 22, TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 12) }; DockPanel.SetDock(heading, Dock.Top); root.Children.Add(heading);
        var note = new TextBlock { Text = "選んだウィンドウを共有します。AIに操作させる場合は、共有後に「操作を許可」をオンにしてください。共有中に送るメッセージには画面の読み取りを添付し、会話に保存します。検索設定に応じて検索語を外部へ送信する場合があります。", TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 8) };
        var explanation = new ScrollViewer { Content = note, MaxHeight = 72, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Margin = new(0, 0, 0, 8) }; DockPanel.SetDock(explanation, Dock.Top); root.Children.Add(explanation);
        var footer = new StackPanel { Margin = new(0, 12, 0, 0) }; DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        var status = new TextBlock { Text = "ウィンドウを読み込み中…", TextWrapping = TextWrapping.Wrap }; footer.Children.Add(status);
        var buttons = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right }; footer.Children.Add(buttons);
        var refresh = new Button { Content = "更新", Margin = new(0, 8, 8, 0) }; buttons.Children.Add(refresh); ShareButton.Margin = new(0, 8, 0, 0); ShareButton.SetResourceReference(StyleProperty, "PrimaryButton"); buttons.Children.Add(ShareButton);
        Targets.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        var template = new DataTemplate(); var text = new FrameworkElementFactory(typeof(TextBlock)); text.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(ComputerTarget.Label))); text.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap); template.VisualTree = text; Targets.ItemTemplate = template;
        root.Children.Add(Targets); Targets.SelectionChanged += (_, _) => ShareButton.IsEnabled = Targets.SelectedItem is ComputerTarget;
        async Task Refresh()
        {
            refresh.IsEnabled = ShareButton.IsEnabled = false;
            try { Targets.ItemsSource = await new ComputerDesktop().WindowsAsync(_cancel.Token); status.Text = "最小化したウィンドウは表示してから選んでください。"; }
            catch (OperationCanceledException) { }
            catch (Exception ex) { status.Text = ex.Message; }
            finally { refresh.IsEnabled = true; }
        }
        Loaded += async (_, _) => await Refresh(); refresh.Click += async (_, _) => await Refresh();
        ShareButton.Click += (_, _) => { if (Targets.SelectedItem is ComputerTarget target) { try { chat.StartSharing(target); Close(); } catch (Exception ex) { status.Text = ex.Message; } } };
        Closed += (_, _) => { _cancel.Cancel(); _cancel.Dispose(); };
    }
}
