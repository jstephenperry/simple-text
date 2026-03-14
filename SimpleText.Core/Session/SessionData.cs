namespace SimpleText.Core.Session;

public sealed class SessionData
{
    public string? FilePath { get; set; }
    public string? Content { get; set; }
    public int CursorPosition { get; set; }
    public bool IsDirty { get; set; }
    public string? OriginalFileHash { get; set; }
    public string? Mode { get; set; }
}
