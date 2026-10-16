using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Media.Imaging;

namespace Eluna.App.Models;

public sealed record SavedComputerTask(string Task, string ModelId, string Stage, string Plan,
    List<ComputerStep> Steps, List<string> Instructions, bool Automatic = true, int MaxSteps = 100, double SecondsUsed = 0, Guid? ProjectId = null, string? AccessStamp = null);

public sealed record ComputerInstalledApp(string Name, string Shortcut, string Sha256);

public sealed class ComputerWindowChoice(ComputerTarget target) : ObservableObject
{
    private bool _selected;
    public ComputerTarget Target { get; } = target;
    public string Label => Target.Label;
    public bool Selected { get => _selected; set => Set(ref _selected, value); }
}

public sealed class ComputerSession : ObservableObject
{
    private string _stage = "selecting", _detail = "操作するアプリを選んでください。", _plan = "", _input = "", _windowHint = "";
    private bool _automatic = true;
    private BitmapSource? _preview;
    private ComputerReview? _review;
    private ComputerInstalledApp? _app;
    public string Task { get; init; } = "";
    internal ComputerTarget? SharedTarget { get; init; }
    public string ModelId { get; init; } = "";
    public Guid? ProjectId { get; init; }
    public string? AccessStamp { get; init; }
    public string ModelName { get; internal set; } = "";
    public int MaxSteps { get; set; } = 100;
    public double SecondsUsed { get; internal set; }
    public string Stage { get => _stage; internal set { if (Set(ref _stage, value)) { Changed(); } } }
    public string Detail { get => _detail; internal set => Set(ref _detail, value); }
    public string Plan { get => _plan; internal set => Set(ref _plan, value); }
    public bool Automatic { get => _automatic; set => Set(ref _automatic, value); }
    public string ReviewInput { get => _input; set => Set(ref _input, value); }
    public string WindowHint { get => _windowHint; internal set => Set(ref _windowHint, value); }
    public BitmapSource? Preview { get => _preview; private set => Set(ref _preview, value); }
    public ComputerReview? Review { get => _review; internal set { Set(ref _review, value); Notify(nameof(ReviewText)); Notify(nameof(ReviewIsText)); } }
    public string ReviewText => Review is { } r ? r.ConfirmationReason + "\n" + r.Observation.Title + " · " + r.Action.Label +
        "\n" + (r.Observation.Elements.FirstOrDefault(e => e.Id == r.Action.Element)?.Name ?? $"座標 {r.Action.X}, {r.Action.Y} / 1000") + "\n" + r.Action.Reason : "";
    public bool ReviewIsText => Review?.Action.Type is "type_text" or "set_value";
    public bool Selecting => Stage == "selecting";
    public bool Reviewing => Stage == "review";
    public bool Running => Stage is "running" or "review" or "queued";
    public bool CanPause => Stage is "running" or "review" or "queued";
    public bool CanResume => Stage is "paused" or "takeover" or "interrupted" or "failed";
    public bool Pending => Selecting || Running || Stage is "paused" or "takeover";
    public string Label => "Computer Use · " + (Stage switch { "selecting" => "アプリの操作許可", "queued" => "操作の順番待ち", "running" => "自動操作中", "review" => "重要な操作の確認", "paused" => "一時停止", "takeover" => "あなたの操作待ち", "interrupted" => "再開できます", "complete" => "モデルの完了報告", "failed" => "停止・再試行できます", "limit" => "上限で停止", _ => "停止" });
    public ObservableCollection<ComputerWindowChoice> Windows { get; } = [];
    public ObservableCollection<ComputerInstalledApp> Applications { get; } = [];
    public ComputerInstalledApp? SelectedApplication { get => _app; set => Set(ref _app, value); }
    public ObservableCollection<ComputerStep> Steps { get; } = [];
    public List<string> Instructions { get; } = [];
    internal ComputerObservation? Observation { get; private set; }
    internal Action<string>? Command { get; set; }
    internal Action<string>? Steer { get; set; }
    internal Task Completion { get; set; } = System.Threading.Tasks.Task.CompletedTask;
    public void Invoke(string command) => Command?.Invoke(command);
    internal void Observe(ComputerObservation observation)
    {
        Observation = observation;
        using var stream = new MemoryStream(Convert.FromBase64String(observation.ImageBase64));
        var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.StreamSource = stream; bitmap.EndInit(); bitmap.Freeze(); Preview = bitmap;
        WindowHint = observation.Title + " · " + DateTime.Now.ToString("HH:mm:ss");
    }
    internal void Changed() { foreach (string p in new[] { nameof(Label), nameof(Selecting), nameof(Reviewing), nameof(Running), nameof(CanPause), nameof(CanResume), nameof(Pending) }) { Notify(p); } }
    public SavedComputerTask Save() => new(Task, ModelId, Stage, Plan, [.. Steps], [.. Instructions], Automatic, MaxSteps, SecondsUsed, ProjectId, AccessStamp);
    internal static ComputerSession Restore(SavedComputerTask saved)
    {
        var session = new ComputerSession { Task = saved.Task, ModelId = saved.ModelId, ProjectId = saved.ProjectId, AccessStamp = saved.AccessStamp, Stage = saved.Stage is "complete" or "limit" or "stopped" ? saved.Stage : "interrupted", Plan = saved.Plan, Automatic = saved.Automatic, MaxSteps = saved.MaxSteps, SecondsUsed = saved.SecondsUsed };
        foreach (ComputerStep step in saved.Steps) { session.Steps.Add(step); }
        session.Instructions.AddRange(saved.Instructions);
        session.Detail = session.CanResume ? "前回の操作は再送しません。再開するとアプリの許可と最新画面を確認します。" : "保存した操作記録です。画面画像は保存していません。";
        return session;
    }
}
