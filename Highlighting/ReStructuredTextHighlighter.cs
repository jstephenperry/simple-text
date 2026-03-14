using System.Text.RegularExpressions;

namespace SimpleText.Highlighting;

internal sealed class ReStructuredTextHighlighter : ISyntaxHighlighter
{
    public string FileTypeName => "reStructuredText";

    private static readonly Color HeadingColor = Color.FromArgb(0, 100, 180);
    private static readonly Color CodeColor = Color.FromArgb(180, 60, 0);
    private static readonly Color LinkColor = Color.FromArgb(0, 130, 100);
    private static readonly Color ListColor = Color.FromArgb(160, 80, 160);
    private static readonly Color DirectiveColor = Color.FromArgb(180, 120, 0);

    private static readonly (Regex Pattern, Color Color, FontStyle Style)[] Rules =
    [
        // Heading underlines (lines of = - ~ ^ " ` # * +)
        (new Regex(@"^[=\-~\^""`#\*\+]{2,}\s*$", RegexOptions.Multiline | RegexOptions.Compiled), HeadingColor, FontStyle.Bold),
        // Directives
        (new Regex(@"^\.\.\s+\w+::", RegexOptions.Multiline | RegexOptions.Compiled), DirectiveColor, FontStyle.Bold),
        // Bold
        (new Regex(@"\*\*[^*\n]+\*\*", RegexOptions.Compiled), Color.Black, FontStyle.Bold),
        // Italic
        (new Regex(@"(?<!\*)\*(?!\*)[^*\n]+\*(?!\*)", RegexOptions.Compiled), Color.Black, FontStyle.Italic),
        // Inline code
        (new Regex(@"``[^`\n]+``", RegexOptions.Compiled), CodeColor, FontStyle.Regular),
        // References/links
        (new Regex(@"`[^`\n]+`_", RegexOptions.Compiled), LinkColor, FontStyle.Regular),
        (new Regex(@"\.\.\s+_[^:]+:\s+\S+", RegexOptions.Compiled), LinkColor, FontStyle.Regular),
        // List markers
        (new Regex(@"^\s*[-*+]\s", RegexOptions.Multiline | RegexOptions.Compiled), ListColor, FontStyle.Regular),
        (new Regex(@"^\s*\d+\.\s", RegexOptions.Multiline | RegexOptions.Compiled), ListColor, FontStyle.Regular),
        (new Regex(@"^\s*#\.\s", RegexOptions.Multiline | RegexOptions.Compiled), ListColor, FontStyle.Regular),
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
