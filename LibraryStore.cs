using System.Text.Json;

namespace Tugle;

internal sealed class LibraryEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Url { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? IconUrl { get; set; }
    public string? IconPng { get; set; }
    public DateTime AddedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Local bookmarks. Legacy reading-list links are preserved as bookmarks.</summary>
internal sealed class LibraryStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _filePath;
    private readonly bool _persistent;
    private readonly List<LibraryEntry> _entries = [];

    public LibraryStore(string profileDirectory, bool persistent)
    {
        _filePath = Path.Combine(profileDirectory, "library.json");
        _persistent = persistent;
        if (!_persistent) return;

        try
        {
            if (!File.Exists(_filePath)) return;
            var loaded = JsonSerializer.Deserialize<List<LibraryEntry>>(File.ReadAllText(_filePath), JsonOptions) ?? [];
            foreach (var entry in loaded.Where(IsValidEntry))
            {
                entry.Url = new Uri(entry.Url.Trim()).AbsoluteUri;
                if (IsBookmarked(entry.Url)) continue;
                if (entry.Id == Guid.Empty || _entries.Any(item => item.Id == entry.Id)) entry.Id = Guid.NewGuid();
                entry.Title = string.IsNullOrWhiteSpace(entry.Title) ? new Uri(entry.Url).Host : entry.Title.Trim();
                entry.IconUrl = string.IsNullOrWhiteSpace(entry.IconUrl) ? null : entry.IconUrl.Trim();
                if (entry.IconPng?.Length > 32768) entry.IconPng = null;
                _entries.Add(entry);
            }
        }
        catch
        {
            // Library data is a convenience feature. A broken file must not stop Tugle.
        }
    }

    public IReadOnlyList<LibraryEntry> Bookmarks => _entries
        .OrderByDescending(entry => entry.AddedAt)
        .ToArray();

    public bool IsBookmarked(string url) => _entries.Any(entry =>
        string.Equals(entry.Url, NormalizeUrl(url), StringComparison.Ordinal));

    public void ToggleBookmark(string url, string? title, string? iconUrl)
    {
        var existing = _entries.FirstOrDefault(entry =>
            string.Equals(entry.Url, NormalizeUrl(url), StringComparison.Ordinal));
        if (existing is not null)
        {
            _entries.Remove(existing);
        }
        else
        {
            Add(url, title, iconUrl);
        }
        Save();
    }

    public void Remove(Guid id)
    {
        if (_entries.RemoveAll(entry => entry.Id == id) > 0) Save();
    }

    public bool Update(Guid id, string title, string url)
    {
        var entry = _entries.FirstOrDefault(item => item.Id == id);
        if (entry is null || !IsRestorableUrl(url)) return false;
        url = NormalizeUrl(url);
        if (_entries.Any(item => item.Id != id && item.Url == url)) return false;
        if (entry.Url != url) { entry.IconUrl = null; entry.IconPng = null; }
        entry.Url = url;
        entry.Title = string.IsNullOrWhiteSpace(title) ? new Uri(url).Host : title.Trim();
        Save();
        return true;
    }

    public bool UpdateIcon(Guid id, string pageUrl, string? iconUrl, string png)
    {
        var entry = _entries.FirstOrDefault(item => item.Id == id && item.Url == pageUrl);
        if (entry is null || png.Length > 32768 || (entry.IconPng == png && entry.IconUrl == iconUrl)) return false;
        entry.IconPng = png;
        entry.IconUrl = iconUrl;
        Save();
        return true;
    }

    private void Add(string url, string? title, string? iconUrl)
    {
        if (!IsRestorableUrl(url)) return;
        var uri = new Uri(url);
        _entries.Add(new LibraryEntry
        {
            Url = uri.AbsoluteUri,
            Title = string.IsNullOrWhiteSpace(title) ? uri.Host : title.Trim(),
            IconUrl = string.IsNullOrWhiteSpace(iconUrl) ? null : iconUrl.Trim()
        });
    }

    private static bool IsValidEntry(LibraryEntry entry) =>
        entry is not null &&
        IsRestorableUrl(entry.Url);

    private static string NormalizeUrl(string url) => Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) ? uri.AbsoluteUri : url.Trim();

    private static bool IsRestorableUrl(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        uri.Scheme is "http" or "https";

    private void Save()
    {
        if (!_persistent) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
            File.WriteAllText(_filePath + ".tmp", JsonSerializer.Serialize(_entries, JsonOptions));
            File.Move(_filePath + ".tmp", _filePath, overwrite: true);
        }
        catch
        {
            // A read-only profile must not affect browsing.
        }
    }
}
