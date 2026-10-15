using Eluna.App.Services;
using Eluna.App.ViewModels;

namespace Eluna.App.Models;

public sealed record MediaRequest(string Kind, string Prompt, string Instruction, Guid? ProjectId, string OutputPath, ArtifactContext? Context = null, ImageGenerationOptions? Image = null);
public sealed record SavedMediaJob(Guid Id, string Title, DateTimeOffset CreatedAt, string State, MediaRequest Request, string Text = "", List<KnowledgeReference>? Knowledge = null, List<AppliedSkill>? Skills = null);
public sealed class MediaJob : ObservableObject
{
    private string _state = "queued", _detail = "実行枠を待っています", _text = "";
    internal Func<CancellationToken, Task>? Operation { get; }
    internal CancellationTokenSource Cancellation { get; set; }
    internal TaskCompletionSource Completion { get; set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Task Finished => Completion.Task;
    public Guid Id { get; init; } = Guid.NewGuid();
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public string Title { get; }
    public MediaRequest? Request { get; }
    internal bool IsCommitted { get; set; }
    public string Text { get => _text; internal set => Set(ref _text, value); }
    public List<KnowledgeReference> Knowledge { get; internal set; } = [];
    public List<AppliedSkill> Skills { get; internal set; } = [];
    public string State { get => _state; internal set { Set(ref _state, value); Notify(nameof(IsPending)); Notify(nameof(CanResume)); } }
    public string Detail { get => _detail; internal set => Set(ref _detail, value); }
    public bool IsPending => State is "queued" or "active";
    public bool CanResume => Request is not null && State is "paused" or "failed";
    public MediaJob(string title, Func<CancellationToken, Task> operation, CancellationToken token) { Title = title; Operation = operation; Cancellation = CancellationTokenSource.CreateLinkedTokenSource(token); }
    public MediaJob(string title, MediaRequest request) { Title = title; Request = request; Cancellation = new(); }
    public void Cancel() { if (IsPending) { Cancellation.Cancel(); } else if (CanResume) { State = "canceled"; Detail = "取り消しました"; } }
    internal SavedMediaJob Save() => new(Id, Title, CreatedAt, State, Request!, Text, Knowledge, Skills);
}
