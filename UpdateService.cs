using System.Reflection;
using System.Text.Json;

namespace Tugle;

internal sealed record TugleRelease(
    Version Version,
    string Label,
    string? InstallerDownloadUrl,
    string? PortableDownloadUrl)
{
    public string? DownloadUrl => InstallerDownloadUrl ?? PortableDownloadUrl;
}

internal static class UpdateService
{
    private const string LatestReleaseUrl = "https://api.github.com/repos/TaiFradl/Tugle/releases/latest";
    private const string InstallerPackageName = "Tugle-Setup.exe";
    private const string PortablePackageName = "Tugle-browser.zip";
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(6) };
    private static readonly HttpClient DownloadClient = new() { Timeout = TimeSpan.FromMinutes(10) };

    static UpdateService()
    {
        Client.DefaultRequestHeaders.UserAgent.ParseAdd("Tugle-Updater/1.0");
        Client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        DownloadClient.DefaultRequestHeaders.UserAgent.ParseAdd("Tugle-Updater/1.0");
        DownloadClient.DefaultRequestHeaders.Accept.ParseAdd("application/octet-stream");
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

        string? portableDownloadUrl = null;
        string? installerDownloadUrl = null;
        if (release.TryGetProperty("assets", out var assets))
        {
            foreach (var asset in assets.EnumerateArray())
            {
                if (!asset.TryGetProperty("name", out var name) ||
                    !asset.TryGetProperty("browser_download_url", out var url)) continue;

                if (string.Equals(name.GetString(), InstallerPackageName, StringComparison.OrdinalIgnoreCase))
                {
                    installerDownloadUrl = GetSafeDownloadUrl(url.GetString());
                }

                if (string.Equals(name.GetString(), PortablePackageName, StringComparison.OrdinalIgnoreCase))
                    portableDownloadUrl = GetSafeDownloadUrl(url.GetString());
            }
        }

        var current = Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0);
        var normalizedRelease = NormalizeReleaseVersion(version);
        var normalizedCurrent = NormalizeReleaseVersion(current);
        return (installerDownloadUrl is not null || portableDownloadUrl is not null) && normalizedRelease > normalizedCurrent
            ? new TugleRelease(version, label, installerDownloadUrl, portableDownloadUrl)
            : null;
    }

    internal static async Task<string?> DownloadInstallerAsync(
        TugleRelease release,
        CancellationToken cancellationToken = default)
    {
        var url = release.InstallerDownloadUrl;
        if (url is null) return null;

        var updateDirectory = Path.Combine(TugleSettings.ProfileDirectory, "updates");
        var targetPath = Path.Combine(updateDirectory, $"Tugle-Setup-{release.Version}.exe");
        var partialPath = targetPath + ".download";
        try
        {
            Directory.CreateDirectory(updateDirectory);
            if (File.Exists(targetPath) && new FileInfo(targetPath).Length > 1024 * 1024)
                return targetPath;

            foreach (var stale in Directory.EnumerateFiles(updateDirectory, "Tugle-Setup-*.exe"))
            {
                if (!string.Equals(stale, targetPath, StringComparison.OrdinalIgnoreCase))
                    File.Delete(stale);
            }

            using var response = await DownloadClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode) return null;
            if (response.Content.Headers.ContentLength is > 200 * 1024 * 1024) return null;

            await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var destination = new FileStream(partialPath, FileMode.Create, FileAccess.Write, FileShare.None, 128 * 1024, useAsync: true))
            {
                await source.CopyToAsync(destination, cancellationToken);
            }

            var downloadedLength = new FileInfo(partialPath).Length;
            if (downloadedLength < 1024 * 1024) return null;
            File.Move(partialPath, targetPath, overwrite: true);
            return targetPath;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            try { if (File.Exists(partialPath)) File.Delete(partialPath); } catch { }
            return null;
        }
    }

    private static string? GetSafeDownloadUrl(string? value)
    {
        return Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
            uri.Scheme == Uri.UriSchemeHttps &&
            (uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) ||
             uri.Host.EndsWith(".github.com", StringComparison.OrdinalIgnoreCase))
            ? uri.AbsoluteUri
            : null;
    }

    private static Version NormalizeReleaseVersion(Version version) =>
        new(version.Major, Math.Max(0, version.Minor), Math.Max(0, version.Build));
}
