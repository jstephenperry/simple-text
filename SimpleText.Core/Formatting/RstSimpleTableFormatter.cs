namespace SimpleText.Core.Formatting;

/// <summary>
/// Aligns a reStructuredText "simple" table — rows bounded by <c>===  ===</c> rule lines,
/// with columns separated by runs of two or more spaces:
/// <code>
/// =====  =====
/// Name   Role
/// =====  =====
/// Alice  Engineer
/// =====  =====
/// </code>
/// The number of columns comes from the top rule; each data row is split on its gaps (so a
/// table whose cells have drifted out of alignment still parses), and the last column keeps
/// any internal spaces. An optional header (one rule between the top and bottom) is kept.
/// Returns <c>null</c> if the block is not bounded by rules or has an unexpected structure.
/// </summary>
internal static class RstSimpleTableFormatter
{
    private const int MinColumnWidth = 1;

    public static IReadOnlyList<string>? Format(IReadOnlyList<string> block)
    {
        var lines = block.Where(l => l.Trim().Length > 0).ToList();
        if (lines.Count < 2) return null;
        if (!IsRule(lines[0]) || !IsRule(lines[^1])) return null;

        int columns = CountRuns(lines[0].Trim());
        if (columns == 0) return null;

        var middles = new List<int>();
        for (int i = 1; i < lines.Count - 1; i++)
            if (IsRule(lines[i])) middles.Add(i);
        if (middles.Count > 1) return null; // more than a single header separator: leave alone

        string[] Parse(string line) => SplitOnGaps(line.Trim(), columns);

        List<string[]> header, body;
        if (middles.Count == 1)
        {
            int m = middles[0];
            header = Range(lines, 1, m).Select(Parse).ToList();
            body = Range(lines, m + 1, lines.Count - 1).Select(Parse).ToList();
        }
        else
        {
            header = [];
            body = Range(lines, 1, lines.Count - 1).Select(Parse).ToList();
        }

        var widths = TableFormatter.ColumnWidths(header.Concat(body).ToList(), columns, MinColumnWidth);

        string Rule() => string.Join("  ", widths.Select(w => new string('=', w)));
        string Row(string[] r) => string.Join("  ", Enumerable.Range(0, columns)
            .Select(c => TableFormatter.CellAt(r, c).PadRight(widths[c]))).TrimEnd();

        var output = new List<string> { Rule() };
        output.AddRange(header.Select(Row));
        if (header.Count > 0) output.Add(Rule());
        output.AddRange(body.Select(Row));
        output.Add(Rule());
        return output;
    }

    private static bool IsRule(string line)
    {
        var t = line.Trim();
        return t.Length > 0 && t.Contains('=') && t.All(c => c is '=' or ' ');
    }

    private static int CountRuns(string rule)
    {
        int runs = 0;
        bool inRun = false;
        foreach (var c in rule)
        {
            if (c == '=' && !inRun) { runs++; inRun = true; }
            else if (c != '=') inRun = false;
        }
        return runs;
    }

    /// <summary>Splits on runs of 2+ spaces into at most <paramref name="max"/> trimmed cells; the last keeps the remainder.</summary>
    private static string[] SplitOnGaps(string s, int max)
    {
        var parts = new List<string>();
        int i = 0;
        while (i < s.Length && parts.Count < max - 1)
        {
            int gap = FindGap(s, i);
            if (gap < 0) break;
            parts.Add(s[i..gap].Trim());
            int j = gap;
            while (j < s.Length && s[j] == ' ') j++;
            i = j;
        }
        parts.Add(s[i..].Trim());
        return parts.ToArray();
    }

    private static int FindGap(string s, int from)
    {
        for (int k = from; k + 1 < s.Length; k++)
            if (s[k] == ' ' && s[k + 1] == ' ') return k;
        return -1;
    }

    private static IEnumerable<string> Range(List<string> lines, int start, int endExclusive)
    {
        for (int i = start; i < endExclusive; i++) yield return lines[i];
    }
}
