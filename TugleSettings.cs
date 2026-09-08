using System.Drawing;
using System.Text.Json;

namespace Tugle;

/// <summary>Small, human-readable profile settings. It deliberately contains no passwords or tokens.</summary>
internal sealed class TugleSettings
{
    public float GuiScale { get; set; } = 0.9f;
    public string ThemeName { get; set; } = "Ocean";
    public string? CustomThemeAccent { get; set; }
    public List<string> SavedCustomThemeAccents { get; set; } = [];
    public string HomeBackgroundMode { get; set; } = "gradient";
    public string HomeBackground { get; set; } = "#071526";
    public string HomeBackgroundSecondary { get; set; } = "#203D5B";
    public string? HomeBackgroundMediaUrl { get; set; }
    public bool SetupCompleted { get; set; }
    public int SetupVersion { get; set; }
    public bool GoogleConnected { get; set; }
    public bool LowMemoryMode { get; set; } = true;
    public bool ReduceMotion { get; set; } = true;
    public List<TugleSessionTab> PreviousSessionTabs { get; set; } = [];
    public List<TugleSessionTab> RecentlyClosedTabs { get; set; } = [];
    public string SearchEngine { get; set; } = "Google";
    public string? CustomSearchUrl { get; set; }
    public string TrackingPrevention { get; set; } = "Balanced";
    public bool UBlockEnabled { get; set; } = true;
    public bool CookieGuardEnabled { get; set; } = true;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    internal static string ProfileDirectory => Environment.GetEnvironmentVariable("TUGLE_PROFILE_DIRECTORY") ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Tugle");

    private static string FilePath => Path.Combine(ProfileDirectory, "settings.json");

    public static TugleSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var loaded = JsonSerializer.Deserialize<TugleSettings>(File.ReadAllText(FilePath), JsonOptions);
                if (loaded is not null)
                {
                    loaded.GuiScale = Math.Clamp(loaded.GuiScale, 0.7f, 1.4f);
                    loaded.SavedCustomThemeAccents = (loaded.SavedCustomThemeAccents ?? [])
                        .Where(value => TryParseColor(value, out _))
                        .Select(value => ToHex(ColorTranslator.FromHtml(value)))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .Take(8)
                        .ToList();
                    loaded.PreviousSessionTabs = NormalizeSessionTabs(loaded.PreviousSessionTabs, ensureActiveTab: true);
                    loaded.RecentlyClosedTabs = NormalizeSessionTabs(loaded.RecentlyClosedTabs, ensureActiveTab: false)
                        .Take(15)
                        .ToList();
                    loaded.SearchEngine = NormalizeSearchEngine(loaded.SearchEngine);
                    loaded.CustomSearchUrl = IsSearchTemplate(loaded.CustomSearchUrl)
                        ? loaded.CustomSearchUrl!.Trim()
                        : null;
                    loaded.TrackingPrevention = loaded.TrackingPrevention is "Basic" or "Balanced" or "Strict"
                        ? loaded.TrackingPrevention
                        : "Balanced";
                    return loaded;
                }
            }
        }
        catch
        {
            // A damaged settings file must never prevent the browser from starting.
        }

        return new TugleSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOptions));
        }
        catch
        {
            // Preferences are a convenience. Keep the running browser usable if the profile is read-only.
        }
    }

    public static string ToHex(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    public static Color FromHex(string? value, Color fallback)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(value)) return ColorTranslator.FromHtml(value);
        }
        catch
        {
            // Use the supplied fallback below.
        }

        return fallback;
    }

    private static bool TryParseColor(string? value, out Color color)
    {
        color = Color.Empty;
        if (string.IsNullOrWhiteSpace(value)) return false;
        try
        {
            color = ColorTranslator.FromHtml(value);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static List<TugleSessionTab> NormalizeSessionTabs(
        IEnumerable<TugleSessionTab>? tabs,
        bool ensureActiveTab)
    {
        var normalized = (tabs ?? [])
            .Where(tab => tab.IsHome || IsRestorableAddress(tab.Url))
            .Take(20)
            .Select(tab => new TugleSessionTab
            {
                IsHome = tab.IsHome,
                IsActive = tab.IsActive,
                IsPinned = tab.IsPinned,
                Url = tab.IsHome ? null : tab.Url!.Trim(),
                Title = string.IsNullOrWhiteSpace(tab.Title) ? null : tab.Title.Trim()[..Math.Min(120, tab.Title.Trim().Length)]
            })
            .ToList();

        if (ensureActiveTab)
        {
            var activeIndex = normalized.FindIndex(tab => tab.IsActive);
            for (var index = 0; index < normalized.Count; index++)
                normalized[index].IsActive = index == (activeIndex >= 0 ? activeIndex : 0);
        }

        return normalized;
    }

    private static bool IsRestorableAddress(string? value)
    {
        return Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
            uri.Scheme is "http" or "https" or "file";
    }

    private static bool IsSearchTemplate(string? value)
    {
        return !string.IsNullOrWhiteSpace(value) && value.Contains("{query}", StringComparison.Ordinal) &&
            Uri.TryCreate(value.Replace("{query}", "test", StringComparison.Ordinal), UriKind.Absolute, out var uri) &&
            uri.Scheme is "http" or "https";
    }

    private static string NormalizeSearchEngine(string? value) => value switch
    {
        "Google" or "DuckDuckGo" or "Bing" or "Brave" or "Custom" => value,
        _ => "Google"
    };
}

/// <summary>A restorable tab without page content, cookies, or form data.</summary>
internal sealed class TugleSessionTab
{
    public string? Url { get; set; }
    public bool IsHome { get; set; }
    public bool IsActive { get; set; }
    public bool IsPinned { get; set; }
    public string? Title { get; set; }
}
