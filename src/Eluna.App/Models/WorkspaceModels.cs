namespace Eluna.App.Models;

public sealed record AgentAccount(Guid Id, string Name, string Icon = "✦", string Instruction = "", string Specialty = "",
    string? ModelId = null, bool Enabled = true, string? VoiceId = null, int SpeechRate = 0,
    AgentPersonality? Personality = null, List<Guid>? Connections = null, AgentAutonomy? Autonomy = null,
    AgentGeneration? Generation = null, AgentSocial? Social = null, ArtistSkill? Artist = null, bool Retired = false)
{
    public static readonly Guid LocalId = Guid.Parse("2cfb9681-cb73-4ee4-8793-43e3d20cbd36");
    public bool IsLocal => Id == LocalId;
    public string Label => Icon + " " + Name + (Enabled ? "" : "（無効）");
}
public sealed record LocalMember(string Name = "自分", string Icon = "◉", string About = "");
public sealed record AgentMemory(Guid Id, Guid AgentId, string Text, string Scope = "room", Guid? SourceConversationId = null,
    Guid? ProjectId = null, bool Enabled = true, DateTimeOffset? UpdatedAt = null);
public sealed record RoomProfile(string Kind, List<Guid> Members, Guid? DefaultAgentId = null, Guid? ParentId = null,
    Guid? OriginMessageId = null, bool ReferenceSiblingChats = false, bool ShareHistory = false, bool AcceptAgentMemory = false,
    List<Guid>? PastMembers = null, string? WorkMode = null, string? InferenceMode = null, List<ChannelGrant>? Permissions = null,
    SocialPolicy? Social = null);
public sealed record ChannelGrant(Guid AgentId, bool View, bool Speak, bool Invite = false, bool Organize = false);
public sealed record AgentPersonality(string Preset = "default", string Role = "", string Setting = "", string Traits = "");
public sealed record AgentAutonomy(bool Automatic = false, bool CreateServers = false, bool CreateChannels = false, bool InviteConnections = false, bool AcceptInvites = true);
public sealed record AgentGeneration(double Temperature = 0.8, double TopP = 0.95, int MaxOutputTokens = 2048);
public sealed record AgentSocial(string Interests = "", double Participation = 0.4, bool Initiates = true, int CooldownSeconds = 45);
public sealed record ArtistSkill(bool Enabled = false, string Style = "", bool Spontaneous = false);
public sealed record SocialPolicy(bool Enabled = false, bool Interjections = true, bool Spontaneous = false,
    int StartHour = 8, int EndHour = 23, int IntervalMinutes = 30, int MaxTurns = 8, int MaxTokens = 4096, int PaceSeconds = 5);
public sealed record MessageIdentity(Guid? AgentId, string Name, string Icon = "✦", string? ModelId = null,
    string? ModelName = null, DateTimeOffset? Time = null, Guid? ReplyTo = null, Guid? WorkId = null, string? Detail = null);
public sealed record AgentChoice(Guid? Id, string Name);
public sealed record GeneratedArtifact(Guid JobId, string Title, string Kind, string ResultPath);
