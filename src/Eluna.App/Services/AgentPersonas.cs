using System.IO;
using Eluna.App.Models;

namespace Eluna.App.Services;

internal static class AgentPersonas
{
    internal sealed record Preset(string Id, string Name, string Instruction);
    internal static IReadOnlyList<Preset> All { get; } = [
        new("default", "標準", ""),
        new("friendly", "親しみやすい相棒", "温かく気さくに話し、相手の考えを聞きながら具体的に手伝う。"),
        new("analyst", "冷静な分析家", "落ち着いた口調で前提と根拠を整理し、結論を明確にする。"),
        new("critic", "率直な批評家", "礼儀を保ちながら弱点・反例を率直に指摘し、改善案も示す。"),
        new("teacher", "面倒見のよい先生", "相手の理解に合わせ、身近な例と段階的な説明で導く。"),
        new("creative", "発想豊かなクリエイター", "好奇心を持って多様な案を考え、遊び心と具体性を両立する。"),
        new("roleplay", "キャラクターを演じる", "利用者が指定した人物像・世界観に沿って、創作として役を演じる。")];
    internal static string Instruction(AgentAccount account)
    {
        AgentPersonality p = account.Personality ?? new();
        string preset = All.FirstOrDefault(a => a.Id == p.Preset)?.Instruction ?? "";
        return account.Instruction + (preset.Length == 0 && p.Role.Length == 0 && p.Setting.Length == 0 && p.Traits.Length == 0 ? "" :
            "\n性格・ロールプレイの設定:\n" + preset + "\n役柄: " + p.Role + "\n世界観・背景: " + p.Setting + "\n口調・性格・一人称: " + p.Traits);
    }
    internal static void Validate(AgentAccount a, IReadOnlySet<Guid> ids)
    {
        if (a.Personality is { } p && (!All.Any(c => c.Id == p.Preset) || p.Role is null || p.Role.Length > 500 || p.Setting is null || p.Setting.Length > 2000 || p.Traits is null || p.Traits.Length > 2000) ||
            a.Connections is { } contacts && (contacts.Count > 100 || contacts.Distinct().Count() != contacts.Count || contacts.Any(id => id == a.Id || !ids.Contains(id))))
        { throw new InvalidDataException("性格・ロールプレイまたはつながりの設定を確認してください。"); }
    }
}
