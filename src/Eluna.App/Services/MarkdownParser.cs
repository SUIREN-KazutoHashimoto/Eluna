using System.Text;
using System.Text.RegularExpressions;

namespace Eluna.App.Services;

public sealed record MarkdownBlock(string Kind, string Text, int Level = 0, string Language = "", List<string[]>? Rows = null);
public sealed record CodeToken(string Text, string Kind);
public sealed record ParsedMarkdownBlock(MarkdownBlock Block, int Start);

public static class MarkdownParser
{
    public static IReadOnlyList<MarkdownBlock> Parse(string text) => ParseDetailed(text).Select(b => b.Block).ToArray();
    internal static IReadOnlyList<ParsedMarkdownBlock> ParseDetailed(string text, CancellationToken token = default)
    {
        string[] lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
        int offset = 0; int[] offsets = lines.Select(line => { int start = offset; offset += line.Length + 1; return start; }).ToArray();
        var blocks = new List<ParsedMarkdownBlock>(); int blockStart = 0;
        void Add(MarkdownBlock block) => blocks.Add(new(block, blockStart));
        for (int i = 0; i < lines.Length;)
        {
            token.ThrowIfCancellationRequested(); blockStart = offsets[i];
            string line = lines[i];
            if (string.IsNullOrWhiteSpace(line)) { i++; continue; }
            Match fence = Regex.Match(line, @"^\s{0,3}(`{3,}|~{3,})(.*)$");
            if (fence.Success)
            {
                string delimiter = fence.Groups[1].Value;
                string language = fence.Groups[2].Value.Trim().Split(' ')[0];
                var code = new List<string>();
                i++;
                while (i < lines.Length && !Regex.IsMatch(lines[i], $@"^\s{{0,3}}{Regex.Escape(delimiter[0].ToString())}{{{delimiter.Length},}}\s*$"))
                {
                    code.Add(lines[i++]);
                }
                if (i < lines.Length) { i++; }
                Add(new("code", string.Join('\n', code), Language: language));
                continue;
            }
            Match heading = Regex.Match(line, @"^\s{0,3}(#{1,6})\s+(.+?)\s*#*\s*$");
            if (heading.Success) { Add(new("heading", heading.Groups[2].Value, heading.Groups[1].Length)); i++; continue; }
            if (i + 1 < lines.Length && line.Contains('|') && IsTableDivider(lines[i + 1]))
            {
                var rows = new List<string[]> { SplitCells(line) };
                i += 2;
                while (i < lines.Length && lines[i].Contains('|') && !string.IsNullOrWhiteSpace(lines[i])) { rows.Add(SplitCells(lines[i++])); }
                Add(new("table", "", Rows: rows));
                continue;
            }
            Match quote = Regex.Match(line, @"^\s{0,3}>\s?(.*)$");
            if (quote.Success)
            {
                var quoted = new List<string>();
                while (i < lines.Length && (quote = Regex.Match(lines[i], @"^\s{0,3}>\s?(.*)$")).Success)
                {
                    quoted.Add(quote.Groups[1].Value); i++;
                }
                Add(new("quote", string.Join('\n', quoted)));
                continue;
            }
            Match list = Regex.Match(line, @"^\s*([-+*]|\d+[.)])\s+(.+)$");
            if (list.Success) { Add(new("list", list.Groups[2].Value, Language: char.IsDigit(list.Groups[1].Value[0]) ? list.Groups[1].Value : "•")); i++; continue; }
            if (Regex.IsMatch(line.Trim(), @"^([-*_])(?:\s*\1){2,}$")) { Add(new("rule", "")); i++; continue; }
            var paragraph = new List<string> { line };
            i++;
            while (i < lines.Length && !string.IsNullOrWhiteSpace(lines[i]) && !IsBlockStart(lines[i]) &&
                !(i + 1 < lines.Length && lines[i].Contains('|') && IsTableDivider(lines[i + 1])))
            {
                paragraph.Add(lines[i++]);
            }
            Add(new("paragraph", string.Join('\n', paragraph)));
        }
        return blocks;
    }

    private static bool IsBlockStart(string line) => Regex.IsMatch(line, @"^\s{0,3}(#{1,6}\s|>|`{3,}|~{3,}|[-+*]\s|\d+[.)]\s)");
    private static bool IsTableDivider(string line) => SplitCells(line).Length > 0 && SplitCells(line).All(c => Regex.IsMatch(c.Trim(), @"^:?-{3,}:?$"));

    internal static string[] SplitCells(string line)
    {
        string value = line.Trim();
        if (value.StartsWith('|')) { value = value[1..]; }
        if (value.EndsWith('|') && !value.EndsWith("\\|", StringComparison.Ordinal)) { value = value[..^1]; }
        var cells = new List<string>();
        var cell = new StringBuilder();
        bool code = false;
        for (int i = 0; i < value.Length; i++)
        {
            char ch = value[i];
            if (ch == '\\' && i + 1 < value.Length && value[i + 1] == '|') { cell.Append('|'); i++; continue; }
            if (ch == '`') { code = !code; }
            if (ch == '|' && !code) { cells.Add(cell.ToString().Trim()); cell.Clear(); }
            else { cell.Append(ch); }
        }
        cells.Add(cell.ToString().Trim());
        return cells.ToArray();
    }

    public static IReadOnlyList<CodeToken> Highlight(string code, string language)
    {
        if (code.Length > 64000) { return [new(code, "plain")]; }
        string lang = language.ToLowerInvariant();
        if (lang is not ("c#" or "csharp" or "cs" or "cpp" or "c++" or "c" or "js" or "javascript" or "ts" or "typescript" or
            "python" or "py" or "json" or "sql" or "powershell" or "ps1" or "bash" or "sh")) { return [new(code, "plain")]; }
        string comment = lang is "python" or "py" or "powershell" or "ps1" or "bash" or "sh" ? @"\#[^\n]*" : @"//[^\n]*|/\*[\s\S]*?\*/";
        var tokens = new List<CodeToken>();
        string pattern = $"(?<comment>{comment})|(?<string>\"(?:\\\\.|[^\"\\\\])*\"|'(?:\\\\.|[^'\\\\])*')|(?<number>\\b\\d+(?:\\.\\d+)?\\b)|(?<word>\\b[A-Za-z_][A-Za-z_0-9]*\\b)";
        var keywords = new HashSet<string>(("public private protected internal static class record namespace using return if else for foreach while switch case break continue " +
            "new null true false var int string double bool async await void const let function import export from def None True False print try catch finally throw " +
            "SELECT FROM WHERE JOIN ON AS INSERT UPDATE DELETE CREATE TABLE AND OR NOT ORDER BY GROUP LIMIT").Split(' '), StringComparer.OrdinalIgnoreCase);
        int position = 0;
        try
        {
            foreach (Match match in Regex.Matches(code, pattern, RegexOptions.None, TimeSpan.FromMilliseconds(200)))
            {
                if (match.Index > position) { tokens.Add(new(code[position..match.Index], "plain")); }
                string kind = match.Groups["comment"].Success ? "comment" : match.Groups["string"].Success ? "string" :
                    match.Groups["number"].Success ? "number" : keywords.Contains(match.Value) ? "keyword" : "plain";
                tokens.Add(new(match.Value, kind));
                position = match.Index + match.Length;
            }
        }
        catch (RegexMatchTimeoutException) { return [new(code, "plain")]; }
        if (position < code.Length) { tokens.Add(new(code[position..], "plain")); }
        return tokens;
    }
}

public sealed class StreamingMarkdownParser
{
    private string _text = "";
    private readonly List<ParsedMarkdownBlock> _blocks = [];
    public long ParsedCharacters { get; private set; }
    public IReadOnlyList<MarkdownBlock> Update(string text, CancellationToken token = default)
    {
        string normalized = text.Contains('\r') ? text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n') : text;
        bool append = normalized.StartsWith(_text, StringComparison.Ordinal);
        int stable = append ? Math.Max(0, _blocks.Count - 2) : 0;
        int offset = stable < _blocks.Count && append ? _blocks[stable].Start : 0;
        string suffix = normalized[offset..];
        var changed = MarkdownParser.ParseDetailed(suffix, token);
        _blocks.RemoveRange(stable, _blocks.Count - stable);
        _blocks.AddRange(changed.Select(b => b with { Start = b.Start + offset }));
        _text = normalized; ParsedCharacters += suffix.Length;
        return _blocks.Select(b => b.Block).ToArray();
    }
}
