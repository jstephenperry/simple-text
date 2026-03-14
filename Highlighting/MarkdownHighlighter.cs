using System.Text.RegularExpressions;

namespace SimpleText.Highlighting;

internal sealed class MarkdownHighlighter : ISyntaxHighlighter
{
    public string FileTypeName => "Markdown";

    private static readonly Color HeadingColor = Color.FromArgb(0, 100, 180);
    private static readonly Color CodeColor = Color.FromArgb(180, 60, 0);
    private static readonly Color LinkColor = Color.FromArgb(0, 130, 100);
    private static readonly Color ListColor = Color.FromArgb(160, 80, 160);
    private static readonly Color QuoteColor = Color.FromArgb(130, 130, 130);

    private static readonly (Regex Pattern, Color Color, FontStyle Style)[] Rules =
    [
        // Code fences (before other rules to avoid conflicts)
        (new Regex(@"^```.*$", RegexOptions.Multiline | RegexOptions.Compiled), CodeColor, FontStyle.Regular),
        // Headings
        (new Regex(@"^#{1,6}\s+.+$", RegexOptions.Multiline | RegexOptions.Compiled), HeadingColor, FontStyle.Bold),
        // Bold
        (new Regex(@"\*\*[^*\n]+\*\*", RegexOptions.Compiled), Color.Black, FontStyle.Bold),
        (new Regex(@"__[^_\n]+__", RegexOptions.Compiled), Color.Black, FontStyle.Bold),
        // Italic
        (new Regex(@"(?<!\*)\*(?!\*)[^*\n]+\*(?!\*)", RegexOptions.Compiled), Color.Black, FontStyle.Italic),
        (new Regex(@"(?<!_)_(?!_)[^_\n]+_(?!_)", RegexOptions.Compiled), Color.Black, FontStyle.Italic),
        // Inline code
        (new Regex(@"`[^`\n]+`", RegexOptions.Compiled), CodeColor, FontStyle.Regular),
        // Links
        (new Regex(@"\[[^\]\n]+\]\([^)\n]+\)", RegexOptions.Compiled), LinkColor, FontStyle.Regular),
        // List markers
        (new Regex(@"^\s*[-*+]\s", RegexOptions.Multiline | RegexOptions.Compiled), ListColor, FontStyle.Regular),
        (new Regex(@"^\s*\d+\.\s", RegexOptions.Multiline | RegexOptions.Compiled), ListColor, FontStyle.Regular),
        // Blockquotes
        (new Regex(@"^>\s?.*$", RegexOptions.Multiline | RegexOptions.Compiled), QuoteColor, FontStyle.Italic),
    ];

    public List<HighlightSpan> GetHighlights(string text, int startOffset, int length)
    {
        var region = text.Substring(startOffset, length);
        var spans = new List<HighlightSpan>();

        foreach (var (pattern, color, style) in Rules)
        {
            foreach (Match match in pattern.Matches(region))
            {
                spans.Add(new HighlightSpan(
                    startOffset + match.Index,
                    match.Length,
                    color,
                    style));
            }
        }

        return spans;
    }
}
