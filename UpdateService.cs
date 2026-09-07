using System.Reflection;
using System.Text.Json;

namespace Tugle;

internal sealed record TugleRelease(Version Version, string Label, string DownloadUrl);

internal static class UpdateService
{
    private const string LatestReleaseUrl = "https://api.github.com/repos/TaiFradl/Tugle/releases/latest";
    private const string PortablePackageName = "Tugle-browser.zip";
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(6) };

    static UpdateService()
    {
        Client.DefaultRequestHeaders.UserAgent.ParseAdd("Tugle-Updater/1.0");
        Client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
    }

    internal static async Task<TugleRelease?> GetAvailableReleaseAsync(CancellationToken cancellationToken = default)
    {
        using var response = await Client.GetAsync(LatestReleaseUrl, cancellationToken);
        if (!response.IsSuccessStatusCode) return null;

        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellationToken));
        var release = document.RootElement;
        if (!release.TryGetProperty("tag_name", out var tag)) return null;
        var label = tag.GetString()?.Trim() ?? string.Empty;
        if (!Version.TryParse(label.TrimStart('v', 'V'), out var version)) return null;

        string? downloadUrl = null;
        if (release.TryGetProperty("assets", out var assets))
        {
            foreach (var asset in assets.EnumerateArray())
            {
                if (asset.TryGetProperty("name", out var name) &&
                    string.Equals(name.GetString(), PortablePackageName, StringComparison.OrdinalIgnoreCase) &&
                    asset.TryGetProperty("browser_download_url", out var url))
                {
                    downloadUrl = url.GetString();
                    break;
                }
            }
        }

        var current = Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0);
        return !string.IsNullOrWhiteSpace(downloadUrl) && version > current
            ? new TugleRelease(version, label, downloadUrl)
            : null;
    }
}
