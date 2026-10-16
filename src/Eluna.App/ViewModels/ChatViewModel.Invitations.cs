using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Eluna.App.Models;
using Eluna.App.Services;

namespace Eluna.App.ViewModels;

public sealed partial class ChatViewModel
{
    public async Task InviteMembersAsync(Guid roomId, IReadOnlyList<Guid> ids, Guid? actor = null, Guid? job = null)
    {
        Conversation room = Conversations.FirstOrDefault(c => c.Id == roomId) ?? throw new InvalidOperationException("招待先のチャンネルがありません。");
        RoomProfile profile = ChannelInvitations.Apply(CaptureState(), room.Save(), ids, actor);
        Guid[] joined = profile.Members.Except(room.Room!.Members).ToArray();
        if (profile.Members.SequenceEqual(room.Room!.Members) && actor is not null) { return; }
        CancelWorkspaceRequests(Conversations.Where(c => c.Id == roomId || c.Room?.ParentId == roomId).Select(c => c.Id).ToHashSet(), exceptJob: job);
        room.Room = profile; room.UpdatedAt = DateTimeOffset.UtcNow;
        if (joined.Length > 0)
        {
            var notice = new ChatMessage { Role = "assistant", Identity = new(null, "参加のお知らせ", "＋", Time: DateTimeOffset.UtcNow, Detail: "member-joined"), Content = string.Join("、", joined.Select(id => MentionService.Format(Agents.First(a => a.Id == id)).TrimEnd())) + " が参加しました。" };
            ChatMessage? current = job is Guid jobId ? Inferences.Items.FirstOrDefault(j => j.Request.Id == jobId) is { } currentJob ? FindAnswer(currentJob.Request) : null : null;
            if (current is not null && room.Messages.Contains(current)) { room.Messages.Insert(room.Messages.IndexOf(current), notice); } else { room.Messages.Add(notice); }
            foreach (WorkSession discussion in room.Branches.SelectMany(b => b.Messages).Select(m => m.Work).OfType<WorkSession>().Where(w => w.Mode == "debate" && w.Options.DebateStyle == "free" && w.Stage is not ("complete" or "limited" or "moved")))
            { discussion.Discussion = discussion.Discussion with { PendingParticipants = RoomWorkOptions(room, discussion.Options).Participants.ToList() }; }
        }
        RefreshRoom(); await PersistSafelyAsync();
    }
    private async Task<bool> TryInviteFromChatAsync(Conversation context, ChatMessage answer, InferenceJob job, LlamaService engine, CancellationToken token)
    {
        string text = context.Messages.Last(m => m.Role == "user").Content;
        if (!ChannelInvitations.Requested(text)) { return false; }
        // A new workspace request belongs to the team planner; this action adds members to an existing place.
        if (Regex.IsMatch(text, @"(?:新しい|新規|新たな).{0,20}(?:サーバー|チャンネル).{0,20}(?:作|用意)", RegexOptions.None, TimeSpan.FromMilliseconds(100))) { return false; }
        Conversation source = Conversations.First(c => c.Id == job.Request.ConversationId); AgentAccount? actor = Agents.FirstOrDefault(a => a.Id == job.Request.Agent?.Id);
        if (actor is null) { answer.Content = "招待を行うメンバーを選んでください。"; return true; }
        try
        {
            if (!ChannelAccess.CanInvite(CaptureState(), source.Save(), actor.Id)) { throw new InvalidOperationException("このチャンネルの招待権限がありません。「⋯ → アクセス権」で招待を許可してください。"); }
            if (Regex.IsMatch(text, @"#[^\s、。]+", RegexOptions.None, TimeSpan.FromMilliseconds(100)) && !text.Contains("#" + source.Title, StringComparison.Ordinal)) { throw new InvalidOperationException("招待先のチャンネルを開いてから依頼してください。別の場所へ勝手に招待はしません。"); }
            List<AgentAccount> targets = ChannelInvitations.Named(text, Agents, actor.Id);
            if (targets.Count == 0)
            {
                List<AgentAccount> candidates = Agents.Where(a => a.Enabled && actor.Connections?.Contains(a.Id) == true && a.Autonomy?.AcceptInvites != false && !source.Room!.Members.Contains(a.Id)).Take(100).ToList();
                if (candidates.Count == 0) { throw new InvalidOperationException("招待できるつながりがありません。プロフィールから登録するか、メンバー一覧の「招待」で追加してください。"); }
                job.Detail = "招待するメンバーを選んでいます…";
                var schema = new { type = "object", properties = new { members = new { type = "array", maxItems = 6, items = new { type = "integer", @enum = Enumerable.Range(0, candidates.Count).ToArray() } } }, required = new[] { "members" }, additionalProperties = false };
                string input = text + "\n候補:\n" + string.Join('\n', candidates.Select((a, i) => i + ": " + a.Name + " / " + a.Specialty));
                await engine.EnsureModelAsync(job.Request.Model, job.Request.Settings, m => job.Detail = m, token); var json = new StringBuilder();
                await foreach (GenerationUpdate piece in engine.GenerateAsync([new("user", input)], InferenceModes.Find("fast"), token, "Select only the supplied accounts suitable for the explicit invitation request. Return {members:[indices]}. Use [] if uncertain. Do not claim an invitation happened.", 128, schema)) { json.Append(piece.Content); }
                using JsonDocument plan = JsonDocument.Parse(json.ToString()); int[] indices = plan.RootElement.GetProperty("members").EnumerateArray().Select(x => x.GetInt32()).ToArray();
                if (indices.Length > 6 || indices.Any(i => i < 0 || i >= candidates.Count)) { throw new InvalidOperationException("招待候補を特定できませんでした。名前を指定してください。"); }
                targets = indices.Distinct().Select(i => candidates[i]).ToList();
            }
            if (targets.Count == 0) { throw new InvalidOperationException("招待先を特定できませんでした。名前かメンションで指定してください。"); }
            token.ThrowIfCancellationRequested(); bool already = targets.All(a => source.Room!.Members.Contains(a.Id));
            await InviteMembersAsync(source.Id, targets.Select(a => a.Id).ToArray(), actor.Id, job.Request.Id);
            answer.Content = string.Join("、", targets.Select(a => MentionService.Format(a).TrimEnd())) + (already ? " は既にこのチャンネルに参加しています。" : " をこのチャンネルへ招待しました。名前にメンションすると会話に参加します。");
            answer.Identity = answer.Identity! with { Detail = "招待の実行結果" }; job.Detail = "メンバーの招待を確認しました";
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.IO.InvalidDataException or JsonException)
        { answer.Content = "招待を実行できませんでした。\n\n" + ex.Message; answer.Notice = "招待は実行していません。メンバー一覧の「招待」からも追加できます。"; job.Detail = "招待設定を確認してください"; }
        await PersistSafelyAsync(); return true;
    }
}
