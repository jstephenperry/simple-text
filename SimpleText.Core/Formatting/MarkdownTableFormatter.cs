using System.Text.RegularExpressions;

namespace SimpleText.Core.Formatting;

/// <summary>
/// Aligns a GitHub-style Markdown pipe table. The second row is the delimiter
/// (<c>--- | :--: | --:</c>); its colons set per-column alignment, which is preserved and
/// applied to the cell padding. Columns are padded to their widest cell (minimum three, so
/// the delimiter stays readable). Leading/trailing pipes are normalized in.
/// </summary>
internal static class MarkdownTableFormatter
{
    private const int MinColumnWidth = 3;

    private static readonly Regex DelimiterCell = new(@"^:?-+:?$", RegexOptions.Compiled);

    public static IReadOnlyList<string>? Format(IReadOnlyList<string> block)
    {
        var rows = block.Select(line => TableFormatter.PipeCells(line.Trim()).ToArray()).ToList();

        int delimiter = rows.FindIndex(IsDelimiterRow);
        if (delimiter < 0) return null; // no delimiter row -> not a Markdown table

        var aligns = rows[delimiter].Select(Alignment).ToList();
        int columns = Math.Max(rows.Max(r => r.Length), aligns.Count);
        while (aligns.Count < columns) aligns.Add('n');

        var dataRows = rows.Where((_, i) => i != delimiter).ToList();
        var widths = TableFormatter.ColumnWidths(dataRows, columns, MinColumnWidth);

        var output = new List<string>(rows.Count);
        for (int r = 0; r < rows.Count; r++)
            output.Add(r == delimiter
                ? RenderDelimiter(widths, aligns)
                : RenderRow(rows[r], widths, aligns));
        return output;
    }

    private static bool IsDelimiterRow(string[] cells)
        => cells.Length > 0 && cells.All(c => DelimiterCell.IsMatch(c));

    private static char Alignment(string cell)
    {
        bool left = cell.StartsWith(':');
        bool right = cell.EndsWith(':');
        return (left, right) switch
        {
            (true, true) => 'c',
            (false, true) => 'r',
            (true, false) => 'l',
            _ => 'n',
        };
    }

    private static string RenderRow(string[] row, int[] widths, IReadOnlyList<char> aligns)
    {
        var cells = Enumerable.Range(0, widths.Length)
            .Select(c => TableFormatter.Pad(TableFormatter.CellAt(row, c), widths[c], aligns[c]));
        return "| " + string.Join(" | ", cells) + " |";
    }

    private static string RenderDelimiter(int[] widths, IReadOnlyList<char> aligns)
    {
        var cells = Enumerable.Range(0, widths.Length).Select(c =>
        {
            int w = widths[c];
            return aligns[c] switch
            {
                'c' => ":" + new string('-', w - 2) + ":",
                'l' => ":" + new string('-', w - 1),
                'r' => new string('-', w - 1) + ":",
                _ => new string('-', w),
            };
        });
        return "| " + string.Join(" | ", cells) + " |";
    }
}
