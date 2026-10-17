using System.Windows;
using System.Windows.Controls;
using Eluna.App.Services;

namespace Eluna.App.Controls;

internal sealed class CharacterGeneratorPanel : Border
{
    internal TextBox Brief { get; } = new() { MaxLength = 2000, TextWrapping = TextWrapping.Wrap, MinHeight = 50, MaxHeight = 100, AcceptsReturn = true, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    internal Button GenerateButton { get; } = new() { Content = "おまかせ生成" };
    private readonly Button _cancel = new() { Content = "中止", Visibility = Visibility.Collapsed, Margin = new(8, 0, 0, 0) };
    private readonly TextBlock _status = new() { Text = "希望は空欄でも作れます。生成後は自由に直してから保存できます。", TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new(0, 8, 0, 0) };
    private readonly Func<string, CancellationToken, Task<CharacterDraft>> _generate;
    private readonly Action<CharacterDraft> _apply;
    private CancellationTokenSource? _cancelSource;
    private readonly ProgressBar _progress = new() { IsIndeterminate = true, Height = 3, Visibility = Visibility.Collapsed, Margin = new(0, 10, 0, 0) };
    internal void Report(string status) { if (Dispatcher.CheckAccess()) { _status.Text = status; } else { Dispatcher.BeginInvoke(() => _status.Text = status); } }
    internal bool IsBusy => _cancelSource is not null;
    internal event Action<bool>? BusyChanged;
    internal CharacterGeneratorPanel(Func<string, CancellationToken, Task<CharacterDraft>> generate, Action<CharacterDraft> apply)
    {
        _generate = generate; _apply = apply; CornerRadius = new(8); Padding = new(14); Margin = new(0, 0, 0, 14); SetResourceReference(BackgroundProperty, "SurfaceBrush");
        _status.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        var panel = new StackPanel(); Child = panel; panel.Children.Add(new TextBlock { Text = "キャラクターを一緒に作る", FontWeight = FontWeights.SemiBold, Margin = new(0, 0, 0, 6) });
        panel.Children.Add(new TextBlock { Text = "例：落ち着いた天文学好き。少し皮肉屋で、名前と口調も考えて", FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 8) }); panel.Children.Add(Brief);
        System.Windows.Automation.AutomationProperties.SetName(Brief, "キャラクターの希望（空欄でおまかせ）");
        var actions = new WrapPanel { Margin = new(0, 10, 0, 0) }; actions.Children.Add(GenerateButton); actions.Children.Add(_cancel); panel.Children.Add(actions); panel.Children.Add(_progress); panel.Children.Add(_status);
        GenerateButton.Click += async (_, _) => await GenerateAsync(); _cancel.Click += (_, _) => Cancel(); Unloaded += (_, _) => Cancel();
    }
    internal void Cancel() => _cancelSource?.Cancel();
    internal async Task<bool> GenerateAsync()
    {
        if (IsBusy) { return false; }
        var source = new CancellationTokenSource(); _cancelSource = source; BusyChanged?.Invoke(true); Brief.IsEnabled = GenerateButton.IsEnabled = false; _cancel.Visibility = _progress.Visibility = Visibility.Visible; _status.Text = "処理リストの順番を待っています。生成中も別のチャンネルを開けます。";
        try { CharacterDraft draft = await _generate(Brief.Text, source.Token); source.Token.ThrowIfCancellationRequested(); _apply(draft); _status.Text = "案を反映しました。性格や背景を確認し、プロフィールを保存してください。"; return true; }
        catch (OperationCanceledException) { _status.Text = "生成を中止しました。入力した希望は残しています。"; return false; }
        catch (Exception ex) { _status.Text = ex.Message; return false; }
        finally { source.Dispose(); _cancelSource = null; Brief.IsEnabled = GenerateButton.IsEnabled = true; _cancel.Visibility = _progress.Visibility = Visibility.Collapsed; BusyChanged?.Invoke(false); }
    }
}
