using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Eluna.App.Controls;

namespace Eluna.App.Services;

internal static class EmojiVerification
{
    private static void Check(bool ok, string message) { if (!ok) { throw new InvalidOperationException(message); } }
    private static IEnumerable<T> Find<T>(DependencyObject root) where T : DependencyObject
    { for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) { DependencyObject child = VisualTreeHelper.GetChild(root, i); if (child is T item) { yield return item; } foreach (T nested in Find<T>(child)) { yield return nested; } } }
    private static IEnumerable<EmojiRun> Runs(FlowDocument doc) { for (TextPointer? p = doc.ContentStart; p is not null; p = p.GetNextContextPosition(LogicalDirection.Forward)) { if (p.GetPointerContext(LogicalDirection.Forward) == TextPointerContext.ElementStart && p.GetAdjacentElement(LogicalDirection.Forward) is EmojiRun r) { yield return r; } } }
    internal static async Task RunAsync(AppStore store, string output)
    {
        Directory.CreateDirectory(output); MainWindow? main = null; Window? picker = null;
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(3)); CancellationToken token = deadline.Token;
        async Task Until(Func<bool> ready) { while (!ready()) { await Task.Delay(30, token); } }
        try
        {
            UnsavedChanges.Review = _ => false;
            main = new(store) { WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = -20000 }; main.Show(); var chat = main.ViewModel;
            await Until(() => chat.IsReady || chat.HasError); Check(chat.IsReady, chat.Error); Check(EmojiRasterizer.Ready, "Native emoji renderer was not loaded.");
            foreach (string glyph in new[] { "😀", "👍🏽", "👩‍💻" })
            {
                BitmapSource? image = await EmojiRasterizer.RenderAsync(glyph, 96); Check(image is not null, "No color pixels for " + glyph);
                byte[] pixels = new byte[96 * 96 * 4]; image!.CopyPixels(pixels, 96 * 4, 0);
                Check(Enumerable.Range(0, 96 * 96).Any(i => pixels[i * 4 + 3] > 100 && Math.Abs(pixels[i * 4] - pixels[i * 4 + 2]) > 20), "Monochrome emoji: " + glyph);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image)); using var file = File.Create(Path.Combine(output, "glyph-" + char.ConvertToUtf32(glyph, 0) + "-" + glyph.Length + ".png")); encoder.Save(file);
            }
            const string text = "今日はいい感じ 😀 👍🏽 👩‍💻 🇯🇵\nまた話そう！ 🎨";
            var author = chat.Agents.First(); var message = new Eluna.App.Models.ChatMessage { Role = "assistant", Content = text, Identity = new(author.Id, author.Name, "🎨") }; chat.SelectedConversation!.Messages.Add(message); await chat.ToggleReactionAsync(chat.SelectedConversation.Id, chat.SelectedConversation.ActiveBranch.Id, message.Id, "😀");
            var input = (TextBox)main.FindName("InputBox"); input.Text = "日本語 😀 👍🏽 👩‍💻";
            await Task.Delay(700, token); main.UpdateLayout();
            foreach (string theme in new[] { "dark", "light" })
            {
                Appearance.Apply(theme, 1); main.Width = 1280; main.Height = 900;
                await PrototypePreview.RenderAsync(main, Path.Combine(output, "chat-" + theme + ".png"));
                InputEmojiAdorner adornment = AdornerLayer.GetAdornerLayer(input).GetAdorners(input).OfType<InputEmojiAdorner>().Single(); Check(Find<EmojiText>(main).Where(e => e.Text.StartsWith("😀", StringComparison.Ordinal)).Any(e => Find<Image>(e).Any()), "Reaction badge did not show a colored glyph.");
                Check(adornment.ColorCount >= 3, "Composer did not paint completed colored graphemes.");
                RichTextBox reader = Find<RichTextBox>(main).First(r => new TextRange(r.Document.ContentStart, r.Document.ContentEnd).Text.Contains("今日はいい感じ", StringComparison.Ordinal));
                var richAdornment = Find<RichEmojiLayer>(main).Single(l => l.RenderSize == reader.RenderSize); await File.WriteAllTextAsync(Path.Combine(output, "rich-debug.json"), JsonSerializer.Serialize(new { richAdornment.ColorCount, adornerSize = richAdornment.RenderSize.ToString(), readerSize = reader.RenderSize.ToString(), runs = Runs(reader.Document).Select(r => new { r.Text, r.FontSize, cached = EmojiRasterizer.Get(r.Text, r.FontSize * 1.35) is not null, start = r.ContentStart.GetCharacterRect(LogicalDirection.Forward).ToString(), end = r.ContentEnd.GetCharacterRect(LogicalDirection.Backward).ToString() }) }, AppStore.JsonOptions)); Check(richAdornment.ColorCount >= 4, "Message did not paint colored graphemes: " + richAdornment.ColorCount);
                Check(new TextRange(reader.Document.ContentStart, reader.Document.ContentEnd).Text.Contains(text.Split('\n')[0], StringComparison.Ordinal), "Selectable text lost original Unicode.");
            }
            input.Focus(); input.Select(0, 3); input.SelectedText = "入力"; Check(input.CanUndo, "Emoji overlay replaced the native undo stack."); input.Undo(); Check(input.Text.StartsWith("日本語", StringComparison.Ordinal), "Undo did not restore input text.");
            var composition = new TextComposition(InputManager.Current, input, "あ"); input.RaiseEvent(new TextCompositionEventArgs(Keyboard.PrimaryDevice, composition) { RoutedEvent = TextCompositionManager.PreviewTextInputStartEvent });
            await PrototypePreview.RenderAsync(main, Path.Combine(output, "composition.png")); Check(AdornerLayer.GetAdornerLayer(input).GetAdorners(input).OfType<InputEmojiAdorner>().Single().ColorCount == 0, "The emoji overlay obscured IME composition.");
            input.RaiseEvent(new TextCompositionEventArgs(Keyboard.PrimaryDevice, composition) { RoutedEvent = TextCompositionManager.PreviewTextInputEvent });
            var emoji = new EmojiPicker(["😀", "👍🏽"]); picker = new Window { Width = 420, Height = 520, Content = emoji, Owner = main, WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = -20000 }; Appearance.Attach(picker); picker.Show(); await Task.Delay(500, token);
            await PrototypePreview.RenderAsync(picker, Path.Combine(output, "picker.png")); Check(Find<Image>(emoji).Any(i => i.Source is BitmapSource), "Picker did not use colored glyphs.");
            await File.WriteAllTextAsync(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new { success = true, checks = "native color pixels, skin tone and ZWJ graphemes, selectable Unicode including flag fallback, composer undo, simulated IME overlay suppression, picker, dark/light", physicalImeAndDpi = false }, AppStore.JsonOptions), token);
        }
        catch (Exception e) { Environment.ExitCode = 1; await File.WriteAllTextAsync(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new { success = false, error = e.ToString() }, AppStore.JsonOptions)); }
        finally { picker?.Close(); if (main is not null) { await main.ViewModel.ShutdownAsync(); main.Close(); } Application.Current.Shutdown(); }
    }
}
