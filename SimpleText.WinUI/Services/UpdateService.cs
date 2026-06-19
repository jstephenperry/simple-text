using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using SimpleText.Core.Updates;
using Windows.ApplicationModel;

namespace SimpleText.WinUI.Services;

/// <summary>
/// WinUI-specific update flow. Reads the running version from the MSIX package and delegates the
/// GitHub query + version comparison to <see cref="UpdateChecker"/>, selecting the .msix that
/// matches the machine architecture.
///
/// The <c>ms-appinstaller:</c> web protocol is disabled by default on current Windows (App
/// Installer 1.21.3421.0+, the CVE-2021-43890 mitigation), so updates are applied the way
/// Microsoft now prescribes: download the package, then open it with App Installer to update
/// in place — no reliance on the protocol handler.
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

    /// <summary>
    /// Downloads the update .msix to a temp file and opens it with App Installer, which detects
    /// the installed package and applies the in-place update. Returns the downloaded path;
    /// throws on a network/IO failure.
    /// </summary>
    public static async Task<string> DownloadAndLaunchAsync(string msixUrl, CancellationToken cancellationToken = default)
    {
        var fileName = Path.GetFileName(new Uri(msixUrl).LocalPath);
        if (string.IsNullOrEmpty(fileName))
            fileName = "SimpleText-update.msix";
        var destination = Path.Combine(Path.GetTempPath(), fileName);

        using (var client = new HttpClient())
        {
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("SimpleText", "update"));
            using var response = await client.GetAsync(msixUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var file = File.Create(destination);
            await source.CopyToAsync(file, cancellationToken);
        }

        // Shell-executing a local .msix opens App Installer, which offers to update the running
        // package — the supported path now that the ms-appinstaller: protocol is off by default.
        Process.Start(new ProcessStartInfo { FileName = destination, UseShellExecute = true });
        return destination;
    }

    // Pick the .msix matching this machine's architecture (the release ships x64 + arm64) and
    // return its direct download URL; fall back to the first .msix, else (no selector match)
    // UpdateChecker uses the release page.
    private static string? SelectMsixAsset(IReadOnlyList<ReleaseAsset> assets)
    {
        var arch = RuntimeInformation.OSArchitecture switch
        {
            Architecture.Arm64 => "arm64",
            Architecture.X64 => "x64",
            Architecture.X86 => "x86",
            _ => null,
        };

        string? firstMsix = null;
        foreach (var asset in assets)
        {
            if (!asset.Name.EndsWith(".msix", StringComparison.OrdinalIgnoreCase))
                continue;
            if (arch != null && asset.Name.Contains(arch, StringComparison.OrdinalIgnoreCase))
                return asset.DownloadUrl;
            firstMsix ??= asset.DownloadUrl;
        }
        return firstMsix;
    }
}
