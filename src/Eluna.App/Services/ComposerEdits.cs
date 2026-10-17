namespace Eluna.App.Services;

internal sealed record ComposerSnapshot(Guid Room, Guid Branch, string Text, int Start, int Length);
internal sealed record ComposerEdit(int Start, int Length, string Value, int SelectionStart, int SelectionLength)
{
    internal string Apply(string text) => text.Remove(Start, Length).Insert(Start, Value);
}
internal static class ComposerEdits
{
    internal static ComposerEdit Insert(ComposerSnapshot snapshot, string value) => new(snapshot.Start, snapshot.Length, value, snapshot.Start + value.Length, 0);
    internal static ComposerEdit Format(ComposerSnapshot s, string kind)
    {
        string selected = s.Text.Substring(s.Start, s.Length);
        if (kind is "quote" or "list")
        {
            int start = s.Text.LastIndexOf('\n', Math.Max(0, s.Start - 1), Math.Max(0, s.Start)); start = start < 0 ? 0 : start + 1;
            int end = s.Start + s.Length; if (s.Length > 0 && end > start && s.Text[end - 1] == '\n') { end--; if (end > start && s.Text[end - 1] == '\r') { end--; } } else { int next = s.Text.IndexOf('\n', end); end = next < 0 ? s.Text.Length : next; }
            string text = s.Text[start..end], prefix = kind == "quote" ? "> " : "- "; string[] lines = text.Split('\n'); bool remove = lines.All(l => l.StartsWith(prefix, StringComparison.Ordinal));
            string value = string.Join('\n', lines.Select(l => remove ? l[prefix.Length..] : l.StartsWith(prefix, StringComparison.Ordinal) ? l : prefix + l));
            int caret = Math.Clamp(s.Start + (remove ? -prefix.Length : prefix.Length), start, start + value.Length); return new(start, end - start, value, s.Length == 0 ? caret : start, s.Length == 0 ? 0 : value.Length);
        }
        (string before, string after) = kind switch { "bold" => ("**", "**"), "italic" => ("*", "*"), "strike" => ("~~", "~~"), "code" => ("`", "`"), "block" => ("```" + Environment.NewLine, Environment.NewLine + "```"), _ => throw new ArgumentOutOfRangeException(nameof(kind)) };
        if (selected.Length >= before.Length + after.Length && selected.StartsWith(before, StringComparison.Ordinal) && selected.EndsWith(after, StringComparison.Ordinal))
        { string value = selected[before.Length..^after.Length]; return new(s.Start, s.Length, value, s.Start, value.Length); }
        if (s.Start >= before.Length && s.Start + s.Length + after.Length <= s.Text.Length && s.Text.AsSpan(s.Start - before.Length, before.Length).SequenceEqual(before) && s.Text.AsSpan(s.Start + s.Length, after.Length).SequenceEqual(after))
        { return new(s.Start - before.Length, s.Length + before.Length + after.Length, selected, s.Start - before.Length, s.Length); }
        return new(s.Start, s.Length, before + selected + after, s.Start + before.Length, s.Length);
    }
}
