using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Eluna.App.Controls;
using Eluna.App.Models;
using Eluna.App.Services;
using Eluna.App.ViewModels;

namespace Eluna.App;

public sealed class AgentProfileWindow : Window
{
    private readonly ChatViewModel _chat;
    private readonly Guid _id;
    internal TextBox NameBox { get; } = new() { MaxLength = 80 };
    internal ComboBox ModelBox { get; } = new() { DisplayMemberPath = nameof(DebateModelChoice.Name) };
    internal ComboBox VoiceBox { get; } = new() { DisplayMemberPath = nameof(VoiceChoice.Name) };
    private readonly TextBox _icon = new() { MaxLength = 16 }, _specialty = new() { TextWrapping = TextWrapping.Wrap, MaxLength = 1000 },
        _instruction = new() { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 100, MaxHeight = 200, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxLength = 8000 };
    private readonly Slider _rate = new() { Minimum = -10, Maximum = 10, TickFrequency = 1, IsSnapToTickEnabled = true };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 8) };
    internal Button SaveButton { get; } = new() { Content = "変更を保存" };
    private string _baseline = "";
    private bool _saving;
    internal AgentBehaviorEditor Behavior { get; } = new();
    public AgentProfileWindow(ChatViewModel chat, Guid id)
    {
        _chat = chat; _id = id; AgentAccount account = chat.Agents.First(a => a.Id == id);
        Title = account.Name + " · プロフィール"; Width = 580; Height = 750; MinWidth = 420; MinHeight = 450; WindowStartupLocation = WindowStartupLocation.CenterOwner; Appearance.Attach(this);
        var root = new DockPanel { Margin = new(24) }; Content = root;
        var footer = new StackPanel(); DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer); footer.Children.Add(_status);
        var actions = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right }; footer.Children.Add(actions);
        var history = new Button { Content = "履歴・メモリ", Margin = new(0, 0, 8, 8) }; actions.Children.Add(history);
        history.Click += (_, _) => { var manager = new AgentsWindow(chat) { Owner = this }; manager.AgentList.SelectedItem = chat.Agents.First(a => a.Id == id); manager.ShowDialog(); };
        SaveButton.Margin = new(0, 0, 0, 8); SaveButton.SetResourceReference(StyleProperty, "PrimaryButton"); actions.Children.Add(SaveButton);
        var body = new StackPanel(); root.Children.Add(new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        body.Children.Add(new TextBlock { Text = account.Icon, FontSize = 44 });
        body.Children.Add(new TextBlock { Text = account.Name, FontSize = 24, TextWrapping = TextWrapping.Wrap, FontWeight = FontWeights.SemiBold });
        void Field(string label, FrameworkElement element) { body.Children.Add(new TextBlock { Text = label, Margin = new(0, 14, 0, 6), TextWrapping = TextWrapping.Wrap }); body.Children.Add(element); System.Windows.Automation.AutomationProperties.SetName(element, label); }
        Field("名前", NameBox); Field("アイコン", _icon); Field("専門分野", _specialty); Field("使用モデル", ModelBox); Field("基本指示・話し方", _instruction); Field("読み上げ音声", VoiceBox); Field("読み上げ速度", _rate);
        Behavior.Load(account, chat.Agents); body.Children.Add(Behavior);
        body.Children.Add(new TextBlock { Text = "変更は次の依頼から使います。過去の発言と記憶は引き継ぎます。", TextWrapping = TextWrapping.Wrap, Margin = new(0, 14, 0, 0) });
        NameBox.Text = account.Name; _icon.Text = account.Icon; _specialty.Text = account.Specialty; _instruction.Text = account.Instruction; _rate.Value = account.SpeechRate;
        var models = new List<DebateModelChoice> { new("", "既存のモデル設定を使用", "") }; models.AddRange(chat.Models.Select(m => new DebateModelChoice(m.Id, m.DisplayName, m.SizeLabel)));
        if (account.ModelId is string missing && !models.Any(m => m.Id == missing)) { models.Add(new(missing, "未取得 · " + missing, "")); }
        ModelBox.ItemsSource = models; ModelBox.SelectedItem = models.First(m => m.Id == (account.ModelId ?? ""));
        void Trim(ComboBox box, string property)
        {
            box.DisplayMemberPath = ""; var template = new DataTemplate(); var text = new FrameworkElementFactory(typeof(TextBlock)); text.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(property)); text.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis); template.VisualTree = text; box.ItemTemplate = template;
        }
        Trim(ModelBox, nameof(DebateModelChoice.Name)); Trim(VoiceBox, nameof(VoiceChoice.Name));
        VoiceBox.ItemsSource = new[] { new VoiceChoice(account.VoiceId ?? "", "音声を読み込み中…") }; VoiceBox.SelectedIndex = 0; _baseline = Snapshot();
        SaveButton.Click += async (_, _) => await SaveAsync(); UnsavedChanges.Protect(this, () => Snapshot() != _baseline, SaveAsync);
        Loaded += async (_, _) =>
        {
            try
            {
                var voices = new List<VoiceChoice> { new("", "Windows標準の音声") }; voices.AddRange(await WindowsVoiceAudio.VoicesAsync());
                if (!IsVisible) { return; }
                if (account.VoiceId is string selected && !voices.Any(v => v.Id == selected)) { voices.Add(new(selected, "未取得の音声（選び直してください）")); }
                VoiceBox.ItemsSource = voices; VoiceBox.SelectedItem = voices.First(v => v.Id == (account.VoiceId ?? ""));
            }
            catch (Exception ex) { _status.Text = "音声を取得できません: " + ex.Message; }
        };
    }
    private string Snapshot() => JsonSerializer.Serialize(new { NameBox.Text, Icon = _icon.Text, Specialty = _specialty.Text, Instruction = _instruction.Text, Model = (ModelBox.SelectedItem as DebateModelChoice)?.Id, Voice = (VoiceBox.SelectedItem as VoiceChoice)?.Id, Rate = (int)_rate.Value, Behavior.Personality, Behavior.Autonomy, Behavior.Connections });
    internal async Task<bool> SaveAsync()
    {
        if (_saving) { return false; }
        _saving = true; SaveButton.IsEnabled = false; string snapshot = Snapshot();
        try
        {
            AgentAccount current = _chat.Agents.First(a => a.Id == _id); string? model = (ModelBox.SelectedItem as DebateModelChoice)?.Id, voice = (VoiceBox.SelectedItem as VoiceChoice)?.Id;
            await _chat.SaveAgentAsync(current with { Name = NameBox.Text.Trim(), Icon = _icon.Text.Trim(), Specialty = _specialty.Text, Instruction = _instruction.Text, ModelId = string.IsNullOrEmpty(model) ? null : model, VoiceId = string.IsNullOrEmpty(voice) ? null : voice, SpeechRate = (int)_rate.Value, Personality = Behavior.Personality, Connections = Behavior.Connections, Autonomy = Behavior.Autonomy });
            _baseline = snapshot; _status.Text = Snapshot() == snapshot ? "保存しました" : "保存後の編集は未保存です"; return true;
        }
        catch (Exception ex) { _status.Text = ex.Message; return false; }
        finally { _saving = false; SaveButton.IsEnabled = true; }
    }
}
