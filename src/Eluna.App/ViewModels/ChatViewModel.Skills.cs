using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using Eluna.App.Models;
using Eluna.App.Services;

namespace Eluna.App.ViewModels;

public sealed partial class ChatViewModel
{
    public ObservableCollection<SkillRegistration> Skills { get; } = [];
    public SkillStore SkillFiles => new(_store);
    public event Func<SkillExecutionRequest, CancellationToken, Task<bool>>? SkillExecutionReviewRequested;
    internal IReadOnlyList<AppliedSkill> LastArtifactSkills { get; private set; } = [];
    internal Action<string>? SkillTrace { get; set; }
    public string SkillMode
    {
        get => SelectedConversation?.SkillsMode ?? _settings.SkillMode;
        set { if (value is "auto" or "manual" or "off") { if (SelectedConversation is Conversation conversation) { conversation.SkillsMode = value; } else { _settings = _settings with { SkillMode = value }; } NotifySkills(); _ = PersistSafelyAsync(); } }
    }
    public string SkillsLabel => SkillMode == "off" ? "スキル · オフ" : SelectedConversation?.SkillIds.Count > 0 ? $"スキル · 指定 {SelectedConversation.SkillIds.Count}" : SkillMode == "auto" ? "スキル · 自動" : "スキル · 指定のみ";
    public List<SkillRegistration> AvailableSkills => SkillRunner.Available(Skills, SelectedConversation?.ProjectId ?? SelectedProject?.Id, SelectedProject?.UseCommonSkills == true);
    private void NotifySkills() { Notify(nameof(SkillsLabel)); Notify(nameof(SkillMode)); Notify(nameof(AvailableSkills)); Notify(nameof(NetworkNotice)); }
    public Task SetConversationSkillsAsync(IEnumerable<Guid> ids)
    {
        if (SelectedConversation is not Conversation conversation) { return Task.CompletedTask; }
        return SetConversationSkillsAsync(conversation.Id, conversation.ProjectId, ids, null);
    }
    internal async Task SetConversationSkillsAsync(Guid conversationId, Guid? expectedProjectId, IEnumerable<Guid> ids, string? mode)
    {
        Conversation conversation = Conversations.FirstOrDefault(c => c.Id == conversationId) ?? throw new InvalidDataException("対象の会話が見つかりません。");
        if (conversation.ProjectId != expectedProjectId) { throw new InvalidDataException("会話の所属が変更されています。対象の会話から開き直してください。"); }
        List<Guid> selected = ids.Distinct().ToList();
        List<SkillRegistration> available = SkillRunner.Available(Skills, conversation.ProjectId, Projects.FirstOrDefault(p => p.Id == conversation.ProjectId)?.UseCommonSkills == true);
        if (selected.Count > 3 || selected.Any(id => !available.Any(s => s.Id == id))) { throw new InvalidDataException("この範囲で有効なスキルを3件まで選んでください。"); }
        conversation.SkillIds = selected; if (mode is "auto" or "manual" or "off") { conversation.SkillsMode = mode; }
        NotifySkills(); await PersistSafelyAsync();
    }
    public async Task SaveSkillAsync(SkillRegistration skill)
    {
        AppState state = CaptureState();
        List<SkillRegistration> next = Skills.Where(s => s.Id != skill.Id).Append(skill).ToList();
        SkillValidation.State(state with { Skills = next });
        int index = Skills.ToList().FindIndex(s => s.Id == skill.Id);
        if (index < 0) { Skills.Add(skill); } else { Skills[index] = skill; }
        if (!skill.Enabled) { foreach (Conversation conversation in Conversations) { conversation.SkillIds.Remove(skill.Id); } }
        NotifySkills(); await _store.SaveAsync(CaptureState());
    }
    public async Task RemoveSkillAsync(SkillRegistration skill)
    {
        Skills.Remove(skill);
        foreach (Conversation conversation in Conversations) { conversation.SkillIds.Remove(skill.Id); }
        NotifySkills(); await _store.SaveAsync(CaptureState());
    }
    public async Task SetCommonSkillsAsync(Guid projectId, bool include)
    {
        int index = Projects.ToList().FindIndex(p => p.Id == projectId);
        if (index < 0) { throw new InvalidDataException("プロジェクトが見つかりません。"); }
        Projects[index] = Projects[index] with { UseCommonSkills = include };
        NotifyProjectScope(); NotifySkills(); await _store.SaveAsync(CaptureState());
    }
    private async Task PrepareSkillsAsync(ChatMessage answer, InferenceJob job, LlamaService engine, string question, CancellationToken token)
    {
        if (job.Request.Skills is null || job.Request.Skills.Catalog.Count == 0 || job.Request.Skills.Mode == "off") { answer.Skills = []; return; }
        async Task<string> Infer(string input, string instruction, string schema, CancellationToken cancellation)
        {
            await engine.EnsureModelAsync(job.Request.Model, job.Request.Settings, message => job.Detail = message, cancellation);
            int count = await engine.CountInputTokensAsync([new("user", input)], instruction, cancellation);
            if (count > job.Request.Settings.ContextSize - 700) { throw new InvalidOperationException("スキルの入力枠が足りません。モデルの記憶長を増やすか、スキルを短くしてください。"); }
            var text = new StringBuilder();
            using System.Text.Json.JsonDocument structured = System.Text.Json.JsonDocument.Parse(schema);
            await foreach (GenerationUpdate piece in engine.GenerateAsync([new("user", input)], InferenceModes.Find("fast"), cancellation, instruction, 512, structured.RootElement)) { text.Append(piece.Content); }
            return text.ToString();
        }
        DiagnosticScope.Current?.Write("debug", "skills", "prepare.started", "mode=" + job.Request.Skills.Mode + "; catalog=" + job.Request.Skills.Catalog.Count);
        answer.Skills = await new SkillRunner(_store, s => Skills.FirstOrDefault(current => current.Id == s.Id && current.Enabled)?.ExecutionPolicy ?? "never", text => { SkillTrace?.Invoke(text); DiagnosticScope.Current?.Content("skills", "protocol", text); }).PrepareAsync(job.Request.Skills, question, Infer, SkillExecutionReviewRequested is null ? null : (request, ct) => SkillExecutionReviewRequested(request with { JobId = job.Request.Id.ToString("N")[..8], ConversationTitle = job.Request.Title, ProjectLabel = Projects.FirstOrDefault(p => p.Id == job.Request.Settings.SelectedProjectId)?.Name ?? "通常チャット", ModelLabel = job.Request.Model.DisplayName, Revision = job.Request.Skills.Catalog.FirstOrDefault(s => s.Name == request.SkillName)?.Revision ?? "" }, ct),
            job.Request.Settings.WorkMode == "offline", message => job.Detail = message, uses => answer.Skills = uses, token);
        DiagnosticScope.Current?.Write("debug", "skills", "prepare.completed", "applied=" + answer.Skills.Count);
        await PersistSafelyAsync();
    }
}
