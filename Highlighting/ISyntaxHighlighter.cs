namespace SimpleText.Highlighting;

internal readonly record struct HighlightSpan(
    int Start,
    int Length,
    Color ForeColor,
    FontStyle Style);

internal interface ISyntaxHighlighter
{
    string FileTypeName { get; }
    List<HighlightSpan> GetHighlights(string text, int startOffset, int length);
}
