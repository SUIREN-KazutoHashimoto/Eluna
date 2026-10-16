using System.IO;
using System.Text.Json;

namespace Eluna.App.Services;

internal sealed record CharacterDraft(string Name, string Icon, string Specialty, string Instruction, string Role, string Setting, string Traits, string Greeting);
internal static class CharacterGenerator
{
    internal const string Instruction = "Create one original fictional character for a peer group chat. Return compact JSON only, in Japanese, within 600 Japanese characters total. Give a short distinctive name, one Unicode icon, a specialty, a concise behavioral instruction, role, background setting, speaking style and one short example greeting. Every participant, including the person operating the app, is an equal member with a name. Avoid generic AI assistant, servant/master, user-vs-AI framing and claims of real experiences or tool abilities. Honor the requested mood, preserve safe practical conversation, and distinguish fictional background from facts. Do not reuse supplied existing names. If the brief is empty, invent a friendly, memorable character with a distinctive point of view. Do not grant permissions, invite people, select models or change settings.";
    internal static object Schema { get; } = new { type = "object", additionalProperties = false, properties = new { name = new { type = "string", maxLength = 80 }, icon = new { type = "string", maxLength = 16 }, specialty = new { type = "string", maxLength = 300 }, instruction = new { type = "string", maxLength = 3000 }, role = new { type = "string", maxLength = 500 }, setting = new { type = "string", maxLength = 1000 }, traits = new { type = "string", maxLength = 1000 }, greeting = new { type = "string", maxLength = 500 } }, required = new[] { "name", "icon", "specialty", "instruction", "role", "setting", "traits", "greeting" } };
    internal static CharacterDraft Parse(string json)
    {
        CharacterDraft c = JsonSerializer.Deserialize<CharacterDraft>(json, AppStore.JsonOptions) ?? throw new InvalidDataException("キャラクター案を読み取れません。");
        if (string.IsNullOrWhiteSpace(c.Name) || c.Name.Length > 80 || string.IsNullOrWhiteSpace(c.Icon) || c.Icon.Length > 16 || c.Specialty is null || c.Specialty.Length > 300 || string.IsNullOrWhiteSpace(c.Instruction) || c.Instruction.Length > 3000 || string.IsNullOrWhiteSpace(c.Role) || c.Role.Length > 500 || c.Setting is null || c.Setting.Length > 1000 || c.Traits is null || c.Traits.Length > 1000 || c.Greeting is null || c.Greeting.Length > 500) { throw new InvalidDataException("キャラクター案の形式を確認できません。もう一度生成してください。"); }
        return c;
    }
}
