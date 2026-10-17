using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Eluna.App.Services;

namespace Eluna.App;

public sealed class ShortcutsWindow : Window
{
    public ShortcutsWindow()
    {
        Title = "キーボードショートカット · Eluna"; Width = 540; Height = 620; MinWidth = 380; MinHeight = 350; WindowStartupLocation = WindowStartupLocation.CenterOwner; ShowInTaskbar = false; Appearance.Attach(this);
        var root = new DockPanel { Margin = new(24) }; Content = root; var close = new Button { Content = "閉じる · Esc", HorizontalAlignment = HorizontalAlignment.Right, Margin = new(0, 14, 0, 0) }; DockPanel.SetDock(close, Dock.Bottom); root.Children.Add(close); close.Click += (_, _) => Close();
        var list = new StackPanel(); root.Children.Add(new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }); list.Children.Add(new TextBlock { Text = "キーボードで使う", FontSize = 23, FontWeight = FontWeights.SemiBold, Margin = new(0, 0, 0, 20) });
        foreach ((string action, string key) in new[] { ("会話・サーバー・メンバーへ移動", "Ctrl + K"), ("戻る / 進む", "Alt + ← / →"), ("前 / 次のチャンネル", "Alt + ↑ / ↓"), ("メンバー一覧", "Ctrl + U"), ("メッセージを検索", "Ctrl + F"), ("すべての場所から検索", "Ctrl + Shift + F"), ("受信トレイ", "Ctrl + I"), ("ピン留めした発言", "Ctrl + P"), ("絵文字を選ぶ", "Ctrl + E"), ("太字 / 斜体", "Ctrl + Shift + B / I"), ("取り消し線 / コード", "Ctrl + Shift + X / O"), ("行の操作メニュー", "メニューキー / Shift + F10"), ("サイドバー", "Ctrl + B"), ("新しい会話", "Ctrl + N"), ("最新のメッセージへ", "Ctrl + End ※"), ("回答をコピー", "Ctrl + Shift + C"), ("回答を再生成", "Ctrl + Shift + R"), ("メンション候補の確定", "Tab / Enter"), ("候補を閉じる", "Esc"), ("改行", "Shift + Enter"), ("この一覧", "Ctrl + /"), ("PC操作の緊急停止", "Ctrl + Alt + Esc") })
        {
            var row = new Grid { Margin = new(0, 0, 0, 14) }; row.ColumnDefinitions.Add(new()); row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            row.Children.Add(new TextBlock { Text = action, TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 14, 0) }); var keys = new TextBlock { Text = key, FontFamily = new("Consolas"), FontSize = 12, VerticalAlignment = VerticalAlignment.Center }; keys.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush"); Grid.SetColumn(keys, 1); row.Children.Add(keys); list.Children.Add(row);
        }
        list.Children.Add(new TextBlock { Text = "※ テキスト欄の外で使えます。送信キーは設定 → 外観・操作で変更できます。Escは候補・メンバー一覧を閉じ、それらを開いていないときは表示中の実行を停止します。IME変換中は文字の確定を優先します。", TextWrapping = TextWrapping.Wrap, FontSize = 12 });
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; Close(); } };
    }
}
