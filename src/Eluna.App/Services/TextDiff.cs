namespace Eluna.App.Services;

public sealed record DiffLine(char Kind, string Text);
public static class TextDiff
{
    // Bounded Myers search. Very different files fall back to an exact replacement.
    public static IReadOnlyList<DiffLine> Compare(string before, string after, CancellationToken token = default)
    {
        string[] a = before.Length == 0 ? [] : before.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'), b = after.Length == 0 ? [] : after.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        int prefix = 0, suffix = 0;
        while (prefix < a.Length && prefix < b.Length && a[prefix] == b[prefix]) { prefix++; }
        while (suffix < a.Length - prefix && suffix < b.Length - prefix && a[^(suffix + 1)] == b[^(suffix + 1)]) { suffix++; }
        var result = a.Take(prefix).Select(s => new DiffLine(' ', s)).ToList();
        string[] old = a.Skip(prefix).Take(a.Length - prefix - suffix).ToArray(), next = b.Skip(prefix).Take(b.Length - prefix - suffix).ToArray();
        var trace = new List<Dictionary<int, int>>(); var frontier = new Dictionary<int, int> { [1] = 0 };
        bool found = false;
        for (int d = 0; d <= Math.Min(512, old.Length + next.Length); d++)
        {
            token.ThrowIfCancellationRequested(); trace.Add(new(frontier));
            for (int k = -d; k <= d; k += 2)
            {
                int x = k == -d || k != d && frontier.GetValueOrDefault(k - 1) < frontier.GetValueOrDefault(k + 1) ? frontier.GetValueOrDefault(k + 1) : frontier.GetValueOrDefault(k - 1) + 1;
                int y = x - k; while (x < old.Length && y < next.Length && old[x] == next[y]) { x++; y++; }
                frontier[k] = x;
                if (x >= old.Length && y >= next.Length)
                {
                    var middle = new List<DiffLine>(); int px = old.Length, py = next.Length;
                    for (int depth = d; depth >= 0; depth--)
                    {
                        var prior = trace[depth]; int diagonal = px - py;
                        int previous = diagonal == -depth || diagonal != depth && prior.GetValueOrDefault(diagonal - 1) < prior.GetValueOrDefault(diagonal + 1) ? diagonal + 1 : diagonal - 1;
                        int previousX = prior.GetValueOrDefault(previous), previousY = previousX - previous;
                        while (px > previousX && py > previousY) { middle.Add(new(' ', old[--px])); py--; }
                        if (depth == 0) { break; }
                        if (px == previousX) { middle.Add(new('+', next[--py])); } else { middle.Add(new('-', old[--px])); }
                    }
                    middle.Reverse(); result.AddRange(middle); found = true; break;
                }
            }
            if (found) { break; }
        }
        if (!found) { result.AddRange(old.Select(s => new DiffLine('-', s))); result.AddRange(next.Select(s => new DiffLine('+', s))); }
        result.AddRange(a.Skip(a.Length - suffix).Select(s => new DiffLine(' ', s))); return result;
    }
}
