namespace SimpleText.Core.Highlighting;

public static class HighlighterRegistry
{
    private static readonly Lazy<MarkdownSemanticHighlighter> Markdown =
        new(() => new MarkdownSemanticHighlighter());

    private static readonly Lazy<TextMateHighlighter> AsciiDoc =
        new(() => new TextMateHighlighter("AsciiDoc", SimpleTextRegistryOptions.AsciiDocScope));

    private static readonly Lazy<TextMateHighlighter> ReStructuredText =
        new(() => new TextMateHighlighter("reStructuredText", SimpleTextRegistryOptions.ReStructuredTextScope));

    public static ISpanHighlighter? ForMode(string? mode) => mode switch
    {
        TextModes.Markdown => Markdown.Value,
        TextModes.AsciiDoc => AsciiDoc.Value,
        TextModes.ReStructuredText => ReStructuredText.Value,
        _ => null,
    };

    public static string FileTypeNameForMode(string? mode)
        => ForMode(mode)?.FileTypeName ?? "Plain Text";
}
