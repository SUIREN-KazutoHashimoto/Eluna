using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using Eluna.App.Models;

namespace Eluna.App.Services;

public sealed record SavedDownload(Guid Id, ModelFile? File, string? ImportPath, string Name, string Status, long Received, long Total, string Error = "", string? ModelId = null, DateTimeOffset? CreatedAt = null, string AssetKind = "chat", string? ResultPath = null);
public sealed class DownloadItem : ObservableObject
{
    private string _status = "queued", _stage = "待機中", _error = "";
    private long _received, _total; private double _speed;
    public Guid Id { get; init; } = Guid.NewGuid();
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public ModelFile? File { get; init; }
    public string? ImportPath { get; init; }
    public string Name { get; init; } = "";
    public string? ModelId { get; set; }
    public string AssetKind { get; init; } = "chat";
    public string? ResultPath { get; set; }
    public string Status { get => _status; set { if (Set(ref _status, value)) { Notify(nameof(CanResume)); Notify(nameof(CanPause)); Notify(nameof(CanUse)); Notify(nameof(CanRemove)); Notify(nameof(Label)); } } }
    public string Stage { get => _stage; set { if (Set(ref _stage, value)) { Notify(nameof(Label)); } } }
    public string Error { get => _error; set { if (Set(ref _error, value)) { Notify(nameof(Detail)); } } }
    public long Received { get => _received; set { if (Set(ref _received, value)) { Notify(nameof(Percent)); Notify(nameof(Detail)); } } }
    public long Total { get => _total; set { if (Set(ref _total, value)) { Notify(nameof(Percent)); Notify(nameof(Detail)); } } }
    public double Speed { get => _speed; set { if (Set(ref _speed, value)) { Notify(nameof(Detail)); } } }
    public double Percent => Total > 0 ? Math.Clamp(Received * 100.0 / Total, 0, 100) : 0;
    public bool CanResume => Status is "paused" or "failed";
    public bool CanPause => Status is "queued" or "active";
    public bool CanUse => Status == "complete" && ModelId is not null;
    public bool CanRemove => Status != "active";
    public string Label => Status switch { "queued" => "待機中", "paused" => "一時停止", "failed" => "再試行できます", "complete" => "完了", _ => Stage };
    public string Detail => $"{FileSize.Format(Received)} / {FileSize.Format(Total)}" +
        (Speed > 0 && Status == "active" ? $" · {FileSize.Format((long)Speed)}/秒 · 残り約 {TimeSpan.FromSeconds(Math.Min(8640000, Math.Max(0, Total - Received) / Speed)):g}" : "") +
        (Error.Length > 0 ? "\n" + Error : "");
    public SavedDownload Save() => new(Id, File, ImportPath, Name, Status, Received, Total, Error, ModelId, CreatedAt, AssetKind, ResultPath);
}
public sealed class DownloadQueue : ObservableObject, IDisposable
{
    private readonly AppStore _store; private readonly ModelCatalog _catalog;
    private readonly SemaphoreSlim _saveGate = new(1, 1); private readonly object _snapshotLock = new();
    private readonly Dictionary<DownloadItem, (CancellationTokenSource Cancellation, Task Work)> _workers = [];
    private bool _closing, _pumping, _pausing;
    private int _limit = 1;
    public int Limit { get => _limit; set { if (value is < 1 or > 4) { throw new ArgumentOutOfRangeException(nameof(value)); } _limit = value; Start(); Notify(); } }
    public ObservableCollection<DownloadItem> Items { get; } = [];
    public event Action<LocalModel>? Completed;
    public bool IsActive => _workers.Count > 0;
    public string Label => IsActive ? $"取得中 · {_workers.Count}件 / 予約 {Items.Count(i => i.Status == "queued")}件" : Items.Any(i => i.CanResume) ? "取得キュー · 一時停止あり" : "ダウンロード管理";
    public string? RecoveryNotice { get; private set; }
    public DownloadQueue(AppStore store, ModelCatalog? catalog = null) { _store = store; _catalog = catalog ?? new(store); }
    public async Task LoadAsync()
    {
        string path = Path.Combine(_store.Root, "downloads-v2.json"); if (!System.IO.File.Exists(path)) { path = Path.Combine(_store.Root, "downloads.json"); }
        if (!System.IO.File.Exists(path)) { return; }
        try
        {
            List<SavedDownload> saved = JsonSerializer.Deserialize<List<SavedDownload>>(await System.IO.File.ReadAllTextAsync(path), AppStore.JsonOptions) ?? [];
            if (saved.Count > 1000 || saved.Any(s => s is null || s.Name is null || s.Error is null || s.File is null && s.ImportPath is null)) { throw new InvalidDataException("取得キューの形式が不正です。"); }
            foreach (SavedDownload item in saved)
            {
                if (item.AssetKind is not ("chat" or "image")) { throw new InvalidDataException("取得の種類が不正です。"); }
                var restored = new DownloadItem { Id = item.Id, CreatedAt = item.CreatedAt ?? DateTimeOffset.UtcNow, File = item.File, ImportPath = item.ImportPath, Name = item.Name, Status = item.Status == "complete" ? "complete" : "paused", Stage = "再開待ち", Received = item.Received, Total = item.Total, Error = item.Error, ModelId = item.ModelId, AssetKind = item.AssetKind, ResultPath = item.ResultPath };
                if (restored.Status == "complete" && !HasCompletedAsset(restored)) { restored.Status = "paused"; restored.Received = 0; restored.Error = "モデル本体が見つかりません。再開すると再取得します。"; }
                if (restored.Status == "complete") { restored.Stage = "完了"; }
                Items.Add(restored);
            }
            Notify(nameof(Label));
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException or IOException)
        { System.IO.File.Copy(path, path + ".broken-" + Guid.NewGuid().ToString("N")); RecoveryNotice = "取得キューを復元できませんでした。元のファイルと途中のGGUFは保管しています。"; }
    }
    public DownloadItem Enqueue(ModelFile file) => Enqueue(file, "chat");
    public DownloadItem EnqueueImage(ModelFile file) { if (file.Files.Count != 1) { throw new InvalidDataException("画像生成には一体型GGUFを選んでください。"); } return Enqueue(file, "image"); }
    private DownloadItem Enqueue(ModelFile file, string kind)
    {
        DownloadItem? existing = Items.FirstOrDefault(i => i.AssetKind == kind && i.File?.Repository == file.Repository && i.File.Revision == file.Revision && i.File.Filename == file.Filename);
        if (existing is not null)
        {
            if (existing.Status == "complete" && !HasCompletedAsset(existing)) { existing.Status = "paused"; }
            if (existing.CanResume) { Resume(existing); }
            return existing;
        }
        var item = new DownloadItem { File = file, AssetKind = kind, Name = file.Filename, Total = file.Size }; lock (_snapshotLock) { Items.Add(item); }
        _ = PersistAsync(); Start(); return item;
    }
    public DownloadItem Import(string path)
    {
        var item = new DownloadItem { ImportPath = Path.GetFullPath(path), Name = Path.GetFileName(path), Total = new FileInfo(path).Length };
        lock (_snapshotLock) { Items.Add(item); }
        _ = PersistAsync(); Start(); return item;
    }
    private bool HasCompletedAsset(DownloadItem item)
    {
        try
        {
            if (item.AssetKind == "image") { return System.IO.File.Exists(item.ResultPath) && new FileInfo(item.ResultPath!).Length == item.Total; }
            string? id = item.ModelId;
            if (id is null && item.File is { } file) { id = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes($"{file.Repository}/{file.Revision}/{file.Filename}"))).ToLowerInvariant()[..24]; }
            if (id is null || id.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || id is "." or "..") { return false; }
            string path = Path.Combine(_store.ModelsDirectory, id, "model.json");
            if (!System.IO.File.Exists(path)) { string folder = id.StartsWith("local-", StringComparison.Ordinal) && id.Length >= 26 ? id[..26] : id.Length > 24 ? id[..24] : id; path = Path.Combine(_store.ModelsDirectory, folder, "model.json"); }
            LocalModel? model = System.IO.File.Exists(path) ? JsonSerializer.Deserialize<LocalModel>(System.IO.File.ReadAllText(path), AppStore.JsonOptions) : null;
            return model is not null && model.Files.All(f => System.IO.File.Exists(f.FilePath) && new FileInfo(f.FilePath).Length == f.Size);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return false; }
    }
    public void Pause(DownloadItem item)
    {
        if (_workers.TryGetValue(item, out var worker)) { worker.Cancellation.Cancel(); }
        else if (item.Status == "queued") { item.Status = "paused"; _ = PersistAsync(); Notify(nameof(Label)); }
    }
    public void Resume(DownloadItem item) { if (!_closing && item.CanResume) { item.Error = ""; item.Status = "queued"; _ = PersistAsync(); Start(); } }
    public void Remove(DownloadItem item) { if (item.Status != "active") { lock (_snapshotLock) { Items.Remove(item); } _ = PersistAsync(); Notify(nameof(Label)); } }
    public void MarkDeleted(string id) { foreach (DownloadItem item in Items.Where(i => i.ModelId == id)) { item.Status = "paused"; item.Error = "モデルは削除済みです。再取得できます。"; } _ = PersistAsync(); }
    private void Start()
    {
        if (_closing || _pausing || _pumping) { return; }
        _pumping = true;
        try
        {
            while (_workers.Count < Limit)
            {
                DownloadItem? item = Items.FirstOrDefault(i => i.Status == "queued" && (i.ImportPath is null || !_workers.Keys.Any(w => w.ImportPath is not null)));
                if (item is null) { break; }
                var cancellation = new CancellationTokenSource(); item.Status = "active"; item.Stage = "準備中"; item.Error = "";
                _workers.Add(item, (cancellation, Task.CompletedTask)); _workers[item] = (cancellation, RunAsync(item, cancellation));
            }
        }
        finally { _pumping = false; Notify(nameof(IsActive)); Notify(nameof(Label)); }
    }
    private async Task RunAsync(DownloadItem item, CancellationTokenSource cancellation)
    {
        using var diagnostic = DiagnosticScope.Start(_store.Trace, item.Id); DiagnosticScope.Current!.Write("info", "download", "started", "kind=" + item.AssetKind);
        await Task.Yield();
        var clock = Stopwatch.StartNew(); long lastBytes = item.Received, lastTime = 0, lastSave = 0; string previousStage = "";
        var progress = new QueueProgress(p =>
        {
            long now = clock.ElapsedMilliseconds; bool downloading = p.Stage.StartsWith("ダウンロード", StringComparison.Ordinal);
            if (downloading && previousStage.StartsWith("ダウンロード", StringComparison.Ordinal) && now > lastTime && p.Received >= lastBytes)
            { double speed = (p.Received - lastBytes) * 1000.0 / (now - lastTime); item.Speed = item.Speed == 0 ? speed : item.Speed * 0.75 + speed * 0.25; }
            else { item.Speed = 0; }
            item.Received = p.Received; item.Total = p.Total; item.Stage = p.Stage; lastBytes = p.Received; lastTime = now; previousStage = p.Stage;
            if (now - lastSave > 2000) { lastSave = now; _ = PersistAsync(); }
            DiagnosticScope.Current?.Write("debug", "download", "progress", "received=" + item.Received + "; total=" + item.Total + "; stage=" + item.Stage);
            Notify(nameof(Label));
        });
        try
        {
            await PersistAsync();
            if (item.AssetKind == "image")
            {
                ModelFile file = item.File!; string folder = Path.Combine(_store.Root, "ImageModels", file.Sha256[..24]); Directory.CreateDirectory(folder);
                item.ResultPath = await _catalog.DownloadFileAsync(file, folder, progress, cancellation.Token); item.Total = item.Received = file.Size; item.Status = "complete"; item.Stage = "完了";
            }
            else
            {
                LocalModel model = item.File is not null ? await _catalog.DownloadAsync(item.File, progress, cancellation.Token) : await _catalog.ImportAsync(item.ImportPath!, progress, cancellation.Token);
                item.ModelId = model.Id; item.Total = item.Received = model.Size; item.Status = "complete"; item.Stage = "完了"; Completed?.Invoke(model);
            }
        }
        catch (OperationCanceledException) { DiagnosticScope.Current!.Write("info", "download", "paused"); item.Status = "paused"; item.Stage = "一時停止"; }
        catch (Exception ex) { DiagnosticScope.Current!.Failure("download", "failed", ex); item.Status = "failed"; item.Error = DiagnosticsService.Explain(ex); item.Stage = "取得できませんでした"; }
        finally { DiagnosticScope.Current!.Write("info", "download", "finished", "state=" + item.Status + "; received=" + item.Received); item.Speed = 0; cancellation.Dispose(); _workers.Remove(item); Notify(nameof(IsActive)); Notify(nameof(Label)); await PersistAsync(); Start(); }
    }
    private sealed class QueueProgress(Action<DownloadProgress> callback) : IProgress<DownloadProgress> { public void Report(DownloadProgress value) => callback(value); }
    private async Task PersistAsync()
    {
        await _saveGate.WaitAsync();
        try { List<SavedDownload> snapshot; lock (_snapshotLock) { snapshot = Items.Select(i => i.Save()).ToList(); } string path = Path.Combine(_store.Root, "downloads-v2.json"); await System.IO.File.WriteAllTextAsync(path + ".tmp", JsonSerializer.Serialize(snapshot, AppStore.JsonOptions)); System.IO.File.Move(path + ".tmp", path, true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { RecoveryNotice = "取得キューを保存できませんでした。空き容量とアクセス権を確認してください。"; Notify(nameof(RecoveryNotice)); }
        finally { _saveGate.Release(); }
    }
    public async Task PauseAllAsync()
    {
        _pausing = true;
        try
        {
            foreach (DownloadItem item in Items.Where(i => i.Status == "queued")) { item.Status = "paused"; }
            var workers = _workers.Values.ToArray(); foreach (var worker in workers) { worker.Cancellation.Cancel(); }
            await Task.WhenAll(workers.Select(w => w.Work));
            await PersistAsync();
        }
        finally { _pausing = false; }
    }
    public async Task ShutdownAsync() { _closing = true; await PauseAllAsync(); }
    public void Dispose() { _closing = true; foreach (var worker in _workers.Values.ToArray()) { worker.Cancellation.Cancel(); } _catalog.Dispose(); }
}
