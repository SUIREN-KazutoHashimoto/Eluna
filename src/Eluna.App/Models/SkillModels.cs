using System.Text.Json.Serialization;

namespace Eluna.App.Models;

public sealed record SkillRegistration(Guid Id, Guid? ProjectId, string Name, string Description, string Revision,
    bool Enabled = true, string ExecutionPolicy = "ask", string License = "", string Compatibility = "")
{
    [JsonIgnore] public string Label => (Enabled ? "● " : "○ ") + Name;
    [JsonIgnore] public string Scope => ProjectId is null ? "共通" : "プロジェクト限定";
}
public sealed record SkillFile(string Path, long Size, string Sha256);
public sealed record SkillManifest(string Name, string Description, string License, string Compatibility, List<SkillFile> Files);
public sealed record SkillPackage(string Name, string Description, string Instructions, string Markdown, string License, string Compatibility, Dictionary<string, byte[]> Files);
public sealed record SkillRequest(string Mode, List<SkillRegistration> Catalog, List<Guid> Forced, bool IncludeCommon = false);
public sealed record SkillResourceUse(string Path, string Text);
public sealed record SkillExecution(string Path, string Input, string Status, string Output, List<string> Artifacts);
public sealed record AppliedSkill(Guid Id, string Name, string Revision, string Reason, string Instructions,
    List<SkillResourceUse> Resources, List<SkillExecution> Executions);
public sealed record SkillExecutionRequest(string SkillName, string ScriptPath, string Script, string Input, string OutputDirectory, string ConversationTitle = "", string ProjectLabel = "", string ModelLabel = "", string Revision = "", string JobId = "");
