using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Eluna.App.Services;

namespace Eluna.App.Controls;

// Text remains the automation label; each image represents a whole Unicode grapheme.
public sealed class EmojiText : ContentControl
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(nameof(Text), typeof(string), typeof(EmojiText), new FrameworkPropertyMetadata("", Rebuild));
    public static readonly DependencyProperty TextWrappingProperty = DependencyProperty.Register(nameof(TextWrapping), typeof(TextWrapping), typeof(EmojiText), new FrameworkPropertyMetadata(TextWrapping.NoWrap, Rebuild));
    public static readonly DependencyProperty TextTrimmingProperty = DependencyProperty.Register(nameof(TextTrimming), typeof(TextTrimming), typeof(EmojiText), new FrameworkPropertyMetadata(TextTrimming.None, Rebuild));
    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public TextWrapping TextWrapping { get => (TextWrapping)GetValue(TextWrappingProperty); set => SetValue(TextWrappingProperty, value); }
    public TextTrimming TextTrimming { get => (TextTrimming)GetValue(TextTrimmingProperty); set => SetValue(TextTrimmingProperty, value); }
    private readonly TextBlock _text = new();
    private bool _scheduled;
    public EmojiText()
    {
        Focusable = false; IsTabStop = false; Padding = new(0); Content = _text;
        Loaded += (_, _) => { EmojiRasterizer.Changed += QueueRefresh; Refresh(); };
        Unloaded += (_, _) => EmojiRasterizer.Changed -= QueueRefresh;
    }
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    { base.OnPropertyChanged(e); if (e.Property == FontSizeProperty || e.Property == FontFamilyProperty) { QueueRefresh(); } }
    private static void Rebuild(DependencyObject d, DependencyPropertyChangedEventArgs _) => ((EmojiText)d).QueueRefresh();
    private void QueueRefresh()
    {
        if (Dispatcher.HasShutdownStarted) { return; }
        _ = Dispatcher.InvokeAsync(() => { if (_scheduled) { return; } _scheduled = true; _ = Dispatcher.InvokeAsync(() => { _scheduled = false; if (IsLoaded) { Refresh(); } }); });
    }
    private void Refresh()
    {
        _text.Inlines.Clear(); _text.TextWrapping = TextWrapping; _text.TextTrimming = TextTrimming;
        foreach (var element in EmojiRasterizer.Elements(Text ?? ""))
        {
            var bitmap = element.Emoji ? EmojiRasterizer.Get(element.Text, FontSize * 1.35, VisualTreeHelper.GetDpi(this).DpiScaleX) : null;
            if (bitmap is null) { _text.Inlines.Add(new Run(element.Text)); }
            else { _text.Inlines.Add(new InlineUIContainer(new Image { Source = bitmap, Width = FontSize * 1.2, Height = FontSize * 1.2, Stretch = Stretch.Uniform }) { BaselineAlignment = BaselineAlignment.Center }); }
        }
    }
    protected override AutomationPeer OnCreateAutomationPeer() => new EmojiPeer(this);
    private sealed class EmojiPeer(EmojiText owner) : FrameworkElementAutomationPeer(owner)
    { protected override string GetClassNameCore() => nameof(EmojiText); protected override string GetNameCore() => string.IsNullOrEmpty(base.GetNameCore()) ? owner.Text : base.GetNameCore(); protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Text; }
}

internal sealed class EmojiRun(string glyph) : Run(glyph) { internal string Glyph => Text; }

// The document keeps real Runs, so selection and clipboard retain the original Unicode.
internal sealed class RichEmojiLayer : FrameworkElement
{
    private readonly RichTextBox _reader;
    internal int ColorCount { get; private set; }
    internal RichEmojiLayer(RichTextBox reader)
    {
        _reader = reader; IsHitTestVisible = false;
        reader.Loaded += (_, _) => EmojiRasterizer.Changed += Changed;
        reader.Unloaded += (_, _) => EmojiRasterizer.Changed -= Changed;
        reader.TextChanged += (_, _) => Changed(); reader.SizeChanged += (_, _) => InvalidateVisual();
        if (reader.IsLoaded) { EmojiRasterizer.Changed += Changed; }
    }
    private void Changed() { if (!Dispatcher.HasShutdownStarted) { _ = Dispatcher.InvokeAsync(InvalidateVisual); } }
    protected override void OnRender(DrawingContext drawing)
    {
        ColorCount = 0; if (!EmojiRasterizer.Ready) { return; }
        drawing.PushClip(new RectangleGeometry(new Rect(_reader.RenderSize)));
        try
        {
            for (TextPointer? at = _reader.Document.ContentStart; at is not null && at.CompareTo(_reader.Document.ContentEnd) < 0; at = at.GetNextContextPosition(LogicalDirection.Forward))
            {
                if (at.GetPointerContext(LogicalDirection.Forward) != TextPointerContext.ElementStart || at.GetAdjacentElement(LogicalDirection.Forward) is not EmojiRun run) { continue; }
                Rect rect = run.ContentStart.GetCharacterRect(LogicalDirection.Forward), end = run.ContentEnd.GetCharacterRect(LogicalDirection.Backward);
                if (rect.IsEmpty || end.IsEmpty || rect.Bottom < 0 || rect.Top > _reader.ActualHeight) { continue; }
                double size = run.FontSize * 1.35;
                var bitmap = EmojiRasterizer.Get(run.Glyph, size, VisualTreeHelper.GetDpi(this).DpiScaleX);
                if (bitmap is null) { continue; }

                double width = Math.Max(rect.Width, end.Right - rect.Left);
                drawing.DrawImage(bitmap, new Rect(rect.Left + (width - size) / 2, rect.Top + (rect.Height - size) / 2, size, size)); ColorCount++;
            }
        }
        finally { drawing.Pop(); }
    }
}

internal sealed class InputEmojiAdorner : Adorner
{
    private readonly TextBox _input;
    private readonly Func<bool> _composing;
    internal int ColorCount { get; private set; }
    internal InputEmojiAdorner(TextBox input, Func<bool> composing) : base(input)
    {
        _input = input; _composing = composing; IsHitTestVisible = false;
        input.Loaded += (_, _) => EmojiRasterizer.Changed += Changed;
        input.Unloaded += (_, _) => EmojiRasterizer.Changed -= Changed;
        if (input.IsLoaded) { EmojiRasterizer.Changed += Changed; }
    }
    private void Changed() { if (!Dispatcher.HasShutdownStarted) { _ = Dispatcher.InvokeAsync(InvalidateVisual); } }
    protected override void OnRender(DrawingContext drawing)
    {
        ColorCount = 0; if (_composing() || !EmojiRasterizer.Ready || _input.Text.Length > 65536 || _input.FlowDirection != FlowDirection.LeftToRight) { return; }
        drawing.PushClip(new RectangleGeometry(new Rect(_input.RenderSize)));
        try
        {
            foreach (var element in EmojiRasterizer.Elements(_input.Text).Where(e => e.Emoji).Take(500))
            {
                Rect start = _input.GetRectFromCharacterIndex(element.Index), end = _input.GetRectFromCharacterIndex(element.Index + element.Text.Length - 1, true);
                if (start.IsEmpty || end.IsEmpty || start.Bottom < 0 || start.Top > _input.ActualHeight || Math.Abs(start.Top - end.Top) > 1) { continue; }
                double size = _input.FontSize * 1.35;
                var bitmap = EmojiRasterizer.Get(element.Text, size, VisualTreeHelper.GetDpi(this).DpiScaleX);
                if (bitmap is null) { continue; }
                bool selected = _input.SelectionLength > 0 && element.Index < _input.SelectionStart + _input.SelectionLength && element.Index + element.Text.Length > _input.SelectionStart;
                Brush background = selected ? _input.SelectionBrush : (Brush)_input.FindResource("ChatInputBrush");
                var cell = new Rect(start.Left, start.Top, Math.Max(1, end.Right - start.Left), start.Height);
                drawing.DrawRectangle(background, null, cell);
                drawing.DrawImage(bitmap, new Rect(cell.Left + (cell.Width - size) / 2, cell.Top + (cell.Height - size) / 2, size, size)); ColorCount++;
                if (_input.IsKeyboardFocused && _input.SelectionLength == 0 && _input.CaretIndex >= element.Index && _input.CaretIndex <= element.Index + element.Text.Length)
                { Rect caret = _input.GetRectFromCharacterIndex(_input.CaretIndex); if (!caret.IsEmpty) { drawing.DrawLine(new Pen(_input.CaretBrush ?? _input.Foreground, 1), caret.TopLeft, caret.BottomLeft); } }
            }
        }
        catch (ArgumentOutOfRangeException) { }
        finally { drawing.Pop(); }
    }
}
