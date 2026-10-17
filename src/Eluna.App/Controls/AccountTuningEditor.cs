using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Eluna.App.Models;
using Eluna.App.Services;

namespace Eluna.App.Controls;

internal sealed class AccountTuningEditor : StackPanel
{
    private readonly CheckBox _override = new() { Content = "このアカウントの生成設定を使う（オフはモデル設定を継承）" },
        _initiates = new() { Content = "自分から話題を始める" }, _artist = new() { Content = "絵描きスキルを持つ" },
        _spontaneousArt = new() { Content = "自律会話で自分から絵を描くことを許可" };
    private readonly Slider _temperature = Range(0, 2, .05), _topP = Range(.05, 1, .05), _output = Range(128, 8192, 64),
        _participation = Range(0, 1, .05), _cooldown = Range(5, 3600, 5);
    private readonly TextBox _interests = new() { MaxLength = 1000, TextWrapping = TextWrapping.Wrap },
        _artStyle = new() { MaxLength = 1000, TextWrapping = TextWrapping.Wrap };
    private static Slider Range(double min, double max, double step) => new() { Minimum = min, Maximum = max, TickFrequency = step, IsSnapToTickEnabled = true, Margin = new(0, 4, 0, 10) };
    internal AccountTuningEditor(Func<AgentPersonality> personality)
    {
        var social = Section("会話への参加");
        Field(social, "興味・好きな話題", _interests); Field(social, "参加頻度（0は指名された時だけ、1は積極的）", _participation, "{0:P0}");
        social.Children.Add(_initiates); Field(social, "自発的な発言の間隔（秒）", _cooldown, "{0:F0}");
        social.Children.Add(new TextBlock { Text = "発言と開始にはチャンネル側の自律会話設定も必要です。指名された返信は参加頻度の制限を受けません。", TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 0) });
        var art = Section("絵描きスキル"); art.Children.Add(_artist); Field(art, "得意な画風・描くもの", _artStyle); art.Children.Add(_spontaneousArt);
        art.Children.Add(new TextBlock { Text = "画像生成エンジンとモデルはバックエンドとして使います。未設定時は設定から準備できます。", TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 0) });
        var generation = Section("生成設定（詳細）"); generation.Children.Add(_override);
        Field(generation, "温度（多様さ）", _temperature, "{0:F2}"); Field(generation, "Top P", _topP, "{0:F2}"); Field(generation, "出力上限（token）", _output, "{0:F0}");
        var suggest = new Button { Content = "性格から生成設定を提案", HorizontalAlignment = HorizontalAlignment.Left };
        suggest.Click += (_, _) => Apply(AccountDefaults.Suggest(personality())); generation.Children.Add(suggest);
    }
    private StackPanel Section(string title)
    {
        var content = new StackPanel { Margin = new(0, 8, 0, 0) };
        Children.Add(new Expander { Header = title, Content = content, Margin = new(0, 14, 0, 0) }); return content;
    }
    private static void Field(StackPanel parent, string title, FrameworkElement control, string? format = null)
    {
        parent.Children.Add(new TextBlock { Text = title, TextWrapping = TextWrapping.Wrap, Margin = new(0, 12, 0, 5) });
        System.Windows.Automation.AutomationProperties.SetName(control, title);
        if (format is not null)
        {
            var label = new TextBlock(); label.SetBinding(TextBlock.TextProperty, new Binding("Value") { Source = control, StringFormat = format }); parent.Children.Add(label);
        }
        parent.Children.Add(control);
    }
    internal void Load(AgentAccount account)
    {
        Apply(account.Generation ?? AccountDefaults.Suggest(account.Personality ?? new())); _override.IsChecked = account.Generation is not null;
        AgentSocial social = account.Social ?? new(); _interests.Text = social.Interests; _participation.Value = social.Participation; _initiates.IsChecked = social.Initiates; _cooldown.Value = social.CooldownSeconds;
        ArtistSkill art = account.Artist ?? new(); _artist.IsChecked = art.Enabled; _artStyle.Text = art.Style; _spontaneousArt.IsChecked = art.Spontaneous;
    }
    internal void Apply(AgentGeneration generation)
    { _override.IsChecked = true; _temperature.Value = generation.Temperature; _topP.Value = generation.TopP; _output.Value = generation.MaxOutputTokens; }
    internal AgentGeneration? Generation => _override.IsChecked == true ? new(_temperature.Value, _topP.Value, (int)_output.Value) : null;
    internal AgentSocial Social => new(_interests.Text.Trim(), _participation.Value, _initiates.IsChecked == true, (int)_cooldown.Value);
    internal ArtistSkill Artist => new(_artist.IsChecked == true, _artStyle.Text.Trim(), _spontaneousArt.IsChecked == true);
}
