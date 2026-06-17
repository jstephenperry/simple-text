using SimpleText.Core;
using SimpleText.Core.Formatting;
using Xunit;

namespace SimpleText.Core.Tests;

public class TableFormatterTests
{
    // Formats the whole text (caret on the first line) and returns the aligned block text.
    private static string Align(string table, string? mode)
    {
        var edit = TableFormatter.Format(table, 0, mode);
        Assert.NotNull(edit);
        return edit.Value.Text;
    }

    // Applies an edit to produce the full resulting document.
    private static (string Text, TableEdit Edit) Apply(string text, int caret, string? mode)
    {
        var edit = TableFormatter.Format(text, caret, mode);
        Assert.NotNull(edit);
        var e = edit.Value;
        return (text[..e.Start] + e.Text + text[(e.Start + e.Length)..], e);
    }

    // --- Markdown ----------------------------------------------------------

    [Fact]
    public void Markdown_aligns_columns_to_widest_cell()
    {
        var input =
            "| Name | Role |\n" +
            "| --- | --- |\n" +
            "| Alice | Engineer |";

        var expected =
            "| Name  | Role     |\n" +
            "| ----- | -------- |\n" +
            "| Alice | Engineer |";

        Assert.Equal(expected, Align(input, TextModes.Markdown));
    }

    [Fact]
    public void Markdown_pads_ragged_rows_and_keeps_all_cells()
    {
        var input =
            "| a | b | c |\n" +
            "| - | - | - |\n" +
            "| 1 | 2 |\n" +          // short row
            "| x | y | z | extra |"; // long row keeps the extra cell

        var result = Align(input, TextModes.Markdown);
        var lines = result.Split('\n');

        Assert.All(lines, l => Assert.Equal(lines[0].Length, l.Length)); // every row same width
        Assert.Equal(4, lines.Length);
        Assert.Contains("extra", result);                 // no data lost
        Assert.Contains("|       |", lines[2]);            // the missing cell became blank
    }

    [Fact]
    public void Markdown_preserves_alignment_markers()
    {
        var input =
            "| left | center | right |\n" +
            "| :--- | :----: | ----: |\n" +
            "| a | b | c |";

        var rows = Align(input, TextModes.Markdown).Split('\n');

        Assert.Equal("| left | center | right |", rows[0]);
        Assert.Equal("| :--- | :----: | ----: |", rows[1]); // colons (alignment) preserved
        Assert.Equal("| a    |   b    |     c |", rows[2]);  // left / center / right padding
    }

    [Fact]
    public void Markdown_without_delimiter_row_is_not_a_table()
    {
        var input =
            "| a | b |\n" +
            "| c | d |";
        Assert.Null(TableFormatter.Format(input, 0, TextModes.Markdown));
    }

    // --- Grid (plain text and reStructuredText grid) -----------------------

    [Fact]
    public void Grid_regenerates_borders_to_fit_content()
    {
        var input =
            "+----------+----------+\n" +
            "| Header   | Header |\n" +
            "+----------+----------+\n" +
            "| Cell | A longer cell |\n" +
            "+----------+----------+";

        var expected =
            "+--------+---------------+\n" +
            "| Header | Header        |\n" +
            "+--------+---------------+\n" +
            "| Cell   | A longer cell |\n" +
            "+--------+---------------+";

        Assert.Equal(expected, Align(input, null)); // plain text mode
    }

    [Fact]
    public void Grid_preserves_the_rst_header_separator()
    {
        var input =
            "+-----+-------+\n" +
            "| A | B |\n" +
            "+=====+=======+\n" +
            "| c | dddddd |\n" +
            "+-----+-------+";

        var expected =
            "+-----+--------+\n" +
            "| A   | B      |\n" +
            "+=====+========+\n" +
            "| c   | dddddd |\n" +
            "+-----+--------+";

        Assert.Equal(expected, Align(input, TextModes.ReStructuredText));
    }

    // --- AsciiDoc ----------------------------------------------------------

    [Fact]
    public void AsciiDoc_aligns_rows_within_the_fences()
    {
        var input =
            "|===\n" +
            "| Name | Role\n" +
            "| Alice | Engineer\n" +
            "| Bob | Product Manager\n" +
            "|===";

        var expected =
            "|===\n" +
            "| Name  | Role\n" +
            "| Alice | Engineer\n" +
            "| Bob   | Product Manager\n" +
            "|===";

        Assert.Equal(expected, Align(input, TextModes.AsciiDoc));
    }

    [Fact]
    public void AsciiDoc_only_reformats_inside_the_fence_pair()
    {
        // Caret in open prose between two tables: nothing to align.
        var text =
            "|===\n| a | b\n|===\n" +
            "\nmiddle\n\n" +
            "|===\n| c | d\n|===\n";
        int caret = text.IndexOf("middle", StringComparison.Ordinal);
        Assert.Null(TableFormatter.Format(text, caret, TextModes.AsciiDoc));
    }

    // --- reStructuredText simple tables ------------------------------------

    [Fact]
    public void RstSimple_aligns_header_and_body()
    {
        var input =
            "=====  =====\n" +
            "Name   Role\n" +
            "=====  =====\n" +
            "Alice  Engineer\n" +
            "Bob    PM\n" +
            "=====  =====";

        var expected =
            "=====  ========\n" +
            "Name   Role\n" +
            "=====  ========\n" +
            "Alice  Engineer\n" +
            "Bob    PM\n" +
            "=====  ========";

        Assert.Equal(expected, Align(input, TextModes.ReStructuredText));
    }

    [Fact]
    public void RstSimple_realigns_drifted_columns()
    {
        // Cells are no longer under their rules — exactly when you reach for the command.
        var input =
            "=====  =====\n" +
            "Name  Role\n" +
            "=====  =====\n" +
            "Alice   Engineer\n" +
            "Bob  PM\n" +
            "=====  =====";

        var expected =
            "=====  ========\n" +
            "Name   Role\n" +
            "=====  ========\n" +
            "Alice  Engineer\n" +
            "Bob    PM\n" +
            "=====  ========";

        Assert.Equal(expected, Align(input, TextModes.ReStructuredText));
    }

    [Fact]
    public void RstSimple_keeps_spaces_in_the_last_column()
    {
        var input =
            "==  ==\n" +
            "id  note\n" +
            "1   a short note here\n" +
            "==  ==";

        var result = Align(input, TextModes.ReStructuredText);
        Assert.Contains("a short note here", result);
    }

    // --- Idempotency -------------------------------------------------------

    [Theory]
    [InlineData("| a | b |\n| :- | -: |\n| 1 | 22 |", TextModes.Markdown)]
    [InlineData("+---+---+\n| a | b |\n+---+---+\n| c | d |\n+---+---+", null)]
    [InlineData("|===\n| a | b\n| cc | dd\n|===", TextModes.AsciiDoc)]
    [InlineData("==  ==\na  bbbb\nccc  d\n==  ==", TextModes.ReStructuredText)]
    public void Aligning_twice_changes_nothing(string table, string? mode)
    {
        var once = Align(table, mode);
        var twice = Align(once, mode);
        Assert.Equal(once, twice);
    }

    // --- Detection / no-ops ------------------------------------------------

    [Fact]
    public void Returns_null_when_caret_is_not_in_a_table()
    {
        Assert.Null(TableFormatter.Format("just some prose\nmore prose", 0, TextModes.Markdown));
        Assert.Null(TableFormatter.Format("", 0, TextModes.Markdown));
        Assert.Null(TableFormatter.Format("   \n   ", 0, null));
    }

    // --- Edit application --------------------------------------------------

    [Fact]
    public void Edit_replaces_only_the_table_and_leaves_surrounding_text()
    {
        var text =
            "# Title\n" +
            "\n" +
            "| a | bbbb |\n" +
            "| - | - |\n" +
            "| cc | d |\n" +
            "\n" +
            "Trailing paragraph.\n";

        int caret = text.IndexOf("bbbb", StringComparison.Ordinal);
        var (result, edit) = Apply(text, caret, TextModes.Markdown);

        Assert.StartsWith("# Title\n\n", result);
        Assert.EndsWith("\nTrailing paragraph.\n", result);
        Assert.Contains("| a   | bbbb |", result);   // aligned
        Assert.Contains("| cc  | d    |", result);

        // Caret lands inside the replaced, aligned table.
        Assert.InRange(edit.CaretOffset, edit.Start, edit.Start + edit.Text.Length);
    }

    [Fact]
    public void Crlf_documents_keep_crlf_line_endings()
    {
        var text = "intro\r\n| a | b |\r\n| - | - |\r\n| cc | dd |\r\n";
        int caret = text.IndexOf("a |", StringComparison.Ordinal);

        var edit = TableFormatter.Format(text, caret, TextModes.Markdown);
        Assert.NotNull(edit);
        Assert.Contains("\r\n", edit.Value.Text);
        Assert.DoesNotContain("\n", edit.Value.Text.Replace("\r\n", ""));
    }
}
