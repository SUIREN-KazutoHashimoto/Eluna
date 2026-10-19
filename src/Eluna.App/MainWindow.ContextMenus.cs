using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Eluna.App.Models;
using Eluna.App.Services;

namespace Eluna.App;

public partial class MainWindow
{
    private static DependencyObject? ContextParent(DependencyObject child) => child is Visual ? VisualTreeHelper.GetParent(child) : child is FrameworkContentElement content ? content.Parent : LogicalTreeHelper.GetParent(child);
    private static FrameworkElement? ContextRow(DependencyObject? source, Type type)
    { while (source is not null) { if (source is FrameworkElement element && element.DataContext?.GetType() == type) { return element; } source = ContextParent(source); } return null; }
    private static bool ContextMenuKey(KeyEventArgs e) => (e.Key == Key.System ? e.SystemKey : e.Key) == Key.Apps && Keyboard.Modifiers == ModifierKeys.None || (e.Key == Key.System ? e.SystemKey : e.Key) == Key.F10 && Keyboard.Modifiers == ModifierKeys.Shift;
    private void InitializeContextMenus()
    {
        HistoryList.PreviewMouseDown += (_, e) => { if (e.ChangedButton == MouseButton.Right && ContextRow(e.OriginalSource as DependencyObject, typeof(Conversation)) is not null) { e.Handled = true; } };
        HistoryList.PreviewMouseUp += (_, e) => { if (e.ChangedButton == MouseButton.Right && ContextRow(e.OriginalSource as DependencyObject, typeof(Conversation)) is { } row) { e.Handled = true; HistoryMenu_Click(row, new RoutedEventArgs()); } };
        HistoryList.PreviewKeyDown += (_, e) => { if (ContextMenuKey(e)) { FrameworkElement? row = ContextRow(Keyboard.FocusedElement as DependencyObject, typeof(Conversation)); if (row is null && HistoryList.SelectedItem is not null) { row = HistoryList.ItemContainerGenerator.ContainerFromItem(HistoryList.SelectedItem) as FrameworkElement; } if (row is not null) { e.Handled = true; HistoryMenu_Click(row, new RoutedEventArgs()); } } };
        MessagesList.PreviewMouseUp += (_, e) => { if (e.ChangedButton == MouseButton.Right && ContextRow(e.OriginalSource as DependencyObject, typeof(ChatMessage)) is { } row) { Message_RightClick(row, e); } };
        MessagesList.PreviewKeyDown += (_, e) =>
        {
            if (!ContextMenuKey(e) || MessageHasTextSelection(e.OriginalSource as DependencyObject)) { return; }
            FrameworkElement? row = ContextRow(e.OriginalSource as DependencyObject, typeof(ChatMessage));
            if (row is null && MessagesList.SelectedItem is ChatMessage message) { row = MessagesList.ItemContainerGenerator.ContainerFromItem(message) as FrameworkElement; }
            if (row is not null) { e.Handled = true; MessageMenu_Click(row, new RoutedEventArgs()); }
        };
    }
    private bool MessageHasTextSelection(DependencyObject? source)
    { while (source is not null && !ReferenceEquals(source, MessagesList)) { if (source is RichTextBox rich && !rich.Selection.IsEmpty || source is TextBox { SelectionLength: > 0 }) { return true; } source = ContextParent(source); } return false; }
    private void Message_RightClick(object sender, MouseButtonEventArgs e)
    {
        if (MessageHasTextSelection(e.OriginalSource as DependencyObject)) { return; }
        if (sender is FrameworkElement row) { e.Handled = true; MessageMenu_Click(row, new RoutedEventArgs()); }
    }
    private void Member_RightClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: MemberCard card } element) { return; }
        e.Handled = true;
        OpenMemberMenu(element, card);
    }
    private void Member_ContextKey(object sender, KeyEventArgs e)
    { if (ContextMenuKey(e) && sender is FrameworkElement { DataContext: MemberCard card } element) { e.Handled = true; OpenMemberMenu(element, card); } }
    private void OpenMemberMenu(FrameworkElement element, MemberCard card)
    {
        AgentAccount account = card.Account; ComposerSnapshot? snapshot = CaptureComposer(); var menu = new ContextMenu { PlacementTarget = element, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom }; element.ContextMenu = menu;
        var profile = new MenuItem { Header = "プロフィール" }; profile.Click += (_, _) => OpenProfile(account.Id); menu.Items.Add(profile);
        var mention = new MenuItem { Header = "メンション", IsEnabled = snapshot is not null && account.Enabled }; mention.Click += (_, _) => { if (snapshot is not null) { ApplyComposerEdit(snapshot, ComposerEdits.Insert(snapshot, MentionService.Format(account))); } }; menu.Items.Add(mention);
        if (!account.IsLocal) { var dm = new MenuItem { Header = "メッセージ", IsEnabled = account.Enabled }; dm.Click += async (_, _) => { try { await _viewModel.OpenDirectMessageAsync(account.Id); } catch (Exception ex) { _viewModel.Error = ex.Message; } }; menu.Items.Add(dm); }
        menu.IsOpen = true;
    }
}
