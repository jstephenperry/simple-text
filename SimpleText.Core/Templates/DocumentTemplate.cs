using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SimpleText.Core.Templates;

/// <summary>
/// A built-in document template. <see cref="Mode"/> is the editor mode token
/// (see <see cref="TextModes"/>) or <c>null</c> for plain text.
/// </summary>
public sealed record DocumentTemplate(string Category, string Variant, string? Mode, string Content)
{
    /// <summary>Menu label, e.g. <c>"Technical Report — Markdown"</c>.</summary>
    public string DisplayName => $"{Category} — {Variant}";
}

/// <summary>
/// The built-in template catalog, loaded from the embedded manifest
/// (<c>Templates/templates.json</c>) and its companion content files under
/// <c>Templates/Content/</c>. Templates are data, not code: to add, edit, or
/// reorder one, change the manifest and content files — no code changes needed.
/// </summary>
public static class DocumentTemplates
{
    private const string ManifestResource = "SimpleText.Core.Templates.templates.json";
    private const string ContentResourcePrefix = "SimpleText.Core.Templates.Content.";

    private static readonly JsonSerializerOptions ManifestOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    public static IReadOnlyList<DocumentTemplate> All { get; } = Load();

    private static IReadOnlyList<DocumentTemplate> Load()
    {
        var assembly = typeof(DocumentTemplates).Assembly;
        var resourceNames = assembly.GetManifestResourceNames();

        var manifestJson = ReadResourceText(assembly, ResolveResource(resourceNames, ManifestResource));
        var manifest = JsonSerializer.Deserialize<TemplateManifest>(manifestJson, ManifestOptions)
            ?? throw new InvalidOperationException("Template manifest could not be parsed.");

        var templates = new List<DocumentTemplate>(manifest.Templates.Count);
        foreach (var entry in manifest.Templates)
        {
            var resource = ResolveResource(resourceNames, ContentResourcePrefix + entry.File);
            var content = ReadResourceText(assembly, resource);
            templates.Add(new DocumentTemplate(
                entry.Category,
                entry.Variant,
                NormalizeMode(entry.Mode),
                StripTrailingNewline(content)));
        }

        return templates;
    }

    /// <summary>
    /// Resolve an embedded resource by its expected name, tolerating any
    /// build-time munging of the name by falling back to a unique suffix match.
    /// </summary>
    private static string ResolveResource(string[] resourceNames, string expected)
    {
        if (Array.IndexOf(resourceNames, expected) >= 0)
            return expected;

        var fileName = expected[(expected.LastIndexOf('.', expected.LastIndexOf('.') - 1) + 1)..];
        var matches = resourceNames.Where(n => n.EndsWith(fileName, StringComparison.Ordinal)).ToArray();
        return matches.Length == 1
            ? matches[0]
            : throw new InvalidOperationException(
                $"Embedded template resource '{expected}' not found (matches: {matches.Length}).");
    }

    private static string? NormalizeMode(string? mode) => mode switch
    {
        null or "" => null,
        TextModes.Markdown => TextModes.Markdown,
        TextModes.AsciiDoc => TextModes.AsciiDoc,
        TextModes.ReStructuredText => TextModes.ReStructuredText,
        _ => throw new InvalidOperationException($"Unknown template mode '{mode}' in manifest."),
    };

    /// <summary>Drop the single conventional end-of-file newline so applied templates have no trailing blank line.</summary>
    private static string StripTrailingNewline(string text)
    {
        if (text.EndsWith('\n'))
            text = text[..^1];
        if (text.EndsWith('\r'))
            text = text[..^1];
        return text;
    }

    private static string ReadResourceText(Assembly assembly, string resourceName)
    {
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded template resource '{resourceName}' not found.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private sealed record TemplateManifest(
        [property: JsonPropertyName("templates")] IReadOnlyList<TemplateEntry> Templates);

    private sealed record TemplateEntry(
        [property: JsonPropertyName("category")] string Category,
        [property: JsonPropertyName("variant")] string Variant,
        [property: JsonPropertyName("mode")] string? Mode,
        [property: JsonPropertyName("file")] string File);
}
