using Markdig;
using Markdig.Extensions.TaskLists;
using Markdig.Extensions.Yaml;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace SimpleText.Core.Highlighting;

public sealed class MarkdownSemanticHighlighter : CachedSpanHighlighter
{
    // MarkdownPipeline is immutable after Build(), so one shared instance is safe.
    // UsePreciseSourceLocation is required for accurate inline (not just block) spans.
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UsePreciseSourceLocation()
        .UseEmphasisExtras()
        .UseAutoLinks()
        .UseTaskLists()
        .UsePipeTables()
        .UseYamlFrontMatter()
        .Build();

    public override string FileTypeName => "Markdown";

    protected override List<HighlightSpan> ParseDocument(string text)
    {
        var spans = new List<HighlightSpan>();
        foreach (var node in Markdown.Parse(text, Pipeline).Descendants())
        {
            switch (node)
            {
                case HeadingBlock heading: // ATX and setext (span covers both setext lines)
                    Add(spans, text, heading.Span, HighlightKind.Heading);
                    break;
                case YamlFrontMatterBlock yaml: // must precede CodeBlock: derives from it
                    Add(spans, text, yaml.Span, HighlightKind.Directive);
                    break;
                case CodeBlock code: // fenced (including fence lines) and indented
                    Add(spans, text, code.Span, HighlightKind.Code);
                    break;
                case CodeInline codeInline:
                    Add(spans, text, codeInline.Span, HighlightKind.Code);
                    break;
                case EmphasisInline emphasis:
                    // Emphasis-extras delimiters (~~, ++, ==, superscript/subscript) read
                    // as Italic; only standard doubled */_ is Bold.
                    bool bold = emphasis.DelimiterChar is '*' or '_' && emphasis.DelimiterCount >= 2;
                    Add(spans, text, emphasis.Span, bold ? HighlightKind.Bold : HighlightKind.Italic);
                    break;
                case LinkInline link: // links and images
                    Add(spans, text, link.Span, HighlightKind.Link);
                    break;
                case AutolinkInline autolink:
                    Add(spans, text, autolink.Span, HighlightKind.Link);
                    break;
                case LinkReferenceDefinition definition:
                    Add(spans, text, definition.Span, HighlightKind.Link);
                    break;
                case TaskList taskList: // the [x] / [ ] checkbox
                    Add(spans, text, taskList.Span, HighlightKind.List);
                    break;
                case ListItemBlock item:
                    AddListMarker(spans, text, item.Span.Start);
                    break;
                case QuoteBlock quote:
                    Add(spans, text, quote.Span, HighlightKind.Quote);
                    break;
                case ThematicBreakBlock thematicBreak:
                    Add(spans, text, thematicBreak.Span, HighlightKind.Heading);
                    break;
                case HtmlBlock html: // raw HTML is inert literal markup, styled like code
                    Add(spans, text, html.Span, HighlightKind.Code);
                    break;
                case HtmlInline htmlInline:
                    Add(spans, text, htmlInline.Span, HighlightKind.Code);
                    break;
            }
        }
        return spans;
    }

    // Markdig SourceSpan offsets are inclusive on both ends.
    private static void Add(List<HighlightSpan> spans, string text, SourceSpan span, HighlightKind kind)
    {
        if (span.Start < 0 || span.End < span.Start || span.Start >= text.Length)
            return;
        int end = Math.Min(span.End + 1, text.Length);
        spans.Add(new HighlightSpan(span.Start, end - span.Start, kind));
    }

    // Color just the marker ("- ", "1. ", "2) "), not the item content, matching the
    // pre-Markdig highlighter. The item span starts at the marker character.
    private static void AddListMarker(List<HighlightSpan> spans, string text, int start)
    {
        if (start < 0 || start >= text.Length)
            return;
        int i = start;
        if (char.IsAsciiDigit(text[i]))
        {
            while (i < text.Length && char.IsAsciiDigit(text[i]))
                i++;
            if (i >= text.Length || (text[i] != '.' && text[i] != ')'))
                return;
            i++;
        }
        else if (text[i] is '-' or '+' or '*')
        {
            i++;
        }
        else
        {
            return;
        }
        if (i < text.Length && text[i] == ' ')
            i++;
        spans.Add(new HighlightSpan(start, i - start, HighlightKind.List));
    }
}
