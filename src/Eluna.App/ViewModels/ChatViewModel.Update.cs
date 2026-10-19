using Eluna.App.Models;

namespace Eluna.App.ViewModels;

public sealed partial class ChatViewModel
{
    internal bool CanApplyUpdate => _ready && !_shuttingDown && !_dataMaintenance && !_modelsChanging && !IsBusy && !VoiceJoined && !Inferences.HasPending && !Downloads.Items.Any(d => d.Status is "active" or "queued") && !MediaJobs.Any(j => j.IsPending);
    internal void FreezeForUpdate() { _dataMaintenance = true; Inferences.Suspended = true; RefreshCommands(); }
}
