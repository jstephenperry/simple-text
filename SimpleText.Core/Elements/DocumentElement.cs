namespace SimpleText.Core.Elements;

/// <summary>
/// An insertable markup fragment — a "base document element" such as a table, code
/// block, link, or equation — scoped to an editor mode and exposed through the
/// Insert menu (cf. the insert palettes of LaTeX/Markdown editors).
///
/// <para><see cref="Body"/> is inserted at the caret (replacing any selection);
/// <see cref="CaretOffset"/> is where the caret should land within the inserted
/// text (e.g. after a heading marker, or inside a link's label).</para>
/// </summary>
public sealed record DocumentElement(string Category, string Name, string Body, int CaretOffset)
{
    /// <summary>
    /// Sentinel marking the desired caret position inside an element template.
    /// Authored as <c>\f</c> (form feed), which never appears in real markup.
    /// </summary>
    public const char CaretMarker = '\f';

    /// <summary>
    /// Builds an element from a <paramref name="template"/> whose body may contain a
    /// single <see cref="CaretMarker"/> indicating where the caret should land. The
    /// marker is stripped from <see cref="Body"/>; if it is absent the caret goes to
    /// the end of the inserted text.
    /// </summary>
    public static DocumentElement Create(string category, string name, string template)
    {
        int marker = template.IndexOf(CaretMarker);
        string body = marker >= 0 ? template.Remove(marker, 1) : template;
        int caret = marker >= 0 ? marker : body.Length;
        return new DocumentElement(category, name, body, caret);
    }
}
