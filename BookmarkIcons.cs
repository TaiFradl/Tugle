using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace Tugle;

/// <summary>Small local thumbnails; never load a whole page to obtain its icon.</summary>
internal static class BookmarkIcons
{
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(4) };
    private static readonly SemaphoreSlim DownloadSlots = new(4);
    private const int MaximumDownloadBytes = 256 * 1024;

    public static string Encode(Image source)
    {
        using var thumbnail = new Bitmap(32, 32);
        using (var graphics = Graphics.FromImage(thumbnail))
        {
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            var scale = Math.Min(32f / source.Width, 32f / source.Height);
            var width = source.Width * scale;
            var height = source.Height * scale;
            graphics.DrawImage(source, (32 - width) / 2, (32 - height) / 2, width, height);
        }
        using var output = new MemoryStream();
        thumbnail.Save(output, ImageFormat.Png);
        return Convert.ToBase64String(output.ToArray());
    }

    public static Image? Decode(string? png)
    {
        if (string.IsNullOrEmpty(png) || png.Length > 32768) return null;
        try
        {
            using var input = new MemoryStream(Convert.FromBase64String(png));
            using var decoded = Image.FromStream(input);
            if (decoded.Width > 256 || decoded.Height > 256) return null;
            return new Bitmap(decoded);
        }
        catch { return null; }
    }

    public static async Task<string?> DownloadAsync(string url, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")) return null;
        var acquired = false;
        try
        {
            await DownloadSlots.WaitAsync(cancellationToken).ConfigureAwait(false);
            acquired = true;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(4));
            using var response = await Client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > MaximumDownloadBytes) return null;
            await using var input = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
            using var bytes = new MemoryStream();
            var buffer = new byte[8192];
            int count;
            while ((count = await input.ReadAsync(buffer, timeout.Token).ConfigureAwait(false)) > 0)
            {
                if (bytes.Length + count > MaximumDownloadBytes) return null;
                bytes.Write(buffer, 0, count);
            }
            bytes.Position = 0;
            using var decoded = Image.FromStream(bytes);
            if (decoded.Width > 1024 || decoded.Height > 1024) return null;
            return Encode(decoded);
        }
        catch { return null; } // Missing or unsupported icons retain the monogram.
        finally { if (acquired) DownloadSlots.Release(); }
    }
}
