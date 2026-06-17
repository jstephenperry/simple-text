using System.Text.RegularExpressions;

namespace SimpleText.Core.Formatting;

/// <summary>
/// A targeted edit that re-aligns the table under the caret: replace
/// <see cref="Length"/> characters at <see cref="Start"/> with <see cref="Text"/>,
/// then move the caret to <see cref="CaretOffset"/>. All offsets are in the same
/// '\n'-normalized space the editors expose through their <c>GetText()</c>.
/// </summary>
public readonly record struct TableEdit(int Start, int Length, string Text, int CaretOffset);

/// <summary>
/// Re-flows the source of a table so its columns line up, for every format the editor
/// supports. The shape is detected from the editor <c>mode</c> and the lines around the
/// caret — Markdown pipe tables, AsciiDoc <c>|===</c> tables, reStructuredText grid and
/// simple tables, and the ASCII grid tables plain text uses (the same kind the Insert
/// menu drops in). Cell contents are preserved; only padding, separators, and borders are
/// rewritten, so re-aligning an already-aligned table is a no-op.
///
/// <para>This is pure text-in/text-out logic with no UI dependency: each frontend reads
/// the document and caret, calls <see cref="Format"/>, and applies the returned
/// <see cref="TableEdit"/>. <see cref="Format"/> returns <c>null</c> when the caret is not
/// inside a table the active mode knows how to align.</para>
/// </summary>
public static class TableFormatter
{
    /// <summary>
    /// Computes the edit that re-aligns the table containing <paramref name="caretOffset"/>
    /// in <paramref name="text"/>, interpreted for <paramref name="mode"/> (a
    /// <see cref="TextModes"/> value, or <c>null</c> for plain text). Returns <c>null</c>
    /// when there is no alignable table at the caret.
    /// </summary>
    public static TableEdit? Format(string text, int caretOffset, string? mode)
    {
        if (string.IsNullOrEmpty(text)) return null;

        var doc = new SourceLines(text);
        int caret = Math.Clamp(caretOffset, 0, text.Length);
        int caretLine = doc.LineIndexOf(caret);

        Block? block = mode switch
        {
            TextModes.Markdown => Detect(doc, caretLine, MarkdownTableFormatter.Format, IsPipeLine),
            TextModes.AsciiDoc => DetectAsciiDoc(doc, caretLine),
            TextModes.ReStructuredText => DetectRst(doc, caretLine),
            _ => Detect(doc, caretLine, GridTableFormatter.Format, IsGridLine), // plain text
        };

        if (block is not { } b) return null;
        return BuildEdit(doc, b.Start, b.End, b.Lines, caret, caretLine);
    }

    // --- Detection ---------------------------------------------------------

    /// <summary>A detected table: its line range and its already-aligned replacement lines.</summary>
    private readonly record struct Block(int Start, int End, IReadOnlyList<string> Lines);

    private delegate IReadOnlyList<string>? BlockFormatter(IReadOnlyList<string> block);

    /// <summary>
    /// Expands a contiguous run of lines satisfying <paramref name="isMember"/> around the
    /// caret, then asks <paramref name="format"/> to align it. Returns the line range and
    /// the aligned lines, or <c>null</c> if the caret line is not a member or the block is
    /// not a valid table.
    /// </summary>
    private static Block? Detect(
        SourceLines doc, int caretLine, BlockFormatter format, Func<string, bool> isMember)
    {
        if (!isMember(doc.Logical(caretLine))) return null;

        int start = caretLine, end = caretLine;
        while (start > 0 && isMember(doc.Logical(start - 1))) start--;
        while (end < doc.Count - 1 && isMember(doc.Logical(end + 1))) end++;

        var formatted = format(doc.LogicalRange(start, end));
        if (formatted == null) return null;
        return new Block(start, end, formatted);
    }

    /// <summary>Finds the <c>|===</c> fence pair that encloses the caret line.</summary>
    private static Block? DetectAsciiDoc(SourceLines doc, int caretLine)
    {
        var fences = new List<int>();
        for (int i = 0; i < doc.Count; i++)
            if (IsAsciiDocFence(doc.Logical(i))) fences.Add(i);

        // Fences pair up open/close; find the pair that brackets the caret.
        for (int i = 0; i + 1 < fences.Count; i += 2)
        {
            int open = fences[i], close = fences[i + 1];
            if (caretLine < open || caretLine > close) continue;
            var formatted = AsciiDocTableFormatter.Format(doc.LogicalRange(open, close));
            if (formatted == null) return null;
            return new Block(open, close, formatted);
        }
        return null;
    }

    /// <summary>reStructuredText has two table styles: grid (<c>+--+</c>/<c>|</c>) and simple (<c>===</c>).</summary>
    private static Block? DetectRst(SourceLines doc, int caretLine)
    {
        if (IsGridLine(doc.Logical(caretLine)))
            return Detect(doc, caretLine, GridTableFormatter.Format, IsGridLine);

        // Simple tables are bounded by blank lines; the formatter validates the === rules.
        if (doc.Logical(caretLine).Trim().Length == 0) return null;
        int start = caretLine, end = caretLine;
        while (start > 0 && doc.Logical(start - 1).Trim().Length > 0) start--;
        while (end < doc.Count - 1 && doc.Logical(end + 1).Trim().Length > 0) end++;

        var formatted = RstSimpleTableFormatter.Format(doc.LogicalRange(start, end));
        if (formatted == null) return null;
        return new Block(start, end, formatted);
    }

    private static bool IsPipeLine(string line)
    {
        var t = line.Trim();
        return t.Length > 0 && t.Contains('|');
    }

    private static bool IsGridLine(string line)
    {
        var t = line.TrimStart();
        return t.Length > 0 && (t[0] == '+' || t[0] == '|');
    }

    private static readonly Regex AsciiDocFence = new(@"^\|=+$", RegexOptions.Compiled);

    private static bool IsAsciiDocFence(string line) => AsciiDocFence.IsMatch(line.Trim());

    // --- Edit assembly -----------------------------------------------------

    private static TableEdit BuildEdit(
        SourceLines doc, int blockStart, int blockEnd,
        IReadOnlyList<string> formatted, int caret, int caretLine)
    {
        int start = doc.Start(blockStart);
        // Stop before any trailing '\r' on the last line so a '\r\n' document keeps its line
        // ending after the table (the replacement re-joins with the document's own EOL).
        int length = doc.Start(blockEnd) + doc.Logical(blockEnd).Length - start;
        string replacement = string.Join(doc.Eol, formatted);

        // Keep the caret on the same row of the table when we can, at a clamped column.
        int row = Math.Clamp(caretLine - blockStart, 0, formatted.Count - 1);
        int column = caret - doc.Start(caretLine);

        int caretOffset = start;
        for (int i = 0; i < row; i++)
            caretOffset += formatted[i].Length + doc.Eol.Length;
        caretOffset += Math.Clamp(column, 0, formatted[row].Length);

        return new TableEdit(start, length, replacement, caretOffset);
    }

    // --- Shared cell helpers (used by the per-format formatters) -----------

    /// <summary>
    /// Splits a pipe-delimited row into cells, honoring <c>\|</c> escapes, and drops the
    /// empty cells produced by a leading and/or trailing pipe. Cells are returned trimmed.
    /// </summary>
    internal static List<string> PipeCells(string line)
    {
        var raw = SplitEscapedPipes(line);
        if (line.StartsWith('|') && raw.Count > 0 && raw[0].Length == 0)
            raw.RemoveAt(0);
        if (line.EndsWith('|') && raw.Count > 0 && raw[^1].Length == 0)
            raw.RemoveAt(raw.Count - 1);
        for (int i = 0; i < raw.Count; i++)
            raw[i] = raw[i].Trim();
        return raw;
    }

    private static List<string> SplitEscapedPipes(string s)
    {
        var cells = new List<string>();
        var current = new System.Text.StringBuilder();
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] == '\\' && i + 1 < s.Length && s[i + 1] == '|')
            {
                current.Append('|');
                i++;
            }
            else if (s[i] == '|')
            {
                cells.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(s[i]);
            }
        }
        cells.Add(current.ToString());
        return cells;
    }

    /// <summary>Cell at <paramref name="column"/>, or empty when the row is shorter.</summary>
    internal static string CellAt(IReadOnlyList<string> row, int column)
        => column < row.Count ? row[column] : string.Empty;

    internal static int[] ColumnWidths(IReadOnlyList<string[]> rows, int columns, int minimum)
    {
        var widths = new int[columns];
        for (int c = 0; c < columns; c++) widths[c] = minimum;
        foreach (var row in rows)
            for (int c = 0; c < row.Length; c++)
                widths[c] = Math.Max(widths[c], row[c].Length);
        return widths;
    }

    /// <summary>Pads <paramref name="value"/> to <paramref name="width"/>: 'l'/'n' left, 'r' right, 'c' centered.</summary>
    internal static string Pad(string value, int width, char align)
    {
        int diff = width - value.Length;
        if (diff <= 0) return value;
        return align switch
        {
            'r' => new string(' ', diff) + value,
            'c' => new string(' ', diff / 2) + value + new string(' ', diff - diff / 2),
            _ => value + new string(' ', diff),
        };
    }

    /// <summary>
    /// A document split into lines on '\n', tracking each line's start offset and raw length
    /// so edits map back to the editor's character offsets regardless of line endings. The
    /// "logical" view of a line drops a trailing '\r' so '\r\n' documents parse the same.
    /// </summary>
    private sealed class SourceLines
    {
        private readonly string[] _content; // line text without the '\n', may keep a trailing '\r'
        private readonly int[] _start;

        public string Eol { get; }

        public int Count => _content.Length;

        public SourceLines(string text)
        {
            var content = new List<string>();
            var starts = new List<int> { 0 };
            int begin = 0;
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] != '\n') continue;
                content.Add(text.Substring(begin, i - begin));
                begin = i + 1;
                starts.Add(begin);
            }
            content.Add(text.Substring(begin));

            _content = content.ToArray();
            _start = starts.ToArray();
            Eol = text.Contains("\r\n") ? "\r\n" : "\n";
        }

        public int Start(int line) => _start[line];

        public string Logical(int line) => _content[line].TrimEnd('\r');

        public IReadOnlyList<string> LogicalRange(int start, int end)
        {
            var range = new string[end - start + 1];
            for (int i = start; i <= end; i++) range[i - start] = Logical(i);
            return range;
        }

        public int LineIndexOf(int offset)
        {
            int index = Array.BinarySearch(_start, offset);
            if (index < 0) index = ~index - 1;
            return Math.Clamp(index, 0, _content.Length - 1);
        }
    }
}
