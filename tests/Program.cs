using System.Collections;
using System.Diagnostics;
using System.Drawing.Imaging;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using Tugle;

internal static class Program
{
    private const BindingFlags Members = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
    private static readonly Assembly AppAssembly = typeof(MainForm).Assembly;
    private static readonly string Profile = Path.Combine(Path.GetTempPath(), "Tugle-checks-" + Guid.NewGuid().ToString("N"));
    private static readonly string Output = Path.Combine(AppContext.BaseDirectory, "renders");
    private static Type AppType(string name) => AppAssembly.GetType("Tugle." + name)!;
    private static object New(string name, params object[] args) => Activator.CreateInstance(AppType(name), args)!;
    private static object? Call(object target, string method, params object?[] args) => target.GetType().GetMethod(method, Members)!.Invoke(target, args);
    private static T Get<T>(object target, string name) => (T)(target.GetType().GetProperty(name, Members)?.GetValue(target) ?? target.GetType().GetField(name, Members)!.GetValue(target))!;
    private static object? Value(object target, string name) => target.GetType().GetProperty(name, Members)?.GetValue(target) ?? target.GetType().GetField(name, Members)?.GetValue(target);
    private static void Set(object target, string name, object? value)
    {
        var property = target.GetType().GetProperty(name, Members);
        if (property is not null) property.SetValue(target, value);
        else target.GetType().GetField(name, Members)!.SetValue(target, value);
    }
    private static object[] Items(object target, string name) => Get<IEnumerable>(target, name).Cast<object>().ToArray();
    private static object? IconCall(string method, params object?[] args) => AppType("BookmarkIcons").GetMethod(method, BindingFlags.Public | BindingFlags.Static)!.Invoke(null, args);
    private static object? StaticCall(string type, string method, params object?[] args) => AppType(type).GetMethod(method, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, args);
    private static string TestIconPng()
    {
        using var image = new Bitmap(64, 64);
        using (var graphics = Graphics.FromImage(image))
        {
            graphics.Clear(Color.Teal);
            graphics.FillEllipse(Brushes.White, 16, 16, 32, 32);
        }
        return (string)IconCall("Encode", image)!;
    }
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        Console.WriteLine("PASS " + message);
    }

    [STAThread]
    private static void Main(string[] args)
    {
        Application.SetHighDpiMode(args.Contains("--dpi") ? HighDpiMode.PerMonitorV2 : HighDpiMode.DpiUnaware);
        Environment.SetEnvironmentVariable("TUGLE_PROFILE_DIRECTORY", Profile);
        Directory.CreateDirectory(Profile);
        Directory.CreateDirectory(Output);
        TestStores();
        using var form = new MainForm { Opacity = 0, ShowInTaskbar = false, WindowState = FormWindowState.Normal, Size = new Size(1200, 850) };
        // Exercise the actual chrome, but do not run setup or contact the updater.
        var loadKey = typeof(Form).GetFields(BindingFlags.Static | BindingFlags.NonPublic).First(field => field.Name.Contains("load", StringComparison.OrdinalIgnoreCase)).GetValue(null)!;
        var events = (System.ComponentModel.EventHandlerList)typeof(System.ComponentModel.Component).GetProperty("Events", Members)!.GetValue(form)!;
        events.RemoveHandler(loadKey, events[loadKey]);
        form.Shown += async (_, _) =>
        {
            try
            {
                TestChrome(form);
                if (args.Contains("--live")) await TestBrowserAsync(form);
                if (args.Contains("--network")) await TestNetworkAsync(form);
                Console.WriteLine("All checks passed. Renders: " + Output);
            }
            catch (Exception error)
            {
                Console.Error.WriteLine(error);
                Environment.ExitCode = 1;
            }
            finally { form.Close(); }
        };
        Application.Run(form);
    }

    private static void TestStores()
    {
        const string query = "café cats + dogs & tea #1 / 漢字";
        foreach (var provider in new[] { "Google", "DuckDuckGo", "Bing" })
        {
            var url = (string)StaticCall("SearchProvider", "BuildUrl", provider, query)!;
            Check((string?)StaticCall("SearchProvider", "GetQuery", url) == query, provider + " search URL round-trips special characters");
        }
        foreach (var removed in new[] { "Brave", "Custom", "unknown" })
            Check((string?)StaticCall("SearchProvider", "Normalize", removed) == "Google", removed + " migrates to Google");
        Check(StaticCall("SearchProvider", "GetQuery", "https://duckduckgo.com.evil.example/?q=test") is null, "search detection checks exact provider hosts");
        Check((string?)StaticCall("SearchProvider", "GetQuery", "https://duckduckgo.com/?q=two+words") == "two words", "DuckDuckGo query decoding accepts plus-separated words");
        Check((bool)StaticCall("GoogleSession", "IsGoogleCookieDomain", ".google.com")! &&
            !(bool)StaticCall("GoogleSession", "IsGoogleCookieDomain", "google.com.evil.example")!, "Google session operations are scoped to Google domains");
        Check((bool)StaticCall("MainForm", "IsSensitivePermission", CoreWebView2PermissionKind.Camera)! &&
            (bool)StaticCall("MainForm", "IsSensitivePermission", CoreWebView2PermissionKind.ClipboardRead)! &&
            !(bool)StaticCall("MainForm", "IsSensitivePermission", CoreWebView2PermissionKind.Autoplay)!,
            "site permission prompts cover sensitive capabilities without interrupting autoplay");
        Check((string)StaticCall("MainForm", "GetPermissionOrigin", "https://example.test/path?query=1")! == "https://example.test",
            "site permission prompts identify the requesting origin");
        var legacySettings = Path.Combine(Profile, "settings.json");
        var workspaceId = Guid.Parse("d15d319a-d4e7-4bcb-a8a2-6c9dfc11d101");
        var unknownWorkspaceId = Guid.Parse("7546b953-731f-4ac7-9e12-68f4d2e67280");
        File.WriteAllText(legacySettings, JsonSerializer.Serialize(new
        {
            searchEngine = "Brave",
            customSearchUrl = "https://example.com/?q={query}",
            googleConnected = true,
            themeName = "Slate",
            tabGroups = new[]
            {
                new { id = workspaceId, name = "Research", color = "#6095E5" }
            },
            previousSessionTabs = new[]
            {
                new { url = "https://workspace-member.example/", title = "Workspace member", isActive = true, groupId = (Guid?)workspaceId },
                new { url = "https://orphaned-workspace.example/", title = "Orphaned member", isActive = false, groupId = (Guid?)unknownWorkspaceId }
            }
        }));
        var migrated = StaticCall("TugleSettings", "Load")!;
        Check(Get<string>(migrated, "SearchEngine") == "Google" && Get<string>(migrated, "ThemeName") == "Slate", "removed search settings migrate without resetting appearance");
        var loadedWorkspaces = Get<IList>(migrated, "Workspaces").Cast<object>().ToArray();
        var loadedGroups = Get<IList>(migrated, "TabGroups").Cast<object>().ToArray();
        var loadedTabs = Get<IList>(migrated, "PreviousSessionTabs").Cast<object>().ToArray();
        var legacyWorkspaceId = Get<Guid>(loadedWorkspaces.Single(), "Id");
        Check(loadedWorkspaces.Length == 1 && Get<string>(loadedWorkspaces[0], "Name") == "Personal" &&
            Get<Guid?>(migrated, "ActiveWorkspaceId") == legacyWorkspaceId,
            "profiles from before workspaces receive one selected Personal workspace");
        Check(loadedGroups.Length == 1 && Get<Guid>(loadedGroups[0], "Id") == workspaceId &&
            Get<string>(loadedGroups[0], "Name") == "Research" && Get<string>(loadedGroups[0], "Color") == "#6095E5",
            "settings load retains a valid tab workspace");
        var validMember = loadedTabs.Single(tab => Get<string>(tab, "Url") == "https://workspace-member.example/");
        var orphanedMember = loadedTabs.Single(tab => Get<string>(tab, "Url") == "https://orphaned-workspace.example/");
        var validGroupId = validMember.GetType().GetProperty("GroupId", Members)!.GetValue(validMember);
        var orphanedGroupId = orphanedMember.GetType().GetProperty("GroupId", Members)!.GetValue(orphanedMember);
        Check(Equals(validGroupId, workspaceId) && orphanedGroupId is null,
            "settings load keeps valid workspace membership and discards unknown group IDs");
        Check(loadedTabs.All(tab => Get<Guid>(tab, "WorkspaceId") == legacyWorkspaceId),
            "legacy session tabs migrate into the selected workspace");
        Call(migrated, "Save");
        Check(!File.ReadAllText(legacySettings).Contains("googleConnected") && !File.ReadAllText(legacySettings).Contains("customSearchUrl"), "account state and removed custom engine are no longer saved in settings");
        TestWorkspaceSettingsNormalization(legacySettings);
        var path = Path.Combine(Profile, "stores");
        Directory.CreateDirectory(path);
        using (var history = (IDisposable)New("HistoryStore", path, true))
        {
            Call(history, "RecordVisit", "https://one.example/a", "One", null);
            Call(history, "RecordVisit", "https://two.example/a", "Two", null);
            Call(history, "RecordVisit", "https://www.one.example/b", "Another page", null);
            for (var i = 0; i < 20; i++) Call(history, "UpdateMetadata", "https://www.one.example/b", "Updated title", "https://one.example/icon.png");
            var sites = Items(history, "MostUsedSites");
            Check(sites.Length == 2 && Get<int>(sites[0], "VisitCount") == 2, "most-used sites group pages and metadata does not inflate counts");
            Check(Get<string>(sites[0], "Url") == "https://www.one.example/", "site tiles link to the site root");
            Call(history, "RecordVisit", "file:///test.html", "Local", null);
            Call(history, "RecordVisit", "https://www.google.com/search?q=private", "Search", null);
            Call(history, "RecordVisit", "https://duckduckgo.com/?q=private", "Search", null);
            Call(history, "RecordVisit", "https://www.bing.com/search?q=private", "Search", null);
            Check(Items(history, "MostUsedSites").Length == 2, "local pages and search results are excluded");
            ((Task)Call(history, "FlushAsync")!).GetAwaiter().GetResult();
        }
        using (var history = (IDisposable)New("HistoryStore", path, true))
        {
            Check(Get<int>(Items(history, "MostUsedSites")[0], "VisitCount") == 2, "usage counts survive reopening");
            Call(history, "ClearSince", DateTime.UtcNow.AddMinutes(-1));
            Check(Items(history, "MostUsedSites").Length == 0, "clearing a time range removes affected site aggregates");
        }
        var legacy = Path.Combine(Profile, "legacy");
        Directory.CreateDirectory(legacy);
        File.WriteAllText(Path.Combine(legacy, "history.json"), """{"visits":[{"url":"https://one.example/a","title":"A","visitedAt":"2026-01-01T00:00:00Z"},{"url":"https://one.example/b","title":"B","visitedAt":"2026-01-01T00:00:00Z"}],"searches":{}}""");
        using (var history = (IDisposable)New("HistoryStore", legacy, true))
            Check(Get<int>(Items(history, "MostUsedSites").Single(), "VisitCount") == 1, "old recent history seeds each site once");
        File.WriteAllText(Path.Combine(legacy, "library.json"), """[{"kind":0,"url":"https://one.example/","title":"Bookmark"},{"kind":1,"url":"https://one.example/","title":"Duplicate"},{"kind":1,"url":"https://two.example/","title":"Saved article"}]""");
        var library = New("LibraryStore", legacy, true);
        Check(Items(library, "Bookmarks").Length == 2, "Read later links migrate without losing links or duplicating bookmarks");
        var entry = Items(library, "Bookmarks").First(item => Get<string>(item, "Url").Contains("two.example"));
        var id = Get<Guid>(entry, "Id");
        Check(!(bool)Call(library, "Update", id, "Bad", "javascript:alert(1)")!, "bookmark editing rejects non-web addresses");
        Check(!(bool)Call(library, "Update", id, "Duplicate", "https://one.example/")!, "bookmark editing rejects duplicates");
        Check((bool)Call(library, "Update", id, "Renamed", "https://two.example/new")!, "bookmark name and address can be edited");
        Check(Items(New("LibraryStore", legacy, true), "Bookmarks").Any(item => Get<string>(item, "Title") == "Renamed"), "bookmark edits persist");
        var iconPng = TestIconPng();
        Check((bool)Call(library, "UpdateIcon", id, "https://two.example/new", "https://two.example/favicon.ico", iconPng)!, "bookmark stores a small local page icon");
        var restoredIcon = Items(New("LibraryStore", legacy, true), "Bookmarks").First(item => Get<Guid>(item, "Id") == id);
        Check(Get<string>(restoredIcon, "IconPng") == iconPng, "bookmark icon survives reopening");
        using (var thumbnail = (Image)IconCall("Decode", iconPng)!)
            Check(thumbnail.Size == new Size(32, 32), "cached icons are bounded 32-pixel thumbnails");
        Check(IconCall("Decode", "broken image") is null, "corrupt cached icon falls back safely");
        Call(library, "Update", id, "Changed", "https://three.example/");
        Check(!((bool)Call(library, "UpdateIcon", id, "https://two.example/new", null, iconPng)!), "late icon downloads cannot overwrite an edited bookmark");
        Check(entry.GetType().GetProperty("IconPng")!.GetValue(entry) is null, "editing the address clears the old icon");
        var privatePath = Path.Combine(Profile, "private");
        using (var history = (IDisposable)New("HistoryStore", privatePath, false))
            Call(history, "RecordVisit", "https://private.example/", "Private", null);
        Call(New("LibraryStore", privatePath, false), "ToggleBookmark", "https://private.example/", "Private", null);
        Check(!Directory.Exists(privatePath), "private stores do not write browsing data");
        var palettes = ((IEnumerable)AppType("ThemePalettes").GetProperty("ColorOptions")!.GetValue(null)!).Cast<object>();
        Check(palettes.Count() == 12 && palettes.Any(p => Get<string>(p, "Name") == "Slate"), "shared browser/setup palette includes Slate");
    }

    private static void TestWorkspaceSettingsNormalization(string settingsPath)
    {
        var workspaceIds = Enumerable.Range(0, 13).Select(_ => Guid.NewGuid()).ToArray();
        var primaryWorkspaceId = workspaceIds[0];
        var activeWorkspaceId = workspaceIds[1];
        var overflowWorkspaceId = workspaceIds[12];
        var primaryGroupId = Guid.NewGuid();
        var activeGroupId = Guid.NewGuid();
        var legacyGroupId = Guid.NewGuid();
        var fillerGroupIds = Enumerable.Range(0, 46).Select(_ => Guid.NewGuid()).ToArray();
        var overflowGroupId = fillerGroupIds[^1];
        var staleWorkspaceId = Guid.NewGuid();
        var staleTabId = Guid.NewGuid();
        var primaryTabId = Guid.NewGuid();
        var activeTabId = Guid.NewGuid();
        var crossGroupTabId = Guid.NewGuid();
        var ruleId = Guid.NewGuid();
        var wildcardRuleId = Guid.NewGuid();
        var legacyRuleId = Guid.NewGuid();
        var activeRuleId = Guid.NewGuid();
        var routeId = Guid.NewGuid();
        var wildcardRouteId = Guid.NewGuid();
        var crossGroupRouteId = Guid.NewGuid();
        var longName = new string('R', 45);

        var primaryRules = new List<object>
        {
            new { id = ruleId, workspaceId = (Guid?)Guid.Empty, targetGroupId = primaryGroupId, hostPattern = " GitHub.COM. ", enabled = true },
            new { id = wildcardRuleId, workspaceId = (Guid?)primaryWorkspaceId, targetGroupId = primaryGroupId, hostPattern = "*.Docs.Example.COM", enabled = true },
            new { id = legacyRuleId, workspaceId = (Guid?)Guid.Empty, targetGroupId = primaryGroupId, hostPattern = "localhost", enabled = false },
            new { id = Guid.NewGuid(), workspaceId = (Guid?)activeWorkspaceId, targetGroupId = primaryGroupId, hostPattern = "cross-owner.example", enabled = true },
            new { id = Guid.NewGuid(), workspaceId = (Guid?)primaryWorkspaceId, targetGroupId = activeGroupId, hostPattern = "cross-target.example", enabled = true },
            new { id = Guid.NewGuid(), workspaceId = (Guid?)primaryWorkspaceId, targetGroupId = primaryGroupId, hostPattern = "github.com/path", enabled = true },
            new { id = ruleId, workspaceId = (Guid?)primaryWorkspaceId, targetGroupId = primaryGroupId, hostPattern = "duplicate-id.example", enabled = true }
        };
        var activeRules = new List<object>
        {
            new { id = activeRuleId, workspaceId = (Guid?)activeWorkspaceId, targetGroupId = activeGroupId, hostPattern = "  *.Example.NET  ", enabled = true },
            new { id = ruleId, workspaceId = (Guid?)activeWorkspaceId, targetGroupId = activeGroupId, hostPattern = "duplicate-across-workspaces.example", enabled = true }
        };
        var sourceRoutes = new List<object>
        {
            new { id = routeId, hostPattern = " GitHub.COM. ", targetWorkspaceId = activeWorkspaceId, targetGroupId = (Guid?)activeGroupId, enabled = true },
            new { id = wildcardRouteId, hostPattern = "*.Docs.Example.COM", targetWorkspaceId = primaryWorkspaceId, targetGroupId = (Guid?)null, enabled = true },
            new { id = crossGroupRouteId, hostPattern = "cross-group-route.example", targetWorkspaceId = primaryWorkspaceId, targetGroupId = (Guid?)activeGroupId, enabled = true },
            new { id = routeId, hostPattern = "duplicate-id-route.example", targetWorkspaceId = primaryWorkspaceId, targetGroupId = (Guid?)null, enabled = true },
            new { id = Guid.NewGuid(), hostPattern = "stale-route.example", targetWorkspaceId = staleWorkspaceId, targetGroupId = (Guid?)null, enabled = true },
            new { id = Guid.NewGuid(), hostPattern = "github.com/path", targetWorkspaceId = primaryWorkspaceId, targetGroupId = (Guid?)null, enabled = true }
        };
        var sourceWorkspaces = new List<object>
        {
            new
            {
                id = primaryWorkspaceId,
                name = "  " + longName + "  ",
                color = "red",
                icon = "  Research++  ",
                startupPageUrl = " https://start.example/ ",
                searchEngine = "",
                trackingPrevention = "not-a-level",
                useWorkspaceMemorySettings = false,
                lowMemoryMode = true,
                discardInactiveTabs = false,
                lastActiveTabId = (Guid?)staleTabId,
                rules = primaryRules
            },
            new
            {
                id = activeWorkspaceId,
                name = "Development",
                color = "#6095E5",
                icon = "◆",
                startupPageUrl = "javascript:alert(1)",
                searchEngine = "DuckDuckGo",
                trackingPrevention = "Basic",
                useWorkspaceMemorySettings = true,
                lowMemoryMode = true,
                discardInactiveTabs = false,
                lastActiveTabId = (Guid?)activeTabId,
                rules = activeRules
            },
            new { id = activeWorkspaceId, name = "Duplicate workspace ID", color = "#000000" }
        };
        sourceWorkspaces.AddRange(workspaceIds.Skip(2).Select((id, index) => new
        {
            id,
            name = "Workspace " + (index + 2),
            color = "#6095E5"
        }));
        sourceWorkspaces.Add(new { id = Guid.Empty, name = "Missing ID", color = "#6095E5" });
        sourceWorkspaces.Add(new { id = Guid.NewGuid(), name = " ", color = "#6095E5" });
        sourceWorkspaces.Add(new { id = Guid.NewGuid(), name = "Invalid color", color = "not-a-color" });

        var sourceGroups = new List<object>
        {
            new { id = primaryGroupId, workspaceId = primaryWorkspaceId, name = "Primary group", color = "red", icon = "  P  ", isCollapsed = true },
            new { id = activeGroupId, workspaceId = activeWorkspaceId, name = "Active group", color = "#6095E5", icon = "◆", isCollapsed = false },
            new { id = legacyGroupId, workspaceId = Guid.Empty, name = "Legacy group", color = "#6095E5", icon = "", isCollapsed = false },
            new { id = primaryGroupId, workspaceId = primaryWorkspaceId, name = "Duplicate ID is ignored", color = "#000000" },
            new { id = Guid.NewGuid(), workspaceId = staleWorkspaceId, name = "Stale group", color = "#6095E5" }
        };
        sourceGroups.AddRange(fillerGroupIds.Select((id, index) => new
        {
            id,
            workspaceId = primaryWorkspaceId,
            name = "Group " + index,
            color = "#6095E5"
        }));
        sourceGroups.Add(new { id = Guid.Empty, workspaceId = primaryWorkspaceId, name = "Missing ID", color = "#6095E5" });
        sourceGroups.Add(new { id = Guid.NewGuid(), workspaceId = primaryWorkspaceId, name = " ", color = "#6095E5" });
        sourceGroups.Add(new { id = Guid.NewGuid(), workspaceId = primaryWorkspaceId, name = "Invalid color", color = "not-a-color" });

        File.WriteAllText(settingsPath, JsonSerializer.Serialize(new
        {
            searchEngine = "Bing",
            trackingPrevention = "Strict",
            lowMemoryMode = false,
            discardInactiveTabs = true,
            activeWorkspaceId,
            workspaces = sourceWorkspaces,
            tabGroups = sourceGroups,
            workspaceRoutes = sourceRoutes,
            previousSessionTabs = new[]
            {
                new { id = primaryTabId, workspaceId = primaryWorkspaceId, url = "https://primary-workspace.example/", title = "Primary", isActive = true, groupId = (Guid?)primaryGroupId },
                new { id = activeTabId, workspaceId = activeWorkspaceId, url = "https://active-workspace.example/", title = "Active", isActive = false, groupId = (Guid?)activeGroupId },
                new { id = crossGroupTabId, workspaceId = primaryWorkspaceId, url = "https://cross-group.example/", title = "Cross group", isActive = false, groupId = (Guid?)activeGroupId },
                new { id = Guid.NewGuid(), workspaceId = staleWorkspaceId, url = "https://orphan-workspace.example/", title = "Orphan", isActive = false, groupId = (Guid?)primaryGroupId },
                new { id = primaryTabId, workspaceId = primaryWorkspaceId, url = "https://duplicate-tab.example/", title = "Duplicate", isActive = false, groupId = (Guid?)primaryGroupId },
                new { id = Guid.Empty, workspaceId = primaryWorkspaceId, url = "https://missing-id.example/", title = "Missing ID", isActive = false, groupId = (Guid?)primaryGroupId },
                new { id = Guid.NewGuid(), workspaceId = primaryWorkspaceId, url = "javascript:alert(1)", title = "Unsafe", isActive = false, groupId = (Guid?)primaryGroupId }
            },
            recentlyClosedTabs = new[]
            {
                new { id = Guid.NewGuid(), workspaceId = primaryWorkspaceId, url = "https://closed-workspace.example/", title = "Closed", isActive = false, groupId = (Guid?)primaryGroupId },
                new { id = Guid.NewGuid(), workspaceId = activeWorkspaceId, url = "https://closed-cross-group.example/", title = "Closed cross group", isActive = false, groupId = (Guid?)primaryGroupId }
            }
        }));

        var normalized = StaticCall("TugleSettings", "Load")!;
        var workspaces = Get<IList>(normalized, "Workspaces").Cast<object>().ToArray();
        var workspaceSet = workspaces.Select(workspace => Get<Guid>(workspace, "Id")).ToHashSet();
        Check(workspaces.Length == 12 && workspaceSet.SetEquals(workspaceIds.Take(12)) && !workspaceSet.Contains(overflowWorkspaceId),
            "workspace loading de-duplicates IDs and bounds the saved workspace count");
        Check(Get<Guid?>(normalized, "ActiveWorkspaceId") == activeWorkspaceId &&
            Get<string>(normalized, "SearchEngine") == "DuckDuckGo" && Get<string>(normalized, "TrackingPrevention") == "Basic",
            "the selected workspace restores its own search and privacy preferences");
        var primaryWorkspace = workspaces.Single(workspace => Get<Guid>(workspace, "Id") == primaryWorkspaceId);
        var activeWorkspace = workspaces.Single(workspace => Get<Guid>(workspace, "Id") == activeWorkspaceId);
        Check(Get<string>(primaryWorkspace, "Name") == new string('R', 40) &&
            Get<string>(primaryWorkspace, "Color") == "#FF0000" && Get<string>(primaryWorkspace, "Icon") == "Research" &&
            Get<string>(primaryWorkspace, "StartupPageUrl") == "https://start.example/" &&
            Get<string>(primaryWorkspace, "SearchEngine") == "Bing" && Get<string>(primaryWorkspace, "TrackingPrevention") == "Strict" &&
            !Get<bool>(primaryWorkspace, "LowMemoryMode") && Get<bool>(primaryWorkspace, "DiscardInactiveTabs"),
            "workspace loading normalizes visual, startup, search, privacy, and inherited memory settings");
        Check(Value(activeWorkspace, "StartupPageUrl") is null && Get<bool>(activeWorkspace, "LowMemoryMode") &&
            !Get<bool>(activeWorkspace, "DiscardInactiveTabs"),
            "workspace-specific startup and memory settings remain isolated");

        var groups = Get<IList>(normalized, "TabGroups").Cast<object>().ToArray();
        var groupSet = groups.Select(group => Get<Guid>(group, "Id")).ToHashSet();
        Check(groups.Length == 48 && groupSet.Contains(primaryGroupId) && groupSet.Contains(activeGroupId) &&
            groupSet.Contains(legacyGroupId) && !groupSet.Contains(overflowGroupId),
            "tab groups retain valid owners, migrate legacy owners, and bound the saved group count");
        var primaryGroup = groups.Single(group => Get<Guid>(group, "Id") == primaryGroupId);
        var legacyGroup = groups.Single(group => Get<Guid>(group, "Id") == legacyGroupId);
        Check(Get<Guid>(primaryGroup, "WorkspaceId") == primaryWorkspaceId && Get<bool>(primaryGroup, "IsCollapsed") &&
            Get<string>(primaryGroup, "Color") == "#FF0000" && Get<string>(primaryGroup, "Icon") == "P" &&
            Get<Guid>(legacyGroup, "WorkspaceId") == activeWorkspaceId && Get<string>(legacyGroup, "Icon") == "●",
            "tab group loading preserves collapse state and never silently binds stale owners");

        var normalizedPrimaryRules = Get<IList>(primaryWorkspace, "Rules").Cast<object>().ToArray();
        var normalizedActiveRules = Get<IList>(activeWorkspace, "Rules").Cast<object>().ToArray();
        Check(normalizedPrimaryRules.Length == 3 && normalizedActiveRules.Length == 1 &&
            normalizedPrimaryRules.All(rule => Get<Guid>(rule, "WorkspaceId") == primaryWorkspaceId && Get<Guid>(rule, "TargetGroupId") == primaryGroupId) &&
            normalizedActiveRules.Single() is { } activeRule && Get<Guid>(activeRule, "WorkspaceId") == activeWorkspaceId &&
            Get<Guid>(activeRule, "TargetGroupId") == activeGroupId,
            "workspace rules retain only in-workspace targets with unique IDs");
        Check(Get<string>(normalizedPrimaryRules.Single(rule => Get<Guid>(rule, "Id") == ruleId), "HostPattern") == "github.com" &&
            Get<string>(normalizedPrimaryRules.Single(rule => Get<Guid>(rule, "Id") == wildcardRuleId), "HostPattern") == "*.docs.example.com" &&
            Get<bool>(normalizedPrimaryRules.Single(rule => Get<Guid>(rule, "Id") == legacyRuleId), "Enabled") == false &&
            Get<string>(normalizedActiveRules.Single(), "HostPattern") == "*.example.net",
            "workspace rules normalize exact and wildcard host patterns without enabling disabled rules");

        var normalizedRoutes = Get<IList>(normalized, "WorkspaceRoutes").Cast<object>().ToArray();
        Check(normalizedRoutes.Length == 3 &&
            Get<string>(normalizedRoutes.Single(route => Get<Guid>(route, "Id") == routeId), "HostPattern") == "github.com" &&
            Get<Guid>(normalizedRoutes.Single(route => Get<Guid>(route, "Id") == routeId), "TargetWorkspaceId") == activeWorkspaceId &&
            Get<Guid?>(normalizedRoutes.Single(route => Get<Guid>(route, "Id") == routeId), "TargetGroupId") == activeGroupId &&
            Get<string>(normalizedRoutes.Single(route => Get<Guid>(route, "Id") == wildcardRouteId), "HostPattern") == "*.docs.example.com" &&
            Value(normalizedRoutes.Single(route => Get<Guid>(route, "Id") == crossGroupRouteId), "TargetGroupId") is null,
            "workspace routes retain safe targets and remove invalid group ownership");

        var restoredTabs = Get<IList>(normalized, "PreviousSessionTabs").Cast<object>().ToArray();
        Check(restoredTabs.Length == 6 && restoredTabs.Count(tab => Get<bool>(tab, "IsActive")) == 1 &&
            restoredTabs.Select(tab => Get<Guid>(tab, "Id")).All(id => id != Guid.Empty) &&
            restoredTabs.Select(tab => Get<Guid>(tab, "Id")).Distinct().Count() == restoredTabs.Length,
            "workspace sessions keep restorable tabs with one active selection and unique generated IDs");
        var primaryTab = restoredTabs.Single(tab => Get<string>(tab, "Url") == "https://primary-workspace.example/");
        var activeTab = restoredTabs.Single(tab => Get<string>(tab, "Url") == "https://active-workspace.example/");
        var crossGroupTab = restoredTabs.Single(tab => Get<string>(tab, "Url") == "https://cross-group.example/");
        var orphanTab = restoredTabs.Single(tab => Get<string>(tab, "Url") == "https://orphan-workspace.example/");
        Check(Get<Guid>(primaryTab, "WorkspaceId") == primaryWorkspaceId &&
            Get<Guid?>(primaryTab, "GroupId") == primaryGroupId && Get<Guid>(activeTab, "WorkspaceId") == activeWorkspaceId &&
            Get<Guid?>(activeTab, "GroupId") == activeGroupId && Value(crossGroupTab, "GroupId") is null &&
            Get<Guid>(orphanTab, "WorkspaceId") == activeWorkspaceId && Value(orphanTab, "GroupId") is null,
            "session tabs retain groups only inside their workspace and safely migrate orphaned workspace IDs");
        Check(Get<Guid?>(primaryWorkspace, "LastActiveTabId") == primaryTabId &&
            Get<Guid?>(activeWorkspace, "LastActiveTabId") == activeTabId,
            "each workspace restores a valid last-active tab independently");

        var closedTabs = Get<IList>(normalized, "RecentlyClosedTabs").Cast<object>().ToArray();
        var closedKnown = closedTabs.Single(tab => Get<string>(tab, "Url") == "https://closed-workspace.example/");
        var closedCrossGroup = closedTabs.Single(tab => Get<string>(tab, "Url") == "https://closed-cross-group.example/");
        Check(Get<Guid?>(closedKnown, "GroupId") == primaryGroupId && Value(closedCrossGroup, "GroupId") is null,
            "recently closed tabs cannot retain cross-workspace group membership");

        Call(normalized, "Save");
        var saved = File.ReadAllText(settingsPath);
        Check(!saved.Contains("Duplicate workspace ID") && !saved.Contains("Stale group") && !saved.Contains("github.com/path") &&
            !saved.Contains("javascript:alert(1)"),
            "normalized workspace settings persist without discarded cross-workspace or unsafe data");
    }

    private static void TestChrome(MainForm form)
    {
        var library = Get<object>(form, "_library");
        var modes = typeof(MainForm).GetNestedType("ActionFlyoutMode", BindingFlags.NonPublic)!;
        Call(form, "ShowActionFlyout", Enum.Parse(modes, "Library"));
        Check(Get<FlowLayoutPanel>(form, "_actionFlyoutItems").Controls.Count == 0, "empty bookmarks has no instructional paragraphs");
        Call(form, "HideActionFlyout");
        var sampleIcon = TestIconPng();
        for (var i = 0; i < 45; i++)
        {
            var url = $"https://example.com/page-{i}";
            Call(library, "ToggleBookmark", url, $"Bookmark {i}: design notes and useful references", null);
            var savedEntry = Items(library, "Bookmarks").First(item => Get<string>(item, "Url") == url);
            Call(library, "UpdateIcon", Get<Guid>(savedEntry, "Id"), url, null, sampleIcon);
        }
        foreach (var scale in new[] { .7f, .9f, 1.4f })
        {
            Set(form, "_guiScale", scale);
            Call(form, "ApplyGuiScale");
            var titleArea = Get<Control>(form, "_titleArea");
            var workspaceSwitcher = Get<Control>(form, "_workspaceButton");
            var tabViewport = Get<Control>(form, "_tabsFlow");
            var addTab = Get<Control>(form, "_newTabButton");
            Check(workspaceSwitcher.Visible && workspaceSwitcher.Width >= 70 * scale &&
                titleArea.ClientRectangle.Contains(workspaceSwitcher.Bounds),
                $"workspace switcher stays usable at {scale}");
            Check(!workspaceSwitcher.Bounds.IntersectsWith(tabViewport.Bounds) &&
                !workspaceSwitcher.Bounds.IntersectsWith(addTab.Bounds),
                $"workspace switcher keeps a separate compact hit target at {scale}");
            foreach (var field in new[] { "_navigation", "_utilityActions" })
            {
                var panel = Get<Control>(form, field);
                Check(panel.Controls.Cast<Control>().All(c => c.Right <= panel.ClientSize.Width), $"toolbar fits at {scale}");
            }
            foreach (var mode in new[] { "Library", "Theme", "Privacy", "Downloads", "Accounts", "BrowserSettings", "Performance", "GoogleAccount", "Search" })
            {
                Call(form, "HideActionFlyout");
                Call(form, "ShowActionFlyout", Enum.Parse(modes, mode));
                var panel = Get<Control>(form, "_actionFlyout");
                var items = Get<FlowLayoutPanel>(form, "_actionFlyoutItems");
                Check(items.Controls.Cast<Control>().All(c => c.Right <= items.ClientSize.Width), $"{mode} rows fit at {scale}");
                if (mode == "Downloads") Check(panel.Height < 200 * scale, "empty downloads panel fits its contents");
                if (mode == "Accounts") Check(items.Controls.Count == 2 && items.Controls[0].Text == "Google account" && items.Controls[1].Text == "Browser settings", "Settings has exactly two clear choices");
                if (mode == "Performance") Check(items.Controls[0].Text == "‹  Browser settings" && items.Controls[1].Text == "Memory saver", "Performance makes memory saving a clear choice");
                if (mode == "Search") Check(items.Controls.Cast<Control>().Select(c => c.Text).SequenceEqual(new[] { "‹  Browser settings", "Google", "DuckDuckGo", "Bing" }), "search settings offer only the three supported engines");
                if (mode == "Theme") Check(items.Controls.Cast<Control>().Where(c => c.GetType().Name == "FlyoutSectionLabel").Any(c => Get<string>(c, "DetailText") == "12 colors"), "theme count matches available colors");
                SaveControl(panel, $"{mode}-{scale.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)}");
                if (mode == "Library")
                {
                    Check(!items.Controls.Cast<Control>().Any(c => c.GetType().Name == "FlyoutSectionLabel"), "bookmarks have no saved-pages heading or count");
                    Check(items.Controls.Cast<Control>().Where(c => c.GetType().Name == "HistoryFlyoutItem").All(c => c.GetType().GetProperty("SiteIcon")!.GetValue(c) is Image), "cached page icons render immediately");
                    Check(items.Controls.Cast<Control>().Count(c => c.GetType().Name == "HistoryFlyoutItem") == 30, "bookmark list initially creates only 30 rows");
                    Set(form, "_bookmarkQuery", "page-44");
                    Call(form, "PopulateActionFlyout");
                    Check(items.Controls.Cast<Control>().Count(c => c.GetType().Name == "HistoryFlyoutItem") == 1, "search reaches bookmarks outside the first page");
                    Set(form, "_editingBookmark", Get<Guid>(Items(library, "Bookmarks")[0], "Id"));
                    Call(form, "PopulateActionFlyout");
                    SaveControl(panel, $"Bookmark-edit-{scale.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)}");
                }
            }
            using var tabRow = new Panel { Width = (int)(600 * scale), Height = (int)(42 * scale), BackColor = form.BackColor };
            for (var state = 0; state < 3; state++)
            {
                var button = (Control)New("TabButton");
                Set(button, "UiScale", scale);
                Set(button, "PlayingAudio", state == 1);
                Set(button, "Muted", state == 2);
                Set(button, "Active", state == 1);
                Set(button, "GroupColor", state == 1 ? Color.FromArgb(96, 149, 229) : Color.Empty);
                button.SetBounds((int)(state * 200 * scale), 0, (int)(194 * scale), tabRow.Height);
                button.Text = state == 0 ? "Silent page" : state == 1 ? "Playing audio" : "Muted tab";
                button.ForeColor = Color.FromArgb(235, 240, 248);
                button.Font = new Font("Segoe UI", 10 * scale);
                tabRow.Controls.Add(button);
                var bounds = Get<Rectangle>(button, "MuteBounds");
                Check(bounds.IsEmpty == (state == 0), "audio hit target only exists for playing/muted tabs");
                if (!bounds.IsEmpty) Check(!bounds.IntersectsWith(Get<Rectangle>(button, "CloseBounds")), "mute and close hit targets do not overlap");
                if (state == 1)
                {
                    var workspaceIndicator = Get<Rectangle>(button, "GroupIndicatorBounds");
                    Check(!workspaceIndicator.IsEmpty && button.ClientRectangle.Contains(workspaceIndicator), "colored workspaces expose an in-tab indicator");
                    Check(!workspaceIndicator.IntersectsWith(Get<Rectangle>(button, "CloseBounds")), "workspace indicator does not overlap the close control");
                }
            }
            SaveControl(tabRow, $"Tabs-{scale.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)}");

            using var switcherRow = new Panel { Width = (int)(230 * scale), Height = Math.Max(28, (int)(42 * scale)), BackColor = form.BackColor };
            var switcher = (Control)New("WorkspaceSwitchButton");
            Set(switcher, "UiScale", scale);
            Set(switcher, "WorkspaceName", "Research notes");
            Set(switcher, "WorkspaceIcon", "✦");
            Set(switcher, "WorkspaceColor", Color.FromArgb(152, 123, 234));
            switcher.ForeColor = Color.FromArgb(235, 240, 248);
            switcher.Font = new Font("Segoe UI", 10 * scale);
            switcher.SetBounds(0, 0, switcherRow.Width, switcherRow.Height);
            switcherRow.Controls.Add(switcher);
            Check(switcher.ClientRectangle.Width >= 100 * scale && switcher.ClientRectangle.Height >= 24 * scale,
                $"workspace switcher renders at {scale}");
            SaveControl(switcherRow, $"Workspace-switcher-{scale.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)}");
        }

        var commandEntries = Call(form, "BuildCommandPaletteEntries")!;
        using (var palette = (Form)New("CommandPaletteDialog", commandEntries, Get<object>(form, "_theme"), .9f))
        {
            palette.Show(form);
            Application.DoEvents();
            var results = Get<ListBox>(palette, "_results");
            Check(results.Items.Count > 0 && results.ItemHeight >= 40, "command palette renders compact keyboard results");
            SaveControl(palette, "Command-palette");
            var search = Get<TextBox>(palette, "_search");
            search.Text = "New tab";
            Check(results.SelectedItem is { } topResult && Get<string>(topResult, "Title") == "New tab",
                "palette puts an exact title match first");
            search.Text = "no-such-command-987654321";
            Check(results.Items.Count == 0 && Get<Label>(palette, "_empty").Visible,
                "palette explains searches with no matches");
            SaveControl(palette, "Command-palette-empty");
            search.Clear();
            Check(results.Items.Count > 0 && !Get<Label>(palette, "_empty").Visible,
                "clearing palette search restores selectable results");
            palette.Hide();
        }

        Call(form, "PopulateWorkspaceMenu");
        var workspaceMenu = Get<ContextMenuStrip>(form, "_workspaceMenu");
        var workspaceLabels = workspaceMenu.Items.Cast<ToolStripItem>().Select(item => item.Text).ToArray();
        var advanced = workspaceMenu.Items.OfType<ToolStripMenuItem>()
            .FirstOrDefault(item => item.Text == "More workspace settings");
        var advancedLabels = advanced is null
            ? Array.Empty<string>()
            : advanced!.DropDownItems.OfType<ToolStripItem>().Select(item => item.Text).ToArray();
        Check(workspaceLabels.Any(label => label.Contains("tab", StringComparison.OrdinalIgnoreCase)) &&
            workspaceLabels.Contains("New workspace…") && workspaceLabels.Contains("New from template") &&
            workspaceLabels.Contains("Manage current workspace") && advanced is not null &&
            advancedLabels.Contains("Auto-group sites…") && advancedLabels.Contains("Always open sites in…") &&
            advancedLabels.Contains("Export workspaces…") && advancedLabels.Contains("Import workspace backup…"),
            "workspace menu keeps switching simple and advanced controls tucked away");
    }

    private static void SaveControl(Control control, string name)
    {
        control.CreateControl();
        using var image = new Bitmap(control.Width, control.Height);
        control.DrawToBitmap(image, control.ClientRectangle);
        image.Save(Path.Combine(Output, name + ".png"), ImageFormat.Png);
    }

    private static async Task UntilAsync(Func<bool> condition, string description)
    {
        var timer = Stopwatch.StartNew();
        while (!condition())
        {
            if (timer.Elapsed > TimeSpan.FromSeconds(20)) throw new TimeoutException(description);
            await Task.Delay(50);
        }
        Check(true, description);
    }

    private static async Task TestBrowserAsync(MainForm form)
    {
        using var server = new LocalPageServer();
        Set(form, "_guiScale", .9f);
        Call(form, "ApplyGuiScale");
        Call(form, "HideActionFlyout");
        var settings = Get<object>(form, "_settings");
        Set(settings, "UBlockEnabled", false);
        Set(settings, "CookieGuardEnabled", false);
        var saved = Get<IList>(settings, "PreviousSessionTabs");
        saved.Clear();
        for (var i = 0; i < 20; i++)
        {
            var session = New("TugleSessionTab");
            Set(session, "Url", server.Url + "tab-" + i);
            Set(session, "Title", "Restored " + i);
            Set(session, "IsActive", i == 4);
            Set(session, "IsPinned", i == 0);
            saved.Add(session);
        }
        var start = Stopwatch.StartNew();
        await (Task)Call(form, "RestorePreviousTabsAsync")!;
        var tabs = Get<IList>(form, "_tabs").Cast<object>().ToArray();
        var active = Get<object>(form, "_activeTab");
        WebView2 View(object tab) => Get<WebView2>(tab, "View");
        await UntilAsync(() => Get<bool>(active, "InitialNavigationReady"), "restored selected tab finishes navigation");
        Check(tabs.Length == 20 && tabs.Count(t => View(t).CoreWebView2 is not null) == 1, "20-tab restoration initializes just the selected WebView");
        Check(View(active).CoreWebView2.Source.EndsWith("tab-4"), "previously selected tab is restored");
        Call(form, "CapturePreviousSession");
        Check(Get<IList>(settings, "PreviousSessionTabs").Count == 20, "unloaded tabs remain in saved sessions");
        Console.WriteLine($"Restored selected page in {start.ElapsedMilliseconds} ms; 19 background controllers avoided.");
        var second = tabs[1];
        await (Task)Call(form, "ActivateTabAsync", second)!;
        await UntilAsync(() => Get<bool>(second, "InitialNavigationReady"), "selecting a deferred tab loads it");
        Check(tabs.Count(t => View(t).CoreWebView2 is not null) == 2, "only two selected WebViews exist after switching");
        var core = View(second).CoreWebView2;
        await TestBookmarksAsync(form, second, server);
        var button = Get<object>(second, "Button");
        Check(Get<Rectangle>(button, "MuteBounds").IsEmpty, "a real silent page has no speaker control");
        core.IsMuted = true; // Test audio without producing sound on the user's speakers.
        await core.CallDevToolsProtocolMethodAsync("Runtime.evaluate", JsonSerializer.Serialize(new
        {
            expression = "window.testAudio=new AudioContext();window.testTone=testAudio.createOscillator();testTone.connect(testAudio.destination);testTone.start();testAudio.resume();",
            userGesture = true
        }));
        await UntilAsync(() => Get<bool>(second, "IsPlayingAudio"), "WebView audio events update the tab indicator");
        await core.ExecuteScriptAsync("testTone.stop();testAudio.close();");
        await UntilAsync(() => !Get<bool>(second, "IsPlayingAudio"), "stopping audio updates the tab indicator");
        Check(!Get<Rectangle>(button, "MuteBounds").IsEmpty, "muted tab retains an unmute control after audio stops");
        Call(form, "ToggleTabMute", second);
        await UntilAsync(() => !core.IsMuted && Get<Rectangle>(button, "MuteBounds").IsEmpty, "unmuting a silent tab hides the speaker control");
        var history = Get<object>(form, "_history");
        var before = Get<int>(Items(history, "MostUsedSites").Single(), "VisitCount");
        await core.ExecuteScriptAsync("document.title='Renamed by script';let i=document.createElement('link');i.rel='icon';i.href='/favicon.ico?v=2';document.head.append(i);");
        await Task.Delay(600);
        Check(Get<int>(Items(history, "MostUsedSites").Single(), "VisitCount") == before, "real title/favicon events do not add visits");
        Call(form, "CloseTab", tabs[19]);
        Check(Get<IList>(form, "_tabs").Count == 19, "an unloaded background tab closes without starting a WebView");
        await (Task)Call(form, "ActivateTabAsync", active)!;
        Set(second, "InactiveSinceUtc", DateTime.UtcNow.AddMinutes(-1));
        Set(second, "IsPlayingAudio", true);
        await (Task)Call(form, "SuspendInactiveTabsAsync")!;
        Check(!Get<bool>(second, "IsSuspended"), "memory saving skips audio tabs");
        Set(second, "IsPlayingAudio", false);
        await (Task)Call(form, "SuspendInactiveTabsAsync")!;
        await (Task)Call(form, "ActivateTabAsync", second)!;
        Check(!core.IsSuspended, "selecting an inactive tab resumes its WebView");
        Call(form, "SetMemorySaverEnabled", false);
        Check(!Get<bool>(settings, "LowMemoryMode") && !Get<bool>(second, "IsSuspended"), "turning off memory saver keeps tabs active immediately");
        Call(form, "SetMemorySaverEnabled", true);
        Check(Get<bool>(settings, "LowMemoryMode"), "memory saver can be turned back on");
        var discardTarget = tabs[2];
        await (Task)Call(form, "ActivateTabAsync", discardTarget)!;
        await UntilAsync(() => Get<bool>(discardTarget, "InitialNavigationReady"), "discard candidate finishes navigation");
        await (Task)Call(form, "ActivateTabAsync", second)!;
        var discardedView = View(discardTarget);
        Set(settings, "DiscardInactiveTabs", true);
        Set(discardTarget, "InactiveSinceUtc", DateTime.UtcNow.AddMinutes(-31));
        await (Task)Call(form, "DiscardInactiveTabsAsync")!;
        Check(Get<bool>(discardTarget, "IsDiscarded") && View(discardTarget) != discardedView && View(discardTarget).CoreWebView2 is null,
            "long-idle tab discard releases its WebView controller");
        await (Task)Call(form, "ActivateTabAsync", discardTarget)!;
        await UntilAsync(() => Get<bool>(discardTarget, "InitialNavigationReady"), "discarded tab reloads when selected");
        Check(View(discardTarget).CoreWebView2.Source.EndsWith("tab-2"), "discarded tab keeps its address when reloaded");
        Set(settings, "DiscardInactiveTabs", false);
        await (Task)Call(form, "ActivateTabAsync", second)!;
        var preRecoveryView = View(second);
        await (Task)Call(form, "RecoverBrowserProcessAsync")!;
        await UntilAsync(() => View(second) != preRecoveryView && Get<bool>(second, "InitialNavigationReady"),
            "browser-process recovery recreates the selected tab");
        Check(View(second).CoreWebView2.Source.EndsWith("tab-1") &&
            tabs.Count(tab => !Get<WebView2>(tab, "View").IsDisposed && Get<WebView2>(tab, "View").CoreWebView2 is not null) == 1,
            "browser-process recovery preserves the selected address and lazy tabs");
        await TestScaleDownAsync(form);
        await TestHomeAndSetupAsync(form, second);
        var pending = tabs[18];
        var activating = (Task)Call(form, "ActivateTabAsync", pending)!;
        Call(form, "CloseTab", pending);
        await activating;
        await UntilAsync(() => !Get<IList>(form, "_tabs").Contains(pending), "closing a tab during lazy initialization is safe");
        await TestWorkspaceRuntimeAsync(form, server);
    }

    private static async Task TestWorkspaceRuntimeAsync(MainForm form, LocalPageServer server)
    {
        var settings = Get<object>(form, "_settings");
        var primaryWorkspaceId = Guid.NewGuid();
        var secondaryWorkspaceId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        var workspaces = Get<IList>(settings, "Workspaces");
        var groups = Get<IList>(settings, "TabGroups");
        var routes = Get<IList>(settings, "WorkspaceRoutes");
        workspaces.Clear();
        groups.Clear();
        routes.Clear();

        object MakeWorkspace(Guid id, string name, string color, string icon)
        {
            var workspace = New("TugleWorkspace");
            Set(workspace, "Id", id);
            Set(workspace, "Name", name);
            Set(workspace, "Color", color);
            Set(workspace, "Icon", icon);
            Set(workspace, "SearchEngine", "Google");
            Set(workspace, "TrackingPrevention", "Balanced");
            Set(workspace, "UseWorkspaceMemorySettings", true);
            Set(workspace, "LowMemoryMode", true);
            Set(workspace, "DiscardInactiveTabs", false);
            return workspace;
        }

        var primaryWorkspace = MakeWorkspace(primaryWorkspaceId, "Personal", "#6095E5", "●");
        var secondaryWorkspace = MakeWorkspace(secondaryWorkspaceId, "Research", "#987BEA", "✦");
        workspaces.Add(primaryWorkspace);
        workspaces.Add(secondaryWorkspace);
        Set(settings, "ActiveWorkspaceId", primaryWorkspaceId);
        Call(settings, "ApplyActiveWorkspace");
        foreach (var tab in Get<IList>(form, "_tabs").Cast<object>())
        {
            Set(tab, "WorkspaceId", primaryWorkspaceId);
            Set(tab, "GroupId", null);
        }

        var group = New("TugleTabGroup");
        Set(group, "Id", groupId);
        Set(group, "WorkspaceId", secondaryWorkspaceId);
        Set(group, "Name", "Development");
        Set(group, "Color", "#987BEA");
        Set(group, "Icon", "◆");
        groups.Add(group);

        async Task<object> CreateDeferredWorkspaceTabAsync()
        {
            var creation = (Task)Call(form, "OpenNewTabAsync", false, false, true, null, secondaryWorkspaceId)!;
            await creation;
            return creation.GetType().GetProperty("Result")!.GetValue(creation)!;
        }

        var first = await CreateDeferredWorkspaceTabAsync();
        var second = await CreateDeferredWorkspaceTabAsync();
        Check(Get<Guid>(first, "WorkspaceId") == secondaryWorkspaceId && Get<Guid>(second, "WorkspaceId") == secondaryWorkspaceId,
            "new workspace tabs retain their separate tab-set identity");
        Check((bool)Call(form, "AssignTabToGroup", first, groupId)! &&
            (bool)Call(form, "AssignTabToGroup", second, groupId)!,
            "workspace tabs can be placed into a shared group");

        var rules = Get<IList>(secondaryWorkspace, "Rules");
        var rule = New("TugleWorkspaceRule");
        Set(rule, "Id", Guid.NewGuid());
        Set(rule, "WorkspaceId", secondaryWorkspaceId);
        Set(rule, "TargetGroupId", groupId);
        Set(rule, "HostPattern", new Uri(server.Url).Host);
        Set(rule, "Enabled", true);
        rules.Add(rule);
        Set(first, "GroupId", null);
        Check((bool)Call(form, "ApplyWorkspaceRule", first, server.Url + "workspace-rule")! &&
            Get<Guid?>(first, "GroupId") == groupId,
            "host rules automatically assign matching tabs to a group");

        await (Task)Call(form, "SwitchWorkspaceAsync", secondaryWorkspaceId)!;
        Check(Get<Guid?>(settings, "ActiveWorkspaceId") == secondaryWorkspaceId &&
            Get<Guid>(Get<object>(form, "_activeTab"), "WorkspaceId") == secondaryWorkspaceId,
            "workspace switching activates the selected tab set");
        var displayed = ((IEnumerable)Call(form, "GetDisplayedTabs")!).Cast<object>().ToArray();
        Check(displayed.Length == 2 && displayed.All(tab => Get<Guid>(tab, "WorkspaceId") == secondaryWorkspaceId),
            "workspace layout filters tabs from other workspaces");

        Check((bool)Call(form, "SetTabGroupCollapsed", groupId, true)!, "tab groups collapse without closing their tabs");
        displayed = ((IEnumerable)Call(form, "GetDisplayedTabs")!).Cast<object>().ToArray();
        Check(displayed.Length == 1 && Get<IList>(form, "_tabs").Cast<object>().Count(tab => Get<Guid>(tab, "WorkspaceId") == secondaryWorkspaceId) == 2,
            "collapsed groups keep their tabs while showing one compact representative");
        await (Task)Call(form, "ActivateTabAsync", second)!;
        Check(!Get<bool>(group, "IsCollapsed"), "opening a hidden tab expands its group safely");

        Call(form, "SelectTabFromPointer", first, Keys.None);
        Call(form, "SelectTabFromPointer", second, Keys.Control);
        var selected = Get<IEnumerable>(form, "_selectedTabs").Cast<object>().ToArray();
        Check(selected.Length == 2 && selected.All(tab => Get<Guid>(tab, "WorkspaceId") == secondaryWorkspaceId),
            "Ctrl selection supports multi-tab group operations");

        await (Task)Call(form, "SwitchWorkspaceAsync", primaryWorkspaceId)!;
        Check(Get<Guid?>(settings, "ActiveWorkspaceId") == primaryWorkspaceId &&
            Get<IEnumerable>(form, "_tabs").Cast<object>().Where(tab => Get<Guid>(tab, "WorkspaceId") == secondaryWorkspaceId)
                .All(tab => !Get<Control>(tab, "Button").Visible),
            "switching back hides the other workspace's tab controls");

        var route = New("TugleWorkspaceRoute");
        Set(route, "Id", Guid.NewGuid());
        Set(route, "HostPattern", new Uri(server.Url).Host);
        Set(route, "TargetWorkspaceId", secondaryWorkspaceId);
        Set(route, "TargetGroupId", (Guid?)groupId);
        Set(route, "Enabled", true);
        routes.Add(route);
        Call(form, "SetTabGroupCollapsed", groupId, true);
        var resolvedRoute = Call(form, "GetWorkspaceRoute", server.Url + "smart-route")!;
        Check(Get<Guid>(resolvedRoute, "TargetWorkspaceId") == secondaryWorkspaceId &&
            Get<Guid?>(resolvedRoute, "TargetGroupId") == groupId,
            "smart link routes resolve a domain to its workspace and group");

        var routedTab = Get<object>(form, "_activeTab");
        await (Task)Call(form, "ApplyWorkspaceRouteAsync", routedTab, resolvedRoute, true)!;
        Check(Get<Guid>(routedTab, "WorkspaceId") == secondaryWorkspaceId && Get<Guid?>(routedTab, "GroupId") == groupId &&
            Get<Guid?>(settings, "ActiveWorkspaceId") == secondaryWorkspaceId && !Get<bool>(group, "IsCollapsed") &&
            ReferenceEquals(Get<object>(form, "_activeTab"), routedTab),
            "smart link routing moves and reveals the tab in its destination workspace");

        var commandEntries = ((IEnumerable)Call(form, "BuildCommandPaletteEntries")!).Cast<object>().ToArray();
        Check(commandEntries.Any(entry => Get<string>(entry, "Title") == "Toggle split view") &&
            commandEntries.Any(entry => Get<string>(entry, "Title").Contains("Research")),
            "command palette indexes browser actions and workspaces");

        using (var tabMenu = (ContextMenuStrip)Call(form, "CreateTabContextMenu", first)!)
        {
            Check(tabMenu.Items.OfType<ToolStripItem>().Any(item => item.Text == "Move to workspace"),
                "tab menu offers a direct workspace move");
        }

        await (Task)Call(form, "OpenSplitViewAsync", first)!;
        var splitLeft = Get<object>(form, "_splitLeftTab");
        var splitRight = Get<object>(form, "_splitRightTab");
        var divider = Get<Control>(form, "_splitDivider");
        Check(splitLeft is not null && splitRight is not null && divider.Visible &&
            Get<WebView2>(splitLeft, "View").Visible && Get<WebView2>(splitRight, "View").Visible &&
            Get<WebView2>(splitLeft, "View").Right <= Get<WebView2>(splitRight, "View").Left,
            "split view keeps two workspace tabs visible in separate panes");
        Call(form, "FocusSplitTab", first);
        Check(ReferenceEquals(Get<object>(form, "_activeTab"), first), "split view updates the active tab when a pane is focused");
        Call(form, "CloseSplitView", true);
        Check(Value(form, "_splitLeftTab") is null && Value(form, "_splitRightTab") is null && !divider.Visible,
            "split view closes without closing either tab");
    }

    private static async Task TestScaleDownAsync(MainForm form)
    {
        foreach (var state in new[] { FormWindowState.Normal, FormWindowState.Maximized })
        {
            form.WindowState = state;
            foreach (var scale in new[] { 1.4f, 1.0f, .7f, .9f })
            {
                Call(form, "SetGuiScale", scale);
                await Task.Delay(100);
                var toolbar = Get<Control>(form, "_toolbar");
                var navigation = Get<Control>(form, "_navigation");
                var title = Get<Control>(form, "_titleArea");
                var windowButtons = Get<Control>(form, "_windowButtons");
                Console.WriteLine($"Scaling {state} {scale}: dpi={form.DeviceDpi} mode={form.AutoScaleMode} bounds={form.Bounds} client={form.ClientRectangle} toolbar={toolbar.Bounds} navigation={navigation.Bounds} title={title.Bounds}");
                Check(form.ClientRectangle.Contains(toolbar.Bounds) && navigation.Width > 0 && navigation.Left >= 0 && title.Width > 0,
                    $"scale-down keeps toolbar and tabs in the client area ({state} {scale})");
                Check(navigation.Controls.Cast<Control>().All(c => c.Visible && c.Left >= 0 && c.Right <= navigation.Width), "scale-down keeps all navigation buttons visible");
                var active = Get<object>(form, "_activeTab");
                var button = Get<Control>(active, "Button");
                Check(button.Visible, "scale-down keeps selected tab visible");
                Check(!title.Bounds.IntersectsWith(windowButtons.Bounds), "tabs do not overlap window controls");
                Check(title.ClientRectangle.Contains(Get<Control>(form, "_newTabButton").Bounds), "new-tab button stays inside the tab strip");
                Check(Get<WebView2>(active, "View").Bounds == Get<Control>(form, "_contentHost").ClientRectangle, "WebView fills the resized content area");
                if (state == FormWindowState.Normal)
                    Check(form.PointToScreen(Point.Empty).Y == form.Top, "native caption does not leave a white strip");
                SaveControl(Get<Control>(form, "_titleBar"), $"Scale-tabs-{state}-{scale:0.0}");
                SaveControl(toolbar, $"Scale-toolbar-{state}-{scale:0.0}");
            }
        }
        form.WindowState = FormWindowState.Normal;
        form.Size = form.MinimumSize;
        Call(form, "SetGuiScale", .7f);
        await Task.Delay(100);
        var address = Get<Control>(form, "_addressSurface");
        Check(address.Width >= 100 && address.Left > 0, "address stays usable at minimum window size");
        Call(form, "ToggleFullscreen");
        Call(form, "SetGuiScale", 1.4f);
        Call(form, "SetGuiScale", .7f);
        Call(form, "ToggleFullscreen");
        Check(Get<Control>(form, "_newTabButton").Visible, "fullscreen scale changes preserve new-tab access");
        form.Size = new Size(1200, 850);
    }

    private static async Task TestBookmarksAsync(MainForm form, object tab, LocalPageServer server)
    {
        var core = Get<WebView2>(tab, "View").CoreWebView2;
        await UntilAsync(() => tab.GetType().GetProperty("FaviconImage")!.GetValue(tab) is Image, "real page favicon is available from WebView2");
        var library = Get<object>(form, "_library");
        var modes = typeof(MainForm).GetNestedType("ActionFlyoutMode", BindingFlags.NonPublic)!;
        foreach (var scale in new[] { .7f, .9f, 1.4f })
        {
            Set(form, "_guiScale", scale);
            Call(form, "ApplyGuiScale");
            Call(form, "HideActionFlyout");
            Call(form, "ShowActionFlyout", Enum.Parse(modes, "Library"));
            var rows = Get<FlowLayoutPanel>(form, "_actionFlyoutItems");
            var save = rows.Controls.Cast<Control>().First(c => c.Text == "Bookmark this page");
            Check(Get<bool>(save, "Prominent") && save.Height >= 46 * scale - 1, "bookmark action is filled and full-size");
            Check(save.Right <= rows.ClientSize.Width, "bookmark action fits scaled panel");
            SaveControl(Get<Control>(form, "_actionFlyout"), $"Bookmark-action-{scale.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)}");
        }
        Call(form, "ToggleBookmark", tab);
        var entry = Items(library, "Bookmarks").First(item => Get<string>(item, "Url") == core.Source);
        Check(entry.GetType().GetProperty("IconPng")!.GetValue(entry) is string, "bookmarking captures the actual page icon immediately");
        Call(form, "PopulateActionFlyout");
        var items = Get<FlowLayoutPanel>(form, "_actionFlyoutItems");
        var savedRow = items.Controls.Cast<Control>().First(c => Equals(c.Tag, Get<Guid>(entry, "Id")));
        Check(savedRow.GetType().GetProperty("SiteIcon")!.GetValue(savedRow) is Image, "newly saved bookmark displays the page icon");
        var count = server.IconRequests;
        Call(form, "PopulateActionFlyout");
        Call(form, "PopulateActionFlyout");
        await Task.Delay(150);
        Check(server.IconRequests == count, "reopening bookmarks uses cached icons without new downloads");

        var oldUrl = server.Url + "old-bookmark";
        Call(library, "ToggleBookmark", oldUrl, "Older bookmark", null);
        var oldEntry = Items(library, "Bookmarks").First(item => Get<string>(item, "Url") == oldUrl);
        Call(form, "PopulateActionFlyout");
        await UntilAsync(() => oldEntry.GetType().GetProperty("IconPng")!.GetValue(oldEntry) is string, "older bookmarks fetch and cache their favicon without loading a page");
        Check(server.IconRequests == count + 1, "fallback favicon download occurs only once");
        // Unsupported, large, and malformed responses must not block or break the panel.
        Check(await (Task<string?>)IconCall("DownloadAsync", "file:///not-allowed.ico", CancellationToken.None)! is null, "favicon downloader rejects local file URLs");
        Check(await (Task<string?>)IconCall("DownloadAsync", server.Url + "invalid-icon", CancellationToken.None)! is null, "invalid favicon response falls back safely");
        Check(await (Task<string?>)IconCall("DownloadAsync", server.Url + "large-icon", CancellationToken.None)! is null, "oversized favicon download is rejected");
        Check(await (Task<string?>)IconCall("DownloadAsync", server.Url + "large-stream-icon", CancellationToken.None)! is null, "favicon stream is bounded even without Content-Length");
        Set(form, "_guiScale", .9f);
        Call(form, "ApplyGuiScale");
        Call(form, "HideActionFlyout");
    }

    private static async Task TestHomeAndSetupAsync(MainForm form, object tab)
    {
        var core = Get<WebView2>(tab, "View").CoreWebView2;
        await LoadHomeAsync(form, tab);
        Check(await core.ExecuteScriptAsync("document.querySelector('.section-label').textContent") == "\"Most used sites\"", "Home displays most used sites");
        await TestSearchRoutesAsync(form, tab);
        var theme = new { background = "#071526", backgroundMode = "video", backgroundMedia = "file:///nonexistent-regression-test.webm" };
        var json = JsonSerializer.Serialize(theme);
        await core.ExecuteScriptAsync($"tugleSetTheme({json});window.originalVideo=document.querySelector('video');tugleSetTheme({json});");
        Check(await core.ExecuteScriptAsync("window.originalVideo === document.querySelector('video')") == "true", "unchanged home theme preserves the existing video element");
        // Reset the test media before taking the start-page preview.
        await (Task)Call(form, "PopulateHomeAsync", tab)!;
        await using (var stream = File.Create(Path.Combine(Output, "Home.png")))
            await core.CapturePreviewAsync(Microsoft.Web.WebView2.Core.CoreWebView2CapturePreviewImageFormat.Png, stream);
        var ready = new TaskCompletionSource();
        void Loaded(object? sender, Microsoft.Web.WebView2.Core.CoreWebView2NavigationCompletedEventArgs e)
        {
            core.NavigationCompleted -= Loaded;
            if (e.IsSuccess) ready.TrySetResult(); else ready.TrySetException(new Exception("Setup preview navigation failed"));
        }
        core.NavigationCompleted += Loaded;
        core.Navigate(new Uri(Path.Combine(AppContext.BaseDirectory, "TugleSetup.html")).AbsoluteUri);
        await ready.Task.WaitAsync(TimeSpan.FromSeconds(20));
        var palettes = ((IEnumerable)AppType("ThemePalettes").GetProperty("ColorOptions")!.GetValue(null)!).Cast<object>();
        var paletteData = AppType("FirstRunSetupForm").GetMethod("PaletteData", BindingFlags.Static | BindingFlags.NonPublic)!;
        var data = palettes.Select(palette => paletteData.Invoke(null, [palette]));
        await core.ExecuteScriptAsync($"themes={JsonSerializer.Serialize(data)};step=1;theme='Slate';render(false);");
        Check(await core.ExecuteScriptAsync("document.querySelectorAll('.theme').length") == "12", "setup renders all twelve theme choices");
        Check(await core.ExecuteScriptAsync("document.querySelector('.theme[aria-pressed=true]').textContent") == "\"Slate\"", "Slate is selectable in setup");
        await using (var stream = File.Create(Path.Combine(Output, "Setup-Slate.png")))
            await core.CapturePreviewAsync(Microsoft.Web.WebView2.Core.CoreWebView2CapturePreviewImageFormat.Png, stream);
    }

    private static async Task TestSearchRoutesAsync(MainForm form, object tab)
    {
        var core = Get<WebView2>(tab, "View").CoreWebView2;
        const string query = "café + cats & dogs / 漢字";
        foreach (var provider in new[] { "Google", "DuckDuckGo", "Bing" })
        {
            Call(form, "SetSearchProvider", provider);
            await (Task)Call(form, "PopulateHomeAsync", tab)!;
            Check(await core.ExecuteScriptAsync("document.getElementById('q').placeholder") == JsonSerializer.Serialize("Search " + provider), "Home label follows " + provider);
            var expected = (string)StaticCall("SearchProvider", "BuildUrl", provider, query)!;
            foreach (var fromHome in new[] { true, false })
            {
                var destination = new TaskCompletionSource<string>();
                void Starting(object? sender, Microsoft.Web.WebView2.Core.CoreWebView2NavigationStartingEventArgs e)
                {
                    if (!e.Uri.StartsWith("https://", StringComparison.Ordinal)) return;
                    e.Cancel = true;
                    destination.TrySetResult(e.Uri);
                }
                core.NavigationStarting += Starting;
                try
                {
                    if (fromHome)
                        await core.ExecuteScriptAsync($"document.getElementById('q').value={JsonSerializer.Serialize(query)};document.querySelector('form').requestSubmit();");
                    else
                    {
                        Get<TextBox>(form, "_addressBar").Text = query;
                        Call(form, "NavigateFromAddressBar");
                    }
                    Check(new Uri(await destination.Task.WaitAsync(TimeSpan.FromSeconds(10))) == new Uri(expected), $"{provider} receives the query from {(fromHome ? "Home" : "address bar")}");
                }
                finally { core.NavigationStarting -= Starting; }
                await LoadHomeAsync(form, tab);
            }
        }
        Call(form, "SetSearchProvider", "DuckDuckGo");
        await (Task)Call(form, "PopulateHomeAsync", tab)!;
        var autocompleteMessages = 0;
        void Message(object? sender, Microsoft.Web.WebView2.Core.CoreWebView2WebMessageReceivedEventArgs e)
        {
            if (e.TryGetWebMessageAsString().StartsWith("tugle:google-autocomplete:")) autocompleteMessages++;
        }
        core.WebMessageReceived += Message;
        try
        {
            await core.ExecuteScriptAsync("document.getElementById('q').value='privacy test';document.getElementById('q').dispatchEvent(new Event('input'));");
            await Task.Delay(600);
            Check(autocompleteMessages == 0, "DuckDuckGo does not send typing to Google autocomplete");
        }
        finally { core.WebMessageReceived -= Message; }
        await core.ExecuteScriptAsync("document.getElementById('q').value='';document.getElementById('q').dispatchEvent(new Event('input'));document.getElementById('q').blur();");
        Check(!(bool)await (Task<bool>)StaticCall("GoogleSession", "IsConnectedAsync", core)!, "fresh Tugle profile is not falsely signed into Google");
        var cookie = core.CookieManager.CreateCookie("SID", "tugle-local-regression-test", ".google.com", "/");
        cookie.IsSecure = true;
        core.CookieManager.AddOrUpdateCookie(cookie);
        try { Check(await (Task<bool>)StaticCall("GoogleSession", "IsConnectedAsync", core)!, "Google session detection reads this profile's auth cookie"); }
        finally { Check(await (Task<bool>)StaticCall("GoogleSession", "ClearCookiesAsync", core)!, "Google sign-out waits for this profile's cookie deletion"); }
        Check(!await (Task<bool>)StaticCall("GoogleSession", "IsConnectedAsync", core)!, "Google session status clears after its cookie is removed");
    }

    private static async Task TestNetworkAsync(MainForm form)
    {
        await (Task)Call(form, "ConnectGoogleAccountAsync")!;
        var account = Get<object>(form, "_googleAccountTab");
        var count = Get<IList>(form, "_tabs").Count;
        var core = Get<WebView2>(account, "View").CoreWebView2;
        await UntilAsync(() => core.Source.StartsWith("https://accounts.google.com/") && core.DocumentTitle.Contains("Google"), "Google sign-in loads inside Tugle's own tab");
        await (Task)Call(form, "ConnectGoogleAccountAsync")!;
        Check(Get<IList>(form, "_tabs").Count == count, "Google account action reuses its existing tab");
        await Task.Delay(1500);
        await using (var image = File.Create(Path.Combine(Output, "Google-sign-in.png")))
            await core.CapturePreviewAsync(Microsoft.Web.WebView2.Core.CoreWebView2CapturePreviewImageFormat.Png, image);
        var navigation = new TaskCompletionSource();
        void Loaded(object? sender, Microsoft.Web.WebView2.Core.CoreWebView2NavigationCompletedEventArgs e)
        {
            if (!core.Source.StartsWith("https://duckduckgo.com/")) return;
            if (e.IsSuccess) navigation.TrySetResult(); else navigation.TrySetException(new Exception("DuckDuckGo navigation: " + e.WebErrorStatus));
        }
        core.NavigationCompleted += Loaded;
        try
        {
            core.Navigate((string)StaticCall("SearchProvider", "BuildUrl", "DuckDuckGo", "Microsoft WebView2")!);
            await navigation.Task.WaitAsync(TimeSpan.FromSeconds(30));
            await Task.Delay(2000);
            Check(core.DocumentTitle.Contains("DuckDuckGo"), "real DuckDuckGo search document loads in WebView2");
            Console.WriteLine("DuckDuckGo result links: " + await core.ExecuteScriptAsync("document.querySelectorAll('[data-testid=\"result-title-a\"]').length"));
            await using var image = File.Create(Path.Combine(Output, "DuckDuckGo-search.png"));
            await core.CapturePreviewAsync(Microsoft.Web.WebView2.Core.CoreWebView2CapturePreviewImageFormat.Png, image);
        }
        finally { core.NavigationCompleted -= Loaded; }
        await (Task)Call(form, "ConnectGoogleAccountAsync")!;
        Check(Get<IList>(form, "_tabs").Count == count + 1 && core.Source.StartsWith("https://duckduckgo.com/"), "Google account does not replace a tab the user navigated elsewhere");
    }

    private static async Task LoadHomeAsync(MainForm form, object tab)
    {
        var core = Get<WebView2>(tab, "View").CoreWebView2;
        var ready = new TaskCompletionSource();
        void Loaded(object? sender, Microsoft.Web.WebView2.Core.CoreWebView2NavigationCompletedEventArgs e)
        {
            if (e.IsSuccess && core.Source.EndsWith("TugleHome.html", StringComparison.OrdinalIgnoreCase)) ready.TrySetResult();
        }
        core.NavigationCompleted += Loaded;
        try
        {
            Call(form, "ShowHome", tab);
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(15));
            await (Task)Call(form, "PopulateHomeAsync", tab)!;
        }
        finally { core.NavigationCompleted -= Loaded; }
    }

    private sealed class LocalPageServer : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stop = new();
        private readonly byte[] _icon = Convert.FromBase64String(TestIconPng());
        private int _iconRequests;
        public int IconRequests => Volatile.Read(ref _iconRequests);
        public string Url { get; }
        public LocalPageServer()
        {
            _listener.Start();
            Url = $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/";
            _ = Task.Run(async () =>
            {
                try
                {
                    while (!_stop.IsCancellationRequested)
                    {
                        var client = await _listener.AcceptTcpClientAsync(_stop.Token);
                        _ = ServeAsync(client);
                    }
                }
                catch (OperationCanceledException) { }
                catch (SocketException) when (_stop.IsCancellationRequested) { }
            });
        }
        private async Task ServeAsync(TcpClient client)
        {
            using (client)
            using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token))
            {
                deadline.CancelAfter(TimeSpan.FromSeconds(5));
                try
                {
                    await using var stream = client.GetStream();
                    var request = new byte[8192];
                    var read = await stream.ReadAsync(request, deadline.Token);
                    if (read == 0) return;
                    var path = Encoding.UTF8.GetString(request, 0, read).Split(' ')[1];
                    var icon = path.StartsWith("/favicon.ico");
                    if (icon) Interlocked.Increment(ref _iconRequests);
                    var large = path.StartsWith("/large-");
                    const string body = "<html><head><title>Local test page</title><link rel='icon' href='/favicon.ico'></head><body>Offline regression test</body></html>";
                    var payload = icon ? _icon : large ? new byte[300 * 1024] : Encoding.UTF8.GetBytes(body);
                    var lengthHeader = path == "/large-stream-icon" ? "" : $"Content-Length: {payload.Length}\r\n";
                    var headers = Encoding.UTF8.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: {(icon ? "image/png" : "text/html")}\r\n{lengthHeader}Connection: close\r\n\r\n");
                    await stream.WriteAsync(headers, deadline.Token);
                    await stream.WriteAsync(payload, deadline.Token);
                }
                catch (OperationCanceledException) { }
                catch (IOException) { } // Chromium may preconnect or a bounded icon request may close early.
            }
        }
        public void Dispose() { _stop.Cancel(); _listener.Stop(); }
    }
}
