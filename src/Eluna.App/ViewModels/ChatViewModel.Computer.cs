using Eluna.App.Models;
using Eluna.App.Services;

namespace Eluna.App.ViewModels;

public sealed partial class ChatViewModel
{
    public ComputerActivity? ComputerActivity { get; private set; }
    internal Task ComputerCompletion { get; set; } = Task.CompletedTask;
    internal bool CanStartComputer => _ready && !_shuttingDown && !_dataMaintenance && !_modelsChanging && ComputerActivity?.State != "active";
    internal LlamaService ComputerEngine() => _llama.CreateWorker();
    internal void SetComputerActivity(ComputerActivity? activity)
    { ComputerActivity = activity; Notify(nameof(ComputerActivity)); Notify(nameof(ProcessSummary)); RefreshCommands(); }
    internal void ComputerStatus(string detail)
    { if (ComputerActivity is { } activity) { activity.Detail = detail; } Notify(nameof(ProcessSummary)); }
}
