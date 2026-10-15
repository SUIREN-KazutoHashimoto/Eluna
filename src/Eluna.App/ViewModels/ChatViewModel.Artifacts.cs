using Eluna.App.Models;
using Eluna.App.Services;

namespace Eluna.App.ViewModels;

public sealed record ArtifactContext(AppState State, SavedConversation Conversation, LocalModel Model, AppSettings Settings, SkillRequest Skills, string? FrozenInstruction = null, List<KnowledgeReference>? FrozenReferences = null);

public sealed partial class ChatViewModel
{
    internal ArtifactContext CaptureArtifactContext(string prompt)
    {
        LocalModel model = SelectedModel ?? throw new InvalidOperationException("左上から文章生成用のモデルを選んでください。");
        Conversation conversation = SelectedConversation ?? throw new InvalidOperationException("会話を選択してください。");
        AppState state = CaptureState();
        state = state with { Conversations = state.Conversations.Where(c => c.ProjectId == conversation.ProjectId).ToList(), Projects = state.Projects?.Where(p => p.Id == conversation.ProjectId).ToList(), InferenceJobs = null, Skills = state.Skills?.Where(s => s.ProjectId is null || s.ProjectId == conversation.ProjectId).ToList(), CommonMemories = conversation.ProjectId is not null && SelectedProject?.MemoryMode == "project-only" ? [] : state.CommonMemories };
        return new(state, conversation.Save(), model, EffectiveSettings, SkillRunner.Capture(Skills, conversation.ProjectId, SelectedProject?.UseCommonSkills == true, SkillMode, conversation.SkillIds, prompt));
    }
    internal void ReportMediaProgress(string text)
    { if (_mediaContext.Value is { } media) { media.Job.Detail = text; } else { Status = text; } }
}
