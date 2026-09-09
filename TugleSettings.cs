using System.Drawing;
using System.Globalization;
using System.Net;
using System.Text.Json;

namespace Tugle;

/// <summary>Small, human-readable profile settings. It deliberately contains no passwords or tokens.</summary>
internal sealed class TugleSettings
{
    private const int MaximumWorkspaceCount = 12;
    private const int MaximumTabGroupCount = 48;
    private const int MaximumRuleCountPerWorkspace = 24;
    private const int MaximumWorkspaceRouteCount = 36;
    private const int MaximumSessionTabCount = 80;

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
    public bool LowMemoryMode { get; set; } = true;
    // This is deliberately opt-in: closing a tab's renderer saves more memory,
    // but reopening it reloads the page and cannot preserve unsaved form input.
    public bool DiscardInactiveTabs { get; set; }
    public bool ReduceMotion { get; set; } = true;
    // Workspaces keep independent tab sessions and browsing preferences. The
    // legacy properties below remain as the active-workspace mirror so older
    // profiles and internal callers continue to load safely.
    public List<TugleWorkspace> Workspaces { get; set; } = [];
    public Guid? ActiveWorkspaceId { get; set; }
    public List<TugleTabGroup> TabGroups { get; set; } = [];
    // Routes deliberately use host names rather than full URLs. They can move a
    // new destination to another workspace, with an optional tab group.
    public List<TugleWorkspaceRoute> WorkspaceRoutes { get; set; } = [];
    public List<TugleSessionTab> PreviousSessionTabs { get; set; } = [];
    public List<TugleSessionTab> RecentlyClosedTabs { get; set; } = [];
    public string SearchEngine { get; set; } = "Google";
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
                    loaded.SearchEngine = SearchProvider.Normalize(loaded.SearchEngine);
                    loaded.TrackingPrevention = NormalizeTrackingPrevention(loaded.TrackingPrevention);
                    loaded.Workspaces = NormalizeWorkspaces(
                        loaded.Workspaces,
                        loaded.SearchEngine,
                        loaded.TrackingPrevention,
                        loaded.LowMemoryMode,
                        loaded.DiscardInactiveTabs);
                    loaded.EnsureWorkspaces();
                    var knownWorkspaceIds = loaded.Workspaces.Select(workspace => workspace.Id).ToHashSet();
                    var hadValidActiveWorkspace = loaded.ActiveWorkspaceId is { } requestedWorkspaceId &&
                        knownWorkspaceIds.Contains(requestedWorkspaceId);
                    if (!hadValidActiveWorkspace) loaded.ActiveWorkspaceId = loaded.Workspaces[0].Id;

                    var defaultWorkspaceId = loaded.ActiveWorkspace.Id;
                    loaded.TabGroups = NormalizeTabGroups(loaded.TabGroups, knownWorkspaceIds, defaultWorkspaceId);
                    var groupWorkspaceIds = loaded.TabGroups.ToDictionary(group => group.Id, group => group.WorkspaceId);
                    NormalizeWorkspaceRules(loaded.Workspaces, groupWorkspaceIds);
                    loaded.WorkspaceRoutes = NormalizeWorkspaceRoutes(
                        loaded.WorkspaceRoutes,
                        knownWorkspaceIds,
                        groupWorkspaceIds);
                    loaded.PreviousSessionTabs = NormalizeSessionTabs(
                        loaded.PreviousSessionTabs,
                        ensureActiveTab: true,
                        groupWorkspaceIds,
                        knownWorkspaceIds,
                        defaultWorkspaceId);
                    loaded.RecentlyClosedTabs = NormalizeSessionTabs(
                        loaded.RecentlyClosedTabs,
                        ensureActiveTab: false,
                        groupWorkspaceIds,
                        knownWorkspaceIds,
                        defaultWorkspaceId)
                        .Take(15)
                        .ToList();
                    NormalizeWorkspaceLastActiveTabs(loaded.Workspaces, loaded.PreviousSessionTabs);

                    // Profiles from before workspaces have no selected workspace.
                    // Their selected tab is the least surprising migration target.
                    if (!hadValidActiveWorkspace)
                    {
                        var activeTab = loaded.PreviousSessionTabs.FirstOrDefault(tab => tab.IsActive);
                        if (activeTab is not null) loaded.ActiveWorkspaceId = activeTab.WorkspaceId;
                    }

                    loaded.ApplyActiveWorkspace();
                    return loaded;
                }
            }
        }
        catch
        {
            // A damaged settings file must never prevent the browser from starting.
        }

        var defaults = new TugleSettings();
        defaults.EnsureWorkspaces();
        defaults.ApplyActiveWorkspace();
        return defaults;
    }

    internal TugleWorkspace ActiveWorkspace
    {
        get
        {
            EnsureWorkspaces();
            return Workspaces.First(workspace => workspace.Id == ActiveWorkspaceId);
        }
    }

    internal void EnsureWorkspaces()
    {
        Workspaces ??= [];
        if (Workspaces.Count == 0)
        {
            Workspaces.Add(new TugleWorkspace
            {
                Id = Guid.NewGuid(),
                Name = "Personal",
                Color = "#6095E5",
                Icon = "●",
                SearchEngine = SearchProvider.Normalize(SearchEngine),
                TrackingPrevention = TrackingPrevention,
                LowMemoryMode = LowMemoryMode,
                DiscardInactiveTabs = DiscardInactiveTabs
            });
        }

        if (ActiveWorkspaceId is not { } activeId || Workspaces.All(workspace => workspace.Id != activeId))
            ActiveWorkspaceId = Workspaces[0].Id;
    }

    internal void CaptureActiveWorkspace()
    {
        var workspace = ActiveWorkspace;
        workspace.SearchEngine = SearchProvider.Normalize(SearchEngine);
        workspace.TrackingPrevention = NormalizeTrackingPrevention(TrackingPrevention);
        workspace.LowMemoryMode = LowMemoryMode;
        workspace.DiscardInactiveTabs = DiscardInactiveTabs;
    }

    internal void ApplyActiveWorkspace()
    {
        var workspace = ActiveWorkspace;
        SearchEngine = SearchProvider.Normalize(workspace.SearchEngine);
        TrackingPrevention = NormalizeTrackingPrevention(workspace.TrackingPrevention);
        LowMemoryMode = workspace.LowMemoryMode;
        DiscardInactiveTabs = workspace.DiscardInactiveTabs;
    }

    public void Save()
    {
        try
        {
            CaptureActiveWorkspace();
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

    private static List<TugleWorkspace> NormalizeWorkspaces(
        IEnumerable<TugleWorkspace>? workspaces,
        string searchEngine,
        string trackingPrevention,
        bool lowMemoryMode,
        bool discardInactiveTabs)
    {
        return (workspaces ?? [])
            .Where(workspace => workspace.Id != Guid.Empty &&
                !string.IsNullOrWhiteSpace(workspace.Name) && TryParseColor(workspace.Color, out _))
            .GroupBy(workspace => workspace.Id)
            .Select(group => group.First())
            .Take(MaximumWorkspaceCount)
            .Select(workspace => new TugleWorkspace
            {
                Id = workspace.Id,
                Name = TrimText(workspace.Name, 40),
                Color = ToHex(ColorTranslator.FromHtml(workspace.Color)),
                Icon = NormalizeIcon(workspace.Icon),
                StartupPageUrl = IsRestorableAddress(workspace.StartupPageUrl) ? workspace.StartupPageUrl!.Trim() : null,
                SearchEngine = string.IsNullOrWhiteSpace(workspace.SearchEngine)
                    ? searchEngine
                    : SearchProvider.Normalize(workspace.SearchEngine),
                TrackingPrevention = NormalizeTrackingPrevention(workspace.TrackingPrevention, trackingPrevention),
                LowMemoryMode = workspace.UseWorkspaceMemorySettings ? workspace.LowMemoryMode : lowMemoryMode,
                DiscardInactiveTabs = workspace.UseWorkspaceMemorySettings
                    ? workspace.DiscardInactiveTabs
                    : discardInactiveTabs,
                UseWorkspaceMemorySettings = workspace.UseWorkspaceMemorySettings,
                LastActiveTabId = workspace.LastActiveTabId,
                // Rules are validated only after every group has been assigned
                // to a workspace below. Retain the raw list until that pass.
                Rules = workspace.Rules ?? []
            })
            .ToList();
    }

    private static List<TugleTabGroup> NormalizeTabGroups(
        IEnumerable<TugleTabGroup>? groups,
        IReadOnlySet<Guid> knownWorkspaceIds,
        Guid fallbackWorkspaceId)
    {
        return (groups ?? [])
            .Where(group => group.Id != Guid.Empty &&
                (group.WorkspaceId == Guid.Empty || knownWorkspaceIds.Contains(group.WorkspaceId)) &&
                !string.IsNullOrWhiteSpace(group.Name) && TryParseColor(group.Color, out _))
            .GroupBy(group => group.Id)
            .Select(group => group.First())
            .Take(MaximumTabGroupCount)
            .Select(group => new TugleTabGroup
            {
                Id = group.Id,
                // Guid.Empty is the pre-workspace schema and migrates to the
                // active workspace. A non-empty unknown ID is stale/corrupt and
                // was filtered above instead of being silently rebound.
                WorkspaceId = group.WorkspaceId == Guid.Empty ? fallbackWorkspaceId : group.WorkspaceId,
                Name = TrimText(group.Name, 40),
                Color = ToHex(ColorTranslator.FromHtml(group.Color)),
                Icon = NormalizeIcon(group.Icon),
                IsCollapsed = group.IsCollapsed
            })
            .ToList();
    }

    private static List<TugleSessionTab> NormalizeSessionTabs(
        IEnumerable<TugleSessionTab>? tabs,
        bool ensureActiveTab,
        IReadOnlyDictionary<Guid, Guid> groupWorkspaceIds,
        IReadOnlySet<Guid> knownWorkspaceIds,
        Guid fallbackWorkspaceId)
    {
        var normalized = new List<TugleSessionTab>();
        var usedTabIds = new HashSet<Guid>();
        foreach (var tab in (tabs ?? [])
                     .Where(tab => tab.IsHome || IsRestorableAddress(tab.Url))
                     .Take(MaximumSessionTabCount))
        {
            var workspaceId = tab.WorkspaceId != Guid.Empty && knownWorkspaceIds.Contains(tab.WorkspaceId)
                ? tab.WorkspaceId
                : fallbackWorkspaceId;
            var tabId = tab.Id;
            while (tabId == Guid.Empty || !usedTabIds.Add(tabId)) tabId = Guid.NewGuid();

            // A group is meaningful only inside the workspace that owns it.
            // This also prevents imported or stale IDs from crossing tab sets.
            Guid? groupId = tab.GroupId is { } suppliedGroupId &&
                groupWorkspaceIds.TryGetValue(suppliedGroupId, out var groupWorkspaceId) &&
                groupWorkspaceId == workspaceId
                ? suppliedGroupId
                : null;

            normalized.Add(new TugleSessionTab
            {
                Id = tabId,
                WorkspaceId = workspaceId,
                IsHome = tab.IsHome,
                IsActive = tab.IsActive,
                IsPinned = tab.IsPinned,
                IsHidden = tab.IsHidden,
                GroupId = groupId,
                Url = tab.IsHome ? null : tab.Url!.Trim(),
                Title = string.IsNullOrWhiteSpace(tab.Title) ? null : TrimText(tab.Title, 120)
            });
        }

        if (ensureActiveTab)
        {
            var activeIndex = normalized.FindIndex(tab => tab.IsActive);
            for (var index = 0; index < normalized.Count; index++)
                normalized[index].IsActive = index == (activeIndex >= 0 ? activeIndex : 0);
        }

        return normalized;
    }

    private static void NormalizeWorkspaceRules(
        IEnumerable<TugleWorkspace> workspaces,
        IReadOnlyDictionary<Guid, Guid> groupWorkspaceIds)
    {
        var usedRuleIds = new HashSet<Guid>();
        foreach (var workspace in workspaces)
        {
            var normalized = new List<TugleWorkspaceRule>();
            foreach (var rule in workspace.Rules ?? [])
            {
                if (normalized.Count >= MaximumRuleCountPerWorkspace ||
                    rule.Id == Guid.Empty ||
                    (rule.WorkspaceId != Guid.Empty && rule.WorkspaceId != workspace.Id) ||
                    rule.TargetGroupId == Guid.Empty ||
                    !groupWorkspaceIds.TryGetValue(rule.TargetGroupId, out var groupWorkspaceId) ||
                    groupWorkspaceId != workspace.Id ||
                    !TryNormalizeHostPattern(rule.HostPattern, out var hostPattern))
                {
                    continue;
                }

                // IDs must remain unique across the profile so imports cannot
                // accidentally make a rule from one workspace control another.
                if (!usedRuleIds.Add(rule.Id)) continue;

                normalized.Add(new TugleWorkspaceRule
                {
                    Id = rule.Id,
                    WorkspaceId = workspace.Id,
                    TargetGroupId = rule.TargetGroupId,
                    HostPattern = hostPattern,
                    Enabled = rule.Enabled
                });
            }

            workspace.Rules = normalized;
        }
    }

    private static List<TugleWorkspaceRoute> NormalizeWorkspaceRoutes(
        IEnumerable<TugleWorkspaceRoute>? routes,
        IReadOnlySet<Guid> knownWorkspaceIds,
        IReadOnlyDictionary<Guid, Guid> groupWorkspaceIds)
    {
        var normalized = new List<TugleWorkspaceRoute>();
        var usedIds = new HashSet<Guid>();
        var usedHosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var route in routes ?? [])
        {
            if (normalized.Count >= MaximumWorkspaceRouteCount || route.Id == Guid.Empty ||
                route.TargetWorkspaceId == Guid.Empty || !knownWorkspaceIds.Contains(route.TargetWorkspaceId) ||
                !TryNormalizeHostPattern(route.HostPattern, out var hostPattern) ||
                !usedIds.Add(route.Id) || !usedHosts.Add(hostPattern))
            {
                continue;
            }

            Guid? targetGroupId = route.TargetGroupId;
            if (targetGroupId is { } groupId &&
                (!groupWorkspaceIds.TryGetValue(groupId, out var groupWorkspaceId) ||
                 groupWorkspaceId != route.TargetWorkspaceId))
            {
                targetGroupId = null;
            }

            normalized.Add(new TugleWorkspaceRoute
            {
                Id = route.Id,
                HostPattern = hostPattern,
                TargetWorkspaceId = route.TargetWorkspaceId,
                TargetGroupId = targetGroupId,
                Enabled = route.Enabled
            });
        }

        return normalized;
    }

    private static void NormalizeWorkspaceLastActiveTabs(
        IEnumerable<TugleWorkspace> workspaces,
        IReadOnlyCollection<TugleSessionTab> sessionTabs)
    {
        foreach (var workspace in workspaces)
        {
            var workspaceTabs = sessionTabs.Where(tab => tab.WorkspaceId == workspace.Id).ToArray();
            if (workspace.LastActiveTabId is { } existingId &&
                workspaceTabs.Any(tab => tab.Id == existingId))
            {
                continue;
            }

            // Older profiles carried only one IsActive bit. Use it where it
            // applies, otherwise retain a deterministic first tab for switching.
            workspace.LastActiveTabId = workspaceTabs.FirstOrDefault(tab => tab.IsActive)?.Id ??
                workspaceTabs.FirstOrDefault()?.Id;
        }
    }

    private static bool TryNormalizeHostPattern(string? value, out string hostPattern)
    {
        hostPattern = string.Empty;
        var candidate = value?.Trim();
        if (string.IsNullOrWhiteSpace(candidate) || candidate.Length > 255) return false;

        var wildcard = candidate.StartsWith("*.", StringComparison.Ordinal);
        var host = wildcard ? candidate[2..] : candidate;
        if (host.Length == 0 || host.Contains('*') || host.Contains('/') || host.Contains('\\') ||
            host.Contains(':') || host.Contains('@') || host.Contains('?') || host.Contains('#') ||
            char.IsWhiteSpace(host[0]) || char.IsWhiteSpace(host[^1]))
        {
            return false;
        }

        // A trailing DNS root marker does not alter a web origin; normalize it
        // away so the rule matches WebView2's hostname representation.
        if (host.EndsWith(".", StringComparison.Ordinal)) host = host[..^1];
        if (host.Length == 0 || host.Length > 253) return false;

        try
        {
            host = new IdnMapping().GetAscii(host).ToLowerInvariant();
        }
        catch
        {
            return false;
        }

        var isIpAddress = IPAddress.TryParse(host, out _);
        if (wildcard && (isIpAddress || !host.Contains('.'))) return false;
        if (!isIpAddress && !string.Equals(host, "localhost", StringComparison.Ordinal) &&
            Uri.CheckHostName(host) != UriHostNameType.Dns)
        {
            return false;
        }

        if (!isIpAddress)
        {
            foreach (var label in host.Split('.'))
            {
                if (label.Length is 0 or > 63 || label[0] == '-' || label[^1] == '-' ||
                    label.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '-'))
                {
                    return false;
                }
            }
        }

        hostPattern = wildcard ? "*." + host : host;
        return true;
    }

    private static bool IsRestorableAddress(string? value)
    {
        return Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
            uri.Scheme is "http" or "https" or "file";
    }

    private static string TrimText(string value, int maximumLength)
    {
        var trimmed = value.Trim();
        return trimmed[..Math.Min(maximumLength, trimmed.Length)];
    }

    private static string NormalizeIcon(string? icon)
    {
        var value = icon?.Trim();
        if (string.IsNullOrWhiteSpace(value)) return "●";
        return value[..Math.Min(8, value.Length)];
    }

    private static string NormalizeTrackingPrevention(string? value, string fallback = "Balanced") =>
        value is "Basic" or "Balanced" or "Strict" ? value :
        fallback is "Basic" or "Balanced" or "Strict" ? fallback : "Balanced";

}

/// <summary>A restorable tab without page content, cookies, or form data.</summary>
internal sealed class TugleSessionTab
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public string? Url { get; set; }
    public bool IsHome { get; set; }
    public bool IsActive { get; set; }
    public bool IsPinned { get; set; }
    public bool IsHidden { get; set; }
    public Guid? GroupId { get; set; }
    public string? Title { get; set; }
}

/// <summary>A named, color-coded group of tabs remembered with the browser profile.</summary>
internal sealed class TugleTabGroup
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public string Name { get; set; } = "Tab group";
    public string Color { get; set; } = "#6095E5";
    public string Icon { get; set; } = "●";
    public bool IsCollapsed { get; set; }
}

/// <summary>A compact, independently restorable tab set. It deliberately stores no cookies or credentials.</summary>
internal sealed class TugleWorkspace
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "Personal";
    public string Color { get; set; } = "#6095E5";
    public string Icon { get; set; } = "●";
    public string? StartupPageUrl { get; set; }
    public string SearchEngine { get; set; } = "Google";
    public string TrackingPrevention { get; set; } = "Balanced";
    public bool UseWorkspaceMemorySettings { get; set; } = true;
    public bool LowMemoryMode { get; set; } = true;
    public bool DiscardInactiveTabs { get; set; }
    public Guid? LastActiveTabId { get; set; }
    public List<TugleWorkspaceRule> Rules { get; set; } = [];
}

/// <summary>A deliberately small host rule; arbitrary regular expressions are not accepted.</summary>
internal sealed class TugleWorkspaceRule
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid TargetGroupId { get; set; }
    public string HostPattern { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
}

/// <summary>A safe domain rule that sends a destination to one workspace.</summary>
internal sealed class TugleWorkspaceRoute
{
    public Guid Id { get; set; }
    public string HostPattern { get; set; } = string.Empty;
    public Guid TargetWorkspaceId { get; set; }
    public Guid? TargetGroupId { get; set; }
    public bool Enabled { get; set; } = true;
}

/// <summary>Portable workspace data that intentionally excludes browser profiles, cookies, history, and downloads.</summary>
internal sealed class TugleWorkspaceBackup
{
    public int FormatVersion { get; set; } = 1;
    public List<TugleWorkspace> Workspaces { get; set; } = [];
    public List<TugleTabGroup> TabGroups { get; set; } = [];
    public List<TugleWorkspaceRoute> Routes { get; set; } = [];
    public List<TugleSessionTab> Tabs { get; set; } = [];
}
