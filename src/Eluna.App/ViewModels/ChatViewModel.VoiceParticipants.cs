using Eluna.App.Models;
using Eluna.App.Services;

namespace Eluna.App.ViewModels;

public sealed record VoiceParticipant(Guid Id, string Name, string Icon, string Status, bool Speaking = false);

public sealed partial class ChatViewModel
{
    private readonly HashSet<Guid> _voiceParticipants = [];
    private Guid? _speakingAgent;
    internal long VoiceSession => _voiceEpoch;
    public Guid? VoiceRoomId => _voiceRoom?.Id;
    public IReadOnlyList<AgentAccount> EligibleVoiceMembers
    {
        get
        {
            if (_voiceRoom is not { } room) { return []; }
            AppState state = CaptureState(); SavedConversation source = room.Save();
            return Agents.Where(a => a.Enabled && room.Room?.Members.Contains(a.Id) == true && ChannelAccess.CanSpeak(state, source, a.Id)).ToArray();
        }
    }
    public IReadOnlyList<VoiceParticipant> VoiceParticipants
    {
        get
        {
            if (!VoiceJoined) { return []; }
            var people = new List<VoiceParticipant> { new(AgentAccount.LocalId, Self.Name, Self.Icon, (_voiceMuted ? "マイクをミュート" : "マイク有効") + (_voiceDeafened ? " · 音声出力オフ" : "")) };
            foreach (AgentAccount account in EligibleVoiceMembers.Where(a => _voiceParticipants.Contains(a.Id)))
            {
                bool speaking = _voiceSpeaking && _speakingAgent == account.Id;
                bool thinking = Inferences.Items.Any(j => j.State == "active" && j.Request.ConversationId == _voiceRoom!.Id && j.Request.Agent?.Id == account.Id);
                people.Add(new(account.Id, account.Name, account.Icon, speaking ? "発話中" : thinking ? "考え中" : "音声応答に参加", speaking));
            }
            return people;
        }
    }
    internal async Task SetVoiceParticipantsAsync(Guid room, long session, IReadOnlyList<Guid> members)
    {
        if (_voiceRoom?.Id != room || _voiceEpoch != session || !VoiceValid) { throw new InvalidOperationException("ボイスチャンネルの接続が変わりました。画面を開き直してください。"); }
        if (members.Count > AccountDefaults.MaximumAgents || members.Distinct().Count() != members.Count || members.Any(id => !EligibleVoiceMembers.Any(a => a.Id == id)))
        { throw new InvalidOperationException("このチャンネルの閲覧・発言が許可されているアカウントを選んでください。"); }
        Guid[] added = members.Except(_voiceParticipants).ToArray();
        if (_speakingAgent is Guid speaking && !members.Contains(speaking) && _voiceAudio is { } audio) { await audio.InterruptAsync(); }
        if (_voiceRoom?.Id != room || _voiceEpoch != session) { return; }
        foreach (InferenceJob past in Inferences.Items.Where(j => j.State == "complete" && j.Request.ConversationId == room && added.Contains(j.Request.Agent?.Id ?? Guid.Empty))) { _voiceRead.Add(past.Request.AnswerId); }
        _voiceParticipants.Clear(); foreach (Guid id in members) { _voiceParticipants.Add(id); }
        NotifyVoice();
    }
}
