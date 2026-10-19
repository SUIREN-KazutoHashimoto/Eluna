using Eluna.App.Services;

namespace Eluna.App.Models;

public sealed record SavedInferenceJob(Guid Id, Guid ConversationId, Guid BranchId, Guid AnswerId, string Title,
    DateTimeOffset CreatedAt, LocalModel Model, List<LocalModel> Models, AppSettings Settings,
    string Instruction, List<KnowledgeReference> References, SavedWork? PreviousWork = null, bool ResumeWork = false, SkillRequest? Skills = null,
    AgentAccount? Agent = null, string? AccessStamp = null, SocialTurn? Social = null);

public sealed record SocialTurn(Guid SessionId, Guid? TriggerId, int RemainingTurns, int RemainingTokens,
    bool Prompted = false, bool Initiation = false, List<Guid>? Called = null);

public sealed class InferenceJob(SavedInferenceJob request) : ObservableObject
{
    private string _state = "queued", _detail = "順番を待っています", _metrics = "";
    internal CancellationTokenSource? Cancellation { get; set; }
    internal TaskCompletionSource Completion { get; set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Task Finished => Completion.Task;
    public SavedInferenceJob Request { get; } = request;
    public string Title => Request.Title;
    public string ModelLabel => Request.Model.DisplayName + " · " + InferenceModes.Find(Request.Settings.InferenceMode).Name;
    public string State { get => _state; internal set { Set(ref _state, value); Notify(nameof(Label)); Notify(nameof(CanCancel)); Notify(nameof(CanResume)); Notify(nameof(IsPending)); Notify(nameof(Priority)); } }
    public int Priority => State switch { "active" => 0, "queued" => 1, "paused" or "failed" => 2, _ => 3 };
    public string Label => State switch { "active" => "実行中", "queued" => "予約中", "paused" => "再開待ち", "complete" => "完了", "failed" => "失敗", _ => "キャンセル済み" };
    public string Detail { get => _detail; internal set => Set(ref _detail, value); }
    public string Metrics { get => _metrics; internal set => Set(ref _metrics, value); }
    public bool CanCancel => State is "active" or "queued" or "paused" or "failed";
    public bool CanResume => State is "paused" or "failed";
    public bool IsPending => CanCancel || State == "failed";
}
