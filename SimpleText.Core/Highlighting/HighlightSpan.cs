namespace SimpleText.Core.Highlighting;

public enum HighlightKind
{
    Heading,
    Code,
    Link,
    List,
    Quote,
    Admonition,
    Directive,
    Bold,
    Italic,
}

public readonly record struct HighlightSpan(int Start, int Length, HighlightKind Kind);

public interface ISpanHighlighter
{
    string FileTypeName { get; }

    /// <summary>
    /// Returns highlight spans for the given region. Offsets in the returned spans are
    /// absolute (relative to <paramref name="text"/>, not the region). Line-anchored
    /// rules expect '\n' line endings; callers with '\r'-terminated text (e.g. RichEdit)
    /// must normalize before calling.
    /// </summary>
    List<HighlightSpan> GetHighlights(string text, int startOffset, int length);
}
