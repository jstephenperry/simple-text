namespace SimpleText.Core.Elements;

/// <summary>
/// Built-in palette of insertable markup elements, keyed by editor mode. Like the
/// insert menus of LaTeX/Markdown editors, each supported format exposes a curated
/// set of base building blocks (headings, lists, tables, code, links, math, …)
/// grouped by category. Plain text gets ASCII-convention blocks (underlined
/// headings, ASCII tables, rules, …) — useful as text even though nothing renders them.
///
/// <para>These are deliberately built in (not user-owned files like templates):
/// they are small, format-specific primitives the editor provides out of the box.
/// Element bodies use <c>\n</c> newlines and a single <see cref="DocumentElement.CaretMarker"/>
/// to mark where the caret should land after insertion.</para>
/// </summary>
public static class ElementCatalog
{
    private static readonly IReadOnlyList<DocumentElement> None = [];

    private static readonly IReadOnlyList<DocumentElement> PlainText = PlainTextElements();

    private static readonly IReadOnlyDictionary<string, IReadOnlyList<DocumentElement>> ByMode =
        new Dictionary<string, IReadOnlyList<DocumentElement>>
        {
            [TextModes.Markdown] = Markdown(),
            [TextModes.AsciiDoc] = AsciiDoc(),
            [TextModes.ReStructuredText] = ReStructuredText(),
        };

    /// <summary>
    /// Elements available for <paramref name="mode"/>, in display order (grouped by
    /// category by the caller). Plain text (a null mode) gets ASCII-convention
    /// elements; an unknown non-null mode returns none.
    /// </summary>
    public static IReadOnlyList<DocumentElement> ForMode(string? mode)
    {
        if (mode == null) return PlainText;
        return ByMode.TryGetValue(mode, out var list) ? list : None;
    }

    private static DocumentElement E(string category, string name, string template)
        => DocumentElement.Create(category, name, template);

    // Categories are kept contiguous so a stable GroupBy in the UI preserves this order.

    private static IReadOnlyList<DocumentElement> Markdown() =>
    [
        E("Headings", "Heading 1", "# \f"),
        E("Headings", "Heading 2", "## \f"),
        E("Headings", "Heading 3", "### \f"),

        E("Text", "Bold", "**\f**"),
        E("Text", "Italic", "*\f*"),
        E("Text", "Inline code", "`\f`"),
        E("Text", "Strikethrough", "~~\f~~"),

        E("Lists", "Bulleted list", "- \f\n- \n- "),
        E("Lists", "Numbered list", "1. \f\n2. \n3. "),
        E("Lists", "Task list", "- [ ] \f\n- [ ] "),

        E("Blocks", "Blockquote", "> \f"),
        E("Blocks", "Code block", "```\n\f\n```"),
        E("Blocks", "Table", "| \fHeader | Header |\n| --- | --- |\n| Cell | Cell |"),
        E("Blocks", "Horizontal rule", "---\n"),

        E("Links & media", "Link", "[\f](https://example.com)"),
        E("Links & media", "Image", "![\f](path/to/image.png)"),

        E("Math", "Inline equation", "$\f$"),
        E("Math", "Equation block", "$$\n\f\n$$"),
    ];

    private static IReadOnlyList<DocumentElement> AsciiDoc() =>
    [
        E("Headings", "Section level 1", "== \f"),
        E("Headings", "Section level 2", "=== \f"),

        E("Text", "Bold", "*\f*"),
        E("Text", "Italic", "_\f_"),
        E("Text", "Monospace", "`\f`"),

        E("Lists", "Bulleted list", "* \f\n* \n* "),
        E("Lists", "Numbered list", ". \f\n. \n. "),

        E("Blocks", "Source block", "[source,\f]\n----\n\n----"),
        E("Blocks", "Listing block", "----\n\f\n----"),
        E("Blocks", "Note", "[NOTE]\n====\n\f\n===="),
        E("Blocks", "Quote", "[quote]\n____\n\f\n____"),
        E("Blocks", "Table", "[cols=\"1,1\"]\n|===\n| \f | \n| | \n|==="),
        E("Blocks", "Horizontal rule", "'''\n"),

        E("Links & media", "Link", "link:https://example.com[\f]"),
        E("Links & media", "Image", "image::\f[alt]"),

        E("Math", "Inline equation", "latexmath:[\f]"),
        E("Math", "Equation block", "[latexmath]\n++++\n\f\n++++"),
    ];

    private static IReadOnlyList<DocumentElement> ReStructuredText() =>
    [
        // rst headings underline the title; the placeholder underline matches the
        // placeholder word, so rename both together (the underline must be >= the title).
        E("Headings", "Section title", "\fTitle\n====="),
        E("Headings", "Subsection title", "\fSubtitle\n--------"),

        E("Text", "Bold", "**\f**"),
        E("Text", "Italic", "*\f*"),
        E("Text", "Inline code", "``\f``"),

        E("Lists", "Bulleted list", "- \f\n- \n- "),
        E("Lists", "Numbered list", "#. \f\n#. \n#. "),

        E("Blocks", "Code block", ".. code-block:: \f\n\n   "),
        E("Blocks", "Note", ".. note::\n\n   \f"),
        E("Blocks", "Literal block", "::\n\n   \f"),
        E("Blocks", "Transition", "\n----\n"),

        E("Links & media", "Link", "`\f <https://example.com>`_"),
        E("Links & media", "Image", ".. image:: \f"),

        E("Math", "Inline equation", ":math:`\f`"),
        E("Math", "Equation block", ".. math::\n\n   \f"),
    ];

    // Plain text has no renderer, so these are conventions/ASCII art. The table and
    // box are built programmatically so their borders always line up.
    private static IReadOnlyList<DocumentElement> PlainTextElements()
    {
        const char caret = DocumentElement.CaretMarker;
        const int col = 10;

        string Cell(string s) => " " + s + new string(' ', col - 1 - s.Length);

        string rule = new string('-', 60) + "\n";

        const int boxWidth = 30;
        string boxBorder = "+" + new string('-', boxWidth + 2) + "+";
        string box = string.Join("\n",
            boxBorder,
            "| " + caret + new string(' ', boxWidth) + " |",
            boxBorder);

        string sep = "+" + new string('-', col) + "+" + new string('-', col) + "+";
        string headerCaretCell = " " + caret + "Header" + new string(' ', col - 1 - "Header".Length);
        string table = string.Join("\n",
            sep,
            "|" + headerCaretCell + "|" + Cell("Header") + "|",
            sep,
            "|" + Cell("Cell") + "|" + Cell("Cell") + "|",
            sep);

        return
        [
            E("Headings", "Title (underlined)", "\fTitle\n====="),
            E("Headings", "Subtitle (underlined)", "\fSubtitle\n--------"),

            E("Lists", "Bulleted list", "- \f\n- \n- "),
            E("Lists", "Numbered list", "1. \f\n2. \n3. "),
            E("Lists", "Checklist", "[ ] \f\n[ ] \n[ ] "),

            E("Blocks", "Horizontal rule", rule),
            E("Blocks", "ASCII table", table),
            E("Blocks", "Boxed text", box),
            E("Blocks", "Block quote", "> \f"),

            E("Notes", "TODO", "TODO: \f"),
            E("Notes", "Note", "NOTE: \f"),
        ];
    }
}
