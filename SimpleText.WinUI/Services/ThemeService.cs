using System.Text.Json;

namespace SimpleText.WinUI.Services;

/// <summary>
/// Persists the theme preference to %LOCALAPPDATA%\SimpleText\theme.json using the exact
/// same file format and values as SimpleText.Avalonia's ThemeService ({"Theme":"Light"},
/// {"Theme":"Dark"} or {"Theme":"System"}), so both frontends share the preference.
/// </summary>
internal static class ThemeService
{
    private static readonly string ConfigDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SimpleText");

    private static readonly string ThemeFile = Path.Combine(ConfigDir, "theme.json");

    /// <summary>
    /// Returns "Light" or "Dark" for an explicit preference, or null to follow the
    /// system theme (file missing, unreadable, or saved as "System").
    /// </summary>
    public static string? LoadPreference()
    {
        try
        {
            if (!File.Exists(ThemeFile)) return null;
            var json = File.ReadAllText(ThemeFile);
            var data = JsonSerializer.Deserialize<ThemeData>(json);
            return data?.Theme switch
            {
                "Light" => "Light",
                "Dark" => "Dark",
                _ => null // Follow system
            };
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Pass "Light" or "Dark" for an explicit preference; null (or any other value)
    /// is stored as "System" (follow the OS theme). Best-effort: failures are ignored.
    /// </summary>
    public static void SavePreference(string? value)
    {
        try
        {
            Directory.CreateDirectory(ConfigDir);
            var data = new ThemeData
            {
                Theme = value == "Light" ? "Light"
                      : value == "Dark" ? "Dark"
                      : "System"
            };
            File.WriteAllText(ThemeFile, JsonSerializer.Serialize(data));
        }
        catch
        {
            // Best-effort
        }
    }

    private sealed class ThemeData
    {
        public string? Theme { get; set; }
    }
}
