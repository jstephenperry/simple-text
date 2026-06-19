using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using SimpleText.Core.Updates;

namespace SimpleText.Avalonia.Services;

/// <summary>
/// Avalonia-side update check. Reads the running assembly version and delegates the GitHub
/// query + version comparison to <see cref="UpdateChecker"/>. The cross-platform build can't
/// silently self-install (no single installer spans Windows/macOS/Linux), so a found update
/// points the user at the release page to download.
/// </summary>
internal static class UpdateService
{
    public static Task<UpdateInfo> CheckForUpdatesAsync()
        => UpdateChecker.CheckForUpdatesAsync(CurrentVersion, SelectAsset);

    /// <summary>The running assembly version (set via &lt;Version&gt; in the .csproj).</summary>
    private static Version CurrentVersion =>
        Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0, 0);

    // No cross-platform installer asset is published yet, so prefer the release page (return
    // null → UpdateChecker falls back to the release html_url). When per-OS archives are added
    // to releases later, match the right one here by name/RID.
    private static string? SelectAsset(IReadOnlyList<ReleaseAsset> assets) => null;
}
