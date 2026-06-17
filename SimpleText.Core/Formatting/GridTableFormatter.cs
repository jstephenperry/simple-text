namespace SimpleText.Core.Formatting;

/// <summary>
/// Aligns an ASCII grid table — <c>+---+---+</c> borders with <c>| cell | cell |</c> rows.
/// This is the shape plain text uses (the Insert menu's ASCII table) and one of the two
/// reStructuredText table styles. Borders are regenerated from the column widths; a border
/// drawn with <c>=</c> (the reStructuredText header separator) is preserved in place. Any
/// common leading indentation is kept.
/// </summary>
internal static class GridTableFormatter
{
    private const int MinColumnWidth = 3;

    public static IReadOnlyList<string>? Format(IReadOnlyList<string> block)
    {
        string indent = LeadingWhitespace(block[0]);

        var rows = new List<string[]>();
        int headerRows = 0; // content rows above the '=' separator (0 = none)
        int seen = 0;
        foreach (var line in block)
        {
            if (IsBorder(line))
            {
                if (headerRows == 0 && line.Contains('=')) headerRows = seen;
            }
            else if (line.TrimStart().StartsWith('|'))
            {
                rows.Add(TableFormatter.PipeCells(line.Trim()).ToArray());
                seen++;
            }
        }
        if (rows.Count == 0) return null;

        int columns = rows.Max(r => r.Length);
        var widths = TableFormatter.ColumnWidths(rows, columns, MinColumnWidth);

        string Border(char fill) =>
            indent + "+" + string.Join("+", widths.Select(w => new string(fill, w + 2))) + "+";
        string Row(string[] row) =>
            indent + "|" + string.Join("|", Enumerable.Range(0, columns)
                .Select(c => " " + TableFormatter.CellAt(row, c).PadRight(widths[c]) + " ")) + "|";

        var output = new List<string> { Border('-') };
        for (int i = 0; i < rows.Count; i++)
        {
            output.Add(Row(rows[i]));
            output.Add(Border(i + 1 == headerRows ? '=' : '-'));
        }
        return output;
    }

    private static bool IsBorder(string line)
    {
        var t = line.Trim();
        return t.Length > 0 && t[0] == '+' && t.All(c => c is '+' or '-' or '=' or ' ');
    }

    private static string LeadingWhitespace(string line)
    {
        int i = 0;
        while (i < line.Length && (line[i] == ' ' || line[i] == '\t')) i++;
        return line[..i];
    }
}
