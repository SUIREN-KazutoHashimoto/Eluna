using System.Windows;
using Eluna.App.Services;

namespace Eluna.App;

public partial class MainWindow
{
    internal UpdateCoordinator Updates { get; private set; } = null!;
    private string? _updatePlan;
    private async Task<bool> RequestUpdateAsync(bool background)
    {
        if (Updates.Prepared is not { } staged || !_viewModel.CanApplyUpdate || background && (IsActive || OwnedWindows.Count > 0 || InputBox.Text.Length > 0)) { return false; }
        string plan = await UpdateWorker.PrepareAsync(_store, staged, background);
        if (!_viewModel.CanApplyUpdate || background && (IsActive || OwnedWindows.Count > 0 || InputBox.Text.Length > 0)) { return false; }
        _updatePlan = plan; Close(); return true;
    }
}
