using System.IO;
using System.Text.Json;
using Eluna.App.Models;

namespace Eluna.App.Services;

internal sealed record TeamPlan(bool Organize, bool NewServer, string Server, List<PlannedChannel> Channels);
internal sealed record PlannedChannel(string Name, List<PlannedAssignment> Assignments);
internal sealed record PlannedAssignment(Guid Agent, string Task);
internal static class TeamPlanner
{
    internal const string Instruction = "Decide whether the user's task benefits from a separate team workspace and delegation. For simple greetings/questions or roleplay, organize=false. For an explicit request to create a server/channel/team, or complex work with independent deliverables, organize=true. Return compact JSON only. Use 1-3 channels, 1-6 total assignments, at least one invited connection when useful. Respect the explicitly requested number of channels; do not add a reporting channel. Only use supplied account IDs; never invent people, data or already completed work. Each task is ONE short sentence under 100 characters, self-contained using only the user's request, without claiming access to source history/files. Use the user's language. newServer=true only when needed and allowed. These are local workspace changes, not external network invitations. Preserve explicit web search requirements in every relevant task.";
    internal static object Schema(IReadOnlyList<AgentAccount> agents) => new
    {
        type = "object",
        additionalProperties = false,
        properties = new
        {
            organize = new { type = "boolean" },
            newServer = new { type = "boolean" },
            server = new { type = "string", maxLength = 80 },
            channels = new { type = "array", maxItems = 3, items = new { type = "object", additionalProperties = false, properties = new { name = new { type = "string", minLength = 1, maxLength = 80 }, assignments = new { type = "array", minItems = 1, maxItems = 6, items = new { type = "object", additionalProperties = false, properties = new { agent = new { type = "string", @enum = agents.Select(a => a.Id.ToString()).ToArray() }, task = new { type = "string", minLength = 1, maxLength = 2000 } }, required = new[] { "agent", "task" } } } }, required = new[] { "name", "assignments" } } }
        },
        required = new[] { "organize", "newServer", "server", "channels" }
    };
    internal static TeamPlan Parse(string json, IReadOnlySet<Guid> allowed, bool servers)
    {
        TeamPlan plan = JsonSerializer.Deserialize<TeamPlan>(json, AppStore.JsonOptions) ?? throw new InvalidDataException("作業計画を読み取れません。");
        if (!plan.Organize) { return plan; }
        if (plan.NewServer && !servers || plan.Server is null || plan.Server.Length > 80 || plan.NewServer && string.IsNullOrWhiteSpace(plan.Server) || plan.Channels is null || plan.Channels.Count is < 1 or > 3 || plan.Channels.Any(c => c is null || string.IsNullOrWhiteSpace(c.Name) || c.Name.Length > 80 || c.Assignments is null || c.Assignments.Count == 0 || c.Assignments.Any(a => a is null || !allowed.Contains(a.Agent) || string.IsNullOrWhiteSpace(a.Task) || a.Task.Length > 2000)) || plan.Channels.Sum(c => c.Assignments.Count) > 6)
        { throw new InvalidDataException("作業計画の作成数・担当・許可範囲を確認できません。自動編成を中止しました。"); }
        return plan;
    }
    internal static List<AgentAccount> Candidates(AppState state, SavedConversation source, AgentAccount coordinator, IReadOnlySet<string> models, string fallback)
    {
        bool invite = coordinator.Autonomy?.InviteConnections == true && ChannelAccess.CanInvite(state, source, coordinator.Id);
        return (state.Agents ?? []).Where(a => a.Enabled && models.Contains(a.ModelId ?? fallback) && (a.Id == coordinator.Id || invite && coordinator.Connections?.Contains(a.Id) == true && a.Autonomy?.AcceptInvites != false)).Take(6).ToList();
    }
    internal static void Validate(TeamRun run, IReadOnlySet<Guid> agents)
    {
        if (run.Id == Guid.Empty || !agents.Contains(run.CoordinatorId) || run.Stage is not ("running" or "report" or "complete" or "paused" or "limited") || run.Detail is null || run.Detail.Length > 4000 || run.Assignments is null || run.Assignments.Count is < 1 or > 6 || run.Assignments.Any(a => a is null || a.RoomId == Guid.Empty || a.BranchId == Guid.Empty || a.AnswerId == Guid.Empty || !agents.Contains(a.AgentId) || string.IsNullOrWhiteSpace(a.Task) || a.Task.Length > 2000) || run.Assignments.Select(a => a.RoomId).Distinct().Count() > 3 || run.Assignments.Select(a => a.AnswerId).Distinct().Count() != run.Assignments.Count || run.TokensUsed < 0 || !double.IsFinite(run.SecondsUsed) || run.SecondsUsed < 0)
        { throw new InvalidDataException("チーム作業の記録が不正です。"); }
    }
}
