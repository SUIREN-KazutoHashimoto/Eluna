using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows.Threading;
using Eluna.App.Models;
using Eluna.App.ViewModels;

namespace Eluna.App.Services;

internal sealed class UpdateCoordinator : ObservableObject, IDisposable
{
    private readonly AppStore _store;
    private readonly ChatViewModel _chat;
    private readonly UpdateService _service = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMinutes(1) };
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _operation;
    private DateTimeOffset _nextCheck;
    private bool _busy, _disposed;
    private string _status = "更新を確認できます。";
    private ReleaseInfo? _release;
    private StagedUpdate? _ready;
    private string? _failedVersion;
    private UpdateOptions _previousOptions = new();
    public bool Busy { get => _busy; private set => Set(ref _busy, value); }
    public string Status { get => _status; private set => Set(ref _status, value); }
    public ReleaseInfo? Release { get => _release; private set { Set(ref _release, value); Notify(nameof(Notes)); } }
    public string Notes => Release is null ? "" : "# " + Release.Name + "\n\n" + Release.Notes;
    public bool Ready => _ready is not null && _ready.Train == Options.Train;
    internal UpdateOptions Options => _chat.Settings.Updates ?? new();
    internal Func<bool, Task<bool>>? ApplyRequested { get; set; }
    internal UpdateCoordinator(AppStore store, ChatViewModel chat)
    {
        _store = store; _chat = chat; _previousOptions = Options; _timer.Tick += async (_, _) => await TickAsync(); _chat.PropertyChanged += SettingsChanged;
    }
    private void SettingsChanged(object? sender, PropertyChangedEventArgs e)
    { if (e.PropertyName == nameof(ChatViewModel.Settings) && Options != _previousOptions) { _previousOptions = Options; _nextCheck = DateTimeOffset.MinValue; _operation?.Cancel(); Notify(nameof(Ready)); } }
    internal void Start()
    {
        if (Environment.GetCommandLineArgs().Any(a => a.StartsWith("--verify", StringComparison.Ordinal) || a == "--capture-dir")) { return; }
        _timer.Start(); _ = StartLaterAsync();
    }
    private async Task StartLaterAsync() { try { await LoadReadyAsync(); await Task.Delay(TimeSpan.FromSeconds(30), _lifetime.Token); await TickAsync(); } catch (OperationCanceledException) { } }
    private async Task LoadReadyAsync()
    {
        string directory = Path.Combine(_store.Root, "Updates"), path = Path.Combine(directory, "ready.json");
        try
        {
            string outcome = Path.Combine(directory, "last-result.json");
            if (File.Exists(outcome))
            {
                using JsonDocument result = JsonDocument.Parse(await File.ReadAllTextAsync(outcome, _lifetime.Token));
                if (result.RootElement.TryGetProperty("success", out JsonElement success) && success.ValueKind == JsonValueKind.False && result.RootElement.TryGetProperty("version", out JsonElement version))
                { _failedVersion = version.GetString(); Status = "前回の更新に失敗しました。現在の版で起動しています。同じ版は自動で再適用せず、更新画面から再試行できます。"; }
            }
            Cleanup(directory);
            if (!File.Exists(path)) { return; }
            StagedUpdate staged = JsonSerializer.Deserialize<StagedUpdate>(await File.ReadAllTextAsync(path, _lifetime.Token), AppStore.JsonOptions) ?? throw new JsonException("更新記録が空です。");
            if (!UpdateWorker.Under(staged.File, directory) || staged.Train is not ("stable" or "develop") || staged.DataSchema < 5 || staged.Version == _failedVersion || UpdateService.Compare(staged.Version, UpdateService.CurrentVersion) != true) { return; }
            await UpdateService.ValidateFileAsync(staged.File, staged.Sha256, staged.Size, _lifetime.Token); _ready = staged; Status = "取得済みの更新を再開しました。処理の終了後に適用できます。"; Notify(nameof(Ready));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or ArgumentException or InvalidOperationException) { _store.Trace.Failure("update", "resume.failed", e); Status = "取得済みの更新を確認できません。再取得できます。"; }
    }
    private static void Cleanup(string directory)
    {
        if (!Directory.Exists(directory)) { return; }
        // Delete only names that our updater owns, never recurse into unknown contents.
        foreach (string file in Directory.EnumerateFiles(directory, "Eluna-worker-*.exe").Concat(Directory.EnumerateFiles(directory, "apply-*.json")))
        {
            if (File.GetLastWriteTimeUtc(file) > DateTime.UtcNow.AddDays(-1)) { continue; }
            try { File.Delete(file); } catch (IOException) { }
        }
        foreach (string folder in Directory.EnumerateDirectories(directory).Where(d => Guid.TryParseExact(Path.GetFileName(d), "N", out _)))
        {
            if (File.Exists(Path.Combine(directory, "ready.json")) || Directory.GetLastWriteTimeUtc(folder) > DateTime.UtcNow.AddDays(-7)) { continue; }
            InstallerService.RejectLink(folder);
            foreach (string name in new[] { "manifest.json", "Eluna.exe" }) { string path = Path.Combine(folder, name); if (File.Exists(path)) { File.Delete(path); } }
            if (!Directory.EnumerateFileSystemEntries(folder).Any()) { Directory.Delete(folder); }
        }
    }
    private async Task TickAsync()
    {
        if (_disposed || Busy || !Options.Automatic || InstallerService.Current is null) { return; }
        if (Ready && ApplyRequested is not null) { try { await ApplyRequested(true); } catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException) { Status = "更新を保留しました: " + e.Message; _store.Trace.Failure("update", "apply.prepare.failed", e); } return; }
        if (DateTimeOffset.UtcNow < _nextCheck) { return; }
        _nextCheck = DateTimeOffset.UtcNow.AddHours(3);
        await CheckAsync(true);
    }
    internal async Task CheckAsync(bool download = false)
    {
        if (Busy || _disposed) { return; }
        Busy = true; _operation?.Dispose(); _operation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token); _operation.CancelAfter(TimeSpan.FromMinutes(20));
        string train = Options.Train; CancellationToken token = _operation.Token;
        try
        {
            Status = "公式GitHubの" + (train == "develop" ? "開発版" : "正式版") + "を確認しています…";
            Release = await _service.CheckAsync(token, train);
            Status = Release is null ? "選択したトレインの公開リリースが見つかりません。" : Release.IsNewer switch { true => Release.Tag + " を取得できます。", false => "現在より新しい互換版はありません。公開版: " + Release.Tag, _ => "この版との自動比較はできません。" };
            if (download && Release?.IsNewer == true && Release.Tag.TrimStart('v') != _failedVersion) { await StageCoreAsync(Release, train, token); }
        }
        catch (OperationCanceledException) { if (!_disposed) { Status = "更新の確認・取得を中止しました。"; } }
        catch (Exception e) when (e is HttpRequestException or IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        { Status = "更新できませんでした: " + e.Message; _store.Trace.Failure("update", "check.failed", e); }
        finally { Busy = false; }
    }
    internal async Task DownloadAsync()
    {
        if (Busy || Release is null) { return; }
        Busy = true; _operation?.Dispose(); _operation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token); _operation.CancelAfter(TimeSpan.FromMinutes(20));
        try { await StageCoreAsync(Release, Options.Train, _operation.Token); }
        catch (OperationCanceledException) { Status = "更新の取得を中止しました。"; }
        catch (Exception e) when (e is HttpRequestException or IOException or UnauthorizedAccessException or JsonException or InvalidOperationException) { Status = e.Message; _store.Trace.Failure("update", "download.failed", e); }
        finally { Busy = false; }
    }
    private async Task StageCoreAsync(ReleaseInfo release, string train, CancellationToken token)
    {
        _ready = await _service.StageAsync(_store, release, train, progress => _ = System.Windows.Application.Current.Dispatcher.InvokeAsync(() => Status = $"更新を取得中 {progress:N0}%"), token);
        _failedVersion = null; await File.WriteAllTextAsync(Path.Combine(_store.Root, "Updates", "last-result.json"), JsonSerializer.Serialize(new { phase = "downloaded", version = _ready.Version }, AppStore.JsonOptions), token);
        Status = "更新を取得しました。処理の終了後に再起動して適用できます。自動適用は画面を使っていないときに行います。"; Notify(nameof(Ready));
    }
    internal async Task<bool> ApplyAsync(bool background)
    {
        if (_ready is null || !Ready || !_chat.CanApplyUpdate || ApplyRequested is null) { Status = "推論・取得・VCを終了してから適用してください。"; return false; }
        return await ApplyRequested(background);
    }
    internal StagedUpdate? Prepared => Ready ? _ready : null;
    internal void Cancel() => _operation?.Cancel();
    public void Dispose() { if (_disposed) { return; } _disposed = true; _timer.Stop(); _chat.PropertyChanged -= SettingsChanged; _lifetime.Cancel(); _operation?.Cancel(); _service.Dispose(); }
}
