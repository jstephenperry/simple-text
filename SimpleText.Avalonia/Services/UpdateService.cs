using System.Threading.Tasks;
using Velopack;
using Velopack.Sources;

namespace SimpleText.Avalonia.Services;

/// <summary>
/// Avalonia auto-update via Velopack. Updates are served from the project's GitHub Releases
/// (Velopack picks the channel for the running OS), and an installed build can download and
/// apply an update in place, then relaunch. Running unpackaged (e.g. <c>dotnet run</c>, or a
/// loose publish) is a no-op so development and one-off builds are never disrupted.
/// </summary>
internal static class UpdateService
{
    private const string RepoUrl = "https://github.com/jstephenperry/simple-text";

    private static UpdateManager CreateManager()
        => new(new GithubSource(RepoUrl, accessToken: null, prerelease: false));

    /// <summary>
    /// Returns the available update, or <see langword="null"/> when up to date, when not running
    /// as a Velopack install, or when the check fails (network/parse). Never throws.
    /// </summary>
    public static async Task<UpdateInfo?> CheckForUpdatesAsync()
    {
        try
        {
            var manager = CreateManager();
            if (!manager.IsInstalled)
                return null;
            return await manager.CheckForUpdatesAsync();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Downloads the update, applies it, and restarts the app. Does not return on success
    /// (the process is replaced); throws if the download fails.
    /// </summary>
    public static async Task DownloadAndApplyAsync(UpdateInfo update)
    {
        var manager = CreateManager();
        await manager.DownloadUpdatesAsync(update);
        manager.ApplyUpdatesAndRestart(update);
    }
}
