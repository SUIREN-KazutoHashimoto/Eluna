using Eluna.App.Models;
using Eluna.App.Services;

namespace Eluna.App.ViewModels;

public sealed record ArtifactContext(AppState State, SavedConversation Conversation, LocalModel Model, AppSettings Settings, SkillRequest Skills, string? FrozenInstruction = null, List<KnowledgeReference>? FrozenReferences = null, Guid? AgentId = null, string? AccessStamp = null);

public sealed partial class ChatViewModel
{
    internal ArtifactContext CaptureArtifactContext(string prompt)
    {
        LocalModel model = SelectedModel ?? throw new InvalidOperationException("左上から文章生成用のモデルを選んでください。");
        Conversation conversation = SelectedConversation ?? throw new InvalidOperationException("会話を選択してください。");
        AppState state = CaptureState();
        ChannelAccess.RequireSpeaker(state, conversation.Save(), _recipient ?? conversation.Room?.DefaultAgentId);
        state = state with { InferenceJobs = null };
        return new(state, conversation.Save(), model, EffectiveSettings, SkillRunner.Capture(Skills, conversation.ProjectId, SelectedProject?.UseCommonSkills == true, SkillMode, conversation.SkillIds, prompt), AgentId: _recipient ?? conversation.Room?.DefaultAgentId);
    }
    internal void ReportMediaProgress(string text)
    { if (_mediaContext.Value is { } media) { media.Job.Detail = text; } else { Status = text; } }
}
