using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Eluna.App.Controls;
using Eluna.App.Services;

namespace Eluna.App;

public partial class MainWindow
{
    private Popup? _emojiPopup;
    private IInputElement? _emojiFocus;
    internal EmojiPicker? OpenEmojiPanel { get; private set; }
    private void InitializeComposer()
    {
        _viewModel.NavigationStarting += CloseEmojiPicker; Deactivated += (_, _) => { if (OwnedWindows.Cast<Window>().Any(w => w.IsActive)) { CloseEmojiPicker(); } };
        Closed += (_, _) => { CloseEmojiPicker(); _toast?.Dispose(); };
        ComposerArea.SizeChanged += (_, _) => PositionToast();
        var menu = new ContextMenu(); InputBox.ContextMenu = menu;
        foreach ((string label, RoutedUICommand command) in new[] { ("切り取り", ApplicationCommands.Cut), ("コピー", ApplicationCommands.Copy), ("貼り付け", ApplicationCommands.Paste), ("すべて選択", ApplicationCommands.SelectAll) }) { menu.Items.Add(new MenuItem { Header = label, Command = command, CommandTarget = InputBox }); }
        menu.Items.Add(new Separator()); AddFormattingItems(menu.Items);
    }
    private ComposerSnapshot? CaptureComposer()
    { if (_viewModel.SelectedConversation is not { } room || !_viewModel.CanCompose) { return null; } return new(room.Id, room.ActiveBranch.Id, InputBox.Text, InputBox.SelectionStart, InputBox.SelectionLength); }
    private bool ComposerUnchanged(ComposerSnapshot s) => _viewModel.CanCompose && _viewModel.SelectedConversation?.Id == s.Room && _viewModel.SelectedBranch?.Id == s.Branch && InputBox.Text == s.Text;
    internal bool ApplyComposerEdit(ComposerSnapshot snapshot, ComposerEdit edit)
    {
        if (_composing || !ComposerUnchanged(snapshot)) { ShowToast("入力内容や会話が変わったため挿入を中止しました。もう一度選んでください。"); return false; }
        InputBox.Focus(); InputBox.BeginChange(); try { InputBox.Select(edit.Start, edit.Length); InputBox.SelectedText = edit.Value; InputBox.Select(edit.SelectionStart, edit.SelectionLength); } finally { InputBox.EndChange(); }
        return true;
    }
    internal void OpenEmojiPicker(FrameworkElement target, Guid? message = null)
    {
        if (_composing || !_viewModel.CanCompose || _viewModel.SelectedConversation is not { } room) { return; }
        ComposerSnapshot? snapshot = CaptureComposer(); Guid branch = room.ActiveBranch.Id;
        CloseEmojiPicker(); var selected = message is Guid id ? (room.Experience.Reactions ?? []).Where(r => r.Branch == branch && r.Message == id).Select(r => r.Emoji).ToHashSet() : null;
        var panel = new EmojiPicker(_viewModel.Settings.RecentEmoji, selected); var popup = new Popup { PlacementTarget = target, Placement = PlacementMode.Top, StaysOpen = false, AllowsTransparency = true, Child = panel }; OpenEmojiPanel = panel; _emojiPopup = popup;
        _emojiFocus = Keyboard.FocusedElement; panel.CloseRequested += () => CloseEmojiPicker(true);
        panel.Picked += async glyph =>
        {
            if (!ReferenceEquals(_emojiPopup, popup) || !popup.IsOpen) { return; }
            CloseEmojiPicker(true); bool used = message is Guid messageId ? await ToggleReactionAsync(room.Id, branch, messageId, glyph) : snapshot is not null && ApplyComposerEdit(snapshot, ComposerEdits.Insert(snapshot, glyph));
            if (used) { await _viewModel.RememberEmojiAsync(glyph); }
        };
        popup.Closed += (_, _) => { if (ReferenceEquals(_emojiPopup, popup)) { _emojiPopup = null; OpenEmojiPanel = null; } };
        popup.IsOpen = true;
    }
    private void CloseEmojiPicker() => CloseEmojiPicker(false);
    private void CloseEmojiPicker(bool restoreFocus) { Popup? popup = _emojiPopup; IInputElement? focus = _emojiFocus; _emojiFocus = null; _emojiPopup = null; OpenEmojiPanel = null; if (popup is not null) { popup.IsOpen = false; } if (restoreFocus && focus is UIElement { IsVisible: true, IsEnabled: true }) { Keyboard.Focus(focus); } }
    private void Format_Click(object sender, RoutedEventArgs e)
    { if (sender is FrameworkElement anchor) { var menu = new ContextMenu { PlacementTarget = anchor }; AddFormattingItems(menu.Items); menu.IsOpen = true; } }
    private void AddFormattingItems(ItemCollection items)
    {
        foreach ((string kind, string label) in new[] { ("bold", "太字 · Ctrl+Shift+B"), ("italic", "斜体 · Ctrl+Shift+I"), ("strike", "取り消し線 · Ctrl+Shift+X"), ("code", "インラインコード · Ctrl+Shift+O"), ("block", "コードブロック"), ("quote", "引用"), ("list", "箇条書き") })
        { var item = new MenuItem { Header = label }; item.Click += (_, _) => FormatSelection(kind); items.Add(item); }
    }
    internal bool FormatSelection(string kind) { ComposerSnapshot? snapshot = CaptureComposer(); return snapshot is not null && ApplyComposerEdit(snapshot, ComposerEdits.Format(snapshot, kind)); }
    private bool ComposerShortcut(KeyEventArgs e)
    {
        if (_emojiPopup?.IsOpen == true) { if (e.Key == Key.Escape && OpenEmojiPanel?.CategoryOpen != true) { CloseEmojiPicker(true); e.Handled = true; } return true; }
        if (InputBox.IsKeyboardFocusWithin && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        { string? kind = e.Key switch { Key.B => "bold", Key.I => "italic", Key.X => "strike", Key.O => "code", _ => null }; if (kind is not null) { FormatSelection(kind); e.Handled = true; return true; } }
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.E) { OpenEmojiPicker(EmojiButton); e.Handled = true; return true; }
        if (e.Key == Key.Escape && FeedbackHost.IsKeyboardFocusWithin && _toast is not null) { _ = _toast.DismissAsync(); InputBox.Focus(); e.Handled = true; return true; }
        return false;
    }
}
