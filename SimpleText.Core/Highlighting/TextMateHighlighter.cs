using TextMateSharp.Grammars;
using TextMateSharp.Registry;

namespace SimpleText.Core.Highlighting;

/// <summary>
/// Highlighter backed by a TextMate grammar (AsciiDoc, reStructuredText). Tokenizes the
/// document line by line, threading the grammar rule stack between lines so multi-line
/// constructs (delimited blocks, code blocks) tokenize correctly.
/// </summary>
public sealed class TextMateHighlighter : CachedSpanHighlighter
{
    private const int MaxLineLength = 10_000;
    private static readonly TimeSpan LineTimeout = TimeSpan.FromMilliseconds(250);

    private static readonly Lazy<Registry> SharedRegistry =
        new(() => new Registry(new SimpleTextRegistryOptions()));

    // Priority-ordered scope fragment table; the first kind whose fragment occurs in any
    // scope of a token's scope list wins. Ancestor scopes are present in the list, so
    // tokens nested inside e.g. an AsciiDoc listing block inherit the block's kind unless
    // a higher-priority fragment matches. Code must stay first: it claims everything
    // inside raw/listing/literal blocks before generic fragments (comment, keyword.control,
    // variable, ...) emitted by embedded language grammars can misfire.
    private static readonly (HighlightKind Kind, string[] Fragments)[] ScopeMap =
    {
        (HighlightKind.Code, new[]
        {
            "markup.raw", "markup.fenced", ".listing", "literal", "string.interpolated",
            "block.passthrough",
            // The RST grammar embeds these languages in code-block/doctest bodies without
            // a wrapper scope, so the embedded grammars' suffixes are the only marker.
            ".py", ".cpp", ".console", ".js", ".yaml", ".cmake", ".kconfig", ".ruby",
            ".dts", ".shell",
        }),
        (HighlightKind.Heading, new[] { "markup.heading", "entity.name.section" }),
        (HighlightKind.Bold, new[] { "markup.bold", ".strong" }),
        (HighlightKind.Italic, new[] { "markup.italic", ".emphasis" }),
        (HighlightKind.Link, new[]
        {
            "markup.underline", "string.other.link", "markup.link",
            "constant.other.reference", "meta.link", "meta.macro", "entity.name.tag",
        }),
        (HighlightKind.List, new[] { "markup.list", "punctuation.definition.list", "markup.bullet" }),
        (HighlightKind.Quote, new[] { "markup.quote", "comment", "block.sidebar" }),
        (HighlightKind.Admonition, new[] { "admonitionword", "block.example", "delimiter.example" }),
        (HighlightKind.Directive, new[] { "attributeentry", "attributelist", "keyword.control", "variable" }),
    };

    private readonly string _scopeName;
    private IGrammar? _grammar;

    public TextMateHighlighter(string fileTypeName, string scopeName)
    {
        FileTypeName = fileTypeName;
        _scopeName = scopeName;
    }

    public override string FileTypeName { get; }

    protected override List<HighlightSpan> ParseDocument(string text)
    {
        var spans = new List<HighlightSpan>();
        var grammar = _grammar ??= SharedRegistry.Value.LoadGrammar(_scopeName);
        if (grammar == null)
            return spans;

        IStateStack? state = null;
        int lineStart = 0;
        while (lineStart <= text.Length)
        {
            int newline = text.IndexOf('\n', lineStart);
            int lineLength = (newline < 0 ? text.Length : newline) - lineStart;

            if (lineLength <= MaxLineLength)
            {
                var result = grammar.TokenizeLine(
                    new LineText(text.AsMemory(lineStart, lineLength)), state, LineTimeout);
                state = result.RuleStack;
                foreach (var token in result.Tokens)
                {
                    // EndIndex may point one past the line: grammars match a virtual
                    // trailing '\n' that is not part of the text.
                    int start = Math.Min(token.StartIndex, lineLength);
                    int end = Math.Min(token.EndIndex, lineLength);
                    if (end <= start)
                        continue;
                    if (KindForScopes(token.Scopes) is { } kind)
                        spans.Add(new HighlightSpan(lineStart + start, end - start, kind));
                }
            }

            if (newline < 0)
                break;
            lineStart = newline + 1;
        }
        return spans;
    }

    private static HighlightKind? KindForScopes(List<string> scopes)
    {
        foreach (var (kind, fragments) in ScopeMap)
            foreach (var fragment in fragments)
                foreach (var scope in scopes)
                    if (scope.Contains(fragment, StringComparison.Ordinal))
                        return kind;
        return null;
    }
}
