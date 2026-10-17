using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Eluna.App.Models;
using Eluna.App.Services;

namespace Eluna.App.Controls;

// Keep the native TextBox responsible for input/IME/undo. Only paint completed mentions.
internal sealed class MentionAdorner(TextBox input, Func<IEnumerable<AgentAccount>> accounts, Func<bool> composing) : Adorner(input)
{
    internal int HighlightCount { get; private set; }
    protected override void OnRender(DrawingContext drawing)
    {
        HighlightCount = 0;
        if (composing() || input.LineCount < 1 || input.Text.Length is 0 or > 65536 || input.FlowDirection != FlowDirection.LeftToRight) { return; }
        Brush foreground = (Brush)input.FindResource("MentionTextBrush"), background = (Brush)input.FindResource("MentionBackgroundBrush");
        var typeface = new Typeface(input.FontFamily, input.FontStyle, input.FontWeight, input.FontStretch);
        drawing.PushClip(new RectangleGeometry(new Rect(0, 0, input.ActualWidth, input.ActualHeight)));
        try
        {
            foreach (MentionToken token in MentionService.Scan(input.Text, accounts()).Take(100))
            {
                int end = token.Start + token.Length;
                for (int at = token.Start; at < end;)
                {
                    int line = input.GetLineIndexFromCharacterIndex(at); if (line < 0) { break; }
                    int next = Math.Min(end, input.GetCharacterIndexFromLineIndex(line) + input.GetLineLength(line)); if (next <= at) { break; }
                    // Let Windows paint selected text, including its native selection contrast.
                    if (input.SelectionLength > 0 && at < input.SelectionStart + input.SelectionLength && next > input.SelectionStart) { at = next; continue; }
                    Rect start = input.GetRectFromCharacterIndex(at), last = input.GetRectFromCharacterIndex(next - 1, true);
                    if (!start.IsEmpty && !last.IsEmpty && last.X > start.X && start.Bottom >= 0 && start.Top < input.ActualHeight)
                    {
                        var rect = new Rect(start.X, start.Y, last.X - start.X, start.Height);
                        drawing.DrawRoundedRectangle(background, null, rect, 2, 2);
                        drawing.PushClip(new RectangleGeometry(rect));
                        var formatted = new FormattedText(input.Text[at..next], CultureInfo.CurrentUICulture, input.FlowDirection, typeface, input.FontSize, foreground, VisualTreeHelper.GetDpi(input).PixelsPerDip);
                        drawing.DrawText(formatted, new(rect.X, rect.Y + Math.Max(0, (rect.Height - formatted.Height) / 2))); drawing.Pop(); HighlightCount++;
                        if (input.IsKeyboardFocused && input.SelectionLength == 0 && input.CaretIndex >= at && input.CaretIndex < next)
                        { Rect caret = input.GetRectFromCharacterIndex(input.CaretIndex); if (!caret.IsEmpty) { drawing.DrawLine(new Pen(foreground, 1), caret.TopLeft, caret.BottomLeft); } }
                    }
                    at = next;
                }
            }
        }
        catch (ArgumentOutOfRangeException) { /* Layout can advance between a text edit and a paint. */ }
        finally { drawing.Pop(); }
    }
}
