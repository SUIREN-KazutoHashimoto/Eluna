using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using Eluna.App.Models;

namespace Eluna.App.Services;

internal sealed class IncomingMessages : IDisposable
{
    private readonly ObservableCollection<Conversation> _rooms;
    private readonly Action<Conversation, Guid, Guid, string> _arrived;
    private readonly Dictionary<Conversation, RoomWatch> _watches = [];
    internal IncomingMessages(ObservableCollection<Conversation> rooms, Action<Conversation, Guid, Guid, string> arrived)
    { _rooms = rooms; _arrived = arrived; foreach (Conversation room in rooms) { Add(room, true); } rooms.CollectionChanged += RoomsChanged; }
    private void Add(Conversation room, bool seed) { if (!_watches.ContainsKey(room)) { _watches.Add(room, new(room, _arrived, seed)); } }
    private void RoomsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Move) { return; }
        if (e.Action == NotifyCollectionChangedAction.Reset) { var next = _rooms.ToHashSet(); foreach (Conversation old in _watches.Keys.Where(r => !next.Contains(r)).ToArray()) { _watches[old].Dispose(); _watches.Remove(old); } foreach (Conversation room in next) { Add(room, false); } }
        else { if (e.OldItems is not null) { foreach (Conversation old in e.OldItems) { _watches[old].Dispose(); _watches.Remove(old); } } if (e.NewItems is not null) { foreach (Conversation room in e.NewItems) { Add(room, false); } } }
    }
    public void Dispose() { _rooms.CollectionChanged -= RoomsChanged; foreach (RoomWatch watch in _watches.Values) { watch.Dispose(); } _watches.Clear(); }
    private sealed class RoomWatch : IDisposable
    {
        private readonly Conversation _room;
        private readonly Action<Conversation, Guid, Guid, string> _arrived;
        private readonly Dictionary<ConversationBranch, BranchWatch> _branches = [];
        internal RoomWatch(Conversation room, Action<Conversation, Guid, Guid, string> arrived, bool seed)
        { _room = room; _arrived = arrived; foreach (ConversationBranch branch in room.Branches) { _branches.Add(branch, new(room, branch, arrived, seed)); } room.Branches.CollectionChanged += Changed; }
        private void Changed(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action == NotifyCollectionChangedAction.Move) { return; }
            foreach (ConversationBranch old in _branches.Keys.Where(b => !_room.Branches.Contains(b)).ToArray()) { _branches[old].Dispose(); _branches.Remove(old); }
            foreach (ConversationBranch branch in _room.Branches) { if (!_branches.ContainsKey(branch)) { _branches.Add(branch, new(_room, branch, _arrived, true)); } }
        }
        public void Dispose() { _room.Branches.CollectionChanged -= Changed; foreach (BranchWatch watch in _branches.Values) { watch.Dispose(); } }
    }
    private sealed class BranchWatch : IDisposable
    {
        private readonly Conversation _room;
        private readonly ConversationBranch _branch;
        private readonly Action<Conversation, Guid, Guid, string> _arrived;
        private readonly Dictionary<ChatMessage, MessageWatch> _messages = [];
        private readonly HashSet<Guid> _known = [];
        internal BranchWatch(Conversation room, ConversationBranch branch, Action<Conversation, Guid, Guid, string> arrived, bool seed)
        { _room = room; _branch = branch; _arrived = arrived; foreach (ChatMessage m in branch.Messages) { Add(m, seed); } branch.Messages.CollectionChanged += Changed; }
        private void Add(ChatMessage m, bool seed)
        { if (!_messages.ContainsKey(m)) { _messages.Add(m, new(m, (id, text, initial) => { if (_known.Add(id) && !initial) { _arrived(_room, _branch.Id, id, text); } }, seed)); } }
        private void Changed(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action == NotifyCollectionChangedAction.Reset) { var next = _branch.Messages.ToHashSet(); foreach (ChatMessage old in _messages.Keys.Where(m => !next.Contains(m)).ToArray()) { _messages[old].Dispose(); _messages.Remove(old); } foreach (ChatMessage m in next) { Add(m, false); } }
            else { if (e.OldItems is not null) { foreach (ChatMessage old in e.OldItems) { if (_messages.Remove(old, out MessageWatch? watch)) { watch.Dispose(); } } } if (e.NewItems is not null) { foreach (ChatMessage m in e.NewItems) { Add(m, false); } } }
        }
        public void Dispose() { _branch.Messages.CollectionChanged -= Changed; foreach (MessageWatch watch in _messages.Values) { watch.Dispose(); } }
    }
    private sealed class MessageWatch : IDisposable
    {
        private readonly ChatMessage _message;
        private readonly Action<Guid, string, bool> _completed;
        private WorkSession? _work;
        internal MessageWatch(ChatMessage message, Action<Guid, string, bool> completed, bool seed)
        { _message = message; _completed = completed; message.PropertyChanged += Changed; BindWork(); Observe(seed); }
        private void BindWork() { if (_work is not null) { _work.Turns.CollectionChanged -= TurnsChanged; } _work = _message.Work; if (_work is not null) { _work.Turns.CollectionChanged += TurnsChanged; } }
        private void Changed(object? sender, PropertyChangedEventArgs e)
        { if (e.PropertyName == nameof(ChatMessage.Work)) { BindWork(); Observe(false); } else if (e.PropertyName is nameof(ChatMessage.Content) or nameof(ChatMessage.IsGenerating) or nameof(ChatMessage.Identity) or nameof(ChatMessage.Artifact)) { ObserveMessage(false); } }
        private void ObserveMessage(bool seed) { if ((_message.Role == "assistant" || _message.Identity?.AgentId is Guid actor && actor != AgentAccount.LocalId) && MessageLocations.Finished(_message) && _message.Content.Length > 0) { _completed(_message.Id, _message.Content, seed); } }
        private void Observe(bool seed) { ObserveMessage(seed); if (_work?.Mode == "debate") { foreach (DebateTurn turn in _work.Turns.Where(t => !t.Incomplete && (t.Kind != "user" || t.AgentId is Guid actor && actor != AgentAccount.LocalId) && t.Content.Length > 0)) { _completed(turn.Id ?? WorkspaceService.StableId(_work.Id, turn.Round + ":" + turn.Role), turn.Content, seed); } } }
        private void TurnsChanged(object? sender, NotifyCollectionChangedEventArgs e) => Observe(false);
        public void Dispose() { _message.PropertyChanged -= Changed; if (_work is not null) { _work.Turns.CollectionChanged -= TurnsChanged; } }
    }
}
