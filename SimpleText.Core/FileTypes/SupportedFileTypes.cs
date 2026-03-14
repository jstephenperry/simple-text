namespace SimpleText.Core.FileTypes;

public static class SupportedFileTypes
{
    public static readonly FileTypeInfo[] All =
    [
        new("Text Files", "txt"),
        new("Markdown", "md", TextModes.Markdown, "markdown"),
        new("AsciiDoc", "adoc", TextModes.AsciiDoc, "asciidoc"),
        new("reStructuredText", "rst", TextModes.ReStructuredText),
    ];

    public static readonly FileTypeInfo AllFiles = new("All Files", "*");

    /// <summary>
    /// Builds a WinForms-style filter string: "Text Files (*.txt)|*.txt|...|All Files (*.*)|*.*"
    /// </summary>
    public static string BuildWinFormsFilter()
    {
        var parts = All.Select(t =>
        {
            var patterns = string.Join(";", t.AllExtensions.Select(e => $"*.{e}"));
            return $"{t.DisplayName} ({patterns})|{patterns}";
        }).ToList();
        parts.Add("All Files (*.*)|*.*");
        return string.Join("|", parts);
    }

    /// <summary>
    /// Returns file type entries as (DisplayName, Patterns[]) pairs for cross-platform dialogs.
    /// Includes an "All Files" entry at the end.
    /// </summary>
    public static IEnumerable<(string Name, string[] Patterns)> GetFilterEntries()
    {
        foreach (var t in All)
            yield return (t.DisplayName, t.AllExtensions.Select(e => $"*.{e}").ToArray());
        yield return ("All Files", ["*"]);
    }
}
