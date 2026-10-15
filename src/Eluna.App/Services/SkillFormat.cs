using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Eluna.App.Models;

namespace Eluna.App.Services;

public static class SkillFormat
{
    internal static readonly UTF8Encoding Utf8 = new(false, true);
    public static SkillPackage Parse(Dictionary<string, byte[]> files)
    {
        if (!files.TryGetValue("SKILL.md", out byte[]? source)) { throw new InvalidDataException("スキル直下にSKILL.mdが必要です。"); }
        if (source.Length > 128 * 1024) { throw new InvalidDataException("SKILL.mdは128 KBまでです。"); }
        string markdown = Utf8.GetString(source).TrimStart('\uFEFF').Replace("\r\n", "\n");
        string[] lines = markdown.Split('\n');
        if (lines[0] != "---") { throw new InvalidDataException("SKILL.mdはYAMLメタデータの---から開始してください。"); }
        int end = Array.FindIndex(lines, 1, l => l is "---" or "...");
        if (end < 0 || end > 300) { throw new InvalidDataException("SKILL.mdのメタデータ終端が見つかりません。"); }
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int i = 1; i < end; i++)
        {
            string line = lines[i];
            if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith('#')) { continue; }
            if (char.IsWhiteSpace(line[0])) { continue; } // Optional nested metadata is retained in the original file.
            Match match = Regex.Match(line, "^([a-zA-Z][a-zA-Z0-9_-]*):(?:[ \\t]+(.*))?$");
            if (!match.Success) { throw new InvalidDataException("YAMLメタデータはキーと値の形式にしてください。対応する例をヘルプで確認できます。"); }
            string key = match.Groups[1].Value, value = match.Groups[2].Value.Trim();
            if (fields.ContainsKey(key)) { throw new InvalidDataException("メタデータのキーが重複しています: " + key); }
            if (value is ">" or ">-" or ">+" or "|" or "|-" or "|+")
            {
                bool literal = value[0] == '|'; var block = new List<string>();
                while (i + 1 < end && (string.IsNullOrWhiteSpace(lines[i + 1]) || char.IsWhiteSpace(lines[i + 1][0]))) { block.Add(lines[++i].Trim()); }
                value = string.Join(literal ? "\n" : " ", block).Trim();
            }
            else if (value.StartsWith('\''))
            {
                Match quoted = Regex.Match(value, "^'((?:[^']|'')*)'(?:[ \\t]+#.*)?$");
                if (!quoted.Success) { throw new InvalidDataException("単一引用符の値を閉じてください: " + key); }
                value = quoted.Groups[1].Value.Replace("''", "'");
            }
            else if (value.StartsWith('"'))
            {
                Match quoted = Regex.Match(value, "^(?<scalar>\"(?:[^\"\\\\]|\\\\.)*\")(?:[ \\t]+#.*)?$");
                if (!quoted.Success) { throw new InvalidDataException("二重引用符の値を確認してください: " + key); }
                try { value = JsonSerializer.Deserialize<string>(quoted.Groups["scalar"].Value)!; }
                catch (JsonException) { throw new InvalidDataException("二重引用符ではJSON形式のエスケープを使用してください: " + key); }
            }
            else
            {
                int comment = value.IndexOf(" #", StringComparison.Ordinal); if (comment >= 0) { value = value[..comment].TrimEnd(); }
                if (key is "name" or "description" or "license" or "compatibility" && value.StartsWithAny('&', '*', '!', '[', '{'))
                { throw new InvalidDataException("このメタデータは文字列にしてください: " + key); }
            }
            fields.Add(key, value);
        }
        string name = fields.GetValueOrDefault("name", ""), description = fields.GetValueOrDefault("description", "");
        ValidateName(name);
        if (string.IsNullOrWhiteSpace(description) || description.Length > 1024) { throw new InvalidDataException("descriptionは1〜1024文字で、内容と使う場面を書いてください。"); }
        string compatibility = fields.GetValueOrDefault("compatibility", "");
        if (compatibility.Length > 500) { throw new InvalidDataException("compatibilityは500文字までです。"); }
        string body = string.Join('\n', lines.Skip(end + 1)).Trim();
        if (body.Length == 0) { throw new InvalidDataException("SKILL.mdに手順の本文を書いてください。"); }
        return new(name, description, body, markdown, fields.GetValueOrDefault("license", ""), compatibility, files);
    }
    private static bool StartsWithAny(this string value, params char[] values) => value.Length > 0 && values.Contains(value[0]);
    public static void ValidateName(string name)
    {
        if (name.Length is < 1 or > 64 || !Regex.IsMatch(name, "^[a-z0-9]+(?:-[a-z0-9]+)*$", RegexOptions.CultureInvariant))
        { throw new InvalidDataException("nameは64文字以内の英小文字・数字・ハイフンです。先頭・末尾・連続ハイフンは使えません。"); }
    }
    public static string Create(string name, string description, string instructions) =>
        "---\nname: " + name + "\ndescription: " + JsonSerializer.Serialize(description, new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }) + "\n---\n\n" + instructions;
    internal static string Update(SkillPackage original, string name, string description, string instructions)
    {
        string[] lines = original.Markdown.Split('\n'); int end = Array.FindIndex(lines, 1, line => line is "---" or "...");
        var optional = new List<string>();
        for (int i = 1; i < end; i++)
        {
            if (Regex.IsMatch(lines[i], "^(name|description):"))
            { while (i + 1 < end && (string.IsNullOrWhiteSpace(lines[i + 1]) || char.IsWhiteSpace(lines[i + 1][0]))) { i++; } }
            else { optional.Add(lines[i]); }
        }
        string minimal = Create(name, description, instructions);
        int separator = minimal.IndexOf("\n---\n\n", StringComparison.Ordinal);
        return minimal.Insert(separator, "\n" + string.Join('\n', optional));
    }
    public static string WithLicenseAndCompatibility(string markdown, string license, string compatibility)
    {
        string[] lines = markdown.Split('\n'); int end = Array.FindIndex(lines, 1, line => line is "---" or "...");
        if (end < 0) { throw new InvalidDataException("SKILL.mdのメタデータがありません。"); }
        var output = new List<string> { "---" };
        for (int i = 1; i < end; i++)
        {
            if (Regex.IsMatch(lines[i], "^(license|compatibility):")) { while (i + 1 < end && (string.IsNullOrWhiteSpace(lines[i + 1]) || char.IsWhiteSpace(lines[i + 1][0]))) { i++; } }
            else { output.Add(lines[i]); }
        }
        if (!string.IsNullOrWhiteSpace(license)) { output.Add("license: " + JsonSerializer.Serialize(license.Trim())); }
        if (!string.IsNullOrWhiteSpace(compatibility)) { output.Add("compatibility: " + JsonSerializer.Serialize(compatibility.Trim())); }
        output.AddRange(lines.Skip(end)); return string.Join('\n', output);
    }

}
