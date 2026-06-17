namespace SimpleText.Core.Formatting;

/// <summary>
/// Aligns an AsciiDoc table delimited by <c>|===</c> fences, where each row is written on a
/// single line as <c>| a | b</c>. The fences and any other lines inside the block (blank
/// separators, attribute lines) are passed through untouched; only the <c>|</c>-delimited
/// rows are re-spaced so their columns line up. AsciiDoc cells have no trailing pipe, so
/// rows are not right-padded.
/// </summary>
internal static class AsciiDocTableFormatter
{
    private const int MinColumnWidth = 1;

    public static IReadOnlyList<string>? Format(IReadOnlyList<string> block)
    {
        // block[0] and block[^1] are the |=== fences; rows live in between.
        var rowCells = new Dictionary<int, string[]>();
        for (int i = 1; i < block.Count - 1; i++)
            if (block[i].TrimStart().StartsWith('|'))
                rowCells[i] = TableFormatter.PipeCells(block[i].Trim()).ToArray();

        if (rowCells.Count == 0) return null;

        int columns = rowCells.Values.Max(r => r.Length);
        var widths = TableFormatter.ColumnWidths(rowCells.Values.ToList(), columns, MinColumnWidth);

        var output = new List<string> { block[0] };
        for (int i = 1; i < block.Count - 1; i++)
        {
            if (rowCells.TryGetValue(i, out var cells))
            {
                var padded = Enumerable.Range(0, columns)
                    .Select(c => TableFormatter.CellAt(cells, c).PadRight(widths[c]));
                output.Add(("| " + string.Join(" | ", padded)).TrimEnd());
            }
            else
            {
                output.Add(block[i]);
            }
        }
        output.Add(block[^1]);
        return output;
    }
}
