namespace SimpleText.Core.Updates;

/// <summary>The result of an update check: whether a newer release is available, and its version.</summary>
public sealed record UpdateStatus(bool IsUpdateAvailable, string Version)
{
    /// <summary>"No update available" (also the result on any error, since checks never throw).</summary>
    public static UpdateStatus None { get; } = new(false, string.Empty);
}
