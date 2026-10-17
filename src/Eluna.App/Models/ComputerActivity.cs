namespace Eluna.App.Models;

public sealed class ComputerActivity(string title, Action stop, Action open)
{
    public string Title { get; } = title;
    public DateTimeOffset CreatedAt { get; } = DateTimeOffset.UtcNow;
    public string State { get; internal set; } = "active";
    public string Detail { get; internal set; } = "画面を確認しています…";
    internal void Stop() => stop();
    internal void Open() => open();
}
