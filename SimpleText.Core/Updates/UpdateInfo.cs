namespace SimpleText.Core.Updates;

/// <summary>
/// The outcome of an update check: whether a newer release exists and where to get it.
/// <see cref="DownloadUrl"/> is the best install/download target (a frontend-selected release
/// asset when one matches, otherwise the release page); <see cref="ReleaseNotesUrl"/> is the
/// release page on GitHub.
/// </summary>
public sealed record UpdateInfo(bool IsUpdateAvailable, string Version, string DownloadUrl, string ReleaseNotesUrl)
{
    /// <summary>
    /// A "no update available" result. Also returned on any error so callers can fail silently.
    /// </summary>
    public static UpdateInfo None { get; } = new(false, string.Empty, string.Empty, string.Empty);
}
