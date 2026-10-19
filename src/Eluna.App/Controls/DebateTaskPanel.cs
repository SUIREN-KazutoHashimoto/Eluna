using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Eluna.App.Models;
using Eluna.App.ViewModels;

namespace Eluna.App.Controls;

public sealed class DebateTaskPanel : UserControl
{
    public static readonly DependencyProperty WorkProperty = DependencyProperty.Register(nameof(Work), typeof(WorkSession), typeof(DebateTaskPanel), new PropertyMetadata(null, Changed));
    public static readonly DependencyProperty ChatProperty = DependencyProperty.Register(nameof(Chat), typeof(ChatViewModel), typeof(DebateTaskPanel));
    public WorkSession? Work { get => (WorkSession?)GetValue(WorkProperty); set => SetValue(WorkProperty, value); }
    public ChatViewModel? Chat { get => (ChatViewModel?)GetValue(ChatProperty); set => SetValue(ChatProperty, value); }
    private readonly TextBlock _label = new() { FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap }, _budget = new() { FontSize = 11, Margin = new(0, 6, 0, 8), TextWrapping = TextWrapping.Wrap };
    private readonly WrapPanel _actions = new(); private readonly ComboBox _speaker = new() { MinWidth = 130, MaxWidth = 210, DisplayMemberPath = nameof(DebateRole.Name), Margin = new(0, 0, 6, 6) };
    private readonly Dictionary<string, Button> _buttons = [];
    public DebateTaskPanel()
    {
        Visibility = Visibility.Collapsed;
        var body = new StackPanel(); body.Children.Add(_label); body.Children.Add(_budget); body.Children.Add(_actions);
        var card = new Border { Child = body, Padding = new(12), CornerRadius = new(8), BorderThickness = new(1), Margin = new(0, 6, 0, 10) }; card.SetResourceReference(Border.BorderBrushProperty, "StrokeBrush"); Content = card;
        _actions.Children.Add(_speaker);
        void Button(string command, string text)
        {
            var button = new Button { Content = text, Padding = new(8, 5, 8, 5), Margin = new(0, 0, 6, 6) }; _buttons[command] = button; _actions.Children.Add(button);
            button.Click += async (_, _) =>
            {
                if (Work is null || Chat is null) { return; }
                try
                {
                    if (command == "participants") { new RoomWindow(Chat, discussion: Work) { Owner = Window.GetWindow(this) }.Show(); }
                    else if (command == "interrupt") { await Chat.InterruptDebateAsync(Work); }
                    else { await Chat.ControlDebateAsync(Work, command == "automatic" ? Work.Discussion.Automatic ? "manual" : "auto" : command, (_speaker.SelectedItem as DebateRole)?.Id); }
                }
                catch (Exception ex) { Chat.Error = ex.Message; }
            };
        }
        Button("next", "次の発言"); Button("automatic", "自動進行"); Button("pause", "一時停止"); Button("resume", "再開");
        Button("organize", "ここまで整理"); Button("finish", "まとめて終了"); Button("interrupt", "入力内容で割り込む"); Button("participants", "参加者");
    }
    private static void Changed(DependencyObject owner, DependencyPropertyChangedEventArgs e)
    {
        var panel = (DebateTaskPanel)owner;
        if (e.OldValue is WorkSession old) { PropertyChangedEventManager.RemoveHandler(old, panel.Refresh, ""); }
        if (e.NewValue is WorkSession work) { PropertyChangedEventManager.AddHandler(work, panel.Refresh, ""); }
        panel.Refresh(null, new(""));
    }
    private void Refresh(object? sender, PropertyChangedEventArgs e)
    {
        Visibility = Work?.Mode == "debate" ? Visibility.Visible : Visibility.Collapsed; if (Work is not { } work) { return; }
        bool free = work.Options.DebateStyle == "free", done = work.Stage is "complete" or "limited" or "moved";
        _label.Text = (free ? "自由形ディベート" : "ラウンド制ディベート") + " · " + (work.Stage == "debate-wait" ? "次の発言待ち" : work.Label);
        _budget.Text = work.Budget;
        _speaker.Visibility = free ? Visibility.Visible : Visibility.Collapsed;
        if (e.PropertyName is "" or nameof(WorkSession.Discussion))
        {
            string? selected = (_speaker.SelectedItem as DebateRole)?.Id;
            _speaker.ItemsSource = work.Discussion.PendingParticipants ?? work.Options.Participants;
            _speaker.SelectedItem = _speaker.Items.Cast<DebateRole>().FirstOrDefault(r => r.Id == selected);
        }
        foreach ((string command, Button button) in _buttons)
        {
            button.Visibility = free || command is "pause" or "resume" ? Visibility.Visible : Visibility.Collapsed;
            button.IsEnabled = !done && (command == "resume" ? !work.IsRunning : command == "pause" ? work.IsRunning : true);
        }
        _buttons["automatic"].Content = work.Discussion.Automatic ? "自動進行 · オン" : "自動進行 · オフ";
    }
}
