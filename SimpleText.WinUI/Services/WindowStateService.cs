using System.Text.Json;
using SimpleText.Core.Storage;

namespace SimpleText.WinUI.Services;

/// <summary>
/// Persists the window placement — normal (restorable) bounds plus a maximized flag — to
/// window.json under the app state directory (<see cref="AppStorage.StateDirectory"/>), so
/// the window reopens at the size, position, and state you left it. Best-effort: any failure
/// falls back to the default placement.
/// </summary>
internal static class WindowStateService
{
    private static string StateFile => Path.Combine(AppStorage.StateDirectory, "window.json");

    public static WindowPlacement? Load()
    {
        try
        {
            return File.Exists(StateFile)
                ? JsonSerializer.Deserialize<WindowPlacement>(File.ReadAllText(StateFile))
                : null;
        }
        catch
        {
            return null;
        }
    }

    public static void Save(WindowPlacement placement)
    {
        try
        {
            Directory.CreateDirectory(AppStorage.StateDirectory);
            File.WriteAllText(StateFile, JsonSerializer.Serialize(placement));
        }
        catch
        {
            // Best-effort; window placement is non-critical.
        }
    }
}

internal sealed class WindowPlacement
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public bool IsMaximized { get; set; }
}
