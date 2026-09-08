using System.Collections;
using System.Diagnostics;
using System.Drawing.Imaging;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
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
    private static void Set(object target, string name, object? value)
    {
        var property = target.GetType().GetProperty(name, Members);
        if (property is not null) property.SetValue(target, value);
        else target.GetType().GetField(name, Members)!.SetValue(target, value);
    }
    private static object[] Items(object target, string name) => Get<IEnumerable>(target, name).Cast<object>().ToArray();
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        Console.WriteLine("PASS " + message);
    }

    [STAThread]
    private static void Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.DpiUnaware);
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
        var privatePath = Path.Combine(Profile, "private");
        using (var history = (IDisposable)New("HistoryStore", privatePath, false))
            Call(history, "RecordVisit", "https://private.example/", "Private", null);
        Call(New("LibraryStore", privatePath, false), "ToggleBookmark", "https://private.example/", "Private", null);
        Check(!Directory.Exists(privatePath), "private stores do not write browsing data");
        var palettes = ((IEnumerable)AppType("ThemePalettes").GetProperty("ColorOptions")!.GetValue(null)!).Cast<object>();
        Check(palettes.Count() == 12 && palettes.Any(p => Get<string>(p, "Name") == "Slate"), "shared browser/setup palette includes Slate");
    }

    private static void TestChrome(MainForm form)
    {
        var library = Get<object>(form, "_library");
        for (var i = 0; i < 45; i++) Call(library, "ToggleBookmark", $"https://example.com/page-{i}", $"Bookmark {i}: design notes and useful references", null);
        var modes = typeof(MainForm).GetNestedType("ActionFlyoutMode", BindingFlags.NonPublic)!;
        foreach (var scale in new[] { .7f, .9f, 1.4f })
        {
            Set(form, "_guiScale", scale);
            Call(form, "ApplyGuiScale");
            foreach (var field in new[] { "_navigation", "_utilityActions" })
            {
                var panel = Get<Control>(form, field);
                Check(panel.Controls.Cast<Control>().All(c => c.Right <= panel.ClientSize.Width), $"toolbar fits at {scale}");
            }
            foreach (var mode in new[] { "Library", "Theme", "Privacy", "Downloads", "Accounts" })
            {
                Call(form, "HideActionFlyout");
                Call(form, "ShowActionFlyout", Enum.Parse(modes, mode));
                var panel = Get<Control>(form, "_actionFlyout");
                var items = Get<FlowLayoutPanel>(form, "_actionFlyoutItems");
                Check(items.Controls.Cast<Control>().All(c => c.Right <= items.ClientSize.Width), $"{mode} rows fit at {scale}");
                if (mode == "Downloads") Check(panel.Height < 200 * scale, "empty downloads panel fits its contents");
                if (mode == "Theme") Check(items.Controls.Cast<Control>().Where(c => c.GetType().Name == "FlyoutSectionLabel").Any(c => Get<string>(c, "DetailText") == "12 colors"), "theme count matches available colors");
                SaveControl(panel, $"{mode}-{scale.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)}");
                if (mode == "Library")
                {
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
                button.SetBounds((int)(state * 200 * scale), 0, (int)(194 * scale), tabRow.Height);
                button.Text = state == 0 ? "Silent page" : state == 1 ? "Playing audio" : "Muted tab";
                button.ForeColor = Color.FromArgb(235, 240, 248);
                button.Font = new Font("Segoe UI", 10 * scale);
                tabRow.Controls.Add(button);
                var bounds = Get<Rectangle>(button, "MuteBounds");
                Check(bounds.IsEmpty == (state == 0), "audio hit target only exists for playing/muted tabs");
                if (!bounds.IsEmpty) Check(!bounds.IntersectsWith(Get<Rectangle>(button, "CloseBounds")), "mute and close hit targets do not overlap");
            }
            SaveControl(tabRow, $"Tabs-{scale.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)}");
        }
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
        await TestHomeAndSetupAsync(form, second);
        var pending = tabs[18];
        var activating = (Task)Call(form, "ActivateTabAsync", pending)!;
        Call(form, "CloseTab", pending);
        await activating;
        await UntilAsync(() => !Get<IList>(form, "_tabs").Contains(pending), "closing a tab during lazy initialization is safe");
    }

    private static async Task TestHomeAndSetupAsync(MainForm form, object tab)
    {
        var core = Get<WebView2>(tab, "View").CoreWebView2;
        Call(form, "ShowHome", tab);
        await UntilAsync(() => Get<bool>(tab, "IsHome"), "home page loads after browsing");
        await Task.Delay(200);
        Check(await core.ExecuteScriptAsync("document.querySelector('.section-label').textContent") == "\"Most used sites\"", "Home displays most used sites");
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

    private sealed class LocalPageServer : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stop = new();
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
                        using var client = await _listener.AcceptTcpClientAsync(_stop.Token);
                        await using var stream = client.GetStream();
                        var request = new byte[8192];
                        await stream.ReadAsync(request, _stop.Token);
                        const string body = "<html><head><title>Local test page</title></head><body>Offline regression test</body></html>";
                        var response = Encoding.UTF8.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: text/html\r\nContent-Length: {Encoding.UTF8.GetByteCount(body)}\r\nConnection: close\r\n\r\n{body}");
                        await stream.WriteAsync(response, _stop.Token);
                    }
                }
                catch (OperationCanceledException) { }
                catch (SocketException) when (_stop.IsCancellationRequested) { }
            });
        }
        public void Dispose() { _stop.Cancel(); _listener.Stop(); }
    }
}
