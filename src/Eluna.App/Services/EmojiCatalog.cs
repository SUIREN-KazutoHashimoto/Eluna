namespace Eluna.App.Services;

internal sealed record EmojiEntry(string Glyph, string Name, string Category, string Keywords)
{
    public string Hint => Glyph + " " + Name;
}
internal static class EmojiCatalog
{
    internal static readonly IReadOnlyList<KeyValuePair<string, string>> Categories = [new("recent", "最近使ったもの"), new("all", "すべて"), new("faces", "表情"), new("people", "人・ジェスチャー"), new("nature", "自然"), new("food", "食べ物"), new("activity", "活動"), new("objects", "もの"), new("symbols", "記号")];
    internal static readonly IReadOnlyList<EmojiEntry> All = [
        new("😀", "笑顔", "faces", "smile happy 笑う"), new("😃", "にっこり", "faces", "happy smile"), new("😄", "満面の笑み", "faces", "happy 笑顔"), new("😁", "にやり", "faces", "grin smile"), new("😂", "笑い泣き", "faces", "laugh lol 涙"), new("🤣", "大笑い", "faces", "rofl laugh"),
        new("😊", "ほほえみ", "faces", "smile happy"), new("😍", "大好き", "faces", "love heart"), new("🥰", "幸せ", "faces", "love happy"), new("😘", "投げキッス", "faces", "kiss love"), new("😎", "サングラス", "faces", "cool"), new("🤔", "考える", "faces", "think question 疑問"),
        new("🤨", "気になる", "faces", "question doubt"), new("😐", "無表情", "faces", "neutral"), new("😅", "冷や汗", "faces", "sweat smile"), new("😴", "眠い", "faces", "sleep tired"), new("😭", "大泣き", "faces", "cry sad 涙"), new("😢", "悲しい", "faces", "sad cry"),
        new("😮", "びっくり", "faces", "surprise wow"), new("😱", "驚き", "faces", "scream shock"), new("😡", "怒る", "faces", "angry"), new("🥳", "お祝い", "faces", "party celebrate"), new("🤖", "ロボット", "faces", "robot ai"), new("👻", "おばけ", "faces", "ghost"),
        new("👍", "いいね", "people", "thumb up yes 賛成"), new("👎", "よくないね", "people", "thumb down no 反対"), new("👏", "拍手", "people", "clap great"), new("🙏", "ありがとう", "people", "thanks please pray お願い"), new("👋", "手を振る", "people", "wave hello こんにちは"), new("🙌", "万歳", "people", "hooray yay"),
        new("👌", "オーケー", "people", "ok 了解"), new("✌️", "ピース", "people", "peace victory"), new("💪", "力こぶ", "people", "strong muscle 頑張る"), new("🤝", "握手", "people", "handshake agreement 合意"), new("👀", "見ている", "people", "eyes look 確認"), new("🧠", "脳", "people", "brain think"),
        new("🌸", "桜", "nature", "sakura flower 春"), new("🌻", "ひまわり", "nature", "sunflower 夏 花"), new("🌿", "葉", "nature", "leaf green 植物"), new("🌳", "木", "nature", "tree forest"), new("🍁", "紅葉", "nature", "autumn fall"), new("🐈", "猫", "nature", "cat ねこ"),
        new("🐕", "犬", "nature", "dog いぬ"), new("🐧", "ペンギン", "nature", "penguin"), new("🦊", "きつね", "nature", "fox"), new("🌙", "月", "nature", "moon night 夜"), new("⭐", "星", "nature", "star"), new("🌟", "輝く星", "nature", "star shine"), new("☀️", "太陽", "nature", "sun 晴れ"), new("🌧️", "雨", "nature", "rain weather"), new("❄️", "雪", "nature", "snow winter"), new("🌈", "虹", "nature", "rainbow"),
        new("☕", "コーヒー", "food", "coffee break 休憩"), new("🍵", "お茶", "food", "tea"), new("🍰", "ケーキ", "food", "cake birthday 誕生日"), new("🍎", "りんご", "food", "apple fruit"), new("🍊", "みかん", "food", "orange fruit"), new("🍓", "いちご", "food", "strawberry fruit"),
        new("🍙", "おにぎり", "food", "rice lunch"), new("🍜", "ラーメン", "food", "ramen noodle"), new("🍣", "すし", "food", "sushi"), new("🍕", "ピザ", "food", "pizza"), new("🍫", "チョコレート", "food", "chocolate"), new("🥐", "クロワッサン", "food", "bread breakfast パン"),
        new("🎉", "クラッカー", "activity", "party celebrate お祝い"), new("🎊", "くす玉", "activity", "confetti celebrate"), new("🎂", "誕生日ケーキ", "activity", "birthday cake"), new("🎁", "プレゼント", "activity", "gift"), new("🎮", "ゲーム", "activity", "game controller"), new("🎨", "絵を描く", "activity", "art paint"),
        new("🎵", "音符", "activity", "music 音楽"), new("🎸", "ギター", "activity", "guitar music"), new("⚽", "サッカー", "activity", "soccer football"), new("🏆", "トロフィー", "activity", "trophy win 成功"), new("🚀", "ロケット", "activity", "rocket launch 開始"), new("🧩", "パズル", "activity", "puzzle problem"),
        new("📚", "本", "objects", "books read 読書"), new("📖", "開いた本", "objects", "book read"), new("📝", "メモ", "objects", "memo note write"), new("✏️", "えんぴつ", "objects", "pencil edit 編集"), new("💡", "アイデア", "objects", "idea light 思いつき"), new("💻", "パソコン", "objects", "computer laptop pc"),
        new("📷", "カメラ", "objects", "camera photo 写真"), new("📎", "クリップ", "objects", "clip attachment 添付"), new("📌", "ピン", "objects", "pin bookmark 保存"), new("🔍", "虫眼鏡", "objects", "search find 検索"), new("🔑", "鍵", "objects", "key"), new("🔧", "レンチ", "objects", "tool fix 修理"),
        new("❤️", "ハート", "symbols", "heart love 愛"), new("💙", "青いハート", "symbols", "blue heart love"), new("💚", "緑のハート", "symbols", "green heart love"), new("💜", "紫のハート", "symbols", "purple heart love"), new("💛", "黄色のハート", "symbols", "yellow heart love"), new("✨", "きらきら", "symbols", "sparkles shine"),
        new("🔥", "炎", "symbols", "fire hot"), new("✅", "チェック", "symbols", "check done 完了 確認"), new("❌", "バツ", "symbols", "cross no 失敗"), new("⚠️", "注意", "symbols", "warning caution"), new("❓", "疑問符", "symbols", "question 疑問"), new("❗", "感嘆符", "symbols", "exclamation 重要"), new("💯", "百点", "symbols", "hundred perfect"), new("♻️", "リサイクル", "symbols", "recycle reuse")
    ];
    private static readonly HashSet<string> Glyphs = All.Select(e => e.Glyph).ToHashSet(StringComparer.Ordinal);
    internal static bool Contains(string glyph) => Glyphs.Contains(glyph);
    internal static List<string> Remember(string glyph, IReadOnlyList<string>? recent) => new[] { glyph }.Concat(recent ?? []).Where(Contains).Distinct(StringComparer.Ordinal).Take(24).ToList();
    internal static IReadOnlyList<EmojiEntry> Find(string query, string category, IReadOnlyList<string>? recent)
    {
        string[] terms = query.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        IEnumerable<EmojiEntry> entries = category == "recent" ? (recent ?? []).Select(g => All.FirstOrDefault(e => e.Glyph == g)).OfType<EmojiEntry>() : All.Where(e => category == "all" || e.Category == category);
        return entries.Where(e => terms.All(t => (e.Glyph + " " + e.Name + " " + e.Keywords).Contains(t, StringComparison.OrdinalIgnoreCase))).DistinctBy(e => e.Glyph).ToArray();
    }
}
