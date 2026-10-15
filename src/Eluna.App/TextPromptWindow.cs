using System.Windows;
using System.Windows.Controls;
using Eluna.App.Services;

namespace Eluna.App;

public sealed class TextPromptWindow : Window
{
    private readonly TextBox _text;
    public string Result => _text.Text;
    public TextPromptWindow(string title, string initial, bool multiline = false, int maxLength = 0)
    {
        Title = title; Width = 620; Height = multiline ? 420 : 220; MinWidth = 420; MinHeight = 200;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Style = (Style)Application.Current.FindResource(typeof(Window));
        Appearance.Attach(this);
        var grid = new Grid { Margin = new(24) };
        grid.RowDefinitions.Add(new() { Height = GridLength.Auto }); grid.RowDefinitions.Add(new()); grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
        grid.Children.Add(new TextBlock { Text = title, FontSize = 20, TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 18) });
        _text = new TextBox { Text = initial, MaxLength = maxLength, AcceptsReturn = multiline, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalContentAlignment = VerticalAlignment.Top };
        System.Windows.Automation.AutomationProperties.SetName(_text, title); Grid.SetRow(_text, 1); grid.Children.Add(_text);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new(0, 18, 0, 0) };
        var cancel = new Button { Content = "キャンセル", IsCancel = true, Margin = new(0, 0, 10, 0) };
        var ok = new Button { Content = "保存", IsDefault = !multiline, Style = (Style)Application.Current.FindResource("PrimaryButton"), Padding = new(24, 9, 24, 9) };
        ok.Click += (_, _) => DialogResult = true;
        buttons.Children.Add(cancel); buttons.Children.Add(ok); Grid.SetRow(buttons, 2); grid.Children.Add(buttons);
        Content = grid;
        Loaded += (_, _) => { _text.Focus(); _text.SelectAll(); };
    }
}
