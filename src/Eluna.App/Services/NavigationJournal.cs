namespace Eluna.App.Services;

internal sealed class NavigationJournal
{
    private readonly List<Guid> _entries = [];
    private int _index = -1;
    internal bool CanBack => _index > 0;
    internal bool CanForward => _index >= 0 && _index + 1 < _entries.Count;
    internal bool CanMove(int direction, Func<Guid, bool> exists)
    {
        if (direction is not (-1 or 1)) { throw new ArgumentOutOfRangeException(nameof(direction)); }
        for (int next = _index + direction; next >= 0 && next < _entries.Count; next += direction) { if (exists(_entries[next])) { return true; } }
        return false;
    }
    internal void Visit(Guid id)
    {
        if (_index >= 0 && _entries[_index] == id) { return; }
        if (_index + 1 < _entries.Count) { _entries.RemoveRange(_index + 1, _entries.Count - _index - 1); }
        _entries.Add(id); if (_entries.Count > 100) { _entries.RemoveAt(0); }
        _index = _entries.Count - 1;
    }
    internal Guid? Move(int direction, Func<Guid, bool> exists)
    {
        if (direction is not (-1 or 1)) { throw new ArgumentOutOfRangeException(nameof(direction)); }
        for (int next = _index + direction; next >= 0 && next < _entries.Count; next += direction)
        { if (exists(_entries[next])) { _index = next; return _entries[next]; } }
        return null;
    }
}
