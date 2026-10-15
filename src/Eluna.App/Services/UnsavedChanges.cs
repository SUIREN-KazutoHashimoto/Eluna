using System.Runtime.CompilerServices;
using System.Windows;

namespace Eluna.App.Services;

internal static class UnsavedChanges
{
    private sealed class Protection { internal bool Approved; internal Task<bool>? Pending; }
    private static readonly ConditionalWeakTable<Window, Protection> Windows = new();
    internal static Func<Window, bool?>? Review;
    internal static bool? Choose(Window owner)
    {
        if (Review is not null) { return Review(owner); }
        return MessageBox.Show(owner, "未保存の変更があります。保存しますか？\n「いいえ」で変更を破棄し、「キャンセル」で編集を続けます。", "変更を保存", MessageBoxButton.YesNoCancel, MessageBoxImage.Question) switch
        { MessageBoxResult.Yes => true, MessageBoxResult.No => false, _ => null };
    }
    internal static Task<bool> PendingClose(Window owner) => Windows.TryGetValue(owner, out Protection? state) && state.Pending is not null ? state.Pending : Task.FromResult(!owner.IsVisible);
    internal static void Protect(Window owner, Func<bool> dirty, Func<Task<bool>> save)
    {
        var state = new Protection(); Windows.Add(owner, state);
        owner.Closing += (_, e) =>
        {
            if (state.Approved || e.Cancel || !dirty()) { return; }
            e.Cancel = true; if (state.Pending is { IsCompleted: false }) { return; }
            state.Pending = DecideAsync();
        };
        async Task<bool> DecideAsync()
        {
            bool? decision = Choose(owner);
            if (decision is null) { return false; }
            if (decision == true && !await save()) { return false; }
            state.Approved = true;
            await owner.Dispatcher.InvokeAsync(() => { if (owner.IsVisible) { owner.Close(); } });
            return !owner.IsVisible;
        }
    }
}
