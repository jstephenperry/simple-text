using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SimpleText.Core.Updates;
using Windows.ApplicationModel;

namespace SimpleText.WinUI.Services;

/// <summary>
/// WinUI-specific update check. Reads the running version from the MSIX package and delegates
/// the GitHub query + version comparison to <see cref="UpdateChecker"/>, selecting the .msix
/// asset so Windows App Installer can perform the in-place update.
/// </summary>
public static class UpdateService
{
    public static async Task<UpdateInfo> CheckForUpdatesAsync()
    {
        Version currentVersion;
        try
        {
            var packageVersion = Package.Current.Id.Version;
            currentVersion = new Version(packageVersion.Major, packageVersion.Minor, packageVersion.Build, packageVersion.Revision);
        }
        catch
        {
            // Not running as an MSIX package (e.g. launched unpackaged from VS): no update channel.
            return UpdateInfo.None;
        }

        return await UpdateChecker.CheckForUpdatesAsync(currentVersion, SelectMsixAsset);
    }

    // Prefer the .msix asset and hand it to App Installer via the ms-appinstaller protocol so
    // Windows performs the in-place update; otherwise fall back to the release page.
    private static string? SelectMsixAsset(IReadOnlyList<ReleaseAsset> assets)
    {
        foreach (var asset in assets)
        {
            if (asset.Name.EndsWith(".msix", StringComparison.OrdinalIgnoreCase))
            {
                var url = asset.DownloadUrl;
                return url.EndsWith(".msix", StringComparison.OrdinalIgnoreCase)
                    ? $"ms-appinstaller:?source={url}"
                    : url;
            }
        }
        return null;
    }
}
