using System.IO;
using Eluna.App.Models;

namespace Eluna.App.Services;

internal static class AccountDefaults
{
    internal const int MaximumMembers = 50;
    internal const int MaximumAgents = MaximumMembers - 1; // The local member occupies one place.
    internal static IReadOnlySet<Guid> ServerMembers(AppState state, Guid project) => state.Conversations
        .Where(c => c.ProjectId == project).SelectMany(c => c.Room?.Members ?? []).ToHashSet();

    internal static void Validate(AgentAccount account)
    {
        if (account.Generation is { } g && (!double.IsFinite(g.Temperature) || g.Temperature is < 0 or > 2 ||
            !double.IsFinite(g.TopP) || g.TopP is <= 0 or > 1 || g.MaxOutputTokens is < 128 or > 8192) ||
            account.Social is { } s && (s.Interests is null || s.Interests.Length > 1000 || !double.IsFinite(s.Participation) || s.Participation is < 0 or > 1 || s.CooldownSeconds is < 5 or > 3600) ||
            account.Artist is { } a && (a.Style is null || a.Style.Length > 1000))
        { throw new InvalidDataException("アカウントの生成・会話・絵描き設定を確認してください。"); }
    }
    internal static void Validate(SocialPolicy? policy)
    {
        if (policy is { } p && (p.StartHour is < 0 or > 23 || p.EndHour is < 0 or > 23 || p.IntervalMinutes is < 1 or > 1440 ||
            p.MaxTurns is < 1 or > 100 || p.MaxTokens is < 256 or > 65536 || p.PaceSeconds is < 1 or > 300))
        { throw new InvalidDataException("自律会話の時間帯・頻度・会話上限を確認してください。"); }
    }
    internal static AgentGeneration Suggest(AgentPersonality personality) => personality.Preset switch
    {
        "analyst" => new(0.45, 0.9, 2048),
        "critic" => new(0.6, 0.9, 2048),
        "teacher" => new(0.65, 0.95, 2048),
        "creative" or "roleplay" => new(0.95, 0.95, 1536),
        _ => new(0.8, 0.95, 1536)
    };
    internal static LocalModel ModelFor(AgentAccount? account, LocalModel model) => account?.Generation is not { } g ? model :
        model with { Preferences = model.Options with { Temperature = g.Temperature, TopP = g.TopP, MaxOutputTokens = g.MaxOutputTokens } };

    internal static List<AgentAccount> Cast(string? modelId = null)
    {
        (string Name, string Icon, string Preset, string Specialty, string Traits, string Interests, double Rate, string Art)[] cast = [
            ("ミミ", "🐈", "friendly", "話題をつなぐ聞き上手", "一人称は私。軽快で気さく。質問しすぎず、自分の意見も短く言う。", "日常、音楽、食べ物、猫", .65, ""),
            ("カメオ", "🦉", "analyst", "冷静な観察と調査", "一人称は僕。落ち着いて根拠を確かめる。雑談では一言だけでもよい。", "科学、歴史、技術、読書", .35, ""),
            ("スルル", "🐬", "creative", "企画と遊び心", "一人称は私。好奇心旺盛で新しい案を楽しむ。冗談にも気軽に乗る。", "ゲーム、旅、映画、企画", .6, ""),
            ("テラ", "🌱", "teacher", "穏やかな相談相手", "一人称は私。急がず相手を見守る。説教や定型の励ましを繰り返さない。", "自然、園芸、料理、暮らし", .3, ""),
            ("カネカメ", "🐢", "critic", "率直な現実派", "一人称は俺。短く率直。軽口も言うが相手を追い詰めない。", "仕事、ものづくり、お金、スポーツ", .35, ""),
            ("カゲロウ", "🦋", "creative", "物語と水彩イラスト", "一人称はわたし。柔らかい言葉で情景を想像する。必要な時にはっきり意見を言う。", "絵、物語、風景、色彩", .45, "透明な水彩、柔らかい光、自然と物語の情景"),
            ("ルカ", "🎮", "friendly", "ゲームとネットの雑談", "一人称は僕。砕けた口調。草や絵文字だけの返事もする。場の空気を読む。", "ゲーム、アニメ、ネット、配信", .7, ""),
            ("ナギ", "🎧", "default", "音楽と静かな日常", "一人称は私。口数は少なめ。気になる話題に短い感想を添える。", "音楽、映画、夜、散歩", .2, ""),
            ("ソラ", "📷", "creative", "写真とデザイン", "一人称は自分。気取らず具体的に話す。構図や色に興味を持つ。", "写真、建築、デザイン、旅", .4, "写真風の自然な光、端正な構図、落ち着いた配色"),
            ("コハク", "🔧", "analyst", "工作とプログラミング", "一人称は僕。手を動かすのが好き。試せる案を気軽に出す。", "工作、プログラミング、PC、電子機器", .45, ""),
            ("ユイ", "🍳", "friendly", "料理と生活の工夫", "一人称は私。明るくさっぱり。食べ物の話で盛り上がる。", "料理、お菓子、買い物、生活", .55, ""),
            ("レイ", "📚", "critic", "本と多角的な意見", "一人称は私。簡潔で少し皮肉屋。反対意見を面白がるが決めつけない。", "小説、哲学、歴史、映画", .3, "")];
        List<AgentAccount> accounts = cast.Select(c => new AgentAccount(WorkspaceService.StableId(WorkspaceService.DefaultAgent, "cast:" + c.Name), c.Name, c.Icon,
            "このプロフィールは創作の人物像。実際に見聞きしていない体験や実行していない操作を事実として話さない。", c.Specialty, modelId,
            Personality: new(c.Preset, c.Specialty, "同じ場にいる対等な参加者として会話する。", c.Traits),
            Generation: Suggest(new(c.Preset)), Social: new(c.Interests, c.Rate), Artist: new(c.Art.Length > 0, c.Art))).ToList();
        return accounts.Select((a, i) => a with { Connections = [accounts[(i + 1) % accounts.Count].Id, accounts[(i + 3) % accounts.Count].Id] }).ToList();
    }
    internal static AppState FirstRun(AppState state)
    {
        List<AgentAccount> cast = Cast(state.Settings.ModelId);
        Guid server = WorkspaceService.StableId(WorkspaceService.DefaultAgent, "lounge");
        KnowledgeProject lounge = new(server, "ラウンジ", "名前を持つ参加者同士の会話。必要なら助け合い、無理に全員へ返答しない。", "project-only", false, [], []);
        Guid[] members = cast.Take(6).Select(a => a.Id).ToArray();
        Conversation Room(string title, string kind, IEnumerable<Guid> ids, SocialPolicy? policy = null) => new()
        { Title = title, ProjectId = server, Room = new(kind, ids.ToList(), ids.First(), WorkMode: "chat", Social: policy) };
        return state with
        {
            SchemaVersion = 4,
            Settings = state.Settings with { SelectedProjectId = server },
            Agents = cast,
            AgentMemories = [],
            Projects = [lounge],
            Conversations = [Room("雑談", "channel", members, new(Enabled: true, Spontaneous: true, IntervalMinutes: 1)).Save(),
                Room("アトリエ", "channel", cast.Where(a => a.Artist?.Enabled == true).Select(a => a.Id)).Save(),
                Room("一緒に話そう", "voice", cast.Select(a => a.Id)).Save()]
        };
    }
}
