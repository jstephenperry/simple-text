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
using Windows.Foundation.Metadata;
using Windows.Management.Deployment;

namespace SimpleText.WinUI.Services;

/// <summary>
/// MSIX implementation of <see cref="IUpdateService"/>. Checks GitHub Releases via
/// <see cref="UpdateChecker"/>, then applies the update silently with <see cref="PackageManager"/>
/// (deferred registration — the staged package registers on the next launch since the app is in
/// use). Falls back to opening the downloaded .msix with App Installer when the silent API is
/// unavailable (pre-Windows 10 2004) or fails; the ms-appinstaller: protocol is disabled by
/// default, so launching the local file is the supported path.
/// </summary>
public sealed class UpdateService : IUpdateService
{
    private string? _pendingMsixUrl;

    public async Task<UpdateStatus> CheckForUpdatesAsync(CancellationToken cancellationToken = default)
    {
        Version currentVersion;
        try
        {
            var v = Package.Current.Id.Version;
            currentVersion = new Version(v.Major, v.Minor, v.Build, v.Revision);
        }
        catch
        {
            // Not running as an MSIX package (e.g. launched unpackaged from VS): no update channel.
            return UpdateStatus.None;
        }

        var info = await UpdateChecker.CheckForUpdatesAsync(currentVersion, SelectMsixAsset, cancellationToken);
        _pendingMsixUrl = info.IsUpdateAvailable
            && info.DownloadUrl.EndsWith(".msix", StringComparison.OrdinalIgnoreCase)
                ? info.DownloadUrl
                : null;

        return info.IsUpdateAvailable ? new UpdateStatus(true, info.Version) : UpdateStatus.None;
    }

    public async Task<UpdateOutcome> ApplyPendingUpdateAsync(CancellationToken cancellationToken = default)
    {
        var url = _pendingMsixUrl;
        if (string.IsNullOrEmpty(url))
            return UpdateOutcome.NoUpdatePending;

        try
        {
            var msixPath = await DownloadAsync(url, cancellationToken);

            if (await TryStageSilentlyAsync(msixPath))
                return UpdateOutcome.ReadyOnNextLaunch;

            // Fallback: shell-execute the local package so App Installer applies the update.
            Process.Start(new ProcessStartInfo { FileName = msixPath, UseShellExecute = true });
            return UpdateOutcome.LaunchedInstaller;
        }
        catch
        {
            return UpdateOutcome.Failed;
        }
    }

    // Silently stage the update via PackageManager; because the app is in use, registration is
    // deferred to the next launch. Returns false when the API is missing (pre-19041) or the
    // deployment reports an error, so the caller falls back to App Installer.
    private static async Task<bool> TryStageSilentlyAsync(string msixPath)
    {
        if (!ApiInformation.IsMethodPresent("Windows.Management.Deployment.PackageManager", "AddPackageByUriAsync"))
            return false;
        try
        {
            var manager = new PackageManager();
            var options = new AddPackageOptions
            {
                DeferRegistrationWhenPackagesAreInUse = true,
                ForceAppShutdown = false,
            };
            var result = await manager.AddPackageByUriAsync(new Uri(msixPath), options);
            return result.ExtendedErrorCode is null;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<string> DownloadAsync(string msixUrl, CancellationToken cancellationToken)
    {
        var fileName = Path.GetFileName(new Uri(msixUrl).LocalPath);
        if (string.IsNullOrEmpty(fileName))
            fileName = "SimpleText-update.msix";
        var destination = Path.Combine(Path.GetTempPath(), fileName);

        using var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("SimpleText", "update"));
        using var response = await client.GetAsync(msixUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var file = File.Create(destination);
        await source.CopyToAsync(file, cancellationToken);
        return destination;
    }

    // Pick the .msix matching this machine's architecture (the release ships x64 + arm64) and
    // return its direct download URL; fall back to the first .msix.
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
