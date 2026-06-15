using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SimpleText.Core.Storage;

namespace SimpleText.Core.Session;

public static class SessionManager
{
    private static string SessionDir => AppStorage.StateDirectory;

    private static string SessionFile => Path.Combine(SessionDir, "session.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public static SessionData? Load()
    {
        try
        {
            if (!File.Exists(SessionFile)) return null;
            var json = File.ReadAllText(SessionFile, Encoding.UTF8);
            return JsonSerializer.Deserialize<SessionData>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    public static void Save(SessionData data)
    {
        try
        {
            Directory.CreateDirectory(SessionDir);
            var json = JsonSerializer.Serialize(data, JsonOptions);
            File.WriteAllText(SessionFile, json, Encoding.UTF8);
        }
        catch
        {
            // Silently fail — session persistence is best-effort
        }
    }

    public static string ComputeFileHash(string filePath)
    {
        var bytes = File.ReadAllBytes(filePath);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }
}
