using System.Text;
using System.Text.Json;
using SimpleText.Core.Storage;

namespace SimpleText.Core.Session;

/// <summary>
/// Multi-tab session state for frontends with tabbed editing. Stored separately from the
/// single-document session.json so the single-document Avalonia flow is unaffected.
/// </summary>
public sealed class WorkspaceSessionData
{
    public int Version { get; set; } = 1;
    public int ActiveTabIndex { get; set; }
    public List<SessionData> Tabs { get; set; } = [];
}

public static class WorkspaceSessionManager
{
    private static string SessionDir => AppStorage.StateDirectory;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    /// <param name="fileName">
    /// Per-frontend file name (e.g. "session.winui.json", "session.avalonia.json") so two
    /// frontends running at once do not clobber each other's workspace.
    /// </param>
    public static WorkspaceSessionData? Load(string fileName)
    {
        try
        {
            var path = Path.Combine(SessionDir, fileName);
            if (!File.Exists(path)) return null;
            var json = File.ReadAllText(path, Encoding.UTF8);
            return JsonSerializer.Deserialize<WorkspaceSessionData>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    public static void Save(WorkspaceSessionData data, string fileName)
    {
        var sessionFile = Path.Combine(SessionDir, fileName);
        var tempFile = sessionFile + ".tmp";
        try
        {
            Directory.CreateDirectory(SessionDir);
            var json = JsonSerializer.Serialize(data, JsonOptions);
            // Write to a temp file and swap it into place so a crash mid-write
            // cannot truncate the only copy of the session.
            File.WriteAllText(tempFile, json, Encoding.UTF8);
            if (File.Exists(sessionFile))
                File.Replace(tempFile, sessionFile, sessionFile + ".bak");
            else
                File.Move(tempFile, sessionFile);
        }
        catch
        {
            // Silently fail — session persistence is best-effort
            try { File.Delete(tempFile); } catch { /* best-effort cleanup */ }
        }
    }
}
