using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Eluna.App.Models;
using Eluna.App.Services;

namespace Eluna.App;

public partial class MainWindow
{
    private readonly NavigationJournal _navigation = new();
    private bool _movingInHistory;
    private void InitializeNavigation()
    {
        _viewModel.PropertyChanged += NavigationChanged;
        _viewModel.Conversations.CollectionChanged += NavigationCollectionChanged;
        Closed += (_, _) => { _viewModel.PropertyChanged -= NavigationChanged; _viewModel.Conversations.CollectionChanged -= NavigationCollectionChanged; };
        PreviewMouseDown += (_, e) => { if (e.ChangedButton is MouseButton.XButton1 or MouseButton.XButton2 && !_composing) { MoveHistory(e.ChangedButton == MouseButton.XButton1 ? -1 : 1); e.Handled = true; } };
    }
    private void NavigationChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ViewModels.ChatViewModel.SelectedConversation) && _viewModel.SelectedConversation is { } room)
        { if (!_movingInHistory) { _navigation.Visit(room.Id); } RefreshNavigationButtons(); UiMotion.Reveal(MessagesList); }
        if (e.PropertyName == nameof(ViewModels.ChatViewModel.IsReady)) { RefreshNavigationButtons(); }
    }
    private void NavigationCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) => RefreshNavigationButtons();
    private void RefreshNavigationButtons()
    { bool Exists(Guid id) => _viewModel.Conversations.Any(c => c.Id == id && !c.IsArchived); BackButton.IsEnabled = _viewModel.IsReady && _navigation.CanMove(-1, Exists); ForwardButton.IsEnabled = _viewModel.IsReady && _navigation.CanMove(1, Exists); }
    internal void MoveHistory(int direction)
    {
        if (!_viewModel.CanNavigate || !_viewModel.IsReady) { return; }
        if (_navigation.Move(direction, id => _viewModel.Conversations.Any(c => c.Id == id && !c.IsArchived)) is Guid id)
        { _movingInHistory = true; try { _viewModel.HistorySearch = ""; _viewModel.SelectedConversation = _viewModel.Conversations.First(c => c.Id == id); } finally { _movingInHistory = false; } }
        RefreshNavigationButtons();
    }
    internal void OpenQuickSwitcher()
    {
        if (!_viewModel.CanNavigate || !_viewModel.IsReady) { return; }
        if (OwnedWindows.OfType<QuickSwitcherWindow>().FirstOrDefault() is { } existing) { existing.Activate(); existing.Query.Focus(); return; }
        var picker = new QuickSwitcherWindow(_viewModel) { Owner = this }; picker.Closed += (_, _) => InputBox.Focus(); picker.Show();
    }
    private void QuickSwitcher_Click(object sender, RoutedEventArgs e) => OpenQuickSwitcher();
    private void Back_Click(object sender, RoutedEventArgs e) => MoveHistory(-1);
    private void Forward_Click(object sender, RoutedEventArgs e) => MoveHistory(1);
    private void MoveChannel(int direction)
    {
        var rooms = _viewModel.HistoryView.OfType<Conversation>().ToArray(); int index = Array.IndexOf(rooms, _viewModel.SelectedConversation);
        if (rooms.Length > 0) { _viewModel.SelectedConversation = rooms[Math.Clamp(index + direction, 0, rooms.Length - 1)]; InputBox.Focus(); }
    }
    private void Shortcuts_Click(object sender, RoutedEventArgs e) => ShowUnique(() => new ShortcutsWindow { Owner = this });
    private bool NavigationKey(KeyEventArgs e)
    {
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (!InvokeNavigationShortcut(key, Keyboard.Modifiers)) { return false; }
        e.Handled = true; return true;
    }
    internal bool InvokeNavigationShortcut(Key key, ModifierKeys keys)
    {
        if (keys == ModifierKeys.Control && key == Key.K) { OpenQuickSwitcher(); }
        else if (keys == ModifierKeys.Control && key == Key.I) { OpenMessageCenter("unread"); }
        else if (keys == ModifierKeys.Control && key == Key.P) { OpenMessageCenter("pins"); }
        else if (keys == (ModifierKeys.Control | ModifierKeys.Shift) && key == Key.F) { OpenMessageCenter("search", all: true); }
        else if (keys == ModifierKeys.Control && key == Key.U) { Members_Click(this, new RoutedEventArgs()); }
        else if (keys == ModifierKeys.Control && key == Key.OemQuestion) { Shortcuts_Click(this, new RoutedEventArgs()); }
        else if (keys == ModifierKeys.Alt && key is Key.Left or Key.Right) { MoveHistory(key == Key.Left ? -1 : 1); }
        else if (keys == ModifierKeys.Alt && key is Key.Up or Key.Down) { MoveChannel(key == Key.Up ? -1 : 1); }
        else { return false; }
        return true;
    }
}
