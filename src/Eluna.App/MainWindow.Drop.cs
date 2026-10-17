using System.Windows;
using System.Windows.Threading;
using Eluna.App.Services;

namespace Eluna.App;

public partial class MainWindow
{
    private readonly DispatcherTimer _dropTimeout = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private void InitializeFileDrop()
    {
        _dropTimeout.Tick += (_, _) => HideDropFeedback(); Closed += (_, _) => _dropTimeout.Stop();
        PreviewDragOver += (_, e) => { if (!e.Data.GetDataPresent(DataFormats.FileDrop)) { return; } bool allowed = _viewModel.CanChange && _viewModel.SelectedConversation is not null; e.Effects = allowed ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; ShowDropFeedback(allowed); };
        PreviewDragLeave += (_, e) => { Point at = e.GetPosition(this); if (at.X < 0 || at.Y < 0 || at.X > ActualWidth || at.Y > ActualHeight) { HideDropFeedback(); } };
        PreviewDrop += async (_, e) => { if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths) { return; } e.Handled = true; HideDropFeedback(); await AttachDroppedFilesAsync(paths); };
        Deactivated += (_, _) => HideDropFeedback();
    }
    internal void ShowDropFeedback(bool allowed)
    {
        DropTitle.Text = allowed ? "「" + _viewModel.ChannelTitle + "」に添付" : "いまはファイルを追加できません";
        DropDescription.Text = allowed ? "画像や文書をここにドロップ · ページを合わせて12件まで" : "準備中の処理が終わってから、もう一度ドロップしてください。";
        if (DropOverlay.Visibility != Visibility.Visible) { DropOverlay.Visibility = Visibility.Visible; UiMotion.Reveal(DropOverlay); }
        _dropTimeout.Stop(); _dropTimeout.Start();
    }
    internal void HideDropFeedback() { _dropTimeout.Stop(); DropOverlay.Visibility = Visibility.Collapsed; }
    internal async Task AttachDroppedFilesAsync(string[] paths)
    {
        if (!_viewModel.CanChange || _viewModel.SelectedConversation is not { } room) { ShowToast("準備中の処理が終わってからファイルを追加してください。"); return; }
        int before = room.DraftAttachments.Count; await _viewModel.AttachFilesAsync(paths); int added = room.DraftAttachments.Count - before;
        ShowToast(added > 0 ? "「" + room.Title + "」に" + added + "件添付しました。" + (_viewModel.HasError ? "追加できなかったファイルはエラーを確認してください。" : "") : _viewModel.HasError ? "添付できませんでした。" + _viewModel.Error : "添付を中止しました。");
    }
}
