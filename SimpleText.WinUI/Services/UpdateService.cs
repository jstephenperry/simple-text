using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using Windows.ApplicationModel;

namespace SimpleText.WinUI.Services;

public static class UpdateService
{
    private const string GitHubApiUrl = "https://api.github.com/repos/jstephenperry/simple-text/releases/latest";

    public record UpdateInfo(bool IsUpdateAvailable, string Version, string DownloadUrl, string ReleaseNotesUrl);

    public static async Task<UpdateInfo> CheckForUpdatesAsync()
    {
        try
        {
            Version currentVersion;
            try
            {
                var packageVersion = Package.Current.Id.Version;
                currentVersion = new Version(packageVersion.Major, packageVersion.Minor, packageVersion.Build, packageVersion.Revision);
            }
            catch
            {
                // Not running as an MSIX package, maybe running from VS
                return new UpdateInfo(false, "", "", "");
            }

            using var client = new HttpClient();
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("SimpleText", currentVersion.ToString()));
            
            var response = await client.GetStringAsync(GitHubApiUrl);
            var doc = JsonDocument.Parse(response);
            var root = doc.RootElement;
            
            var tagName = root.GetProperty("tag_name").GetString() ?? "";
            var htmlUrl = root.GetProperty("html_url").GetString() ?? "";
            
            var cleanTag = tagName.StartsWith("v", StringComparison.OrdinalIgnoreCase) ? tagName.Substring(1) : tagName;
            
            if (Version.TryParse(cleanTag, out var latestVersion))
            {
                if (latestVersion > currentVersion)
                {
                    string downloadUrl = htmlUrl;
                    if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var asset in assets.EnumerateArray())
                        {
                            var name = asset.GetProperty("name").GetString();
                            if (name != null && name.EndsWith(".msix", StringComparison.OrdinalIgnoreCase))
                            {
                                downloadUrl = asset.GetProperty("browser_download_url").GetString() ?? htmlUrl;
                                if (downloadUrl.EndsWith(".msix", StringComparison.OrdinalIgnoreCase))
                                {
                                    downloadUrl = $"ms-appinstaller:?source={downloadUrl}";
                                }
                                break;
                            }
                        }
                    }
                    
                    return new UpdateInfo(true, tagName, downloadUrl, htmlUrl);
                }
            }
        }
        catch
        {
            // Fail silently on network errors or parsing issues
        }
        
        return new UpdateInfo(false, "", "", "");
    }
}
