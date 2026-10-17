using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Eluna.App.Models;
using Eluna.App.Services;

namespace Eluna.App;

public partial class MainWindow
{
    private CancellationTokenSource? _jumpFeedback;
    private ChatMessage? _highlightedRow;
    private void InitializeMessageExperience()
    {
        _viewModel.NavigationStarting += () => _viewModel.UpdateReadingState(false);
        Activated += (_, _) => Dispatcher.BeginInvoke(RefreshReadingState); Deactivated += (_, _) => _viewModel.UpdateReadingState(false); StateChanged += (_, _) => RefreshReadingState();
        Closed += (_, _) => { _jumpFeedback?.Cancel(); _jumpFeedback?.Dispose(); };
    }
    private void RefreshReadingState() => _viewModel.UpdateReadingState(IsActive && WindowState != WindowState.Minimized && _followOutput && _restoringReading is null && _scrollKey == $"{_viewModel.SelectedConversation?.Id}/{_viewModel.SelectedBranch?.Id}");
    private void Inbox_Click(object sender, RoutedEventArgs e) => OpenMessageCenter("unread");
    private void OpenMessageCenter(string mode, string query = "", bool all = false) => new MessageCenterWindow(_viewModel, mode, query, all) { Owner = this }.Show();
    private void SearchMessages_KeyDown(object sender, KeyEventArgs e) { if (!_composing && e.Key == Key.Enter) { e.Handled = true; OpenMessageCenter("search", HistorySearchBox.Text); } }
    private void ReadSelected_Click(object sender, RoutedEventArgs e) { if (_viewModel.SelectedConversation is { } room) { _viewModel.MarkRead(room.Id); } }
    private void PinMessage_Click(object sender, RoutedEventArgs e)
    { if (sender is FrameworkElement { DataContext: ChatMessage message } && _viewModel.SelectedConversation is { } room) { _ = ToggleMessagePinAsync(room.Id, room.ActiveBranch.Id, message.Id); } }
    private async Task ToggleMessagePinAsync(Guid room, Guid branch, Guid message)
    { try { await _viewModel.ToggleMessagePinAsync(room, branch, message); Conversation target = RequireFeedbackRoom(room); bool pinned = MessageLocations.In(target).Any(m => m.Branch == branch && m.Message == message && m.Pinned); ShowToast(pinned ? "「" + target.Title + "」の発言をピン留めしました。" : "発言のピン留めを解除しました。", UndoMessagePin(room, branch, message, pinned)); } catch (Exception ex) { _viewModel.Error = ex.Message; } }
    private void ReactMessage_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: ChatMessage message } button || _viewModel.SelectedConversation is not { } room) { return; }
        OpenEmojiPicker(button, message.Id);
    }
    private async Task<bool> ToggleReactionAsync(Guid room, Guid branch, Guid message, string emoji)
    { try { await _viewModel.ToggleReactionAsync(room, branch, message, emoji); bool added = RequireFeedbackRoom(room).Experience.Reactions?.Any(r => r.Branch == branch && r.Message == message && r.Emoji == emoji) == true; ShowToast(emoji + (added ? " リアクションを追加しました。" : " リアクションを取り消しました。")); return true; } catch (Exception ex) { _viewModel.Error = ex.Message; return false; } }
    private async void Reaction_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: ReactionBadge badge } element || _viewModel.SelectedConversation is not { } room) { return; }
        DependencyObject? node = element; while (node is not null) { if (node is FrameworkElement { DataContext: ChatMessage message }) { await ToggleReactionAsync(room.Id, room.ActiveBranch.Id, message.Id, badge.Emoji); return; } node = VisualTreeHelper.GetParent(node); }
    }
    private void MarkMessageUnread(Conversation room, Guid branch, ChatMessage message)
    { _viewModel.MarkUnread(room.Id, branch, message.Id); _followOutput = false; _viewModel.UpdateReadingState(false); LatestButton.Visibility = Visibility.Visible; }
    private void JumpToMessage(ChatMessage message)
    {
        _followOutput = false; _restoringReading = null; _scrollVersion++; _viewModel.UpdateReadingState(false);
        _jumpFeedback?.Cancel(); _jumpFeedback?.Dispose(); if (_highlightedRow is not null) { _highlightedRow.JumpHighlighted = false; }
        _highlightedRow = message; message.JumpHighlighted = true; var stop = new CancellationTokenSource(); _jumpFeedback = stop;
        MessagesList.SelectedItem = message; MessagesList.ScrollIntoView(message); LatestButton.Visibility = Visibility.Visible;
        _ = ClearJumpFeedbackAsync(message, stop.Token);
    }
    private static async Task ClearJumpFeedbackAsync(ChatMessage message, CancellationToken token)
    { try { await Task.Delay(1600, token); message.JumpHighlighted = false; } catch (OperationCanceledException) { } }
}
