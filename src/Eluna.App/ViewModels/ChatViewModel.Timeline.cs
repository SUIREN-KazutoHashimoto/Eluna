using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using Eluna.App.Models;
using Eluna.App.Services;

namespace Eluna.App.ViewModels;

public sealed partial class ChatViewModel
{
    public ObservableCollection<ChatMessage> Timeline { get; } = [];
    private ConversationBranch? _timelineBranch;
    private readonly HashSet<WorkSession> _timelineWorks = [];
    private readonly HashSet<ChatMessage> _timelineMessages = [];
    private readonly Dictionary<Guid, ChatMessage> _turnRows = [];
    private void BindTimeline()
    {
        if (_timelineBranch is not null) { _timelineBranch.Messages.CollectionChanged -= TimelineChanged; }
        foreach (WorkSession work in _timelineWorks) { work.Turns.CollectionChanged -= TimelineChanged; work.PropertyChanged -= TimelineWorkChanged; }
        foreach (ChatMessage message in _timelineMessages) { message.PropertyChanged -= TimelineMessageChanged; }
        _timelineWorks.Clear(); _timelineMessages.Clear(); _turnRows.Clear();
        _timelineBranch = SelectedConversation?.ActiveBranch;
        if (_timelineBranch is not null) { _timelineBranch.Messages.CollectionChanged += TimelineChanged; }
        RefreshTimeline();
    }
    private void TimelineMessageChanged(object? sender, PropertyChangedEventArgs e) { if (e.PropertyName is nameof(ChatMessage.Work) or nameof(ChatMessage.Pending)) { RefreshTimeline(); } else if (e.PropertyName == nameof(ChatMessage.Identity)) { TimelinePresentation.Apply(Timeline); } }
    private void TimelineWorkChanged(object? sender, PropertyChangedEventArgs e) { if (e.PropertyName is nameof(WorkSession.Stage) or nameof(WorkSession.IsRunning)) { RefreshTimeline(); Notify(nameof(TypingNotice)); } }
    private void TimelineChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        Notify(nameof(TypingNotice));
        if (e.Action == NotifyCollectionChangedAction.Replace && e.NewItems is { Count: 1 } && e.NewItems[0] is DebateTurn turn &&
            _timelineWorks.FirstOrDefault(w => ReferenceEquals(w.Turns, sender)) is { } work &&
            _turnRows.TryGetValue(turn.Id ?? WorkspaceService.StableId(work.Id, turn.Round + ":" + turn.Role), out ChatMessage? row))
        { UpdateTurnRow(row, turn, work); if (!turn.Incomplete && !Timeline.Contains(row)) { RefreshTimeline(); } return; }
        RefreshTimeline();
    }
    private static void UpdateTurnRow(ChatMessage row, DebateTurn turn, WorkSession work)
    {
        row.Content = turn.Content; row.Knowledge = turn.Knowledge ?? [];
        if (turn.ModelName is not null && row.Identity?.ModelName != turn.ModelName) { row.Identity = row.Identity! with { ModelName = turn.ModelName }; }
        row.IsGenerating = turn.Incomplete && work.IsRunning;
        row.Notice = turn.Incomplete && !work.IsRunning ? "途中で中断した発言です" : "";
    }
    private void RefreshTimeline()
    {
        int previousCount = Timeline.Count;
        var next = new List<ChatMessage>(); var seen = new HashSet<Guid>();
        foreach (ChatMessage message in _timelineBranch?.Messages ?? [])
        {
            if (_timelineMessages.Add(message)) { message.PropertyChanged += TimelineMessageChanged; }
            if (message.Work is { Mode: "debate" } work)
            {
                if (_timelineWorks.Add(work)) { work.Turns.CollectionChanged += TimelineChanged; work.PropertyChanged += TimelineWorkChanged; }
                foreach (DebateTurn turn in work.Turns)
                {
                    Guid id = turn.Id ?? WorkspaceService.StableId(work.Id, turn.Round + ":" + turn.Role);
                    if (!_turnRows.TryGetValue(id, out ChatMessage? row))
                    {
                        row = new()
                        {
                            Id = id,
                            Role = turn.Kind == "user" ? "user" : "assistant",
                            IsProjection = true,
                            DiagnosticsId = message.DiagnosticsId,
                            Identity = new(turn.AgentId, turn.Name ?? turn.Role, turn.Icon, turn.ModelId,
                                turn.ModelName ?? (turn.ModelId is null ? null : Models.FirstOrDefault(m => m.Id == turn.ModelId)?.DisplayName ?? turn.ModelId),
                                turn.Time, turn.ReplyTo, work.Id, work.Options.DebateStyle == "rounds" ? "ラウンド " + turn.Round : turn.Kind switch { "question" => "質問", "rebuttal" => "反論", "supplement" => "補足", "hold" => "保留", "search" => "追加調査", "user" => null, _ => "発言" })
                        };
                        _turnRows.Add(id, row);
                    }
                    UpdateTurnRow(row, turn, work);
                    if (!turn.Incomplete && seen.Add(id)) { next.Add(row); }
                }
            }
            if (!message.Pending && seen.Add(message.Id)) { next.Add(message); }
        }
        Dictionary<Guid, ChatMessage> byId = next.DistinctBy(m => m.Id).ToDictionary(m => m.Id);
        foreach (ChatMessage row in next)
        { if (row.Identity?.ReplyTo is Guid reply && byId.TryGetValue(reply, out ChatMessage? original)) { row.ReplyPreview = "↩ " + original.Speaker + ": " + original.Content[..Math.Min(90, original.Content.Length)].Replace('\n', ' '); } }
        // Reconcile by identity: streaming must keep existing text selection and reading position.
        for (int i = 0; i < next.Count; i++)
        {
            if (i < Timeline.Count && ReferenceEquals(Timeline[i], next[i])) { continue; }
            int old = Timeline.IndexOf(next[i]);
            if (old >= 0) { Timeline.Move(old, i); } else { Timeline.Insert(i, next[i]); }
        }
        while (Timeline.Count > next.Count) { Timeline.RemoveAt(Timeline.Count - 1); }
        TimelinePresentation.Apply(Timeline); ApplyTimelineExperience(); ExperienceChanged();
        Notify(nameof(ReplyLabel)); Notify(nameof(IsEmpty));
        if (Timeline.Count > previousCount) { ScrollRequested?.Invoke(false); }
    }
}
