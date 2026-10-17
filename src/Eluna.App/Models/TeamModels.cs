namespace Eluna.App.Models;

public sealed record TeamAssignment(Guid RoomId, Guid BranchId, Guid AgentId, string Task, Guid AnswerId, bool Complete = false);
public sealed record TeamRun(Guid Id, Guid CoordinatorId, Guid? ProjectId, List<TeamAssignment> Assignments,
    string Stage = "running", string Detail = "作業場所を準備しました", Guid? ActiveRoomId = null, Guid? ActiveAgentId = null, int TokensUsed = 0, double SecondsUsed = 0)
{
    public string Label => (Stage switch { "complete" => "完了", "running" => "チームで作業中", "report" => "報告を作成中", "limited" => "上限に到達", _ => "中断・再開待ち" }) + $" · {Assignments.Count(a => a.Complete)} / {Assignments.Count}担当";
}
