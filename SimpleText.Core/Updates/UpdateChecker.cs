using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SimpleText.Core.Updates;

/// <summary>
/// UI-agnostic "is there a newer release?" check against the project's GitHub releases.
/// Both frontends share this: each supplies its own running version and an asset picker
/// (the packaged WinUI build selects the .msix; the cross-platform Avalonia build falls back
/// to the release page). Every failure is swallowed and surfaces as <see cref="UpdateInfo.None"/>,
/// so an update check can never disrupt the app.
/// </summary>
public static class UpdateChecker
{
    /// <summary>The GitHub API endpoint for the latest published (non-draft, non-prerelease) release.</summary>
    public const string LatestReleaseApiUrl =
        "https://api.github.com/repos/jstephenperry/simple-text/releases/latest";

    // One shared client (the recommended pattern); per-request headers carry the caller's
    // version, so nothing on the shared instance needs mutating per call.
    private static readonly HttpClient SharedClient = new() { Timeout = TimeSpan.FromSeconds(10) };

    /// <summary>
    /// Queries the latest GitHub release and compares its tag to <paramref name="currentVersion"/>.
    /// When a newer version is found, <paramref name="selectAsset"/> chooses the download URL from
    /// the release assets; returning <see langword="null"/> (or no selector) falls back to the
    /// release page. Returns <see cref="UpdateInfo.None"/> when already up to date or on any
    /// network/parse error.
    /// </summary>
    public static async Task<UpdateInfo> CheckForUpdatesAsync(
        Version currentVersion,
        Func<IReadOnlyList<ReleaseAsset>, string?>? selectAsset = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseApiUrl);
            // GitHub requires a User-Agent and recommends an explicit Accept media type.
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("SimpleText", currentVersion.ToString()));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

            using var response = await SharedClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            return ParseLatestRelease(json, currentVersion, selectAsset);
        }
        catch
        {
            // Fail silently on network errors, timeouts, rate limiting, or parsing issues.
            return UpdateInfo.None;
        }
    }

    /// <summary>
    /// Pure parse/compare step over a GitHub "latest release" JSON payload. Exposed so the
    /// version comparison and asset selection can be unit-tested without a network call.
    /// </summary>
    public static UpdateInfo ParseLatestRelease(
        string json,
        Version currentVersion,
        Func<IReadOnlyList<ReleaseAsset>, string?>? selectAsset = null)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var tagName = root.TryGetProperty("tag_name", out var tag) ? tag.GetString() ?? string.Empty : string.Empty;
            var htmlUrl = root.TryGetProperty("html_url", out var html) ? html.GetString() ?? string.Empty : string.Empty;

            if (!TryParseVersion(tagName, out var latestVersion) || latestVersion <= currentVersion)
                return UpdateInfo.None;

            var downloadUrl = htmlUrl;
            if (selectAsset != null
                && root.TryGetProperty("assets", out var assetsElement)
                && assetsElement.ValueKind == JsonValueKind.Array)
            {
                var assets = new List<ReleaseAsset>();
                foreach (var asset in assetsElement.EnumerateArray())
                {
                    var name = asset.TryGetProperty("name", out var n) ? n.GetString() : null;
                    var url = asset.TryGetProperty("browser_download_url", out var u) ? u.GetString() : null;
                    if (name != null && url != null)
                        assets.Add(new ReleaseAsset(name, url));
                }

                var selected = selectAsset(assets);
                if (!string.IsNullOrEmpty(selected))
                    downloadUrl = selected;
            }

            return new UpdateInfo(true, tagName, downloadUrl, htmlUrl);
        }
        catch
        {
            return UpdateInfo.None;
        }
    }

    /// <summary>
    /// Parses a release tag (e.g. <c>"v1.2.3"</c> or <c>"1.2.3"</c>) into a <see cref="Version"/>,
    /// stripping an optional leading "v"/"V". Returns <see langword="false"/> for null/blank or
    /// otherwise unparseable tags.
    /// </summary>
    public static bool TryParseVersion(string? tag, [NotNullWhen(true)] out Version? version)
    {
        version = null;
        if (string.IsNullOrWhiteSpace(tag))
            return false;

        var cleaned = tag.Trim();
        if (cleaned.StartsWith("v", StringComparison.OrdinalIgnoreCase))
            cleaned = cleaned.Substring(1);

        return Version.TryParse(cleaned, out version);
    }
}
