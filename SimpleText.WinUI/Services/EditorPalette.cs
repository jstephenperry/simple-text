using SimpleText.Core.Highlighting;
using Windows.UI;

namespace SimpleText.WinUI.Services;

/// <summary>
/// Maps <see cref="HighlightKind"/> to a foreground color plus bold/italic flags for the
/// light and dark editor themes. The light values match the WinForms highlighter colors.
/// </summary>
internal sealed class EditorPalette
{
    /// <summary>Default text color; also used for the Bold/Italic kinds.</summary>
    public Color DefaultForeground { get; }

    /// <summary>Line-number gutter text color (same gray in both themes).</summary>
    public Color GutterForeground { get; }

    /// <summary>Subtle theme-appropriate gutter background.</summary>
    public Color GutterBackground { get; }

    private readonly Dictionary<HighlightKind, (Color Color, bool Bold, bool Italic)> _map;

    private EditorPalette(
        Color defaultForeground,
        Color gutterBackground,
        Dictionary<HighlightKind, (Color Color, bool Bold, bool Italic)> map)
    {
        DefaultForeground = defaultForeground;
        GutterForeground = Rgb(130, 130, 130);
        GutterBackground = gutterBackground;
        _map = map;
    }

    public (Color Color, bool Bold, bool Italic) For(HighlightKind kind)
        => _map.TryGetValue(kind, out var entry) ? entry : (DefaultForeground, false, false);

    public static EditorPalette Light { get; } = CreateLight();

    public static EditorPalette Dark { get; } = CreateDark();

    private static EditorPalette CreateLight()
    {
        var defaultForeground = Rgb(0, 0, 0);
        var map = new Dictionary<HighlightKind, (Color Color, bool Bold, bool Italic)>
        {
            [HighlightKind.Heading] = (Rgb(0, 100, 180), true, false),
            [HighlightKind.Code] = (Rgb(180, 60, 0), false, false),
            [HighlightKind.Link] = (Rgb(0, 130, 100), false, false),
            [HighlightKind.List] = (Rgb(160, 80, 160), false, false),
            [HighlightKind.Quote] = (Rgb(130, 130, 130), false, true),
            [HighlightKind.Admonition] = (Rgb(180, 120, 0), true, false),
            [HighlightKind.Directive] = (Rgb(180, 120, 0), true, false),
            [HighlightKind.Bold] = (defaultForeground, true, false),
            [HighlightKind.Italic] = (defaultForeground, false, true),
        };
        return new EditorPalette(defaultForeground, Rgb(245, 245, 245), map);
    }

    private static EditorPalette CreateDark()
    {
        var defaultForeground = Rgb(230, 230, 230);
        var map = new Dictionary<HighlightKind, (Color Color, bool Bold, bool Italic)>
        {
            [HighlightKind.Heading] = (Rgb(86, 156, 214), true, false),
            [HighlightKind.Code] = (Rgb(206, 145, 120), false, false),
            [HighlightKind.Link] = (Rgb(78, 201, 176), false, false),
            [HighlightKind.List] = (Rgb(197, 134, 192), false, false),
            [HighlightKind.Quote] = (Rgb(150, 150, 150), false, true),
            [HighlightKind.Admonition] = (Rgb(215, 186, 125), true, false),
            [HighlightKind.Directive] = (Rgb(215, 186, 125), true, false),
            [HighlightKind.Bold] = (defaultForeground, true, false),
            [HighlightKind.Italic] = (defaultForeground, false, true),
        };
        return new EditorPalette(defaultForeground, Rgb(37, 37, 38), map);
    }

    private static Color Rgb(byte r, byte g, byte b) => Color.FromArgb(255, r, g, b);
}
