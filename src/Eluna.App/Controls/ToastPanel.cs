using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Threading;
using Eluna.App.Services;

namespace Eluna.App.Controls;

internal sealed class ToastPanel : Border, IDisposable
{
    private readonly TextBlock _text = new() { TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis, MaxHeight = 96, VerticalAlignment = VerticalAlignment.Center };
    internal Button UndoButton { get; } = new() { Content = "元に戻す", Margin = new(10, 0, 0, 0), Padding = new(8, 4, 8, 4) };
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private Func<Task>? _undo;
    private TimeSpan _remaining;
    private DateTimeOffset _tick = DateTimeOffset.UtcNow;
    private bool _disposed, _busy, _closing;
    internal string Message => _text.Text;
    private void SetMessage(string message) { _text.Text = message; _text.ToolTip = message; System.Windows.Automation.AutomationProperties.SetName(this, message); }
    internal event Action? Dismissed;
    internal ToastPanel(string message, Func<Task>? undo = null)
    {
        _undo = undo; _text.Text = message; _remaining = TimeSpan.FromSeconds(undo is null ? 5 : 10); MaxWidth = 400; CornerRadius = new(7); Padding = new(12, 10, 8, 10); BorderThickness = new(1); SetResourceReference(BackgroundProperty, "SidebarBrush"); SetResourceReference(BorderBrushProperty, "StrokeBrush");
        SetResourceReference(TextElement.ForegroundProperty, "TextBrush");
        _text.ToolTip = message;
        var root = new Grid(); Child = root; root.ColumnDefinitions.Add(new()); root.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); root.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); root.Children.Add(_text); Grid.SetColumn(UndoButton, 1); root.Children.Add(UndoButton); UndoButton.Visibility = undo is null ? Visibility.Collapsed : Visibility.Visible;
        var close = new Button { Content = "×", Padding = new(8, 2, 8, 2), Margin = new(5, 0, 0, 0), ToolTip = "通知を閉じる" }; System.Windows.Automation.AutomationProperties.SetName(close, "通知を閉じる"); Grid.SetColumn(close, 2); root.Children.Add(close); close.Click += async (_, _) => await DismissAsync(); UndoButton.Click += async (_, _) => await UndoAsync();
        System.Windows.Automation.AutomationProperties.SetLiveSetting(this, System.Windows.Automation.AutomationLiveSetting.Polite); System.Windows.Automation.AutomationProperties.SetName(this, message);
        _timer.Tick += Tick; Loaded += (_, _) => { if (!_disposed) { _tick = DateTimeOffset.UtcNow; _timer.Start(); } }; Unloaded += (_, _) => _timer.Stop(); UiMotion.SetFeedback(this, "entrance");
    }
    private async void Tick(object? sender, EventArgs e)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow; TimeSpan elapsed = now - _tick; _tick = now;
        if (!_busy && !IsMouseOver && !IsKeyboardFocusWithin && Window.GetWindow(this)?.IsActive == true) { _remaining -= elapsed; }
        if (_remaining <= TimeSpan.Zero) { await DismissAsync(); }
    }
    internal async Task UndoAsync()
    {
        if (_undo is null || _busy || _disposed || _closing || UndoButton.Visibility != Visibility.Visible) { return; }
        _busy = true; UndoButton.IsEnabled = false;
        try { await _undo(); if (!_disposed) { SetMessage("操作を元に戻しました。"); UndoButton.Visibility = Visibility.Collapsed; _remaining = TimeSpan.FromSeconds(4); } }
        catch (Exception ex) { if (!_disposed) { SetMessage("元に戻せませんでした。" + ex.Message); UndoButton.Visibility = Visibility.Collapsed; _remaining = TimeSpan.FromSeconds(8); } }
        finally { _busy = false; }
    }
    internal async Task DismissAsync()
    {
        if (_disposed || _closing) { return; }
        _closing = true; _timer.Stop(); UiMotion.Fade(this, 0); if (UiMotion.Enabled) { await Task.Delay(130); }
        if (!_disposed) { Dismissed?.Invoke(); }
        Dispose();
    }
    public void Dispose() { if (_disposed) { return; } _disposed = true; _timer.Stop(); _timer.Tick -= Tick; _undo = null; Dismissed = null; }
}
