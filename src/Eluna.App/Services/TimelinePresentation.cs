using System.Globalization;
using Eluna.App.Models;

namespace Eluna.App.Services;

internal static class TimelinePresentation
{
    internal static void Apply(IReadOnlyList<ChatMessage> rows)
    {
        ChatMessage? previous = null;
        foreach (ChatMessage row in rows)
        {
            DateTimeOffset? at = row.Identity?.Time?.ToLocalTime(), before = previous?.Identity?.Time?.ToLocalTime();
            row.DateSeparator = at is { } time && before?.Date != time.Date ? time.ToString("yyyy年M月d日（ddd）", CultureInfo.GetCultureInfo("ja-JP")) : "";
            bool ordinary = row.Identity?.Detail != "member-joined" && !row.HasWork && !row.HasComputer && !row.HasArtifact && !row.HasReply;
            row.Compact = ordinary && previous is not null && previous.Identity?.Detail != "member-joined" && !previous.HasWork && !previous.HasComputer && !previous.HasArtifact &&
                row.Role == previous.Role && row.Identity is { } current && previous.Identity is { } last && current.AgentId == last.AgentId && current.Name == last.Name && current.WorkId == last.WorkId &&
                at is { } now && before is { } prior && now.Date == prior.Date && now >= prior && now - prior <= TimeSpan.FromMinutes(5);
            previous = row;
        }
    }
}
