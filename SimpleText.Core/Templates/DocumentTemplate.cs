namespace SimpleText.Core.Templates;

/// <summary>
/// A document template. <see cref="Mode"/> is the editor mode token
/// (see <see cref="TextModes"/>) or <c>null</c> for plain text.
///
/// <para>Templates are plain files in the user-owned templates folder, discovered
/// and watched by <see cref="TemplateCatalog"/>. The shipped defaults are copied
/// there on first run by <see cref="TemplateSeeder"/>; thereafter every template
/// — default or user-authored — is just a file the user owns.</para>
/// </summary>
public sealed record DocumentTemplate(string Category, string Variant, string? Mode, string Content)
{
    /// <summary>Menu label, e.g. <c>"Technical Report — Markdown"</c>.</summary>
    public string DisplayName => $"{Category} — {Variant}";
}
