using System.Text.Json;

namespace Tugle;

internal sealed record HistoryVisit(string Url, string Title, string? IconUrl, DateTime VisitedAt);

internal sealed class HistoryStore
{
    private sealed class HistoryData
    {
        public List<HistoryVisit> Visits { get; set; } = [];
        public Dictionary<string, int> Searches { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _filePath = Path.Combine(TugleSettings.ProfileDirectory, "history.json");

    private readonly List<HistoryVisit> _visits = [];
    private readonly Dictionary<string, int> _searches = new(StringComparer.OrdinalIgnoreCase);

    public HistoryStore()
    {
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
            foreach (var item in data.Searches)
                _searches[item.Key] = Math.Max(0, item.Value);
        }
        catch
        {
            // A damaged history file should never prevent the browser from opening.
        }
    }

    public IReadOnlyList<HistoryVisit> RecentVisits => _visits;

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
        Save();
    }

    public void RecordVisit(string url, string? title, string? iconUrl = null)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return;
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return;
        if (IsGoogleSearchUrl(uri)) return;

        _visits.RemoveAll(item => string.Equals(item.Url, url, StringComparison.OrdinalIgnoreCase));
        _visits.Insert(0, new HistoryVisit(
            url,
            string.IsNullOrWhiteSpace(title) ? uri.Host : title.Trim(),
            string.IsNullOrWhiteSpace(iconUrl) ? null : iconUrl.Trim(),
            DateTime.UtcNow));
        if (_visits.Count > 25) _visits.RemoveRange(25, _visits.Count - 25);
        Save();
    }

    public void RecordSearch(string query)
    {
        var normalized = query.Trim();
        if (normalized.Length == 0 || normalized.Length > 200) return;
        _searches[normalized] = _searches.TryGetValue(normalized, out var count) ? count + 1 : 1;
        Save();
    }

    private static bool IsGoogleSearchUrl(string url)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out var uri) && IsGoogleSearchUrl(uri);
    }

    private static bool IsGoogleSearchUrl(Uri uri)
    {
        return uri.Host.EndsWith("google.com", StringComparison.OrdinalIgnoreCase) &&
            uri.AbsolutePath.Equals("/search", StringComparison.OrdinalIgnoreCase);
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
            var data = new HistoryData
            {
                Visits = _visits.ToList(),
                Searches = new Dictionary<string, int>(_searches, StringComparer.OrdinalIgnoreCase)
            };
            File.WriteAllText(_filePath, JsonSerializer.Serialize(data, JsonOptions));
        }
        catch
        {
            // History is a convenience feature, so an unwritable profile must stay non-fatal.
        }
    }
}
