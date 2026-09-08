using System.Text.Json;

namespace Tugle;

internal sealed record HistoryVisit(string Url, string Title, string? IconUrl, DateTime VisitedAt);
internal sealed record SiteUsage(string Url, string Title, string? IconUrl, int VisitCount, DateTime LastVisitedAt);

internal sealed class HistoryStore : IDisposable
{
    private sealed class HistoryData
    {
        public List<HistoryVisit> Visits { get; set; } = [];
        public Dictionary<string, int> Searches { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public List<SiteUsage>? Sites { get; set; }
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _filePath;
    private readonly bool _persistent;

    private readonly List<HistoryVisit> _visits = [];
    private readonly Dictionary<string, int> _searches = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, SiteUsage> _sites = new(StringComparer.OrdinalIgnoreCase);
    private readonly System.Windows.Forms.Timer _saveTimer = new() { Interval = 1000 };
    private Task _pendingSave = Task.CompletedTask;
    private bool _dirty;
    private bool _disposed;

    public HistoryStore(string profileDirectory, bool persistent)
    {
        _filePath = Path.Combine(profileDirectory, "history.json");
        _persistent = persistent;
        _saveTimer.Tick += (_, _) => _ = FlushAsync();
        if (!_persistent) return;

        try
        {
            if (!File.Exists(_filePath)) return;
            var data = JsonSerializer.Deserialize<HistoryData>(File.ReadAllText(_filePath), JsonOptions);
            if (data is null) return;
            foreach (var visit in data.Visits.Where(item => !IsGoogleSearchUrl(item.Url)))
            {
                if (_visits.Any(existing => string.Equals(existing.Url, visit.Url, StringComparison.OrdinalIgnoreCase)))
                    continue;
                _visits.Add(visit);
                if (_visits.Count >= 25) break;
            }
            foreach (var item in data.Searches.OrderByDescending(pair => pair.Value).Take(200))
                _searches[item.Key] = Math.Max(0, item.Value);
            if (data.Sites is null)
            {
                // Older profiles have no counts. Seed each distinct recent site once.
                foreach (var visit in _visits)
                    if (TryGetSite(visit.Url, out var uri, out var key) && !_sites.ContainsKey(key))
                        _sites[key] = new SiteUsage(SiteRoot(uri), key, visit.IconUrl, 1, visit.VisitedAt);
            }
            else
            {
                foreach (var site in data.Sites.OrderByDescending(item => item.LastVisitedAt).Take(200))
                    if (TryGetSite(site.Url, out _, out var key) && site.VisitCount > 0)
                        _sites[key] = site;
            }
        }
        catch
        {
            // A damaged history file should never prevent the browser from opening.
        }
    }

    public IReadOnlyList<HistoryVisit> RecentVisits => _visits;
    public IReadOnlyList<SiteUsage> MostUsedSites => _sites.Values
        .OrderByDescending(site => site.VisitCount)
        .ThenByDescending(site => site.LastVisitedAt)
        .Take(6).ToArray();

    public IReadOnlyList<string> SearchSuggestions => _searches
        .OrderByDescending(pair => pair.Value)
        .ThenBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
        .Select(pair => pair.Key)
        .Take(10)
        .ToArray();

    public void Clear()
    {
        _visits.Clear();
        _searches.Clear();
        _sites.Clear();
        Save();
        _ = FlushAsync();
    }

    public void ClearSince(DateTime startUtc)
    {
        _visits.RemoveAll(visit => visit.VisitedAt >= startUtc);
        // Search suggestions in older Tugle profiles do not carry timestamps.
        // Purging them is safer than leaving private query text behind when the
        // user asks to clear a recent time range.
        _searches.Clear();
        // Aggregates cannot be split at an exact time. Remove every affected site
        // rather than retaining evidence of browsing in the cleared time range.
        foreach (var key in _sites.Where(pair => pair.Value.LastVisitedAt >= startUtc).Select(pair => pair.Key).ToArray())
            _sites.Remove(key);
        Save();
        _ = FlushAsync();
    }

    public void RecordVisit(string url, string? title, string? iconUrl = null)
    {
        if (!TryGetSite(url, out var uri, out var key)) return;

        _visits.RemoveAll(item => string.Equals(item.Url, url, StringComparison.OrdinalIgnoreCase));
        _visits.Insert(0, new HistoryVisit(
            url,
            string.IsNullOrWhiteSpace(title) ? uri.Host : title.Trim(),
            string.IsNullOrWhiteSpace(iconUrl) ? null : iconUrl.Trim(),
            DateTime.UtcNow));
        if (_visits.Count > 25) _visits.RemoveRange(25, _visits.Count - 25);
        _sites.TryGetValue(key, out var previous);
        _sites[key] = new SiteUsage(SiteRoot(uri), key, iconUrl ?? previous?.IconUrl,
            (int)Math.Min(int.MaxValue, (long)(previous?.VisitCount ?? 0) + 1), DateTime.UtcNow);
        if (_sites.Count > 200)
            _sites.Remove(_sites.MinBy(pair => pair.Value.LastVisitedAt).Key);
        Save();
    }

    public void UpdateMetadata(string url, string? title, string? iconUrl)
    {
        var index = _visits.FindIndex(visit => string.Equals(visit.Url, url, StringComparison.OrdinalIgnoreCase));
        if (index < 0) return;
        var visit = _visits[index];
        var updated = visit with { Title = string.IsNullOrWhiteSpace(title) ? visit.Title : title.Trim(), IconUrl = iconUrl ?? visit.IconUrl };
        if (updated == visit) return;
        _visits[index] = updated;
        if (TryGetSite(url, out _, out var key) && _sites.TryGetValue(key, out var site))
            _sites[key] = site with { IconUrl = updated.IconUrl };
        Save();
    }

    private static string SiteRoot(Uri uri) => uri.GetLeftPart(UriPartial.Authority) + "/";

    private static bool TryGetSite(string url, out Uri uri, out string key)
    {
        key = string.Empty;
        uri = null!;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var parsed) ||
            parsed.Scheme is not ("http" or "https") || IsGoogleSearchUrl(parsed)) return false;
        uri = parsed;
        key = uri.IdnHost.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? uri.IdnHost[4..] : uri.IdnHost;
        if (!uri.IsDefaultPort) key += ":" + uri.Port;
        return true;
    }

    public void RecordSearch(string query)
    {
        var normalized = query.Trim();
        if (normalized.Length == 0 || normalized.Length > 200) return;
        _searches[normalized] = _searches.TryGetValue(normalized, out var count) ? count + 1 : 1;
        if (_searches.Count > 200)
            _searches.Remove(_searches.MinBy(pair => pair.Value).Key);
        Save();
    }

    private static bool IsGoogleSearchUrl(string url)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out var uri) && IsGoogleSearchUrl(uri);
    }

    private static bool IsGoogleSearchUrl(Uri uri)
    {
        return (uri.Host.Equals("google.com", StringComparison.OrdinalIgnoreCase) || uri.Host.EndsWith(".google.com", StringComparison.OrdinalIgnoreCase)) &&
            uri.AbsolutePath.Equals("/search", StringComparison.OrdinalIgnoreCase);
    }

    private void Save()
    {
        if (!_persistent || _disposed) return;
        _dirty = true;
        _saveTimer.Start();
    }

    public Task FlushAsync()
    {
        _saveTimer.Stop();
        if (!_dirty) return _pendingSave;
        _dirty = false;
        var data = new HistoryData
        {
            Visits = _visits.ToList(),
            Searches = new Dictionary<string, int>(_searches, StringComparer.OrdinalIgnoreCase),
            Sites = _sites.Values.ToList()
        };
        // Snapshots are taken on the UI thread; serialization and disk I/O are
        // ordered on a worker so an older save cannot overwrite newer history.
        _pendingSave = _pendingSave.ContinueWith(_ =>
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
                var temporaryPath = _filePath + ".tmp";
                File.WriteAllText(temporaryPath, JsonSerializer.Serialize(data, JsonOptions));
                File.Move(temporaryPath, _filePath, overwrite: true);
            }
            catch
            {
                // An unwritable profile must stay non-fatal.
            }
        }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
        return _pendingSave;
    }

    public void Dispose()
    {
        if (_disposed) return;
        FlushAsync().GetAwaiter().GetResult();
        _disposed = true;
        _saveTimer.Dispose();
    }
}
