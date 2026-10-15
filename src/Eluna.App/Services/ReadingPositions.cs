using System.IO;
using System.Text.Json;

namespace Eluna.App.Services;

public sealed record ReadingPosition(string Key, double Offset, bool Follow, DateTimeOffset UpdatedAt);
public sealed class ReadingPositions(string root)
{
    private readonly Dictionary<string, ReadingPosition> _items = [];
    private readonly SemaphoreSlim _gate = new(1, 1);
    public ReadingPosition? Find(string key) => _items.GetValueOrDefault(key);
    public ReadingPosition? Latest => _items.Values.MaxBy(p => p.UpdatedAt);
    public void Set(string key, double offset, bool follow)
    {
        if (key.Split('/') is not [var conversation, var branch] || !Guid.TryParse(conversation, out _) || !Guid.TryParse(branch, out _) || !double.IsFinite(offset) || offset < 0) { return; }
        _items[key] = new(key, offset, follow, DateTimeOffset.UtcNow);
    }
    public async Task LoadAsync()
    {
        string path = Path.Combine(root, "reading-positions.json");
        try
        {
            if (!File.Exists(path) || new FileInfo(path).Length > 2 * 1024 * 1024) { return; }
            foreach (ReadingPosition item in JsonSerializer.Deserialize<List<ReadingPosition>>(await File.ReadAllTextAsync(path)) ?? [])
            { if (item is not null && item.Key is not null) { Set(item.Key, item.Offset, item.Follow); if (_items.ContainsKey(item.Key)) { _items[item.Key] = item; } } }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { }
    }
    public async Task SaveAsync()
    {
        ReadingPosition[] snapshot = _items.Values.OrderByDescending(p => p.UpdatedAt).Take(2000).ToArray();
        await _gate.WaitAsync();
        try { string path = Path.Combine(root, "reading-positions.json"); await File.WriteAllTextAsync(path + ".tmp", JsonSerializer.Serialize(snapshot)); File.Move(path + ".tmp", path, true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        finally { _gate.Release(); }
    }
}
