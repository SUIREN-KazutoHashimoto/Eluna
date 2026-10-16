using System.IO;
using System.Text.RegularExpressions;
using Eluna.App.Models;

namespace Eluna.App.Services;

internal static class ChannelInvitations
{
    internal static bool Requested(string text)
    {
        if (text.Length > 16000) { return false; }
        text = Regex.Replace(text, @"```[\s\S]*?(?:```|\z)|`[^`]*`|^\s*>.*$", "", RegexOptions.Multiline, TimeSpan.FromMilliseconds(100));
        text = Regex.Replace(text, "「[^」]*」|『[^』]*』|\"[^\"]*\"", "", RegexOptions.None, TimeSpan.FromMilliseconds(100));
        // Asking to be addressed by a name is conversation, not an invitation.
        text = Regex.Replace(text, @"(?:と|って|(?:名前|愛称|ニックネーム)で)呼んで|(?:私|僕|俺|わたし|ぼく|自分|名前|愛称|ニックネーム)を呼んで", "", RegexOptions.None, TimeSpan.FromMilliseconds(100));
        if (Regex.IsMatch(text, @"^(?:次の|以下の|この)(?:文章|文|テキスト).{0,20}(?:翻訳|要約|添削)|^(?:translate|rewrite|summarize)\b|招待(?:しない|しなくて|不要)", RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(100))) { return false; }
        return Regex.IsMatch(text, @"招待(?:して|を?お願い|を頼|しよう)|呼んで|呼んでもら|参加させて|(?:メンバー|アカウント).{0,60}追加して|\b(?:invite|add).{0,40}(?:here|channel|member)", RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(100));
    }
    internal static List<AgentAccount> Named(string text, IEnumerable<AgentAccount> accounts, Guid actor)
    {
        AgentAccount[] catalog = accounts.ToArray();
        text = Regex.Replace(text, @"```[\s\S]*?(?:```|\z)|`[^`]*`|^\s*>.*$", "", RegexOptions.Multiline, TimeSpan.FromMilliseconds(100));
        text = Regex.Replace(text, "「(?<name>[^」]*)」|『(?<name>[^』]*)』|\"(?<name>[^\"]*)\"", m => catalog.Any(a => a.Name.Equals(m.Groups["name"].Value, StringComparison.OrdinalIgnoreCase)) ? m.Groups["name"].Value : "", RegexOptions.None, TimeSpan.FromMilliseconds(100));
        var named = new List<AgentAccount>(); var spans = new List<(int Start, int End)>();
        foreach (IGrouping<string, AgentAccount> group in catalog.Where(a => a.Id != actor).GroupBy(a => a.Name, StringComparer.OrdinalIgnoreCase).OrderByDescending(g => g.Key.Length))
        {
            for (int at = text.IndexOf(group.Key, StringComparison.OrdinalIgnoreCase); at >= 0; at = text.IndexOf(group.Key, at + group.Key.Length, StringComparison.OrdinalIgnoreCase))
            {
                int after = at + group.Key.Length;
                bool right = after == text.Length || char.IsWhiteSpace(text[after]) || char.IsPunctuation(text[after]) || "をにもとへがは".Contains(text[after]) || text.AsSpan(after).StartsWith("さん", StringComparison.Ordinal);
                bool left = at == 0 || !char.IsLetterOrDigit(text[at - 1]) || "のともにへはや".Contains(text[at - 1]);
                if (!right || !left || spans.Any(s => at >= s.Start && at < s.End)) { continue; }
                named.AddRange(group); spans.Add((at, after)); break;
            }
        }
        if (named.GroupBy(a => a.Name, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1)) { throw new InvalidOperationException("同名のアカウントがあるため招待先を特定できません。名前を区別してください。"); }
        return named;
    }
    internal static RoomProfile Apply(AppState state, SavedConversation room, IReadOnlyList<Guid> ids, Guid? actor)
    {
        RoomProfile profile = room.Room ?? throw new InvalidOperationException("チャンネル情報がありません。");
        if (room.IsArchived) { throw new InvalidOperationException("アーカイブしたチャンネルには招待できません。"); }
        AgentAccount? inviter = actor is Guid agent ? state.Agents?.FirstOrDefault(a => a.Id == agent) : null;
        if (actor is not null && (inviter is null || !ChannelAccess.CanInvite(state, room, actor.Value))) { throw new InvalidOperationException("このメンバーには招待権限がありません。チャンネルの「アクセス権」で招待を許可してください。"); }
        if (actor is not null && inviter?.Autonomy?.InviteConnections != true) { throw new InvalidOperationException("プロフィールの「つながり・自動編成」で、つながりのあるアカウントの招待を許可してください。自動編成そのものはオフでも招待できます。"); }
        if (ids.Count == 0 || ids.Distinct().Count() != ids.Count) { throw new InvalidDataException("招待するメンバーを選んでください。"); }
        foreach (Guid id in ids)
        {
            AgentAccount target = state.Agents?.FirstOrDefault(a => a.Id == id && a.Enabled) ?? throw new InvalidOperationException("招待先が無効、または見つかりません。");
            if (actor is not null && id != actor && (inviter?.Connections?.Contains(id) != true || target.Autonomy?.AcceptInvites == false)) { throw new InvalidOperationException(target.Name + "とのつながり、または相手の招待受け入れを確認してください。"); }
            if (actor is not null && (profile.Permissions?.Any(g => g.AgentId == id && (!g.View || !g.Speak)) == true || profile.ParentId is Guid parent && state.Conversations.FirstOrDefault(c => c.Id == parent) is { } origin && !ChannelAccess.CanView(state, origin, id))) { throw new InvalidOperationException(target.Name + "は閲覧・発言を制限されています。アクセス権を変更できる利用者に確認してください。"); }
        }
        List<Guid> members = profile.Members.Concat(ids).Distinct().ToList();
        if (members.Count > 6) { throw new InvalidOperationException("この場所の自動応答メンバーは6人までです。先に参加者を整理してください。"); }
        List<ChannelGrant>? grants = profile.Permissions;
        if (actor is null) { grants = (grants ?? []).Where(g => !ids.Contains(g.AgentId)).Concat(ids.Select(id => new ChannelGrant(id, true, true))).ToList(); }
        return profile with { Kind = profile.Kind == "dm" && members.Count > 1 ? "group" : profile.Kind, Members = members, Permissions = grants };
    }
}
