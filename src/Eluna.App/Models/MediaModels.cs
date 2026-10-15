using System.Text.Json.Serialization;
using System.Windows.Media.Imaging;

namespace Eluna.App.Models;

public sealed record ChatAttachment(string Name, string Kind, string Text = "", string? ImageBase64 = null)
{
    public string ForQuestion(string question, int limit)
    {
        if (Text.Length <= limit) { return Text; }
        string[] words = System.Text.RegularExpressions.Regex.Matches(question, @"[A-Za-z0-9_.-]{2,}|[一-龯]{2,}|[ァ-ヶー]{2,}").Select(m => m.Value).Distinct().ToArray();
        string[] segments = Enumerable.Range(0, (Text.Length + 599) / 600).Select(i => Text.Substring(i * 600, Math.Min(600, Text.Length - i * 600))).ToArray();
        int count = Math.Max(1, (limit - 200) / 600);
        IEnumerable<int> selected = System.Text.RegularExpressions.Regex.IsMatch(question, "要約|概要|全体|summari|summary", System.Text.RegularExpressions.RegexOptions.IgnoreCase) ?
            Enumerable.Range(0, count).Select(i => i * (segments.Length - 1) / Math.Max(1, count - 1)) :
            segments.Select((text, index) => (index, score: words.Count(w => text.Contains(w, StringComparison.OrdinalIgnoreCase)))).OrderByDescending(p => p.score).ThenBy(p => p.index).Take(count).Select(p => p.index);
        return "［長文のため質問に関連する箇所・全体の一部を抜粋。原文全体を確認したわけではありません］\n" + string.Join("\n…\n", selected.Distinct().Order().Select(i => segments[i]));
    }
    [JsonIgnore] public string Label => Kind == "image" ? "画像 · " + Name : "文書 · " + Name;
    [JsonIgnore]
    public BitmapSource? Preview
    {
        get
        {
            if (ImageBase64 is null) { return null; }
            using var stream = new System.IO.MemoryStream(Convert.FromBase64String(ImageBase64));
            var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.StreamSource = stream; image.DecodePixelWidth = 240; image.EndInit(); image.Freeze(); return image;
        }
    }
}
