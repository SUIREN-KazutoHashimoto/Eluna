using System.IO;
using System.Text.RegularExpressions;
using Eluna.App.Models;

namespace Eluna.App.Services;

internal static class SkillValidation
{
    internal static void Registration(SkillRegistration skill)
    {
        if (skill is null || skill.Name is null || skill.Description is null || skill.Revision is null || skill.License is null || skill.Compatibility is null ||
            skill.Description.Length is < 1 or > 1024 || skill.Compatibility.Length > 500 || skill.License.Length > 4000 ||
            !Regex.IsMatch(skill.Revision, "^[a-f0-9]{64}$") || skill.ExecutionPolicy is not ("never" or "ask" or "trusted"))
        { throw new InvalidDataException("スキルの登録情報が不正です。"); }
        SkillFormat.ValidateName(skill.Name);
    }
    internal static bool Uses(List<AppliedSkill>? skills) => skills is null || skills.Count <= 3 && skills.All(s => s is not null && s.Name is { Length: <= 64 } &&
        s.Revision is { Length: 64 } && s.Reason is { Length: <= 1000 } && s.Instructions is { Length: <= 131072 } &&
        s.Resources is { Count: <= 6 } && s.Resources.All(r => r is not null && r.Path is { Length: <= 240 } && r.Text is { Length: <= 8100 }) &&
        s.Executions is { Count: <= 2 } && s.Executions.All(e => e is not null && e.Path is { Length: <= 240 } && e.Input is { Length: <= 8000 } &&
            e.Status is not null && e.Output is { Length: <= 33000 } && e.Artifacts is { Count: <= 20 } && e.Artifacts.All(a => a is { Length: <= 2048 })));
    internal static void State(AppState state)
    {
        List<SkillRegistration> skills = state.Skills ?? [];
        if (skills.Count > 200 || skills.Any(s => s is null) || skills.Select(s => s.Id).Distinct().Count() != skills.Count ||
            skills.Select(s => (s.ProjectId, s.Name)).Distinct().Count() != skills.Count || state.Settings.SkillMode is not ("auto" or "manual" or "off"))
        { throw new InvalidDataException("スキルの登録件数・名前またはモードが不正です。"); }
        foreach (SkillRegistration skill in skills)
        {
            Registration(skill);
            if (skill.ProjectId is Guid id && state.Projects?.Any(p => p.Id == id) != true) { throw new InvalidDataException("スキルのプロジェクトが見つかりません。"); }
        }
        foreach (SavedConversation conversation in state.Conversations)
        { if (conversation.SkillIds is { Count: > 3 } || conversation.SkillsMode is not (null or "auto" or "manual" or "off")) { throw new InvalidDataException("指定スキルは3件までです。適用モードも確認してください。"); } }
        foreach (SavedInferenceJob job in state.InferenceJobs ?? [])
        {
            if (job is null || job.Settings is null) { throw new InvalidDataException("推論予約が不正です。"); }
            if (job.Skills is not SkillRequest request) { continue; }
            if (request.Catalog is null || request.Catalog.Count > 200 || request.Forced is null || request.Forced.Count > 3 || request.Mode is not ("auto" or "manual" or "off") ||
                request.Catalog.Any(s => s is null || s.ProjectId != job.Settings.SelectedProjectId && !(request.IncludeCommon && s.ProjectId is null)) ||
                request.Forced.Any(id => !request.Catalog.Any(s => s.Id == id))) { throw new InvalidDataException("推論予約のスキル参照が不正です。"); }
            foreach (SkillRegistration skill in request.Catalog) { Registration(skill); }
        }
    }
}
