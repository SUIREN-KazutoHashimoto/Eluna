using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Eluna.App.Models;

namespace Eluna.App.Controls;

internal sealed class ChannelSocialEditor : StackPanel
{
    private readonly CheckBox _enabled = new() { Content = "アカウント同士の会話を有効にする" },
        _interjections = new() { Content = "指名されていないアカウントも会話に参加する" },
        _spontaneous = new() { Content = "静かな時にアカウントから話題を始める" };
    private readonly Slider _start = Range(0, 23), _end = Range(0, 23), _interval = Range(1, 1440),
        _turns = Range(1, 100), _tokens = Range(256, 65536, 256), _pace = Range(1, 300);
    private static Slider Range(int min, int max, int step = 1) => new() { Minimum = min, Maximum = max, TickFrequency = step, IsSnapToTickEnabled = true };
    internal ChannelSocialEditor(SocialPolicy policy)
    {
        Children.Add(new TextBlock { Text = "アプリを開いている間だけ動作します。本人の投稿で新しい会話に切り替わり、停止中のチャンネルは自動で再開しません。推論の同時実行数とアクセス権に従います。", TextWrapping = TextWrapping.Wrap });
        foreach (CheckBox box in new[] { _enabled, _interjections, _spontaneous })
        { box.Content = new TextBlock { Text = (string)box.Content, TextWrapping = TextWrapping.Wrap }; box.Margin = new(0, 10, 0, 0); Children.Add(box); }
        Field("自発的な開始の時間帯・開始時（0〜23）", _start); Field("終了時（開始と同じなら終日）", _end);
        Field("自発的に開始する最小間隔（分）", _interval); Field("1会話の発言機会上限（沈黙・リアクションを含む）", _turns);
        Field("1会話の生成予算（token）", _tokens); Field("返事を待つ間隔（秒）", _pace);
        _enabled.IsChecked = policy.Enabled; _interjections.IsChecked = policy.Interjections; _spontaneous.IsChecked = policy.Spontaneous;
        _start.Value = policy.StartHour; _end.Value = policy.EndHour; _interval.Value = policy.IntervalMinutes;
        _turns.Value = policy.MaxTurns; _tokens.Value = policy.MaxTokens; _pace.Value = policy.PaceSeconds;
    }
    private void Field(string title, Slider control)
    {
        var text = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new(0, 12, 0, 4) };
        text.SetBinding(TextBlock.TextProperty, new Binding("Value") { Source = control, StringFormat = title + ": {0:F0}" });
        Children.Add(text); Children.Add(control); System.Windows.Automation.AutomationProperties.SetName(control, title);
    }
    internal SocialPolicy Policy => new(_enabled.IsChecked == true, _interjections.IsChecked == true, _spontaneous.IsChecked == true,
        (int)_start.Value, (int)_end.Value, (int)_interval.Value, (int)_turns.Value, (int)_tokens.Value, (int)_pace.Value);
}
