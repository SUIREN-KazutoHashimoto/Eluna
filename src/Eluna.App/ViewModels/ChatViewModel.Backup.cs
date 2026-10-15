using Eluna.App.Services;

namespace Eluna.App.ViewModels;

public sealed partial class ChatViewModel
{
    private bool _dataMaintenance;
    public event Func<Task>? BackupStarting;
    public async Task<T> WithBackupAsync<T>(Func<AppStore, CancellationToken, Task<T>> operation, CancellationToken token)
    {
        if (!_ready || _shuttingDown || _dataMaintenance || _modelsChanging || IsBusy)
        { throw new InvalidOperationException("推論・画像文書の生成・モデル管理を停止または完了してから実行してください。予約は保存できます。"); }
        _dataMaintenance = true; Inferences.Suspended = true; RefreshCommands();
        try
        {
            _draftSave?.Cancel(); await Downloads.PauseAllAsync();
            if (BackupStarting is { } prepare) { await prepare(); }
            await PersistSafelyAsync(); await _store.SaveAsync(CaptureState()); await SaveMediaJobsAsync(true);
            token.ThrowIfCancellationRequested(); return await operation(_store, token);
        }
        finally { _dataMaintenance = false; Inferences.Suspended = _isBusy; RefreshCommands(); PumpMedia(); }
    }
}
