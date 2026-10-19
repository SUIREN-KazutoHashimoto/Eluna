using System.Text.Json;
using Eluna.App.Models;

namespace Eluna.App.Services;

internal sealed record DebateDecision(string Speaker, string Kind, Guid? ReplyTo, string Query);
internal static class DebateDirector
{
    internal static readonly object Schema = new
    {
        type = "object",
        properties = new
        {
            speaker = new { type = "string", maxLength = 80 },
            kind = new Dictionary<string, object> { ["type"] = "string", ["enum"] = new[] { "speech", "question", "rebuttal", "supplement", "hold", "search", "finish" } },
            replyTo = new { type = "string", maxLength = 40 },
            query = new { type = "string", maxLength = 300 },
            reason = new { type = "string", maxLength = 160 }
        },
        required = new[] { "speaker", "kind", "replyTo", "query", "reason" },
        additionalProperties = false
    };
    internal static bool TryParse(string json, IReadOnlyList<DebateRole> participants, IEnumerable<DebateTurn> turns, out DebateDecision? decision)
    {
        decision = null;
        try
        {
            using JsonDocument document = JsonDocument.Parse(json); JsonElement root = document.RootElement;
            string speaker = root.GetProperty("speaker").GetString()!, kind = root.GetProperty("kind").GetString()!, query = root.GetProperty("query").GetString()!;
            string reply = root.GetProperty("replyTo").GetString()!;
            if (!participants.Any(p => p.Id == speaker) || kind is not ("speech" or "question" or "rebuttal" or "supplement" or "hold" or "search" or "finish") || query is null || query.Length > 300 ||
                reply is null || reply.Length > 0 && (!Guid.TryParse(reply, out Guid id) || !turns.Any(t => t.Id == id))) { return false; }
            decision = new(speaker, kind, reply.Length == 0 ? null : Guid.Parse(reply), query); return true;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException or FormatException) { return false; }
    }
    internal static DebateRole Choose(IReadOnlyList<DebateRole> participants, IReadOnlyList<DebateTurn> speeches, DebateRole? proposed)
    {
        string? repeated = speeches.Count >= 2 && speeches[^1].Role == speeches[^2].Role ? speeches[^1].Role : null;
        if (proposed is not null && proposed.Id != repeated) { return proposed; }
        return participants.Where(p => p.Id != repeated).OrderBy(p => speeches.Count(t => t.Role == p.Id)).ThenBy(p => speeches.ToList().FindLastIndex(t => t.Role == p.Id)).First();
    }
}
