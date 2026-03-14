using System.Text.Json;
using Avalonia.Styling;

namespace SimpleText.Avalonia.Services;

internal static class ThemeService
{
    private static readonly string ConfigDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SimpleText");

    private static readonly string ThemeFile = Path.Combine(ConfigDir, "theme.json");

    public static ThemeVariant? LoadPreference()
    {
        try
        {
            if (!File.Exists(ThemeFile)) return null;
            var json = File.ReadAllText(ThemeFile);
            var data = JsonSerializer.Deserialize<ThemeData>(json);
            return data?.Theme switch
            {
                "Light" => ThemeVariant.Light,
                "Dark" => ThemeVariant.Dark,
                _ => null // Follow system
            };
        }
        catch
        {
            return null;
        }
    }

    public static void SavePreference(ThemeVariant? theme)
    {
        try
        {
            Directory.CreateDirectory(ConfigDir);
            var data = new ThemeData
            {
                Theme = theme == ThemeVariant.Light ? "Light"
                      : theme == ThemeVariant.Dark ? "Dark"
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
