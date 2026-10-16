namespace Eluna.App.Models;

public sealed record AgentAccount(Guid Id, string Name, string Icon = "✦", string Instruction = "", string Specialty = "",
    string? ModelId = null, bool Enabled = true)
{
    public string Label => Icon + " " + Name + (Enabled ? "" : "（無効）");
}
public sealed record AgentMemory(Guid Id, Guid AgentId, string Text, string Scope = "room", Guid? SourceConversationId = null,
    Guid? ProjectId = null, bool Enabled = true, DateTimeOffset? UpdatedAt = null);
public sealed record RoomProfile(string Kind, List<Guid> Members, Guid? DefaultAgentId = null, Guid? ParentId = null,
    Guid? OriginMessageId = null, bool ReferenceSiblingChats = false, bool ShareHistory = false, bool AcceptAgentMemory = false,
    List<Guid>? PastMembers = null, string? WorkMode = null, string? InferenceMode = null);
public sealed record MessageIdentity(Guid? AgentId, string Name, string Icon = "✦", string? ModelId = null,
    string? ModelName = null, DateTimeOffset? Time = null, Guid? ReplyTo = null, Guid? WorkId = null, string? Detail = null);
public sealed record AgentChoice(Guid? Id, string Name);
public sealed record GeneratedArtifact(Guid JobId, string Title, string Kind, string ResultPath);
