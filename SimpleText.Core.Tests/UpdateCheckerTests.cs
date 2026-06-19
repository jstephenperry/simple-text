using System;
using System.Collections.Generic;
using SimpleText.Core.Updates;
using Xunit;

namespace SimpleText.Core.Tests;

public class UpdateCheckerTests
{
    // A minimal GitHub "latest release" payload with the fields the checker reads.
    private static string ReleaseJson(string tag, params (string Name, string Url)[] assets)
    {
        var assetJson = string.Join(",", Array.ConvertAll(assets, a =>
            $"{{\"name\":\"{a.Name}\",\"browser_download_url\":\"{a.Url}\"}}"));
        return $$"""
        {
          "tag_name": "{{tag}}",
          "html_url": "https://github.com/jstephenperry/simple-text/releases/tag/{{tag}}",
          "assets": [{{assetJson}}]
        }
        """;
    }

    // --- Version comparison ---------------------------------------------------

    [Fact]
    public void Newer_release_reports_update_available()
    {
        var json = ReleaseJson("v2.0.0");

        var info = UpdateChecker.ParseLatestRelease(json, new Version(1, 0, 0, 0));

        Assert.True(info.IsUpdateAvailable);
        Assert.Equal("v2.0.0", info.Version);
        Assert.Equal("https://github.com/jstephenperry/simple-text/releases/tag/v2.0.0", info.ReleaseNotesUrl);
    }

    [Fact]
    public void Same_version_reports_no_update()
    {
        var json = ReleaseJson("v1.0.0.7");

        var info = UpdateChecker.ParseLatestRelease(json, new Version(1, 0, 0, 7));

        Assert.False(info.IsUpdateAvailable);
        Assert.Same(UpdateInfo.None, info);
    }

    [Fact]
    public void Older_release_reports_no_update()
    {
        var json = ReleaseJson("v1.0.0");

        var info = UpdateChecker.ParseLatestRelease(json, new Version(1, 2, 0, 0));

        Assert.False(info.IsUpdateAvailable);
    }

    // --- Download URL / asset selection --------------------------------------

    [Fact]
    public void Falls_back_to_release_page_when_no_selector()
    {
        var json = ReleaseJson("v2.0.0", ("app.msix", "https://example.com/app.msix"));

        var info = UpdateChecker.ParseLatestRelease(json, new Version(1, 0, 0, 0));

        Assert.Equal(info.ReleaseNotesUrl, info.DownloadUrl);
    }

    [Fact]
    public void Falls_back_to_release_page_when_selector_returns_null()
    {
        var json = ReleaseJson("v2.0.0", ("app.msix", "https://example.com/app.msix"));

        var info = UpdateChecker.ParseLatestRelease(json, new Version(1, 0, 0, 0), _ => null);

        Assert.Equal(info.ReleaseNotesUrl, info.DownloadUrl);
    }

    [Fact]
    public void Selector_chooses_matching_asset()
    {
        var json = ReleaseJson("v2.0.0",
            ("SimpleText.cer", "https://example.com/SimpleText.cer"),
            ("SimpleText_2.0.0.0_x64.msix", "https://example.com/x64.msix"));

        var info = UpdateChecker.ParseLatestRelease(json, new Version(1, 0, 0, 0), PickMsix);

        Assert.Equal("https://example.com/x64.msix", info.DownloadUrl);
    }

    [Fact]
    public void Selector_receives_all_assets()
    {
        var json = ReleaseJson("v2.0.0",
            ("a.txt", "https://example.com/a.txt"),
            ("b.zip", "https://example.com/b.zip"));

        IReadOnlyList<ReleaseAsset>? seen = null;
        UpdateChecker.ParseLatestRelease(json, new Version(1, 0, 0, 0), assets =>
        {
            seen = assets;
            return null;
        });

        Assert.NotNull(seen);
        Assert.Equal(2, seen!.Count);
        Assert.Equal("a.txt", seen[0].Name);
        Assert.Equal("https://example.com/b.zip", seen[1].DownloadUrl);
    }

    // Mirrors the WinUI asset selector: pick the architecture-matching .msix's direct URL
    // (App Installer applies the update from the downloaded file).
    private static string? PickMsix(IReadOnlyList<ReleaseAsset> assets)
    {
        foreach (var asset in assets)
            if (asset.Name.EndsWith(".msix", StringComparison.OrdinalIgnoreCase)
                && asset.Name.Contains("x64", StringComparison.OrdinalIgnoreCase))
                return asset.DownloadUrl;
        return null;
    }

    // --- Malformed / missing data --------------------------------------------

    [Fact]
    public void Malformed_json_reports_no_update()
    {
        var info = UpdateChecker.ParseLatestRelease("{ not json", new Version(1, 0, 0, 0));

        Assert.False(info.IsUpdateAvailable);
        Assert.Same(UpdateInfo.None, info);
    }

    [Fact]
    public void Missing_tag_reports_no_update()
    {
        var info = UpdateChecker.ParseLatestRelease("{ \"html_url\": \"https://example.com\" }", new Version(1, 0, 0, 0));

        Assert.False(info.IsUpdateAvailable);
    }

    [Fact]
    public void Unparseable_tag_reports_no_update()
    {
        var json = ReleaseJson("nightly-build");

        var info = UpdateChecker.ParseLatestRelease(json, new Version(1, 0, 0, 0));

        Assert.False(info.IsUpdateAvailable);
    }

    [Fact]
    public void Release_with_no_assets_still_offers_the_page()
    {
        var json = ReleaseJson("v2.0.0");

        var info = UpdateChecker.ParseLatestRelease(json, new Version(1, 0, 0, 0), PickMsix);

        Assert.True(info.IsUpdateAvailable);
        Assert.Equal(info.ReleaseNotesUrl, info.DownloadUrl);
    }

    // --- Tag parsing ----------------------------------------------------------

    [Theory]
    [InlineData("v1.2.3", true, "1.2.3")]
    [InlineData("V1.2.3", true, "1.2.3")]
    [InlineData("1.2.3", true, "1.2.3")]
    [InlineData("v1.0.0.7", true, "1.0.0.7")]
    [InlineData("  v1.0.0  ", true, "1.0.0")]
    [InlineData("", false, null)]
    [InlineData("   ", false, null)]
    [InlineData("vNext", false, null)]
    public void TryParseVersion_strips_prefix_and_validates(string tag, bool expected, string? expectedVersion)
    {
        var ok = UpdateChecker.TryParseVersion(tag, out var version);

        Assert.Equal(expected, ok);
        if (expected)
            Assert.Equal(Version.Parse(expectedVersion!), version);
        else
            Assert.Null(version);
    }
}
