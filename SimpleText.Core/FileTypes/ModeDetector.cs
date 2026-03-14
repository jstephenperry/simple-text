namespace SimpleText.Core.FileTypes;

public static class ModeDetector
{
    public static string? DetectFromPath(string? filePath)
    {
        var ext = Path.GetExtension(filePath)?.ToLowerInvariant();
        return ext switch
        {
            ".md" or ".markdown" => TextModes.Markdown,
            ".adoc" or ".asciidoc" => TextModes.AsciiDoc,
            ".rst" => TextModes.ReStructuredText,
            _ => null
        };
    }
}
