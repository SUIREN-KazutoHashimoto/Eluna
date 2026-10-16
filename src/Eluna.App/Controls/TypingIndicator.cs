using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Eluna.App.Controls;

public sealed class TypingIndicator : Grid
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(nameof(Text), typeof(string), typeof(TypingIndicator), new PropertyMetadata("", Changed));
    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }
    private readonly TextBlock _text = new() { FontSize = 11, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, Margin = new(8, 0, 0, 0) };
    private readonly List<System.Windows.Shapes.Ellipse> _dots = [];
    public TypingIndicator()
    {
        ColumnDefinitions.Add(new() { Width = new(26) }); ColumnDefinitions.Add(new());
        var dots = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center }; Children.Add(dots);
        for (int i = 0; i < 3; i++) { var dot = new System.Windows.Shapes.Ellipse { Width = 4, Height = 4, Margin = new(2, 0, 2, 0) }; dot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "MutedBrush"); dots.Children.Add(dot); _dots.Add(dot); }
        _text.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush"); Grid.SetColumn(_text, 1); Children.Add(_text);
        Loaded += (_, _) => Refresh(); Unloaded += (_, _) => Stop();
        System.Windows.Automation.AutomationProperties.SetLiveSetting(this, System.Windows.Automation.AutomationLiveSetting.Polite);
    }
    private static void Changed(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((TypingIndicator)d).Refresh();
    private void Stop() { foreach (var dot in _dots) { dot.BeginAnimation(OpacityProperty, null); } }
    private void Refresh()
    {
        _text.Text = Text; ToolTip = Text; Visibility = string.IsNullOrEmpty(Text) ? Visibility.Collapsed : Visibility.Visible; Stop();
        if (!IsLoaded || Visibility != Visibility.Visible || !SystemParameters.ClientAreaAnimation) { return; }
        for (int i = 0; i < _dots.Count; i++) { _dots[i].BeginAnimation(OpacityProperty, new DoubleAnimation(0.25, 1, TimeSpan.FromSeconds(0.45)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, BeginTime = TimeSpan.FromSeconds(i * .15) }); }
    }
}
