using System.Collections.ObjectModel;

namespace Eluna.App.Models;

public sealed record WorkMode(string Id, string Name);
public static class WorkModes
{
    public static IReadOnlyList<WorkMode> All { get; } = [new("chat", "チャット · 自動検索"), new("search", "Web検索 · 必ず"), new("offline", "検索しない"), new("research", "深掘り調査"), new("debate", "ディベート"), new("computer", "Computer Use")];
    public static WorkMode Find(string? id) => All.FirstOrDefault(m => m.Id == id) ?? All[0];
}

public sealed record DebateRole(string Id, string Name, string Instruction, string? ModelId = null);
public sealed record WorkOptions(int SearchLimit = 4, int PageLimit = 8, int ParallelPages = 3, int SecondsLimit = 300,
    int TokenLimit = 8000, int Rounds = 2, List<DebateRole>? Roles = null)
{
    public static IReadOnlyList<DebateRole> DefaultRoles { get; } = [
        new("proposer", "提案役", "問いに対して実行可能な案を出し、利益・費用・前提を示す。"),
        new("critic", "批判役", "案の弱点、反例、見落としを指摘する。代替案と改善も示す。"),
        new("verifier", "検証役", "資料本文と主張を照合する。根拠のない主張、日付、食い違い、未確認事項を示す。")];
    public IReadOnlyList<DebateRole> Participants => Roles is { Count: > 0 } ? Roles : DefaultRoles;
}
public sealed record WorkActivity(DateTimeOffset Time, string Kind, string Title, string Detail)
{
    public string Label => $"{Time.ToLocalTime():HH:mm:ss} · {Title}";
}
public sealed record WorkSource(int Number, string Title, string Url, string Text, DateTimeOffset RetrievedUtc, string? Published = null)
{
    public string Label => $"[S{Number}] {Title}";
    public string Detail => $"{new Uri(Url).Host} · 閲覧 {RetrievedUtc.ToLocalTime():yyyy/MM/dd HH:mm}" + (Published is null ? " · 発表日不明" : $" · 発表 {Published}");
}
public sealed record WorkSearchHit(string Title, string Url, string Snippet, string Provider);
public sealed record DebateTurn(int Round, string Role, string Content, string? ModelId = null, string? Name = null)
{
    public string Label => $"ラウンド {Round} · {Name ?? Role}";
}
public sealed record WorkClaim(int SourceNumber, string Quote, string Claim);
public sealed record SavedWork(Guid Id, string Mode, string Question, string ModelId, WorkOptions Options, string Stage, string Plan,
    List<string> Queries, List<string> SearchedQueries, List<string> AttemptedPages, List<WorkSearchHit> Hits, List<WorkSource> Sources,
    List<WorkActivity> Activities, List<DebateTurn> Turns, string Verification, string Report, bool Expanded,
    int SearchesUsed, int PagesUsed, int TokensUsed, double SecondsUsed, bool PlanApproved = false, List<WorkClaim>? Claims = null, bool AutomaticSearch = false);

public sealed class WorkSession : ObservableObject
{
    private string _stage = "planning", _plan = "", _report = "";
    private bool _isRunning;
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Mode { get; init; } = "search";
    public string Question { get; init; } = "";
    public string ModelId { get; init; } = "";
    public bool AutomaticSearch { get; init; }
    public WorkOptions Options { get; init; } = new();
    public string Name => AutomaticSearch ? "自動Web検索" : WorkModes.Find(Mode).Name;
    public string Stage { get => _stage; set { if (Set(ref _stage, value)) { Notify(nameof(Label)); Notify(nameof(CanResume)); } } }
    public bool IsRunning { get => _isRunning; set { if (Set(ref _isRunning, value)) { Notify(nameof(CanResume)); } } }
    public string Plan { get => _plan; set => Set(ref _plan, value); }
    public string Report { get => _report; set => Set(ref _report, value); }
    public string Label => Name + " · " + (Stage switch { "planning" => "計画を作成中", "plan" => "計画の確認", "searching" => "検索・閲覧中", "expanding" => "不足点を調査中", "debating" => "議論中", "verifying" => "根拠を確認中", "reporting" => "回答を作成中", "complete" => "完了", "paused" => "停止・再開できます", "limited" => "上限に到達", "failed" => "再試行できます", "moved" => "所属変更のため再開できません", _ => "準備中" });
    public bool CanResume => !IsRunning && Stage is not ("complete" or "limited" or "moved");
    public ObservableCollection<WorkSource> Sources { get; } = [];
    public ObservableCollection<WorkActivity> Activities { get; } = [];
    public ObservableCollection<DebateTurn> Turns { get; } = [];
    public ObservableCollection<WorkClaim> Claims { get; } = [];
    public List<string> Queries { get; } = [];
    public List<string> SearchedQueries { get; } = [];
    public List<string> AttemptedPages { get; } = [];
    public List<WorkSearchHit> Hits { get; } = [];
    public string Verification { get; set; } = "";
    public bool Expanded { get; set; }
    public bool PlanApproved { get; set; }
    public int SearchesUsed { get; set; }
    public int PagesUsed { get; set; }
    public int TokensUsed { get; set; }
    public double SecondsUsed { get; set; }
    public string Budget => $"検索 {SearchesUsed}/{Options.SearchLimit} · 本文 {PagesUsed}/{Options.PageLimit} · 生成 {TokensUsed:N0}/{Options.TokenLimit:N0} token · {SecondsUsed:F0}/{Options.SecondsLimit}秒";
    public void Changed() { Notify(nameof(Budget)); Notify(nameof(Label)); }
    public SavedWork Save() => new(Id, Mode, Question, ModelId, Options, Stage, Plan, [.. Queries], [.. SearchedQueries], [.. AttemptedPages], [.. Hits], [.. Sources], [.. Activities], [.. Turns], Verification, Report, Expanded, SearchesUsed, PagesUsed, TokensUsed, SecondsUsed, PlanApproved, [.. Claims], AutomaticSearch);
    public static WorkSession Restore(SavedWork saved)
    {
        var work = new WorkSession
        {
            Id = saved.Id,
            Mode = saved.Mode,
            Question = saved.Question,
            ModelId = saved.ModelId,
            AutomaticSearch = saved.AutomaticSearch,
            Options = saved.Options,
            Stage = saved.Stage is "complete" or "limited" ? saved.Stage : "paused",
            Plan = saved.Plan,
            Verification = saved.Verification,
            Report = saved.Report,
            Expanded = saved.Expanded,
            SearchesUsed = saved.SearchesUsed,
            PagesUsed = saved.PagesUsed,
            TokensUsed = saved.TokensUsed,
            SecondsUsed = saved.SecondsUsed,
            PlanApproved = saved.PlanApproved
        };
        work.Queries.AddRange(saved.Queries); work.SearchedQueries.AddRange(saved.SearchedQueries); work.AttemptedPages.AddRange(saved.AttemptedPages); work.Hits.AddRange(saved.Hits);
        foreach (WorkSource source in saved.Sources) { work.Sources.Add(source); }
        foreach (WorkActivity activity in saved.Activities) { work.Activities.Add(activity); }
        foreach (DebateTurn turn in saved.Turns) { work.Turns.Add(turn); }
        foreach (WorkClaim claim in saved.Claims ?? []) { work.Claims.Add(claim); }
        return work;
    }
}
