using System.Windows;
using Eluna.App.Controls;
using Eluna.App.Models;
using Eluna.App.Services;

namespace Eluna.App;

public partial class MainWindow
{
    private ToastPanel? _toast;
    internal ToastPanel? ActiveToast => _toast;
    internal void ShowToast(string message, Func<Task>? undo = null)
    {
        PositionToast();
        _toast?.Dispose(); var panel = new ToastPanel(message, undo); _toast = panel; FeedbackHost.Content = panel;
        panel.Dismissed += () => { if (ReferenceEquals(_toast, panel)) { FeedbackHost.Content = null; _toast = null; } };
    }
    private void PositionToast() => FeedbackHost.Margin = new Thickness(16, 0, 16, Math.Max(84, ComposerArea.ActualHeight + ComposerArea.Margin.Top + 12));
    private Conversation RequireFeedbackRoom(Guid id) => _viewModel.Conversations.FirstOrDefault(c => c.Id == id) ?? throw new InvalidOperationException("対象の会話は削除されています。");
    internal async Task ArchiveWithUndoAsync(Conversation target)
    {
        Task saving = _viewModel.ToggleArchiveAsync(target); bool after = target.IsArchived; long revision = target.ArchiveRevision; await saving;
        if (target.ArchiveRevision != revision || !_viewModel.Conversations.Contains(target)) { return; }
        ShowToast(after ? "「" + target.Title + "」をアーカイブしました。" : "「" + target.Title + "」を保管から戻しました。", async () => { Conversation room = RequireFeedbackRoom(target.Id); if (!ReferenceEquals(room, target) || room.ArchiveRevision != revision || room.IsArchived != after) { throw new InvalidOperationException("その後、保管状態が変更されています。"); } await _viewModel.ToggleArchiveAsync(room); });
    }
    internal async Task PinChannelWithUndoAsync(Conversation target)
    {
        Task saving = _viewModel.TogglePinAsync(target); bool after = target.IsPinned; long revision = target.ChannelPinRevision; await saving;
        if (target.ChannelPinRevision != revision || !_viewModel.Conversations.Contains(target)) { return; }
        ShowToast(after ? "チャンネルをピン留めしました。" : "チャンネルのピン留めを解除しました。", async () => { Conversation room = RequireFeedbackRoom(target.Id); if (!ReferenceEquals(room, target) || room.ChannelPinRevision != revision || room.IsPinned != after) { throw new InvalidOperationException("その後、ピンの状態が変更されています。"); } await _viewModel.TogglePinAsync(room); });
    }
    private Func<Task> UndoMessagePin(Guid room, Guid branch, Guid message, bool after)
    {
        Conversation target = RequireFeedbackRoom(room); long revision = target.MessagePinsRevision;
        return async () => { Conversation source = RequireFeedbackRoom(room); MessageLocation? row = MessageLocations.In(source).FirstOrDefault(m => m.Branch == branch && m.Message == message); if (!ReferenceEquals(source, target) || source.MessagePinsRevision != revision || row is null || row.Pinned != after) { throw new InvalidOperationException("対象の発言またはピンの状態が変更されています。"); } await _viewModel.ToggleMessagePinAsync(room, branch, message); };
    }
}
