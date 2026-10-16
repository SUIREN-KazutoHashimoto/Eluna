using System.Text.Json.Serialization;

namespace Eluna.App.Models;

public sealed record MemoryNote(Guid Id, string Text, DateTimeOffset UpdatedAt)
{
    [JsonIgnore]
    public string Title
    {
        get
        {
            string text = Text.Replace('\r', ' ').Replace('\n', ' ');
            return text.Length > 55 ? text[..(char.IsHighSurrogate(text[54]) ? 54 : 55)] + "…" : text;
        }
    }
    [JsonIgnore] public string Detail => UpdatedAt.ToLocalTime().ToString("yyyy/MM/dd HH:mm", System.Globalization.CultureInfo.InvariantCulture);
}

public sealed record KnowledgeDocument(Guid Id, string Name, string Text, DateTimeOffset ImportedAt, bool Enabled = true, bool VisionExtracted = false)
{
    [JsonIgnore] public string Label => (Enabled ? "● " : "○ ") + Name;
    [JsonIgnore] public string Detail => $"{Text.Length:N0} 文字 · " + (Enabled ? "参照する" : "参照しない") + (VisionExtracted ? " · 画像からの読み取り" : "");
}

public sealed record KnowledgeProject(Guid Id, string Name, string Instruction, string MemoryMode,
    bool ReferenceChats, List<KnowledgeDocument> Documents, List<MemoryNote> Memories, bool UseCommonSkills = false, bool AcceptAgentMemory = false)
{
    [JsonIgnore] public string ScopeLabel => MemoryMode == "project-only" ? "プロジェクト限定メモリ" : "共通メモリも参照";
}

public sealed record ProjectChoice(Guid? Id, string Name)
{
    public string Icon => Id is null ? "DM" : System.Globalization.StringInfo.GetNextTextElement(Name);
}
public sealed record KnowledgeReference(int Number, string Kind, string Title, string Text, Guid? SourceConversationId = null, Guid? AgentId = null, Guid? MemoryId = null)
{
    [JsonIgnore] public string Label => $"[K{Number}] {Title}";
}
