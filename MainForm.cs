using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Net;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Tugle;

public sealed class MainForm : SnapWindowForm
{
    protected override bool IsImmersiveFullscreen => _f11FullscreenMode;
    private Button? _maximizeButton;
    private float _guiScale = 0.9f;
    private readonly TugleSettings _settings;
    private float GuiScale => _guiScale;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpFrameChanged = 0x0020;
    private const int WmSetRedraw = 0x000B;
    private const int WmMouseWheel = 0x020A;
    private const int F11HotKeyId = 0x5475;
    private const int WmHotKey = 0x0312;

    private ThemePalette _theme = ThemePalettes.Ocean;
    private ThemePalette _selectedTheme = ThemePalettes.Ocean;
    private static readonly Color DefaultCustomColor = Color.FromArgb(128, 128, 128);
    private Color _homeBackground = ColorTranslator.FromHtml("#071526");
    private Color _homeBackgroundSecondary = ColorTranslator.FromHtml("#203D5B");
    private string _homeBackgroundMode = "gradient";
    private string? _homeBackgroundMediaUrl;
    private Color _chrome = ThemePalettes.Ocean.Chrome;
    private Color _chromeLighter = ThemePalettes.Ocean.ChromeLighter;
    private Color _text = ThemePalettes.Ocean.Text;
    private Color _muted = ThemePalettes.Ocean.Muted;
    private Color _iconVisible = ThemePalettes.Ocean.Icon;
    private Color _accent = ThemePalettes.Ocean.Accent;

    private readonly TextBox _addressBar = new();
    private readonly NavigationButton _backButton = new(NavigationIcon.Back);
    private readonly NavigationButton _forwardButton = new(NavigationIcon.Forward);
    private readonly NavigationButton _reloadButton = new(NavigationIcon.Reload);
    private readonly NavigationButton _homeButton = new(NavigationIcon.Home);
    private readonly HistoryButton _historyButton = new();
    private readonly GuiScaleButton _guiScaleButton = new();
    private readonly ThemeButton _themeButton = new();
    private readonly BookmarkButton _bookmarksButton = new();
    private readonly DownloadButton _downloadsButton = new();
    private readonly AccountButton _accountButton = new();
    private readonly RoundedSurface _addressSurface = new();
    private readonly ToolTip _tooltips = new();
    private readonly Panel _contentHost = new();
    private readonly Panel _splitDivider = new();
    private readonly Panel _tabsFlow = new();
    private readonly FlowLayoutPanel _navigation = new();
    private readonly FlowLayoutPanel _utilityActions = new();
    private readonly TabAddButton _newTabButton = new();
    private readonly WorkspaceSwitchButton _workspaceButton = new();
    private readonly ContextMenuStrip _downloadsMenu = new();
    private readonly ContextMenuStrip _historyMenu = new();
    private readonly ContextMenuStrip _guiScaleMenu = new();
    private readonly ContextMenuStrip _workspaceMenu = new();
    private readonly TabStripMouseWheelFilter _tabWheelFilter;
    private readonly RoundedFlyoutPanel _actionFlyout = new();
    private readonly Label _actionFlyoutTitle = new();
    private readonly FlowLayoutPanel _actionFlyoutItems = new();
    private readonly CloseGlyphButton _actionFlyoutClose = new();
    private readonly System.Windows.Forms.Timer _suspendTimer = new() { Interval = 10000 };
    private readonly PrivateFontCollection _privateFonts = new();
    private readonly List<BrowserTab> _tabs = [];
    private readonly List<DownloadItem> _downloads = [];
    private readonly HistoryStore _history;
    private readonly LibraryStore _library;
    private readonly bool _isPrivate;
    private readonly Image? _homeTabIcon;
    private CoreWebView2Environment? _environment;
    private Task<CoreWebView2Environment>? _environmentTask;
    private CoreWebView2BrowserExtension? _uBlockExtension;
    private CoreWebView2BrowserExtension? _cookieGuardExtension;
    private BrowserTab? _activeTab;
    private BrowserTab? _splitLeftTab;
    private BrowserTab? _splitRightTab;
    private BrowserTab? _draggedTab;
    private readonly HashSet<BrowserTab> _selectedTabs = [];
    private readonly List<BrowserTab> _draggedTabs = [];
    private BrowserTab? _selectionAnchor;
    private BrowserTab? _dragGroupTarget;
    private bool _creatingTab;
    private int _queuedTabRequests;
    private bool _uBlockEnabled;
    private bool _cookieGuardEnabled;
    private bool _uBlockLoadAttempted;
    private bool _cookieGuardLoadAttempted;
    private Task? _protectionPreparationTask;
    private bool _suspensionSweepRunning;
    private bool _recoveringBrowserProcess;
    private int _revealVersion;
    private int _tabScrollOffset;
    private int _tabContentWidth;
    private int _tabViewportWidth;
    private float _splitRatio = 0.5f;
    private bool _draggingSplitDivider;
    private Panel? _titleBar;
    private Panel? _titleArea;
    private TableLayoutPanel? _toolbar;
    private FlowLayoutPanel? _windowButtons;
    private bool _applyingGuiScale;
    private bool? _googleSignedIn;
    private bool _googleSignInBlocked;
    private bool _openingGoogleAccount;
    private BrowserTab? _googleAccountTab;
    private DateTime _lastFullscreenToggleUtc;
    private bool _f11FullscreenMode;
    private bool _f11HotKeyRegistered;
    private bool _f11PreviousTopMost;
    private FormWindowState _f11PreviousWindowState;
    private Rectangle _f11PreviousBounds;
    private bool _correctingRestoredBounds;
    private bool _checkingForUpdates;
    private FontFamily? _uiFontFamily;
    private ActionFlyoutMode _actionFlyoutMode;
    private string _bookmarkQuery = string.Empty;
    private int _bookmarkLimit = 30;
    private Guid? _editingBookmark;
    private readonly Dictionary<string, Task<string?>> _bookmarkIconLoads = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (DateTime ExpiresUtc, IReadOnlyList<string> Suggestions)> _googleSuggestionCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly CancellationTokenSource _bookmarkIconCancellation = new();

    private static readonly TimeSpan InactiveTabDelay = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan DiscardInactiveTabDelay = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan GoogleSuggestionCacheLifetime = TimeSpan.FromMinutes(2);
    private static readonly HttpClient GoogleSuggestionClient = new()
    {
        Timeout = TimeSpan.FromSeconds(3)
    };

    private static readonly HttpClient HistoryIconClient = new()
    {
        Timeout = TimeSpan.FromSeconds(4)
    };

    private static readonly float[] GuiScales = [0.7f, 0.8f, 0.9f, 1.0f, 1.1f, 1.2f, 1.3f, 1.4f];

    private sealed record WorkspaceColorChoice(string Name, Color Color);

    // Deliberately a small, high-contrast palette. These colors are used as a
    // quiet tab accent, not as a second theme system.
    private static readonly WorkspaceColorChoice[] WorkspaceColors =
    [
        new("Blue", Color.FromArgb(96, 149, 229)),
        new("Mint", Color.FromArgb(84, 184, 155)),
        new("Violet", Color.FromArgb(152, 123, 234)),
        new("Rose", Color.FromArgb(227, 122, 167)),
        new("Amber", Color.FromArgb(231, 182, 92)),
        new("Coral", Color.FromArgb(231, 123, 104))
    ];

    private static readonly string[] WorkspaceIcons = ["●", "◆", "▣", "✦", "▰", "⌘"];

    private sealed record WorkspaceTemplate(
        string Name,
        string Icon,
        Color Color,
        string[] GroupNames);

    private static readonly WorkspaceTemplate[] WorkspaceTemplates =
    [
        new("School", "▣", WorkspaceColors[1].Color, ["Courses", "Assignments"]),
        new("Work", "◆", WorkspaceColors[0].Color, ["Focus", "Meetings"]),
        new("Research", "✦", WorkspaceColors[2].Color, ["Reading", "Sources"])
    ];

    private static readonly HashSet<string> BlockedHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "doubleclick.net",
        "googlesyndication.com",
        "googleadservices.com",
        "adservice.google.com",
        "adsrvr.org",
        "adnxs.com",
        "amazon-adsystem.com",
        "criteo.com",
        "criteo.net",
        "demdex.net",
        "doubleverify.com",
        "moatads.com",
        "openx.net",
        "pubmatic.com",
        "rubiconproject.com",
        "taboola.com"
    };

    private sealed class TabStripMouseWheelFilter(MainForm owner) : IMessageFilter
    {
        public bool PreFilterMessage(ref Message message)
        {
            if (message.Msg != WmMouseWheel || owner.IsDisposed ||
                !owner.IsTabStripUnderCursor() || owner._tabContentWidth <= owner._tabViewportWidth)
                return false;

            var delta = unchecked((short)((message.WParam.ToInt64() >> 16) & 0xFFFF));
            if (delta == 0) return false;
            owner.ScrollTabs(delta);
            return true;
        }
    }

    private sealed class BrowserTab
    {
        public BrowserTab(WebView2 view, TabButton button)
        {
            View = view;
            Button = button;
        }

        public WebView2 View { get; private set; }
        public TabButton Button { get; }
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid WorkspaceId { get; set; }
        public string Title { get; set; } = "Tugle";
        public bool IsHome { get; set; }
        public string? FaviconUrl { get; set; }
        public Image? FaviconImage { get; set; }
        public string? FaviconImageUri { get; set; }
        public int FaviconRequestVersion { get; set; }
        public CancellationTokenSource? SuggestionCancellation { get; set; }
        public DateTime? InactiveSinceUtc { get; set; }
        public bool IsSuspended { get; set; }
        public bool IsSuspending { get; set; }
        public bool IsMuted { get; set; }
        public bool IsPlayingAudio { get; set; }
        public Task? InitializationTask { get; set; }
        public bool DeferredNavigation { get; set; }
        public bool IsPinned { get; set; }
        public Guid? GroupId { get; set; }
        public bool FallbackHostFilterRegistered { get; set; }
        public bool IsClosing { get; set; }
        public bool InitialNavigationStarted { get; set; }
        public string? InitialNavigationTarget { get; set; }
        public bool InitialNavigationReady { get; set; }
        public bool IsDiscarded { get; set; }
        public ulong NavigationId { get; set; }

        public void ReplaceView(WebView2 view) => View = view;
    }

    private sealed class DownloadItem(string path)
    {
        public string Path { get; } = path;
        public CoreWebView2DownloadState State { get; set; } = CoreWebView2DownloadState.InProgress;
        public CoreWebView2DownloadOperation? Operation { get; set; }
        public long BytesReceived { get; set; }
        public long TotalBytes { get; set; }
        public DateTime LastProgressRefreshUtc { get; set; }
        public DownloadFlyoutItem? Row { get; set; }
    }

    private enum ActionFlyoutMode
    {
        None,
        History,
        Library,
        Privacy,
        ClearData,
        Search,
        GuiScale,
        Theme,
        Downloads,
        Accounts,
        BrowserSettings,
        Performance,
        GoogleAccount
    }

    public MainForm(bool isPrivate = false)
    {
        _isPrivate = isPrivate;
        AutoScaleMode = AutoScaleMode.None; // Chrome sizes already include GUI scale and monitor DPI.
        _settings = TugleSettings.Load();
        _history = new HistoryStore(TugleSettings.ProfileDirectory, persistent: !_isPrivate);
        _library = new LibraryStore(TugleSettings.ProfileDirectory, persistent: !_isPrivate);
        LoadSavedSettings();
        Text = _isPrivate ? "Tugle Private" : "Tugle";
        FormBorderStyle = FormBorderStyle.None;
        Padding = UiPadding(7);
        DoubleBuffered = true;

        var iconPath = Path.Combine(AppContext.BaseDirectory, "assets", "tugle-icon.ico");
        if (File.Exists(iconPath))
        {
            Icon = new Icon(iconPath);
            _homeTabIcon = Icon.ToBitmap();
        }

        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = GetScaledMinimumWindowSize();
        Size = new Size(1280, 800);
        WindowState = FormWindowState.Maximized;
        BackColor = _chrome;
        var fontPath = Path.Combine(AppContext.BaseDirectory, "assets", "Slabo13px-Regular.ttf");
        if (File.Exists(fontPath))
        {
            try
            {
                _privateFonts.AddFontFile(fontPath);
                if (_privateFonts.Families.Length > 0)
                    _uiFontFamily = _privateFonts.Families[0];
            }
            catch
            {
                // Keep the browser usable with a system fallback if the bundled font cannot load.
            }
        }

        Font = CreateUiFont(13F);
        KeyPreview = true;

        BuildChrome();
        _tabWheelFilter = new TabStripMouseWheelFilter(this);
        Application.AddMessageFilter(_tabWheelFilter);
        Resize += (_, _) =>
        {
            FitRestoredWindowToWorkingArea();
        };
        DpiChanged += (_, _) => BeginInvoke((Action)ApplyGuiScale);
        _suspendTimer.Tick += async (_, _) => await SuspendInactiveTabsAsync();
        _suspendTimer.Start();
        HandleCreated += (_, _) => RegisterF11HotKey();
        Shown += (_, _) =>
        {
            if (_titleArea is not null)
                LayoutTabStrip(_titleArea);
        };
        Activated += (_, _) => OnWindowActivated();
        Deactivate += (_, _) => OnWindowDeactivated();
        Load += async (_, _) =>
        {
            
            if (!_isPrivate && (!_settings.SetupCompleted || _settings.SetupVersion < FirstRunSetupForm.CurrentVersion))
            {
                using var setup = new FirstRunSetupForm(GetEnvironmentAsync, _selectedTheme, PrepareProtectionAsync);
                if (setup.ShowDialog(this) == DialogResult.OK)
                {
                    _settings.LowMemoryMode = true;
                    _settings.ReduceMotion = true;
                    _selectedTheme = setup.SelectedTheme;
                    SetTheme(_selectedTheme, saveSettings: false);
                    _homeBackgroundMode = setup.SelectedBackgroundMode;
                    _homeBackground = setup.SelectedBackground;
                    _homeBackgroundSecondary = setup.SelectedBackgroundSecondary;
                    _homeBackgroundMediaUrl = setup.SelectedBackgroundMediaUrl;
                    _googleSignedIn = setup.ConnectGoogle;
                    _settings.SetupCompleted = true;
                    _settings.SetupVersion = FirstRunSetupForm.CurrentVersion;
                    SaveSettings();
                }
                else { Close(); return; }
            }

            if (_isPrivate)
                await OpenNewTabAsync();
            else
                await RestorePreviousTabsAsync();
            if (!_isPrivate) _ = CheckForUpdatesAsync(showNoUpdateMessage: false);
        };
    }

    private void LoadSavedSettings()
    {
        _guiScale = Math.Clamp(_settings.GuiScale, 0.7f, 1.4f);
        var savedTheme = ThemePalettes.ColorOptions.FirstOrDefault(theme =>
            string.Equals(theme.Name, _settings.ThemeName, StringComparison.OrdinalIgnoreCase));
        _selectedTheme = _settings.ThemeName == "Custom" && _settings.CustomThemeAccent is not null
            ? ThemePalette.FromAccent("Custom", "Custom accent color", TugleSettings.FromHex(_settings.CustomThemeAccent, ThemePalettes.Ocean.Accent))
            : savedTheme ?? ThemePalettes.Ocean;
        _theme = _selectedTheme;
        if (_settings.CustomThemeAccent is not null &&
            !_settings.SavedCustomThemeAccents.Contains(_settings.CustomThemeAccent, StringComparer.OrdinalIgnoreCase))
            _settings.SavedCustomThemeAccents.Insert(0, _settings.CustomThemeAccent);
        TugleTheme.Current = _theme;
        _homeBackgroundMode = string.IsNullOrWhiteSpace(_settings.HomeBackgroundMode) ? "gradient" : _settings.HomeBackgroundMode;
        _homeBackground = TugleSettings.FromHex(_settings.HomeBackground, ColorTranslator.FromHtml("#071526"));
        _homeBackgroundSecondary = TugleSettings.FromHex(_settings.HomeBackgroundSecondary, ColorTranslator.FromHtml("#203D5B"));
        _homeBackgroundMediaUrl = _settings.HomeBackgroundMediaUrl;
        _chrome = _theme.Chrome;
        _chromeLighter = _theme.ChromeLighter;
        _text = _theme.Text;
        _muted = _theme.Muted;
        _iconVisible = _theme.Icon;
        _accent = _theme.Accent;
    }

    private void SaveSettings()
    {
        if (_isPrivate) return;
        _settings.GuiScale = _guiScale;
        _settings.ThemeName = _selectedTheme.Name;
        if (_selectedTheme.Name == "Custom")
        {
            _settings.CustomThemeAccent = TugleSettings.ToHex(_selectedTheme.Accent);
            RememberCustomThemeAccent(_selectedTheme.Accent);
        }
        _settings.HomeBackgroundMode = _homeBackgroundMode;
        _settings.HomeBackground = TugleSettings.ToHex(_homeBackground);
        _settings.HomeBackgroundSecondary = TugleSettings.ToHex(_homeBackgroundSecondary);
        _settings.HomeBackgroundMediaUrl = _homeBackgroundMediaUrl;
        _settings.Save();
    }

    private WebView2? ActiveWebView => _activeTab?.View;

    private Guid ActiveWorkspaceId => _isPrivate ? Guid.Empty : _settings.ActiveWorkspace.Id;

    private IEnumerable<BrowserTab> ActiveWorkspaceTabs => _isPrivate
        ? _tabs
        : _tabs.Where(tab => tab.WorkspaceId == ActiveWorkspaceId && !tab.IsClosing);

    private TugleWorkspace? GetWorkspace(Guid workspaceId) => _isPrivate
        ? null
        : _settings.Workspaces.FirstOrDefault(workspace => workspace.Id == workspaceId);

    private Guid GetValidWorkspaceId(Guid? workspaceId) =>
        !_isPrivate && workspaceId is { } id && GetWorkspace(id) is not null
            ? id
            : ActiveWorkspaceId;

    private void UpdateWorkspaceSwitcher()
    {
        if (_isPrivate)
        {
            _workspaceButton.Visible = false;
            return;
        }

        var workspace = _settings.ActiveWorkspace;
        _workspaceButton.Visible = true;
        _workspaceButton.WorkspaceName = workspace.Name;
        _workspaceButton.WorkspaceIcon = workspace.Icon;
        _workspaceButton.WorkspaceColor = TugleSettings.FromHex(workspace.Color, _accent);
        _workspaceButton.ForeColor = _text;
        _workspaceButton.BackColor = _chrome;
        _workspaceButton.AccessibleDescription = $"Current workspace: {workspace.Name}. Switch workspaces.";
        var workspaceTabCount = GetWorkspaceTabCount(workspace.Id);
        _tooltips.SetToolTip(_workspaceButton,
            $"{workspace.Name} · {workspaceTabCount} tab{(workspaceTabCount == 1 ? string.Empty : "s")} (Ctrl+Shift+W)");
        _workspaceButton.Invalidate();
        if (_titleArea is not null) LayoutTabStrip(_titleArea);
    }

    private void BuildChrome()
    {
        var titleBar = _titleBar = new Panel { Dock = DockStyle.Top, Height = Ui(54), BackColor = _chrome };

        var titleArea = _titleArea = new Panel { Dock = DockStyle.Fill, BackColor = _chrome };
        titleArea.MouseDown += DragTitle;
        titleBar.MouseDown += DragTitle;
        titleBar.DoubleClick += (_, _) => ToggleMaximize();

        _tabsFlow.Dock = DockStyle.None;
        _tabsFlow.Padding = Padding.Empty;
        _tabsFlow.BackColor = _chrome;
        _tabsFlow.Resize += (_, _) => LayoutTabs();
        _tabsFlow.MouseDown += DragTitle;
        _tabsFlow.MouseWheel += (_, e) => ScrollTabs(e.Delta);
        _tabsFlow.DoubleClick += (_, _) => ToggleMaximize();
        _tooltips.SetToolTip(_tabsFlow, "Scroll with the mouse wheel to view more tabs");
        titleArea.Resize += (_, _) => LayoutTabStrip(titleArea);
        titleBar.Resize += (_, _) => LayoutTabStrip(titleArea);

        _workspaceButton.UiScale = GuiScale;
        _workspaceButton.Font = CreateUiFont(12F);
        _workspaceButton.WorkspaceColor = TugleSettings.FromHex(_settings.ActiveWorkspace.Color, _accent);
        _workspaceButton.WorkspaceIcon = _settings.ActiveWorkspace.Icon;
        _workspaceButton.WorkspaceName = _settings.ActiveWorkspace.Name;
        _workspaceButton.BackColor = _chrome;
        _workspaceButton.ForeColor = _text;
        _workspaceButton.Cursor = Cursors.Hand;
        _workspaceButton.AccessibleName = "Workspaces";
        _workspaceButton.AccessibleDescription = "Switch tab workspaces";
        _workspaceButton.Click += (_, _) => ShowWorkspaceMenu();
        _tooltips.SetToolTip(_workspaceButton, "Workspaces (Ctrl+Shift+W)");
        ConfigureToolbarMenu(_workspaceMenu);
        _workspaceMenu.Opening += (_, _) => PopulateWorkspaceMenu();

        _newTabButton.Text = "+";
        _newTabButton.Size = new Size(Ui(36), Ui(32));
        _newTabButton.Font = CreateUiFont(13F);
        _newTabButton.UiScale = GuiScale;
        _newTabButton.ForeColor = _iconVisible;
        _newTabButton.BackColor = _theme.Surface;
        _newTabButton.FlatStyle = FlatStyle.Flat;
        _newTabButton.FlatAppearance.BorderSize = 1;
        _newTabButton.FlatAppearance.BorderColor = _theme.Border;
        _newTabButton.FlatAppearance.MouseOverBackColor = _theme.SurfaceHover;
        _newTabButton.FlatAppearance.MouseDownBackColor = _theme.SurfacePressed;
        _newTabButton.Margin = Padding.Empty;
        _newTabButton.AccessibleName = "New tab";
        _newTabButton.AccessibleRole = AccessibleRole.PushButton;
        _newTabButton.TabStop = true;
        _newTabButton.Cursor = Cursors.Hand;
        _tooltips.SetToolTip(_newTabButton, "New tab (Ctrl+T)");
        _newTabButton.Click += async (_, _) => await RequestNewTabAsync();
        titleArea.Controls.Add(_workspaceButton);
        titleArea.Controls.Add(_tabsFlow);
        titleArea.Controls.Add(_newTabButton);
        _workspaceButton.BringToFront();
        _newTabButton.BringToFront();
        LayoutTabStrip(titleArea);
        titleBar.Controls.Add(titleArea);

        var windowButtons = _windowButtons = new FlowLayoutPanel
        {
            Dock = DockStyle.Right,
            Width = Ui(138),
            WrapContents = false,
            Margin = Padding.Empty,
            Padding = UiPadding(0, 2, 0, 0),
            BackColor = _chrome
        };
        AddWindowButton(windowButtons, "", "Minimize", () => WindowState = FormWindowState.Minimized);
        AddWindowButton(windowButtons, "", "Maximize or restore", ToggleMaximize);
        AddWindowButton(windowButtons, "", "Close", Close);
        titleBar.Controls.Add(windowButtons);
        titleArea.BringToFront(); // Dock the right-side buttons before the fill panel.
        RegisterSnapButton(_maximizeButton!);
        Resize += (_, _) => _maximizeButton!.Text = WindowState == FormWindowState.Maximized ? "" : "";

        var toolbar = _toolbar = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = Ui(56),
            ColumnCount = 3,
            RowCount = 1,
            Padding = UiPadding(5, 4, 8, 7),
            BackColor = _chrome
        };
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Ui(168)));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Ui(258)));
        toolbar.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        _navigation.Dock = DockStyle.Fill;
        _navigation.WrapContents = false;
        _navigation.Margin = Padding.Empty;
        _navigation.Padding = UiPadding(0, 2, 0, 0);
        _navigation.BackColor = _chrome;
        ConfigureToolbarButton(_backButton, string.Empty, "Back");
        ConfigureToolbarButton(_forwardButton, string.Empty, "Forward");
        ConfigureToolbarButton(_reloadButton, string.Empty, "Reload");
        ConfigureToolbarButton(_homeButton, string.Empty, "Home");
        _backButton.Click += (_, _) => { if (ActiveWebView?.CanGoBack == true) ActiveWebView.GoBack(); };
        _forwardButton.Click += (_, _) => { if (ActiveWebView?.CanGoForward == true) ActiveWebView.GoForward(); };
        _reloadButton.Click += (_, _) => ActiveWebView?.CoreWebView2?.Reload();
        _homeButton.Click += (_, _) => ShowHome();
        _navigation.Controls.AddRange([_backButton, _forwardButton, _reloadButton, _homeButton]);
        toolbar.Controls.Add(_navigation, 0, 0);

        _addressSurface.Dock = DockStyle.Fill;
        _addressSurface.Margin = Padding.Empty;
        _addressSurface.UiScale = GuiScale;
        _addressBar.BorderStyle = BorderStyle.None;
        _addressBar.BackColor = _theme.Surface;
        _addressBar.ForeColor = _text;
        _addressBar.Font = CreateUrlFont(13F);
        _addressBar.PlaceholderText = _isPrivate ? "Private window — search or enter address" : "Search or enter address";
        _addressBar.AccessibleName = "Address";
        _addressSurface.Controls.Add(_addressBar);
        _addressSurface.Resize += (_, _) =>
        {
            var inset = Ui(12);
            _addressBar.SetBounds(
                inset,
                Math.Max(0, (_addressSurface.Height - _addressBar.PreferredHeight) / 2),
                Math.Max(10, _addressSurface.Width - inset * 2),
                _addressBar.PreferredHeight);
        };
        _addressSurface.MouseDown += (_, _) => _addressBar.Focus();
        _addressBar.Enter += (_, _) => { _addressSurface.FocusedField = true; _addressSurface.Invalidate(); };
        _addressBar.Leave += (_, _) => { _addressSurface.FocusedField = false; _addressSurface.Invalidate(); };
        _addressBar.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                NavigateFromAddressBar();
                e.SuppressKeyPress = true;
            }
        };
        toolbar.Controls.Add(_addressSurface, 1, 0);

        _utilityActions.Dock = DockStyle.Fill;
        _utilityActions.WrapContents = false;
        _utilityActions.Margin = Padding.Empty;
        _utilityActions.Padding = UiPadding(2, 2, 0, 0);
        _utilityActions.BackColor = _chrome;
        ConfigureToolbarButton(_historyButton, string.Empty, "History");
        ConfigureToolbarButton(_guiScaleButton, string.Empty, "GUI scale");
        ConfigureToolbarButton(_themeButton, string.Empty, "Theme");
        ConfigureToolbarButton(_bookmarksButton, string.Empty, "Bookmarks");
        ConfigureToolbarButton(_downloadsButton, string.Empty, "Downloads");
        ConfigureToolbarButton(_accountButton, string.Empty, "Settings");
        _historyButton.UiScale = GuiScale;
        _guiScaleButton.UiScale = GuiScale;
        _themeButton.UiScale = GuiScale;
        _bookmarksButton.UiScale = GuiScale;
        _downloadsButton.UiScale = GuiScale;
        _accountButton.UiScale = GuiScale;
        _tooltips.SetToolTip(_themeButton, $"Theme ({_theme.Name})");
        _historyButton.Margin = UiPadding(0, 0, 4, 0);
        _guiScaleButton.Margin = UiPadding(0, 0, 4, 0);
        _bookmarksButton.Margin = Padding.Empty;
        _downloadsButton.Margin = Padding.Empty;
        _accountButton.Margin = Padding.Empty;
        ConfigureToolbarMenu(_downloadsMenu);
        ConfigureToolbarMenu(_historyMenu);
        ConfigureToolbarMenu(_guiScaleMenu);
        _downloadsMenu.Opening += (_, _) => RebuildDownloadsMenu();
        _historyMenu.Opening += (_, _) => RebuildHistoryMenu();
        _historyButton.Click += (_, _) => ShowHistoryMenu();
        _guiScaleButton.Click += (_, _) => ShowGuiScaleMenu();
        _themeButton.Click += (_, _) => ShowThemeMenu();
        _bookmarksButton.Click += (_, _) => ShowLibraryMenu();
        _downloadsButton.Click += (_, _) => ShowDownloadsMenu();
        _accountButton.Click += (_, _) => ShowAccountsMenu();
        _utilityActions.Controls.AddRange([_historyButton, _guiScaleButton, _themeButton, _bookmarksButton, _downloadsButton, _accountButton]);
        toolbar.Controls.Add(_utilityActions, 2, 0);
        LayoutToolbarActions();

        _contentHost.Dock = DockStyle.Fill;
        _contentHost.BackColor = _theme.ContentBackground;
        _contentHost.Resize += (_, _) => LayoutContentViews();
        _splitDivider.BackColor = _theme.Chrome;
        _splitDivider.Cursor = Cursors.SizeWE;
        _splitDivider.Visible = false;
        _splitDivider.AccessibleName = "Resize split view";
        _splitDivider.AccessibleDescription = "Drag to resize. Double-click for equal widths.";
        _tooltips.SetToolTip(_splitDivider, "Drag to resize · Double-click for equal widths");
        _splitDivider.Paint += (_, e) =>
        {
            using var pen = new Pen(_theme.BorderStrong, Ui(1));
            var center = _splitDivider.Width / 2;
            e.Graphics.DrawLine(pen, center, 0, center, _splitDivider.Height);
        };
        _splitDivider.MouseEnter += (_, _) => _splitDivider.BackColor = _theme.SurfaceHover;
        _splitDivider.MouseLeave += (_, _) => _splitDivider.BackColor = _theme.Chrome;
        _splitDivider.MouseDoubleClick += (_, e) =>
        {
            if (e.Button != MouseButtons.Left || !HasSplitView) return;
            _draggingSplitDivider = false;
            _splitDivider.Capture = false;
            _splitRatio = 0.5f;
            LayoutContentViews();
        };
        _splitDivider.MouseDown += BeginSplitDividerDrag;
        _splitDivider.MouseMove += MoveSplitDivider;
        _splitDivider.MouseUp += EndSplitDividerDrag;
        _splitDivider.MouseCaptureChanged += (_, _) => _draggingSplitDivider = false;
        _contentHost.Controls.Add(_splitDivider);
        Controls.Add(_contentHost);
        Controls.Add(toolbar);
        Controls.Add(titleBar);
        BuildActionFlyout();
    }

    private void BuildActionFlyout()
    {
        _actionFlyout.BackColor = _chrome;
        _actionFlyout.BorderStyle = BorderStyle.None;
        _actionFlyout.Padding = Padding.Empty;
        _actionFlyout.UiScale = GuiScale;
        _actionFlyout.Visible = false;

        var header = new Panel
        {
            Dock = DockStyle.Top,
            Height = Ui(48),
            BackColor = _chrome,
            Padding = UiPadding(14, 0, 4, 0)
        };
        _actionFlyoutTitle.Dock = DockStyle.Fill;
        _actionFlyoutTitle.Font = CreateUiFont(14F);
        _actionFlyoutTitle.ForeColor = _text;
        _actionFlyoutTitle.TextAlign = ContentAlignment.MiddleLeft;
        _actionFlyoutClose.Dock = DockStyle.Right;
        _actionFlyoutClose.Width = Ui(38);
        _actionFlyoutClose.Font = CreateUiFont(19F);
        _actionFlyoutClose.ForeColor = _iconVisible;
        _actionFlyoutClose.BackColor = _chrome;
        _actionFlyoutClose.FlatStyle = FlatStyle.Flat;
        _actionFlyoutClose.FlatAppearance.BorderSize = 0;
        _actionFlyoutClose.UiScale = GuiScale;
        _actionFlyoutClose.Cursor = Cursors.Hand;
        _actionFlyoutClose.AccessibleName = "Close side panel";
        _actionFlyoutClose.Click += (_, _) => HideActionFlyout();
        header.Controls.Add(_actionFlyoutTitle);
        header.Controls.Add(_actionFlyoutClose);

        _actionFlyoutItems.Dock = DockStyle.Fill;
        _actionFlyoutItems.AutoScroll = true;
        _actionFlyoutItems.WrapContents = false;
        _actionFlyoutItems.FlowDirection = FlowDirection.TopDown;
        _actionFlyoutItems.Padding = UiPadding(14, 6, 14, 14);
        _actionFlyoutItems.BackColor = _chrome;
        _actionFlyout.Controls.Add(_actionFlyoutItems);
        _actionFlyout.Controls.Add(header);
        Controls.Add(_actionFlyout);

        Resize += (_, _) => LayoutActionFlyout();
        _contentHost.Resize += (_, _) => LayoutActionFlyout();
        LayoutActionFlyout();
    }

    private void LayoutActionFlyout()
    {
        var contentBounds = _contentHost.Bounds;
        var inset = Ui(8);
        var width = Math.Min(Ui(320), Math.Max(0, contentBounds.Width - inset));
        var contentHeight = Ui(48) + _actionFlyoutItems.Padding.Vertical +
            _actionFlyoutItems.Controls.Cast<Control>().Sum(control => control.Height + control.Margin.Vertical);
        _actionFlyout.SetBounds(
            Math.Max(0, contentBounds.Right - width - inset),
            contentBounds.Top + inset,
            width,
            Math.Max(1, Math.Min(Math.Max(Ui(110), contentHeight), contentBounds.Height - inset * 2)));
        _actionFlyout.BringToFront();
    }

    private void LayoutToolbarActions()
    {
        if (_toolbar is null) return;
        _utilityActions.Padding = UiPadding(6, 2, 0, 0);
        foreach (Control button in _utilityActions.Controls)
            button.Margin = UiPadding(0, 0, ReferenceEquals(button, _accountButton) ? 0 : 2, 0);
        _toolbar.ColumnStyles[0].Width = _navigation.Padding.Horizontal +
            _navigation.Controls.Cast<Control>().Sum(button => button.Width + button.Margin.Horizontal);
        _toolbar.ColumnStyles[2].Width = _utilityActions.Padding.Horizontal +
            _utilityActions.Controls.Cast<Control>().Sum(button => button.Width + button.Margin.Horizontal);
    }

    private void ConfigureToolbarButton(Button button, string glyph, string label)
    {
        button.Text = glyph;
        button.Size = new Size(Ui(36), Ui(36));
        if (button is ToolbarActionButton actionButton)
            actionButton.UiScale = GuiScale;
        if (button is not NavigationButton)
            button.Font = new Font("Segoe MDL2 Assets", 13.5F * GuiScale);
        button.ForeColor = _iconVisible;
        button.BackColor = _chrome;
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 0;
        button.FlatAppearance.MouseOverBackColor = _chromeLighter;
        button.FlatAppearance.MouseDownBackColor = _theme.SurfacePressed;
        button.Margin = UiPadding(0, 0, 4, 0);
        button.AccessibleName = label;
        _tooltips.SetToolTip(button, label);
    }

    private void ConfigureToolbarMenu(ContextMenuStrip menu)
    {
        menu.ShowImageMargin = false;
        menu.BackColor = _chromeLighter;
        menu.ForeColor = _text;
        menu.Font = CreateUiFont(13F);
        menu.Renderer = new ToolStripProfessionalRenderer(new TugleColorTable());
        menu.Padding = UiPadding(6);
    }

    private void AddWindowButton(Control parent, string glyph, string label, Action action)
    {
        Button button = label == "Close"
            ? new CloseGlyphButton { UiScale = GuiScale }
            : new Button();
        ConfigureToolbarButton(button, glyph, label);
        button.Size = new Size(Ui(46), Ui(36));
        button.Margin = Padding.Empty;
        button.Font = new Font("Segoe MDL2 Assets", 10F * GuiScale);
        button.Click += (_, _) => action();
        parent.Controls.Add(button);
        if (label == "Maximize or restore") _maximizeButton = button;
    }

    private async Task<CoreWebView2Environment> GetEnvironmentAsync()
    {
        if (_environment is null)
        {
            var environmentOptions = new CoreWebView2EnvironmentOptions(
                "--no-default-browser-check --disable-features=msEdgeSidebarV2",
                null,
                null,
                false,
                new List<CoreWebView2CustomSchemeRegistration>())
            {
                AreBrowserExtensionsEnabled = true,
                EnableTrackingPrevention = true
            };
            var profilePath = Path.Combine(TugleSettings.ProfileDirectory, "WebView2Profile");
            Directory.CreateDirectory(profilePath);
            try
            {
                _environment = await (_environmentTask ??= CoreWebView2Environment.CreateAsync(null, profilePath, environmentOptions));
                _environment.BrowserProcessExited += OnBrowserProcessExited;
            }
            catch
            {
                _environmentTask = null;
                throw;
            }
        }
        return _environment!;
    }

    private WebView2 CreateBrowserView() => new()
    {
        Dock = DockStyle.Fill,
        DefaultBackgroundColor = _theme.ContentBackground,
        Visible = false
    };

    private async Task<BrowserTab?> OpenNewTabAsync(
        bool showHome = true,
        bool activate = true,
        bool deferInitialization = false,
        Guid? workspaceId = null,
        Guid? tabWorkspaceId = null)
    {
        if (_creatingTab) return null;
        _creatingTab = true;
        BrowserTab? createdTab = null;

        try
        {
            var view = CreateBrowserView();
            var tabButton = new TabButton
            {
                Text = "Tugle",
                Size = new Size(Ui(150), Ui(32)),
                Font = CreateUiFont(13F),
                UiScale = GuiScale,
                ForeColor = _muted,
                BackColor = _theme.Surface,
                FlatStyle = FlatStyle.Flat,
                Padding = UiPadding(12, 0, 34, 0),
                TextAlign = ContentAlignment.MiddleLeft
            };
            tabButton.AutoEllipsis = true;
            tabButton.UseCompatibleTextRendering = false;
            tabButton.FlatAppearance.BorderSize = 0;
            var effectiveWorkspaceId = _isPrivate ? Guid.Empty : GetValidWorkspaceId(tabWorkspaceId);
            var tab = createdTab = new BrowserTab(view, tabButton)
            {
                WorkspaceId = effectiveWorkspaceId,
                GroupId = GetValidTabGroupId(workspaceId, effectiveWorkspaceId)
            };
            view.GotFocus += (_, _) => FocusSplitTab(tab);
            tabButton.AccessibleName = "Tugle tab";
            tabButton.AccessibleDescription = "Use the speaker button to mute this tab, or the X to close it.";
            tabButton.MouseDown += (_, e) =>
            {
                if (e.Button == MouseButtons.Left)
                    SelectTabFromPointer(tab, ModifierKeys);
            };
            tabButton.Click += (_, _) => ActivateTab(tab);
            tabButton.CloseRequested += (_, _) => CloseTab(tab);
            tabButton.MuteRequested += (_, _) => ToggleTabMute(tab);
            tabButton.DragStarted += (_, _) => BeginTabDrag(tab);
            tabButton.DragMoved += (_, e) => MoveDraggedTab(tab, e.ScreenLocation);
            tabButton.DragEnded += (_, _) => EndTabDrag(tab);
            tabButton.MouseWheel += (_, e) => ScrollTabs(e.Delta);
            tabButton.ContextMenuStrip = CreateTabContextMenu(tab);

            // Parent and size the native control before creating its controller.
            BatchChromeUpdate(() =>
            {
                _tabs.Add(tab);
                _contentHost.Controls.Add(view);
                _tabsFlow.Controls.Add(tabButton);
                LayoutTabs();
            });

            view.Bounds = _contentHost.ClientRectangle;
            if (deferInitialization) return tab;
            await EnsureTabInitializedAsync(tab);
            if (tab.IsClosing || view.IsDisposed) return null;
            if (showHome) ShowHome(tab);
            if (activate) await ActivateTabAsync(tab);
            return tab;
        }
        catch (Exception ex)
        {
            if (createdTab is not null)
            {
                createdTab.IsClosing = true;
                _tabs.Remove(createdTab);
                createdTab.Button.ContextMenuStrip?.Dispose();
                createdTab.Button.Dispose();
                createdTab.View.Dispose();
                if (ReferenceEquals(createdTab, _activeTab)) _activeTab = null;
                if (!IsDisposed) LayoutTabs();
            }
            if (IsDisposed || Disposing) return null;
            MessageBox.Show(
                "The browser engine could not start. Make sure Microsoft Edge WebView2 Runtime is installed.\n\n" + ex.Message,
                "Tugle",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return null;
        }
        finally
        {
            _creatingTab = false;
        }
    }

    private async Task RequestNewTabAsync()
    {
        if (_creatingTab)
        {
            _queuedTabRequests = Math.Min(_queuedTabRequests + 1, 8);
            return;
        }

        var groupId = _isPrivate ? null : _activeTab?.GroupId;
        Guid? tabWorkspaceId = _isPrivate ? null : _activeTab?.WorkspaceId ?? ActiveWorkspaceId;
        await OpenNewTabAsync(workspaceId: groupId, tabWorkspaceId: tabWorkspaceId);
        while (_queuedTabRequests > 0)
        {
            _queuedTabRequests--;
            await OpenNewTabAsync(workspaceId: groupId, tabWorkspaceId: tabWorkspaceId);
        }
    }

    private async Task RestorePreviousTabsAsync()
    {
        var savedTabs = _settings.PreviousSessionTabs.ToArray();
        if (savedTabs.Length == 0)
        {
            await OpenWorkspaceStartupTabAsync();
            return;
        }

        BrowserTab? activeTab = null;
        BrowserTab? firstTab = null;
        foreach (var savedTab in savedTabs)
        {
            var tab = await OpenNewTabAsync(
                showHome: false,
                activate: false,
                deferInitialization: true,
                workspaceId: savedTab.GroupId,
                tabWorkspaceId: savedTab.WorkspaceId);
            if (tab is null) continue;

            tab.Id = savedTab.Id == Guid.Empty ? tab.Id : savedTab.Id;
            firstTab ??= tab.WorkspaceId == ActiveWorkspaceId ? tab : firstTab;
            tab.Title = string.IsNullOrWhiteSpace(savedTab.Title) ? tab.Title : savedTab.Title;
            tab.IsPinned = savedTab.IsPinned;
            tab.GroupId = GetValidTabGroupId(savedTab.GroupId, tab.WorkspaceId);
            tab.IsHome = savedTab.IsHome;
            tab.DeferredNavigation = true;
            tab.InitialNavigationTarget = savedTab.Url;
            UpdateTabButton(tab);

            if (savedTab.IsActive && tab.WorkspaceId == ActiveWorkspaceId) activeTab = tab;
        }

        OrderPinnedTabs();

        var rememberedId = _settings.ActiveWorkspace.LastActiveTabId;
        if (rememberedId is { } lastActiveId)
            activeTab = _tabs.FirstOrDefault(tab => tab.WorkspaceId == ActiveWorkspaceId && tab.Id == lastActiveId) ?? activeTab;

        var tabToActivate = activeTab ?? firstTab;
        if (tabToActivate is not null)
        {
            await ActivateTabAsync(tabToActivate);
            return;
        }

        // Keep the browser usable if WebView2 was unable to recreate all of a
        // damaged or stale saved session.
        await OpenWorkspaceStartupTabAsync();
    }

    private void CapturePreviousSession()
    {
        // Setup can be dismissed before WebView2 creates any tabs. Preserve a
        // previously saved session in that case instead of replacing it with an
        // empty one.
        if (_tabs.Count == 0) return;

        if (!_isPrivate && _activeTab is { IsClosing: false } active && GetWorkspace(active.WorkspaceId) is { } workspace)
            workspace.LastActiveTabId = active.Id;

        var savedTabs = new List<TugleSessionTab>();
        foreach (var tab in _tabs.Where(tab => !tab.IsClosing))
        {
            var savedTab = CreateSessionTab(tab);
            if (savedTab is not null) savedTabs.Add(savedTab);
        }

        _settings.PreviousSessionTabs = savedTabs;
    }

    private TugleSessionTab? CreateSessionTab(BrowserTab tab)
    {
        if (IsHomeTab(tab))
        {
            return new TugleSessionTab
            {
                Id = tab.Id,
                WorkspaceId = tab.WorkspaceId,
                IsHome = true,
                IsActive = ReferenceEquals(tab, _activeTab),
                IsPinned = tab.IsPinned,
                GroupId = tab.GroupId,
                Title = tab.Title
            };
        }

        return TryGetRestorableTabUrl(tab, out var url)
            ? new TugleSessionTab
            {
                Id = tab.Id,
                WorkspaceId = tab.WorkspaceId,
                Url = url,
                IsActive = ReferenceEquals(tab, _activeTab),
                IsPinned = tab.IsPinned,
                GroupId = tab.GroupId,
                Title = tab.Title
            }
            : null;
    }

    private void RememberClosedTab(BrowserTab tab)
    {
        if (_isPrivate) return;
        var savedTab = CreateSessionTab(tab);
        if (savedTab is null) return;
        savedTab.IsActive = false;
        _settings.RecentlyClosedTabs.RemoveAll(item =>
            item.IsHome == savedTab.IsHome && string.Equals(item.Url, savedTab.Url, StringComparison.OrdinalIgnoreCase));
        _settings.RecentlyClosedTabs.Insert(0, savedTab);
        if (_settings.RecentlyClosedTabs.Count > 15)
            _settings.RecentlyClosedTabs.RemoveRange(15, _settings.RecentlyClosedTabs.Count - 15);
        SaveSettings();
    }

    private async Task ReopenClosedTabAsync()
    {
        if (_isPrivate || _settings.RecentlyClosedTabs.Count == 0) return;
        var savedTab = _settings.RecentlyClosedTabs[0];
        _settings.RecentlyClosedTabs.RemoveAt(0);
        SaveSettings();

        if (savedTab.WorkspaceId != Guid.Empty && savedTab.WorkspaceId != ActiveWorkspaceId)
            await SwitchWorkspaceAsync(savedTab.WorkspaceId);

        var tab = await OpenNewTabAsync(
            showHome: false,
            workspaceId: GetValidTabGroupId(savedTab.GroupId, GetValidWorkspaceId(savedTab.WorkspaceId)),
            tabWorkspaceId: savedTab.WorkspaceId);
        if (tab?.View.CoreWebView2 is null)
        {
            _settings.RecentlyClosedTabs.Insert(0, savedTab);
            SaveSettings();
            return;
        }
        tab.Id = savedTab.Id == Guid.Empty ? tab.Id : savedTab.Id;
        tab.Title = string.IsNullOrWhiteSpace(savedTab.Title) ? tab.Title : savedTab.Title;
        tab.IsPinned = savedTab.IsPinned;
        tab.GroupId = GetValidTabGroupId(savedTab.GroupId, tab.WorkspaceId);
        UpdateTabButton(tab);
        if (savedTab.IsHome)
            ShowHome(tab);
        else if (!string.IsNullOrWhiteSpace(savedTab.Url))
        {
            tab.InitialNavigationTarget = savedTab.Url;
            tab.View.CoreWebView2.Navigate(savedTab.Url);
        }
        OrderPinnedTabs();
    }

    private static bool IsHomeTab(BrowserTab tab)
    {
        if (tab.IsHome) return true;
        try
        {
            return IsHomeSource(tab.View.CoreWebView2?.Source);
        }
        catch
        {
            return false;
        }
    }

    private static bool TryGetRestorableTabUrl(BrowserTab tab, out string url)
    {
        url = string.Empty;
        try
        {
            var source = tab.DeferredNavigation ? tab.InitialNavigationTarget : tab.View.CoreWebView2?.Source;
            if (!Uri.TryCreate(source, UriKind.Absolute, out var uri) ||
                uri.Scheme is not ("http" or "https" or "file"))
                return false;

            url = uri.AbsoluteUri;
            return true;
        }
        catch
        {
            // A WebView can already be closing when Windows asks the form to exit.
            return false;
        }
    }

    private Task EnsureTabInitializedAsync(BrowserTab tab) =>
        tab.InitializationTask ??= InitializeTabAsync(tab);

    private async Task InitializeTabAsync(BrowserTab tab)
    {
        await GetEnvironmentAsync();
        if (tab.IsClosing || tab.View.IsDisposed) return;
        if (_isPrivate)
        {
            var controllerOptions = _environment!.CreateCoreWebView2ControllerOptions();
            controllerOptions.IsInPrivateModeEnabled = true;
            await tab.View.EnsureCoreWebView2Async(_environment, controllerOptions);
        }
        else
        {
            await tab.View.EnsureCoreWebView2Async(_environment);
        }
        var core = tab.View.CoreWebView2;
        if (tab.IsClosing || tab.View.IsDisposed || core is null) return;
        SetWebViewPresentation(tab, false);
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.AreDefaultContextMenusEnabled = true;
        core.Settings.IsZoomControlEnabled = true;
        core.Settings.IsBuiltInErrorPageEnabled = true;
        core.Profile.PreferredColorScheme = CoreWebView2PreferredColorScheme.Dark;
        if (!_isPrivate && Enum.TryParse<CoreWebView2TrackingPreventionLevel>(_settings.TrackingPrevention, out var trackingLevel))
            core.Profile.PreferredTrackingPreventionLevel = trackingLevel;
        if (tab.IsClosing || tab.View.IsDisposed) return;
        // Keep a small built-in filter active while the optional extensions are
        // prepared. Extension setup can involve disk I/O and must not delay the
        // first page a person opens.
        if (_settings.UBlockEnabled) EnableFallbackHostFilter(tab, core);
        _ = PrepareProtectionAsync(core);
        AttachWebViewF11Handler(tab);
        await core.AddScriptToExecuteOnDocumentCreatedAsync("""
            (() => {
                if (!/(^|\\.)google\\.[a-z.]{2,}$/i.test(location.hostname)) return;
                const applyTugleTheme = () => {
                    if (document.getElementById("tugle-google-theme")) return;
                    const style = document.createElement("style");
                    style.id = "tugle-google-theme";
                    style.textContent = `
                        :root { color-scheme: dark !important; }
                        html, body, #cnt, #rcnt, #main, #center_col, #rhs, #res,
                        #appbar, #searchform, #top_nav, #slim_appbar, #gb,
                        [role="main"], [role="navigation"] {
                            background: #0b1018 !important;
                            color: #e8eef8 !important;
                        }
                        a, a:visited, h3 { color: #8ab4f8 !important; }
                        cite, .VuuXrf, .qzEoUe { color: #8ed1c5 !important; }
                        input, textarea, select, [role="combobox"] {
                            background: #162033 !important;
                            color: #edf2f8 !important;
                            border-color: #405574 !important;
                        }
                        [role="button"], button { color: #edf2f8 !important; }
                        #hdtb, #hdtb-tls, .fM33ce { background: #111a28 !important; }
                    `;
                    (document.head || document.documentElement).appendChild(style);
                };
                applyTugleTheme();
                new MutationObserver(applyTugleTheme).observe(document.documentElement, { childList: true });
            })();
            """);
        if (tab.IsClosing || tab.View.IsDisposed) return;

        core.WebMessageReceived += async (_, e) => await OnWebMessageReceivedAsync(tab, e);
        core.NewWindowRequested += async (_, e) => await OnNewWindowRequestedAsync(tab, e);
        core.DownloadStarting += (_, e) => OnDownloadStarting(e);
        core.PermissionRequested += (_, e) => OnPermissionRequested(tab, e);
        core.ProcessFailed += (_, e) => OnProcessFailed(tab, e);
        core.NavigationStarting += (_, e) => OnNavigationStarting(tab, e);
        core.NavigationCompleted += async (_, e) => await OnNavigationCompletedAsync(tab, e);
        core.SourceChanged += (_, _) => { if (ReferenceEquals(tab, _activeTab)) UpdateAddressBar(); };
        core.IsMutedChanged += (_, _) =>
        {
            if (tab.IsClosing) return;
            tab.IsMuted = core.IsMuted;
            UpdateTabButton(tab);
        };
        core.IsDocumentPlayingAudioChanged += (_, _) =>
        {
            if (tab.IsClosing) return;
            tab.IsPlayingAudio = core.IsDocumentPlayingAudio;
            UpdateTabButton(tab);
        };
        core.FaviconChanged += async (_, _) =>
        {
            if (tab.IsClosing) return;
            tab.FaviconUrl = core.FaviconUri;
            var source = core.Source;
            await RefreshTabFaviconAsync(tab);
            if (tab.IsClosing || tab.View.IsDisposed || source != core.Source) return;
            if (!tab.IsHome && !string.IsNullOrWhiteSpace(core.Source))
            {
                if (tab.IsClosing || tab.View.IsDisposed) return;
                _history.UpdateMetadata(core.Source, GetVisitTitle(core.Source, core.DocumentTitle), tab.FaviconUrl);
                RefreshHistoryFlyoutIfVisible();
            }
        };
        core.DocumentTitleChanged += (_, _) =>
        {
            if (tab.IsClosing) return;
            tab.Title = string.IsNullOrWhiteSpace(core.DocumentTitle) ? "Tugle" : TrimTitle(core.DocumentTitle);
            if (!tab.IsHome) _history.UpdateMetadata(core.Source, core.DocumentTitle, tab.FaviconUrl);
            UpdateTabButton(tab);
        };
    }

    private void OnProcessFailed(BrowserTab tab, CoreWebView2ProcessFailedEventArgs e)
    {
        if (tab.IsClosing || IsDisposed || Disposing) return;

        if (e.ProcessFailedKind == CoreWebView2ProcessFailedKind.BrowserProcessExited)
        {
            // BrowserProcessExited is raised by the shared environment after its
            // resources are released. Recover there, rather than racing it here.
            return;
        }

        if (e.ProcessFailedKind != CoreWebView2ProcessFailedKind.RenderProcessExited) return;

        // A crashed page renderer can usually be recreated by a normal reload.
        // Do this only for the affected tab; GPU and utility failures recover in
        // WebView2 without throwing away page state.
        void ReloadFailedRenderer()
        {
            if (tab.IsClosing || tab.View.IsDisposed) return;
            try
            {
                tab.IsSuspended = false;
                tab.View.CoreWebView2?.Reload();
            }
            catch
            {
                // The browser-process handler below will restore all tabs if the
                // failure expands beyond this one renderer.
            }
        }

        if (IsHandleCreated)
        {
            try { BeginInvoke((Action)ReloadFailedRenderer); }
            catch { /* The window may be closing at the same time. */ }
        }
    }

    private void OnBrowserProcessExited(object? sender, CoreWebView2BrowserProcessExitedEventArgs e)
    {
        if (e.BrowserProcessExitKind != CoreWebView2BrowserProcessExitKind.Failed ||
            IsDisposed || Disposing || !IsHandleCreated) return;

        try { BeginInvoke((Action)(() => _ = RecoverBrowserProcessAsync())); }
        catch { /* The window may be closing at the same time. */ }
    }

    private async Task RecoverBrowserProcessAsync()
    {
        if (_recoveringBrowserProcess || IsDisposed || Disposing) return;

        _recoveringBrowserProcess = true;
        try
        {
            var activeTab = _activeTab;
            foreach (var tab in _tabs.Where(tab => !tab.IsClosing).ToArray())
            {
                var restoreHome = tab.IsHome;
                var restoreUrl = tab.DeferredNavigation ? tab.InitialNavigationTarget : null;
                if (!restoreHome && string.IsNullOrWhiteSpace(restoreUrl))
                {
                    if (TryGetRestorableTabUrl(tab, out var currentUrl)) restoreUrl = currentUrl;
                }

                // If the crashed controller cannot still report a valid source,
                // keep the tab usable by falling back to Home.
                if (!restoreHome && string.IsNullOrWhiteSpace(restoreUrl)) restoreHome = true;

                var previousView = tab.View;
                tab.SuggestionCancellation?.Cancel();
                tab.SuggestionCancellation?.Dispose();
                tab.SuggestionCancellation = null;
                tab.FaviconRequestVersion++;
                tab.InitializationTask = null;
                tab.InitialNavigationStarted = false;
                tab.InitialNavigationReady = false;
                tab.InitialNavigationTarget = restoreUrl;
                tab.DeferredNavigation = true;
                tab.IsHome = restoreHome;
                tab.IsSuspended = false;
                tab.IsSuspending = false;
                tab.IsDiscarded = false;
                tab.FallbackHostFilterRegistered = false;

                BatchChromeUpdate(() =>
                {
                    _contentHost.Controls.Remove(previousView);
                    tab.ReplaceView(CreateBrowserView());
                    _contentHost.Controls.Add(tab.View);
                    tab.View.Bounds = _contentHost.ClientRectangle;
                });
                previousView.Dispose();
                UpdateTabButton(tab);
            }

            // A failed browser process cannot be reused. The next selected tab
            // creates a fresh shared environment; other tabs stay lazy.
            _environment = null;
            _environmentTask = null;
            _protectionPreparationTask = null;
            _uBlockExtension = null;
            _cookieGuardExtension = null;
            _uBlockEnabled = false;
            _cookieGuardEnabled = false;
            _uBlockLoadAttempted = false;
            _cookieGuardLoadAttempted = false;
            CapturePreviousSession();
            SaveSettings();

            if (activeTab is not null && _tabs.Contains(activeTab) && !activeTab.IsClosing)
                await ActivateTabAsync(activeTab);
            else if (_tabs.Count > 0)
                await ActivateTabAsync(_tabs.First(tab => !tab.IsClosing));
        }
        catch
        {
            if (!IsDisposed && !Disposing)
                MessageBox.Show(this, "Tugle restored your tabs after the browser engine stopped. Select a tab to retry any page that did not reload.",
                    "Tugle recovery", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        finally
        {
            _recoveringBrowserProcess = false;
        }
    }

    private void OnPermissionRequested(BrowserTab tab, CoreWebView2PermissionRequestedEventArgs e)
    {
        if (tab.IsClosing || !IsSensitivePermission(e.PermissionKind)) return;

        if (IsDisposed || Disposing || !IsHandleCreated)
        {
            e.State = CoreWebView2PermissionState.Deny;
            e.SavesInProfile = false;
            return;
        }

        var deferral = e.GetDeferral();
        try
        {
            BeginInvoke((Action)(() =>
            {
                try
                {
                    if (IsDisposed || Disposing || tab.IsClosing)
                    {
                        e.State = CoreWebView2PermissionState.Deny;
                        e.SavesInProfile = false;
                        return;
                    }

                    var origin = GetPermissionOrigin(e.Uri);
                    var permission = GetPermissionLabel(e.PermissionKind);
                    var persistence = _isPrivate ? "This choice applies only to this private window." :
                        "This choice is remembered for this site.";
                    var allow = MessageBox.Show(
                        this,
                        $"{origin} wants to use {permission}.\n\n{persistence}",
                        "Site permission",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Question,
                        MessageBoxDefaultButton.Button2) == DialogResult.Yes;
                    e.State = allow ? CoreWebView2PermissionState.Allow : CoreWebView2PermissionState.Deny;
                    e.SavesInProfile = !_isPrivate;
                }
                catch
                {
                    e.State = CoreWebView2PermissionState.Deny;
                    e.SavesInProfile = false;
                }
                finally
                {
                    deferral.Complete();
                }
            }));
        }
        catch
        {
            e.State = CoreWebView2PermissionState.Deny;
            e.SavesInProfile = false;
            deferral.Complete();
        }
    }

    private static bool IsSensitivePermission(CoreWebView2PermissionKind permission) => permission is
        CoreWebView2PermissionKind.Microphone or
        CoreWebView2PermissionKind.Camera or
        CoreWebView2PermissionKind.Geolocation or
        CoreWebView2PermissionKind.Notifications or
        CoreWebView2PermissionKind.ClipboardRead or
        CoreWebView2PermissionKind.MultipleAutomaticDownloads or
        CoreWebView2PermissionKind.FileReadWrite;

    private static string GetPermissionLabel(CoreWebView2PermissionKind permission) => permission switch
    {
        CoreWebView2PermissionKind.Microphone => "your microphone",
        CoreWebView2PermissionKind.Camera => "your camera",
        CoreWebView2PermissionKind.Geolocation => "your location",
        CoreWebView2PermissionKind.Notifications => "notifications",
        CoreWebView2PermissionKind.ClipboardRead => "your clipboard",
        CoreWebView2PermissionKind.MultipleAutomaticDownloads => "multiple downloads",
        CoreWebView2PermissionKind.FileReadWrite => "files on your device",
        _ => "a protected browser capability"
    };

    private static string GetPermissionOrigin(string? uri)
    {
        if (Uri.TryCreate(uri, UriKind.Absolute, out var parsed)) return parsed.GetLeftPart(UriPartial.Authority);
        return "This site";
    }

    private void AttachWebViewF11Handler(BrowserTab tab)
    {
        // The WinForms WebView2 control keeps its controller internal, but the
        // controller is the reliable place to intercept keys while a webpage
        // owns focus. Keep ProcessCmdKey below as the native-chrome fallback.
        var controllerField = typeof(WebView2).GetField(
            "_coreWebView2Controller",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        if (controllerField?.GetValue(tab.View) is not CoreWebView2Controller controller) return;

        controller.AcceleratorKeyPressed += (_, e) =>
        {
            if (e.KeyEventKind != CoreWebView2KeyEventKind.KeyDown) return;

            var modifiers = Control.ModifierKeys & (Keys.Control | Keys.Shift | Keys.Alt);
            if (!TryHandleBrowserShortcut(modifiers | (Keys)e.VirtualKey)) return;
            e.Handled = true;
        };
    }

    private Task PrepareProtectionAsync(CoreWebView2 core) =>
        _protectionPreparationTask ??= PrepareProtectionCoreAsync(core);

    private async Task PrepareProtectionCoreAsync(CoreWebView2 core)
    {
        await EnsureUBlockAsync(core);
        await EnsureCookieGuardAsync(core);
    }

    private async Task EnsureUBlockAsync(CoreWebView2 core)
    {
        if (!_settings.UBlockEnabled)
        {
            _uBlockEnabled = false;
            return;
        }
        if (_uBlockEnabled) return;
        if (_uBlockLoadAttempted && _uBlockExtension is null) return;
        _uBlockLoadAttempted = true;

        var extensionPath = Path.Combine(AppContext.BaseDirectory, "assets", "ublock");
        if (!File.Exists(Path.Combine(extensionPath, "manifest.json")))
        {
            _uBlockEnabled = false;
            return;
        }

        try
        {
            var installed = await core.Profile.GetBrowserExtensionsAsync();
            _uBlockExtension = installed.FirstOrDefault(extension =>
                extension.Name.Equals("uBlock Origin", StringComparison.OrdinalIgnoreCase));

            if (_uBlockExtension is null)
                _uBlockExtension = await core.Profile.AddBrowserExtensionAsync(extensionPath);

            if (_uBlockExtension is not null && !_uBlockExtension.IsEnabled)
                await _uBlockExtension.EnableAsync(true);

            _uBlockEnabled = _uBlockExtension?.IsEnabled == true;
        }
        catch
        {
            // The built-in host list below remains active if this WebView2 runtime
            // cannot load the unpacked uBlock extension.
            _uBlockEnabled = false;
            _uBlockLoadAttempted = false;
        }
    }

    private async Task EnsureCookieGuardAsync(CoreWebView2 core)
    {
        if (!_settings.CookieGuardEnabled)
        {
            _cookieGuardEnabled = false;
            return;
        }
        if (_cookieGuardEnabled) return;
        if (_cookieGuardLoadAttempted && _cookieGuardExtension is null) return;
        _cookieGuardLoadAttempted = true;

        var extensionPath = Path.Combine(AppContext.BaseDirectory, "assets", "cookie-guard");
        if (!File.Exists(Path.Combine(extensionPath, "manifest.json"))) return;

        try
        {
            var installed = await core.Profile.GetBrowserExtensionsAsync();
            _cookieGuardExtension = installed.FirstOrDefault(extension =>
                extension.Name.Equals("Tugle Cookie Guard", StringComparison.OrdinalIgnoreCase));

            if (_cookieGuardExtension is null)
                _cookieGuardExtension = await core.Profile.AddBrowserExtensionAsync(extensionPath);

            if (_cookieGuardExtension is not null && !_cookieGuardExtension.IsEnabled)
                await _cookieGuardExtension.EnableAsync(true);

            _cookieGuardEnabled = _cookieGuardExtension?.IsEnabled == true;
        }
        catch
        {
            // Cookie banners are optional UI. A failed extension install must not stop browsing.
            _cookieGuardEnabled = false;
            _cookieGuardLoadAttempted = false;
        }
    }

    private void OnNavigationStarting(BrowserTab tab, CoreWebView2NavigationStartingEventArgs e)
    {
        tab.NavigationId = e.NavigationId;
        tab.InitialNavigationStarted = true;
        tab.IsHome = false;
        tab.IsSuspended = false;
        tab.IsDiscarded = false;
        if (!e.IsRedirected) RecordSearchFromNavigation(e.Uri);
        if (ReferenceEquals(tab, _activeTab)) SetLoadingState(true);
    }

    private async Task OnNavigationCompletedAsync(BrowserTab tab, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (tab.IsClosing || !tab.InitialNavigationStarted || e.NavigationId != tab.NavigationId)
        {
            // Ignore WebView2's internal about:blank initialization page.
            return;
        }

        var completedSource = tab.View.CoreWebView2?.Source;
        if (!tab.InitialNavigationReady && IsBlankPage(completedSource) &&
            !string.Equals(completedSource, tab.InitialNavigationTarget, StringComparison.OrdinalIgnoreCase))
        {
            // A blank completion can arrive around the first real Navigate call.
            // Do not reveal that intermediate document.
            return;
        }

        // Keep a newly-created WebView hidden until its first document is ready.
        // Showing the blank controller and then swapping in the home page is the
        // source of the white/dark flash users see when opening a tab.
        tab.InitialNavigationReady = true;

        if (e.IsSuccess)
        {
            var core = tab.View.CoreWebView2;
            if (core is null) return;
            if (IsGoogleHost(core.Source))
                await RefreshGoogleConnectionStateAsync(core);
            if (tab.IsClosing || tab.View.IsDisposed || e.NavigationId != tab.NavigationId) return;
            tab.IsHome = IsHomeSource(core.Source);
            tab.FaviconUrl = core.FaviconUri;
            // Favicon decoding must not delay showing a ready page.
            _ = RefreshTabFaviconAsync(tab);
            if (tab.IsHome)
            {
                tab.Title = "Tugle";
                // Apply the compact palette first so the Home shell can be shown
                // immediately without a theme flash. History and recent sites
                // arrive after it is visible.
                await ApplyHomeThemeAsync(tab);
                if (tab.IsClosing || tab.View.IsDisposed || e.NavigationId != tab.NavigationId) return;
                _ = PopulateHomeDataAsync(tab);
            }
            else if (!string.IsNullOrWhiteSpace(core.Source))
            {
                _history.RecordVisit(core.Source, GetVisitTitle(core.Source, core.DocumentTitle), tab.FaviconUrl);
                RefreshHistoryFlyoutIfVisible();
            }
            if (ApplyWorkspaceRule(tab, core.Source))
                LayoutTabs();
            if (GetWorkspaceRoute(core.Source) is { } route)
                await ApplyWorkspaceRouteAsync(tab, route, activateDestination: ReferenceEquals(tab, _activeTab));
            UpdateTabButton(tab);
            if (ReferenceEquals(tab, _activeTab)) _ = RevealTabAsync(tab);
        }
        else if (ReferenceEquals(tab, _activeTab))
        {
            // Reveal failed navigations too so WebView2 can show its normal error page.
            _ = RevealTabAsync(tab);
        }

        if (ReferenceEquals(tab, _activeTab))
        {
            SetLoadingState(false);
            UpdateAddressBar();
            UpdateNavigationButtons();
        }
    }

    private bool ApplyWorkspaceRule(BrowserTab tab, string? source)
    {
        if (_isPrivate || tab.IsClosing || GetWorkspace(tab.WorkspaceId) is not { } workspace ||
            !Uri.TryCreate(source, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            return false;

        var host = uri.Host.ToLowerInvariant();
        var rule = workspace.Rules.FirstOrDefault(candidate => candidate.Enabled &&
            HostMatchesWorkspaceRule(host, candidate.HostPattern) &&
            _settings.TabGroups.Any(group => group.Id == candidate.TargetGroupId && group.WorkspaceId == tab.WorkspaceId));
        if (rule is null || tab.GroupId == rule.TargetGroupId) return false;
        tab.GroupId = rule.TargetGroupId;
        UpdateTabButton(tab);
        CapturePreviousSession();
        SaveSettings();
        return true;
    }

    private static bool HostMatchesWorkspaceRule(string host, string? pattern)
    {
        var value = pattern?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(value)) return false;
        if (value.StartsWith("*.", StringComparison.Ordinal))
        {
            var suffix = value[2..];
            return host.Length > suffix.Length && host.EndsWith("." + suffix, StringComparison.Ordinal);
        }
        return string.Equals(host, value, StringComparison.Ordinal);
    }

    private async Task RefreshTabFaviconAsync(BrowserTab tab)
    {
        if (tab.View.IsDisposed || tab.View.CoreWebView2 is null) return;

        var core = tab.View.CoreWebView2;
        var requestVersion = ++tab.FaviconRequestVersion;
        var source = core.Source;
        var faviconUri = core.FaviconUri;

        if (tab.IsHome || string.IsNullOrWhiteSpace(faviconUri))
        {
            ReplaceTabFavicon(tab, null);
            return;
        }

        if (tab.FaviconImage is not null &&
            string.Equals(tab.FaviconImageUri, faviconUri, StringComparison.OrdinalIgnoreCase))
        {
            UpdateTabButton(tab);
            return;
        }

        Image? favicon = null;
        try
        {
            using var stream = await core.GetFaviconAsync(CoreWebView2FaviconImageFormat.Png);
            using var decoded = Image.FromStream(stream);
            favicon = new Bitmap(decoded);
        }
        catch
        {
            // A page may navigate again before its favicon finishes downloading.
            return;
        }

        if (tab.View.IsDisposed || requestVersion != tab.FaviconRequestVersion ||
            !string.Equals(source, core.Source, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(faviconUri, core.FaviconUri, StringComparison.OrdinalIgnoreCase))
        {
            favicon.Dispose();
            return;
        }

        ReplaceTabFavicon(tab, favicon);
    }

    private void ReplaceTabFavicon(BrowserTab tab, Image? favicon)
    {
        var previous = tab.FaviconImage;
        tab.FaviconImage = favicon;
        tab.FaviconImageUri = favicon is null ? null : tab.FaviconUrl;
        previous?.Dispose();
        UpdateTabButton(tab);
        CaptureBookmarkIcon(tab);
    }

    private async Task PopulateHomeAsync(BrowserTab tab)
    {
        await ApplyHomeThemeAsync(tab);
        await PopulateHomeDataAsync(tab);
    }

    private async Task ApplyHomeThemeAsync(BrowserTab tab)
    {
        if (!tab.IsHome || tab.View.IsDisposed || tab.View.CoreWebView2 is null || tab.IsSuspended) return;

        var themePayload = new
        {
            background = ColorToCss(_homeBackground),
            panel = ColorToCss(_theme.Surface),
            panelHover = ColorToCss(_theme.SurfaceHover),
            border = ColorToCss(_theme.Border),
            borderStrong = ColorToCss(_theme.BorderStrong),
            text = ColorToCss(_theme.Text),
            muted = ColorToCss(_theme.Muted),
            accent = ColorToCss(_theme.Accent),
            selection = ColorToCss(_theme.Selection),
            detail = ColorToCss(_theme.Detail),
            backgroundMode = _homeBackgroundMode,
            backgroundMedia = _homeBackgroundMediaUrl,
            backgroundSecondary = ColorToCss(_homeBackgroundSecondary)
        };
        var themeJson = JsonSerializer.Serialize(themePayload);

        try
        {
            await tab.View.CoreWebView2.ExecuteScriptAsync($"window.tugleSetTheme({themeJson});");
        }
        catch
        {
            // The home page may be navigating away while this update is sent.
        }
    }

    private async Task PopulateHomeDataAsync(BrowserTab tab)
    {
        if (!tab.IsHome || tab.View.IsDisposed || tab.View.CoreWebView2 is null || tab.IsSuspended) return;

        var payload = new
        {
            visits = _history.MostUsedSites
                .Select(item => new
                {
                    item.Url,
                    item.Title,
                    item.IconUrl
                }),
            searches = _history.SearchSuggestions,
            searchEngine = _settings.SearchEngine,
            searchUrl = BuildSearchUrl(string.Empty)
        };
        var json = JsonSerializer.Serialize(payload);

        try
        {
            await tab.View.CoreWebView2.ExecuteScriptAsync($"window.tugleSetData({json});");
        }
        catch
        {
            // The home page may be navigating away while this update is sent.
        }
    }

    private void EnableFallbackHostFilter(BrowserTab tab, CoreWebView2 core)
    {
        if (tab.FallbackHostFilterRegistered) return;
        core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
        core.WebResourceRequested += (_, e) => OnWebResourceRequested(tab, e);
        tab.FallbackHostFilterRegistered = true;
    }

    private void OnWebResourceRequested(BrowserTab tab, CoreWebView2WebResourceRequestedEventArgs e)
    {
        if (!Uri.TryCreate(e.Request.Uri, UriKind.Absolute, out var uri)) return;
        // A page document must always be allowed. Only subresources from dedicated
        // advertising hosts are filtered, so a page cannot fail to open here.
        if (!_settings.UBlockEnabled || _uBlockEnabled || e.ResourceContext == CoreWebView2WebResourceContext.Document ||
            !IsBlockedHost(uri.Host) || tab.View.CoreWebView2 is null) return;

        e.Response = tab.View.CoreWebView2.Environment.CreateWebResourceResponse(
            Stream.Null,
            204,
            "Blocked",
            "Content-Type: text/plain");
    }

    private static bool IsBlockedHost(string host)
    {
        return BlockedHosts.Any(blocked =>
            host.Equals(blocked, StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith("." + blocked, StringComparison.OrdinalIgnoreCase));
    }

    private async Task OnWebMessageReceivedAsync(
        BrowserTab tab,
        CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (!tab.IsHome || tab.View.IsDisposed || tab.View.CoreWebView2 is null ||
            !IsHomeSource(e.Source)) return;

        string message;
        try
        {
            message = e.TryGetWebMessageAsString();
        }
        catch
        {
            return;
        }

        if (string.Equals(message, "tugle:customize-background", StringComparison.Ordinal))
        {
            ChooseCustomHomeBackground(tab);
            return;
        }

        const string backgroundPrefix = "tugle:background:";
        if (message.StartsWith(backgroundPrefix, StringComparison.Ordinal))
        {
            switch (message[backgroundPrefix.Length..])
            {
                case "solid":
                    ChooseCustomHomeBackground(tab);
                    break;
                case "gradient":
                    ChooseGradientBackground();
                    break;
                case "image":
                    ChooseBackgroundMedia(video: false);
                    break;
                case "video":
                    ChooseBackgroundMedia(video: true);
                    break;
                case "reset":
                    ResetHomeBackground();
                    break;
            }
            return;
        }

        const string searchPrefix = "tugle:search:";
        if (message.StartsWith(searchPrefix, StringComparison.Ordinal))
        {
            var search = message[searchPrefix.Length..].Trim();
            if (search.Length > 0 && search.Length <= 4096)
                tab.View.CoreWebView2.Navigate(BuildSearchUrl(search));
            return;
        }

        const string prefix = "tugle:google-autocomplete:";
        if (_settings.SearchEngine != "Google") return;
        if (!message.StartsWith(prefix, StringComparison.Ordinal)) return;

        var query = message[prefix.Length..].Trim();
        if (query.Length < 2 || query.Length > 120) return;

        tab.SuggestionCancellation?.Cancel();
        tab.SuggestionCancellation?.Dispose();
        var cancellation = new CancellationTokenSource();
        tab.SuggestionCancellation = cancellation;

        try
        {
            var suggestions = await FetchGoogleSuggestionsAsync(query, cancellation.Token);
            if (cancellation.IsCancellationRequested || tab.View.IsDisposed ||
                _settings.SearchEngine != "Google" ||
                !ReferenceEquals(tab, _activeTab) || !tab.IsHome ||
                tab.View.CoreWebView2 is null || !IsHomeSource(tab.View.CoreWebView2.Source)) return;

            var json = JsonSerializer.Serialize(new { query, suggestions });
            await tab.View.CoreWebView2.ExecuteScriptAsync(
                $"window.tugleApplyGoogleSuggestions({json});");
        }
        catch
        {
            // Local search history remains available when Google suggestions are offline.
        }
        finally
        {
            if (ReferenceEquals(tab.SuggestionCancellation, cancellation))
            {
                tab.SuggestionCancellation = null;
                cancellation.Dispose();
            }
        }
    }

    private async Task<IReadOnlyList<string>> FetchGoogleSuggestionsAsync(
        string query,
        CancellationToken cancellationToken)
    {
        var cacheKey = query.Trim();
        var now = DateTime.UtcNow;
        if (_googleSuggestionCache.TryGetValue(cacheKey, out var cached) && cached.ExpiresUtc > now)
            return cached.Suggestions;

        foreach (var expired in _googleSuggestionCache
                     .Where(item => item.Value.ExpiresUtc <= now)
                     .Select(item => item.Key)
                     .ToArray())
            _googleSuggestionCache.Remove(expired);

        var endpoint = "https://suggestqueries.google.com/complete/search?client=firefox&hl=en&q=" +
            Uri.EscapeDataString(cacheKey);
        using var response = await GoogleSuggestionClient.GetAsync(endpoint, cancellationToken);
        if (!response.IsSuccessStatusCode) return [];

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (document.RootElement.ValueKind != JsonValueKind.Array ||
            document.RootElement.GetArrayLength() < 2) return [];

        var result = new List<string>();
        foreach (var item in document.RootElement[1].EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String) continue;
            var value = item.GetString()?.Trim();
            if (!string.IsNullOrWhiteSpace(value) && !result.Contains(value, StringComparer.OrdinalIgnoreCase))
                result.Add(value);
            if (result.Count == 5) break;
        }
        var suggestions = (IReadOnlyList<string>)result;
        _googleSuggestionCache[cacheKey] = (now + GoogleSuggestionCacheLifetime, suggestions);
        return suggestions;
    }

    private void NavigateFromAddressBar()
    {
        var input = _addressBar.Text.Trim();
        if (string.IsNullOrWhiteSpace(input) || ActiveWebView?.CoreWebView2 is null) return;

        string destination;
        if (Uri.TryCreate(input, UriKind.Absolute, out var absolute) &&
            (absolute.Scheme == Uri.UriSchemeHttp || absolute.Scheme == Uri.UriSchemeHttps))
        {
            destination = input;
        }
        else if (input.Contains('.') && !input.Contains(' '))
        {
            destination = "https://" + input;
        }
        else
        {
            destination = BuildSearchUrl(input);
        }

        _ = NavigateActiveTabAsync(destination);
    }

    private async Task OnNewWindowRequestedAsync(BrowserTab source, CoreWebView2NewWindowRequestedEventArgs e)
    {
        using var deferral = e.GetDeferral();
        try
        {
            var route = GetWorkspaceRoute(e.Uri);
            if (route?.TargetGroupId is { } routeGroupId &&
                _settings.TabGroups.FirstOrDefault(group => group.Id == routeGroupId) is { IsCollapsed: true })
            {
                SetTabGroupCollapsed(routeGroupId, false);
            }
            var tab = await OpenNewTabAsync(
                showHome: false,
                workspaceId: _isPrivate ? null : route?.TargetGroupId ?? source.GroupId,
                tabWorkspaceId: _isPrivate ? null : route?.TargetWorkspaceId ?? source.WorkspaceId);
            if (tab?.View.CoreWebView2 is null)
            {
                e.Handled = true;
                return;
            }

            // A destination route may point at a collapsed group. Activate it
            // once more so the newly opened tab, not an older representative,
            // is the visible page in that workspace.
            if (route is not null)
                await ActivateTabAsync(tab);

            e.NewWindow = tab.View.CoreWebView2;
            tab.InitialNavigationTarget = e.Uri;
            tab.View.CoreWebView2.Navigate(e.Uri);
        }
        catch
        {
            e.Handled = true;
        }
    }

    private void OnDownloadStarting(CoreWebView2DownloadStartingEventArgs e)
    {
        try
        {
            var folder = GetDownloadsFolder();
            Directory.CreateDirectory(folder);

            var suggestedName = Path.GetFileName(e.ResultFilePath);
            if (string.IsNullOrWhiteSpace(suggestedName)) suggestedName = "download";
            e.ResultFilePath = GetAvailableDownloadPath(folder, suggestedName);
            var item = new DownloadItem(e.ResultFilePath);
            _downloads.RemoveAll(existing => string.Equals(existing.Path, item.Path, StringComparison.OrdinalIgnoreCase));
            _downloads.Insert(0, item);
            if (_downloads.Count > 20) _downloads.RemoveRange(20, _downloads.Count - 20);
            var operation = e.DownloadOperation;
            item.Operation = operation;
            item.BytesReceived = operation.BytesReceived;
            item.TotalBytes = ToDownloadSize(operation.TotalBytesToReceive);
            operation.BytesReceivedChanged += (_, _) => UpdateDownloadProgress(item, operation);
            operation.EstimatedEndTimeChanged += (_, _) => UpdateDownloadProgress(item, operation);
            operation.StateChanged += (_, _) =>
            {
                item.State = operation.State;
                item.BytesReceived = operation.BytesReceived;
                item.TotalBytes = ToDownloadSize(operation.TotalBytesToReceive);
                UpdateDownloadsButton();
                if (_downloadsMenu.Visible) RebuildDownloadsMenu();
                if (_actionFlyout.Visible && _actionFlyoutMode == ActionFlyoutMode.Downloads)
                    PopulateActionFlyout();
            };
            e.Handled = true;
            UpdateDownloadsButton();
        }
        catch
        {
            // If Tugle cannot prepare its folder, WebView2 keeps its normal download behavior.
        }
    }

    private void UpdateDownloadProgress(DownloadItem item, CoreWebView2DownloadOperation operation)
    {
        item.BytesReceived = operation.BytesReceived;
        item.TotalBytes = ToDownloadSize(operation.TotalBytesToReceive);
        if (DateTime.UtcNow - item.LastProgressRefreshUtc < TimeSpan.FromMilliseconds(350)) return;
        item.LastProgressRefreshUtc = DateTime.UtcNow;
        if (!IsDisposed && IsHandleCreated && _actionFlyout.Visible && _actionFlyoutMode == ActionFlyoutMode.Downloads)
        {
            BeginInvoke((Action)(() =>
            {
                if (_actionFlyout.Visible && _actionFlyoutMode == ActionFlyoutMode.Downloads &&
                    item.State == CoreWebView2DownloadState.InProgress && item.Row is { IsDisposed: false } row)
                {
                    row.Detail = FormatDownloadProgress(item);
                    row.Invalidate();
                }
            }));
        }
    }

    private static long ToDownloadSize(ulong? bytes) => bytes is { } value
        ? (long)Math.Min(value, (ulong)long.MaxValue)
        : 0;

    private ContextMenuStrip CreateTabContextMenu(BrowserTab tab)
    {
        var menu = new ContextMenuStrip();
        ConfigureToolbarMenu(menu);

        var muteItem = new ToolStripMenuItem();
        muteItem.Click += (_, _) => ToggleTabMute(tab);

        var duplicateItem = new ToolStripMenuItem("Duplicate tab");
        duplicateItem.Click += async (_, _) => await DuplicateTabAsync(tab);

        var pinItem = new ToolStripMenuItem();
        pinItem.Click += (_, _) => ToggleTabPinned(tab);

        var workspaceItem = new ToolStripMenuItem("Tab group") { Visible = !_isPrivate };
        workspaceItem.DropDownOpening += (_, _) => PopulateTabGroupMenu(workspaceItem, tab);

        var moveWorkspaceItem = new ToolStripMenuItem("Move to workspace") { Visible = !_isPrivate };
        moveWorkspaceItem.DropDownOpening += (_, _) => PopulateTabWorkspaceMenu(moveWorkspaceItem, tab);

        var splitItem = new ToolStripMenuItem();
        splitItem.Click += async (_, _) => await ToggleSplitViewAsync(tab);

        var bookmarkItem = new ToolStripMenuItem();
        bookmarkItem.Click += (_, _) => ToggleBookmark(tab);

        var pdfItem = new ToolStripMenuItem("Save page as PDF…");
        pdfItem.Click += async (_, _) => await SavePageAsPdfAsync(tab);

        var reloadItem = new ToolStripMenuItem("Reload tab");
        reloadItem.Click += (_, _) => tab.View.CoreWebView2?.Reload();

        var closeItem = new ToolStripMenuItem("Close tab");
        closeItem.Click += (_, _) => CloseTab(tab);

        menu.Opening += (_, _) =>
        {
            muteItem.Text = tab.IsMuted ? "Unmute tab" : "Mute tab";
            muteItem.Checked = tab.IsMuted;
            muteItem.Visible = tab.IsPlayingAudio || tab.IsMuted;
            pinItem.Text = tab.IsPinned ? "Unpin tab" : "Pin tab";
            bookmarkItem.Text = IsPageBookmarked(tab) ? "Remove bookmark" : "Bookmark page";
            duplicateItem.Enabled = !tab.IsClosing;
            reloadItem.Enabled = !tab.IsClosing && tab.View.CoreWebView2 is not null;
            pinItem.Enabled = !tab.IsClosing;
            workspaceItem.Enabled = !_isPrivate && !tab.IsClosing && _tabs.Contains(tab);
            moveWorkspaceItem.Enabled = workspaceItem.Enabled && _settings.Workspaces.Count > 1;
            var splitCandidateExists = ActiveWorkspaceTabs.Any(item => !ReferenceEquals(item, tab));
            splitItem.Visible = !tab.IsClosing && tab.WorkspaceId == ActiveWorkspaceId && splitCandidateExists;
            splitItem.Text = IsSplitParticipant(tab) ? "Close split view" : "Open in split view";
            splitItem.Enabled = splitItem.Visible;
            bookmarkItem.Enabled = !_isPrivate && TryGetPageDetails(tab, out _, out _, out _);
            pdfItem.Enabled = tab.View.CoreWebView2 is not null;
            closeItem.Enabled = !tab.IsClosing;
        };
        menu.Items.Add(muteItem);
        menu.Items.Add(pinItem);
        menu.Items.Add(workspaceItem);
        menu.Items.Add(moveWorkspaceItem);
        menu.Items.Add(splitItem);
        menu.Items.Add(duplicateItem);
        menu.Items.Add(bookmarkItem);
        menu.Items.Add(pdfItem);
        menu.Items.Add(reloadItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(closeItem);
        return menu;
    }

    private void PopulateTabWorkspaceMenu(ToolStripMenuItem menu, BrowserTab tab)
    {
        menu.DropDownItems.Clear();
        if (_isPrivate || tab.IsClosing || !_tabs.Contains(tab))
        {
            menu.Enabled = false;
            return;
        }

        var selectedTabs = GetTabsForOperation(tab);
        foreach (var workspace in _settings.Workspaces)
        {
            var targetWorkspace = workspace;
            var tabCount = GetWorkspaceTabCount(targetWorkspace.Id);
            var item = new ToolStripMenuItem(
                $"{targetWorkspace.Icon}  {targetWorkspace.Name}  ·  {tabCount} tab{(tabCount == 1 ? string.Empty : "s")}")
            {
                Checked = targetWorkspace.Id == tab.WorkspaceId,
                Enabled = targetWorkspace.Id != tab.WorkspaceId
            };
            item.Click += async (_, _) => await MoveTabsToWorkspaceAsync(selectedTabs, targetWorkspace.Id);
            menu.DropDownItems.Add(item);
        }

        menu.DropDownItems.Add(new ToolStripSeparator());
        var hint = new ToolStripMenuItem(selectedTabs.Count > 1
            ? $"Move {selectedTabs.Count} selected tabs"
            : "Move this tab")
        {
            Enabled = false
        };
        menu.DropDownItems.Add(hint);
    }

    private void PopulateTabGroupMenu(ToolStripMenuItem menu, BrowserTab tab)
    {
        menu.DropDownItems.Clear();
        if (_isPrivate || tab.IsClosing || !_tabs.Contains(tab))
        {
            menu.Enabled = false;
            return;
        }

        menu.Enabled = true;
        var selectedTabs = GetTabsForOperation(tab);
        var selectionLabel = selectedTabs.Count > 1 ? $"selected tabs ({selectedTabs.Count})" : "this tab";
        var current = GetTabGroup(tab);
        if (current is not null)
        {
            var currentItem = new ToolStripMenuItem($"{current.Icon} {current.Name}") { Enabled = false };
            menu.DropDownItems.Add(currentItem);

            var renameItem = new ToolStripMenuItem("Rename tab group…");
            renameItem.Click += (_, _) => RenameTabGroup(current.Id);
            menu.DropDownItems.Add(renameItem);

            var colorsItem = new ToolStripMenuItem("Color");
            foreach (var choice in WorkspaceColors)
            {
                var color = TugleSettings.ToHex(choice.Color);
                var colorItem = new ToolStripMenuItem(choice.Name)
                {
                    Checked = string.Equals(current.Color, color, StringComparison.OrdinalIgnoreCase)
                };
                colorItem.Click += (_, _) => SetTabGroupColor(current.Id, color);
                colorsItem.DropDownItems.Add(colorItem);
            }
            var customColor = new ToolStripMenuItem("Custom color…");
            customColor.Click += (_, _) => ChooseTabGroupColor(current.Id);
            colorsItem.DropDownItems.Add(new ToolStripSeparator());
            colorsItem.DropDownItems.Add(customColor);
            menu.DropDownItems.Add(colorsItem);

            var iconsItem = new ToolStripMenuItem("Icon");
            foreach (var icon in WorkspaceIcons)
            {
                var iconItem = new ToolStripMenuItem(icon) { Checked = string.Equals(current.Icon, icon, StringComparison.Ordinal) };
                iconItem.Click += (_, _) => SetTabGroupIcon(current.Id, icon);
                iconsItem.DropDownItems.Add(iconItem);
            }
            menu.DropDownItems.Add(iconsItem);

            var collapseItem = new ToolStripMenuItem(current.IsCollapsed ? "Expand tab group" : "Collapse tab group");
            collapseItem.Click += (_, _) => SetTabGroupCollapsed(current.Id, !current.IsCollapsed);
            menu.DropDownItems.Add(collapseItem);

            var ungroupItem = new ToolStripMenuItem($"Remove {selectionLabel} from group");
            ungroupItem.Click += (_, _) => UngroupTabs(selectedTabs);
            menu.DropDownItems.Add(ungroupItem);

            var deleteItem = new ToolStripMenuItem("Delete tab group (keep tabs)");
            deleteItem.Click += (_, _) => DeleteTabGroup(current.Id);
            menu.DropDownItems.Add(deleteItem);
            menu.DropDownItems.Add(new ToolStripSeparator());
        }

        var createItem = new ToolStripMenuItem("New tab group…")
        {
            Enabled = _settings.TabGroups.Count < 48 && _settings.TabGroups.Count(group => group.WorkspaceId == tab.WorkspaceId) < 12
        };
        createItem.Click += (_, _) => CreateTabGroupForTabs(selectedTabs, tab.WorkspaceId);
        menu.DropDownItems.Add(createItem);

        var availableGroups = _settings.TabGroups
            .Where(group => group.WorkspaceId == tab.WorkspaceId)
            .OrderBy(group => group.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
        if (availableGroups.Length > 0)
        {
            menu.DropDownItems.Add(new ToolStripSeparator());
            foreach (var group in availableGroups)
            {
                var groupItem = new ToolStripMenuItem($"{group.Icon} {group.Name}")
                {
                    Checked = selectedTabs.All(item => item.GroupId == group.Id)
                };
                groupItem.Click += (_, _) => AssignTabsToGroup(selectedTabs, group.Id);
                menu.DropDownItems.Add(groupItem);
            }
        }

        var host = GetTabHost(tab);
        if (host is null || availableGroups.Length == 0) return;

        menu.DropDownItems.Add(new ToolStripSeparator());
        var rulesItem = new ToolStripMenuItem("Auto-group this site");
        foreach (var group in availableGroups)
        {
            var ruleItem = new ToolStripMenuItem($"Always send {host} to {group.Name}")
            {
                Checked = _settings.ActiveWorkspace.Rules.Any(rule =>
                    rule.Enabled && rule.TargetGroupId == group.Id &&
                    string.Equals(rule.HostPattern, host, StringComparison.OrdinalIgnoreCase))
            };
            ruleItem.Click += (_, _) => ToggleWorkspaceRule(tab.WorkspaceId, host, group.Id);
            rulesItem.DropDownItems.Add(ruleItem);
        }
        menu.DropDownItems.Add(rulesItem);
    }

    private TugleTabGroup? GetTabGroup(BrowserTab tab) =>
        !_isPrivate && tab.GroupId is { } groupId
            ? _settings.TabGroups.FirstOrDefault(group => group.Id == groupId && group.WorkspaceId == tab.WorkspaceId)
            : null;

    private Guid? GetValidTabGroupId(Guid? groupId, Guid? workspaceId = null) =>
        !_isPrivate && groupId is { } id && _settings.TabGroups.Any(group =>
            group.Id == id && group.WorkspaceId == (workspaceId ?? ActiveWorkspaceId))
            ? id
            : null;

    private Guid CreateTabGroup(string name, string color, Guid? workspaceId = null)
    {
        var targetWorkspaceId = GetValidWorkspaceId(workspaceId);
        if (_isPrivate || _settings.TabGroups.Count >= 48 || _settings.TabGroups.Count(group => group.WorkspaceId == targetWorkspaceId) >= 12 ||
            !TryNormalizeTabGroupName(name, out var normalizedName) ||
            !TryParseTabGroupColor(color, out var parsedColor) ||
            HasTabGroupName(normalizedName, workspaceId: targetWorkspaceId))
            return Guid.Empty;

        var group = new TugleTabGroup
        {
            Id = Guid.NewGuid(),
            WorkspaceId = targetWorkspaceId,
            Name = normalizedName,
            Color = TugleSettings.ToHex(parsedColor),
            Icon = WorkspaceIcons[_settings.TabGroups.Count(group => group.WorkspaceId == targetWorkspaceId) % WorkspaceIcons.Length]
        };
        _settings.TabGroups.Add(group);
        SaveSettings();
        return group.Id;
    }

    private bool RenameTabGroup(Guid groupId)
    {
        if (_isPrivate) return false;
        var group = _settings.TabGroups.FirstOrDefault(item => item.Id == groupId);
        if (group is null) return false;

        using var dialog = new WorkspaceNameDialog("Rename tab group", group.Name, _theme);
        if (dialog.ShowDialog(this) != DialogResult.OK ||
            !TryNormalizeTabGroupName(dialog.WorkspaceName, out var name))
            return false;

        if (HasTabGroupName(name, exceptGroupId: groupId, workspaceId: group.WorkspaceId))
        {
            MessageBox.Show(this, "That tab group name is already in use.", "Tab group", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return false;
        }

        group.Name = name;
        UpdateTabButtonsForGroup(groupId);
        SaveSettings();
        return true;
    }

    private void CreateTabGroupForTabs(IReadOnlyCollection<BrowserTab> tabs, Guid workspaceId)
    {
        if (_isPrivate || tabs.Count == 0 || tabs.Any(tab => tab.IsClosing || tab.WorkspaceId != workspaceId) ||
            _settings.TabGroups.Count >= 48 || _settings.TabGroups.Count(group => group.WorkspaceId == workspaceId) >= 12) return;

        using var dialog = new WorkspaceNameDialog(
            "New tab group",
            $"Tab group {_settings.TabGroups.Count(group => group.WorkspaceId == workspaceId) + 1}",
            _theme);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        if (HasTabGroupName(dialog.WorkspaceName, workspaceId: workspaceId))
        {
            MessageBox.Show(this, "That tab group name is already in use.", "Tab group", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var groupCount = _settings.TabGroups.Count(group => group.WorkspaceId == workspaceId);
        var color = TugleSettings.ToHex(WorkspaceColors[groupCount % WorkspaceColors.Length].Color);
        var groupId = CreateTabGroup(dialog.WorkspaceName, color, workspaceId);
        if (groupId != Guid.Empty) AssignTabsToGroup(tabs, groupId);
    }

    private bool AssignTabToGroup(BrowserTab tab, Guid groupId) => AssignTabsToGroup([tab], groupId);

    private bool AssignTabsToGroup(IEnumerable<BrowserTab> sourceTabs, Guid groupId)
    {
        if (_isPrivate) return false;
        var tabs = sourceTabs.Distinct().Where(tab => !tab.IsClosing && _tabs.Contains(tab)).ToArray();
        var group = _settings.TabGroups.FirstOrDefault(item => item.Id == groupId);
        if (tabs.Length == 0 || group is null || tabs.Any(tab => tab.WorkspaceId != group.WorkspaceId))
            return false;

        var changed = false;
        foreach (var tab in tabs)
        {
            if (tab.GroupId == groupId) continue;
            tab.GroupId = groupId;
            UpdateTabButton(tab);
            changed = true;
        }
        if (!changed) return true;
        CapturePreviousSession();
        SaveSettings();
        LayoutTabs();
        return true;
    }

    private bool UngroupTab(BrowserTab tab) => UngroupTabs([tab]);

    private bool UngroupTabs(IEnumerable<BrowserTab> sourceTabs)
    {
        if (_isPrivate) return false;
        var tabs = sourceTabs.Distinct().Where(tab => !tab.IsClosing && _tabs.Contains(tab) && tab.GroupId is not null).ToArray();
        if (tabs.Length == 0) return false;

        foreach (var tab in tabs)
        {
            tab.GroupId = null;
            UpdateTabButton(tab);
        }
        CapturePreviousSession();
        SaveSettings();
        LayoutTabs();
        return true;
    }

    private bool SetTabGroupColor(Guid groupId, string color)
    {
        if (_isPrivate || !TryParseTabGroupColor(color, out var parsedColor)) return false;
        var group = _settings.TabGroups.FirstOrDefault(item => item.Id == groupId);
        if (group is null) return false;

        group.Color = TugleSettings.ToHex(parsedColor);
        UpdateTabButtonsForGroup(groupId);
        SaveSettings();
        return true;
    }

    private void ChooseTabGroupColor(Guid groupId)
    {
        var group = _settings.TabGroups.FirstOrDefault(item => item.Id == groupId);
        if (group is null) return;
        using var dialog = new ColorPickerDialog(
            TugleSettings.FromHex(group.Color, _accent),
            "Tab group color",
            _theme);
        if (dialog.ShowDialog(this) == DialogResult.OK)
            SetTabGroupColor(groupId, TugleSettings.ToHex(dialog.SelectedColor));
    }

    private bool SetTabGroupIcon(Guid groupId, string icon)
    {
        if (_isPrivate || !WorkspaceIcons.Contains(icon, StringComparer.Ordinal)) return false;
        var group = _settings.TabGroups.FirstOrDefault(item => item.Id == groupId);
        if (group is null) return false;
        group.Icon = icon;
        UpdateTabButtonsForGroup(groupId);
        SaveSettings();
        return true;
    }

    private bool SetTabGroupCollapsed(Guid groupId, bool collapsed)
    {
        if (_isPrivate) return false;
        var group = _settings.TabGroups.FirstOrDefault(item => item.Id == groupId);
        if (group is null || group.IsCollapsed == collapsed) return false;
        group.IsCollapsed = collapsed;
        UpdateTabButtonsForGroup(groupId);
        LayoutTabs();
        CapturePreviousSession();
        SaveSettings();
        return true;
    }

    private bool DeleteTabGroup(Guid groupId)
    {
        if (_isPrivate) return false;
        var group = _settings.TabGroups.FirstOrDefault(item => item.Id == groupId);
        if (group is null) return false;

        _settings.TabGroups.Remove(group);
        foreach (var tab in _tabs.Where(item => item.GroupId == groupId))
        {
            tab.GroupId = null;
            UpdateTabButton(tab);
        }
        foreach (var closedTab in _settings.RecentlyClosedTabs.Where(item => item.GroupId == groupId))
            closedTab.GroupId = null;
        foreach (var workspace in _settings.Workspaces)
            workspace.Rules.RemoveAll(rule => rule.TargetGroupId == groupId);
        CapturePreviousSession();
        SaveSettings();
        return true;
    }

    private void UpdateTabButtonsForGroup(Guid groupId)
    {
        foreach (var tab in _tabs.Where(item => item.GroupId == groupId))
            UpdateTabButton(tab);
    }

    private static bool TryNormalizeTabGroupName(string? value, out string name)
    {
        name = value?.Trim() ?? string.Empty;
        if (name.Length == 0) return false;
        if (name.Length > 40) name = name[..40];
        return true;
    }

    private bool HasTabGroupName(string? value, Guid? exceptGroupId = null, Guid? workspaceId = null) =>
        TryNormalizeTabGroupName(value, out var name) && _settings.TabGroups.Any(group =>
            group.Id != exceptGroupId && group.WorkspaceId == (workspaceId ?? ActiveWorkspaceId) &&
            string.Equals(group.Name, name, StringComparison.CurrentCultureIgnoreCase));

    private static bool TryParseTabGroupColor(string? value, out Color color)
    {
        color = Color.Empty;
        if (string.IsNullOrWhiteSpace(value)) return false;
        try
        {
            color = ColorTranslator.FromHtml(value);
            return !color.IsEmpty;
        }
        catch
        {
            return false;
        }
    }

    private IReadOnlyList<BrowserTab> GetTabsForOperation(BrowserTab focus)
    {
        if (_selectedTabs.Contains(focus))
        {
            var selected = _selectedTabs
                .Where(tab => _tabs.Contains(tab) && !tab.IsClosing && tab.WorkspaceId == focus.WorkspaceId)
                .ToArray();
            if (selected.Length > 0) return selected;
        }

        return [focus];
    }

    private void SelectTabFromPointer(BrowserTab tab, Keys modifiers)
    {
        if (tab.IsClosing || !_tabs.Contains(tab) || (!_isPrivate && tab.WorkspaceId != ActiveWorkspaceId)) return;

        var control = (modifiers & Keys.Control) == Keys.Control;
        var range = (modifiers & Keys.Shift) == Keys.Shift;
        if (range && _selectionAnchor is not null && _selectionAnchor.WorkspaceId == tab.WorkspaceId)
        {
            var tabs = GetDisplayedTabs().ToArray();
            var first = Array.IndexOf(tabs, _selectionAnchor);
            var last = Array.IndexOf(tabs, tab);
            if (first >= 0 && last >= 0)
            {
                _selectedTabs.Clear();
                foreach (var item in tabs.Skip(Math.Min(first, last)).Take(Math.Abs(last - first) + 1))
                    _selectedTabs.Add(item);
            }
            else
            {
                _selectedTabs.Clear();
                _selectedTabs.Add(tab);
            }
        }
        else if (control)
        {
            if (!_selectedTabs.Add(tab)) _selectedTabs.Remove(tab);
            _selectionAnchor ??= tab;
        }
        else
        {
            _selectedTabs.Clear();
            _selectedTabs.Add(tab);
            _selectionAnchor = tab;
        }

        UpdateTabSelectionVisuals();
    }

    private void ClearTabSelection()
    {
        if (_selectedTabs.Count == 0) return;
        _selectedTabs.Clear();
        _selectionAnchor = null;
        UpdateTabSelectionVisuals();
    }

    private void UpdateTabSelectionVisuals()
    {
        foreach (var item in _tabs)
            UpdateTabButton(item);
    }

    private static string? GetTabHost(BrowserTab? tab)
    {
        if (tab is null) return null;
        var source = tab.DeferredNavigation ? tab.InitialNavigationTarget : tab.View.CoreWebView2?.Source;
        return Uri.TryCreate(source, UriKind.Absolute, out var uri) &&
            uri.Scheme is "http" or "https" && !string.IsNullOrWhiteSpace(uri.Host)
            ? uri.Host.ToLowerInvariant()
            : null;
    }

    private void ToggleWorkspaceRule(Guid workspaceId, string host, Guid groupId)
    {
        if (_isPrivate || GetWorkspace(workspaceId) is not { } workspace ||
            !_settings.TabGroups.Any(group => group.Id == groupId && group.WorkspaceId == workspaceId)) return;

        var existing = workspace.Rules.FirstOrDefault(rule => rule.TargetGroupId == groupId &&
            string.Equals(rule.HostPattern, host, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
            workspace.Rules.Remove(existing);
        else
            workspace.Rules.Add(new TugleWorkspaceRule
            {
                Id = Guid.NewGuid(),
                WorkspaceId = workspaceId,
                TargetGroupId = groupId,
                HostPattern = host,
                Enabled = true
            });
        SaveSettings();
    }

    private TugleWorkspaceRoute? GetWorkspaceRoute(string? destination)
    {
        if (_isPrivate || !Uri.TryCreate(destination, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https"))
        {
            return null;
        }

        var host = uri.Host.ToLowerInvariant();
        return _settings.WorkspaceRoutes
            .Where(route => route.Enabled && GetWorkspace(route.TargetWorkspaceId) is not null &&
                (route.TargetGroupId is null || _settings.TabGroups.Any(group =>
                    group.Id == route.TargetGroupId && group.WorkspaceId == route.TargetWorkspaceId)) &&
                HostMatchesWorkspaceRule(host, route.HostPattern))
            .OrderByDescending(route => !route.HostPattern.StartsWith("*.", StringComparison.Ordinal))
            .ThenByDescending(route => route.HostPattern.Length)
            .FirstOrDefault();
    }

    private bool ToggleWorkspaceRoute(string host, Guid targetWorkspaceId, Guid? targetGroupId)
    {
        if (_isPrivate || GetWorkspace(targetWorkspaceId) is null || !IsSafeWorkspaceHostPattern(host) ||
            targetGroupId is { } groupId && !_settings.TabGroups.Any(group =>
                group.Id == groupId && group.WorkspaceId == targetWorkspaceId))
        {
            return false;
        }

        host = host.Trim().ToLowerInvariant();
        var existing = _settings.WorkspaceRoutes.FirstOrDefault(route =>
            string.Equals(route.HostPattern, host, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            if (existing.TargetWorkspaceId == targetWorkspaceId && existing.TargetGroupId == targetGroupId)
                _settings.WorkspaceRoutes.Remove(existing);
            else
            {
                existing.TargetWorkspaceId = targetWorkspaceId;
                existing.TargetGroupId = targetGroupId;
                existing.Enabled = true;
            }
        }
        else
        {
            if (_settings.WorkspaceRoutes.Count >= 36) return false;
            _settings.WorkspaceRoutes.Add(new TugleWorkspaceRoute
            {
                Id = Guid.NewGuid(),
                HostPattern = host,
                TargetWorkspaceId = targetWorkspaceId,
                TargetGroupId = targetGroupId,
                Enabled = true
            });
        }

        SaveSettings();
        return true;
    }

    private async Task NavigateActiveTabAsync(string destination)
    {
        var tab = _activeTab;
        if (tab?.View.CoreWebView2 is null || tab.IsClosing) return;

        var route = GetWorkspaceRoute(destination);
        if (route is not null)
            await ApplyWorkspaceRouteAsync(tab, route);

        if (tab.IsClosing || tab.View.CoreWebView2 is null) return;
        tab.View.CoreWebView2.Navigate(destination);
    }

    private async Task ApplyWorkspaceRouteAsync(BrowserTab tab, TugleWorkspaceRoute route, bool activateDestination = true)
    {
        if (_isPrivate || tab.IsClosing || !_tabs.Contains(tab) || GetWorkspace(route.TargetWorkspaceId) is not { } target)
            return;
        var targetGroupId = GetValidTabGroupId(route.TargetGroupId, target.Id);
        if (tab.WorkspaceId == target.Id)
        {
            if (tab.GroupId != targetGroupId)
            {
                tab.GroupId = targetGroupId;
                UpdateTabButton(tab);
                LayoutTabs();
                CapturePreviousSession();
                SaveSettings();
            }
            return;
        }

        if (HasSplitView) CloseSplitView(revealActive: false);
        if (GetWorkspace(tab.WorkspaceId) is { } source && ReferenceEquals(tab, _activeTab))
            source.LastActiveTabId = _tabs.FirstOrDefault(item => item.WorkspaceId == source.Id && !ReferenceEquals(item, tab) && !item.IsClosing)?.Id;

        tab.WorkspaceId = target.Id;
        tab.GroupId = targetGroupId;
        if (targetGroupId is { } groupId && _settings.TabGroups.FirstOrDefault(group => group.Id == groupId) is { IsCollapsed: true })
            SetTabGroupCollapsed(groupId, false);
        target.LastActiveTabId = tab.Id;
        if (activateDestination)
            ClearTabSelection();
        UpdateTabButton(tab);
        LayoutTabs();
        if (activateDestination)
        {
            await SwitchWorkspaceAsync(target.Id);
            await ActivateTabAsync(tab);
        }
        CapturePreviousSession();
        SaveSettings();
    }

    private async Task MoveTabsToWorkspaceAsync(IReadOnlyCollection<BrowserTab> sourceTabs, Guid targetWorkspaceId)
    {
        if (_isPrivate || GetWorkspace(targetWorkspaceId) is not { } target) return;

        var moving = sourceTabs
            .Where(tab => _tabs.Contains(tab) && !tab.IsClosing)
            .Distinct()
            .ToArray();
        if (moving.Length == 0 || moving.All(tab => tab.WorkspaceId == targetWorkspaceId)) return;

        if (HasSplitView && moving.Any(IsSplitParticipant))
            CloseSplitView(revealActive: false);

        foreach (var tab in moving)
        {
            if (GetWorkspace(tab.WorkspaceId) is { } source && ReferenceEquals(tab, _activeTab))
                source.LastActiveTabId = _tabs.FirstOrDefault(item => item.WorkspaceId == source.Id &&
                    !ReferenceEquals(item, tab) && !item.IsClosing)?.Id;

            tab.WorkspaceId = targetWorkspaceId;
            // Groups belong to their workspace, so a cross-workspace move starts
            // ungrouped and can be assigned to a destination group immediately.
            tab.GroupId = null;
            UpdateTabButton(tab);
        }

        target.LastActiveTabId = moving[0].Id;
        ClearTabSelection();
        LayoutTabs();
        CapturePreviousSession();
        SaveSettings();

        if (targetWorkspaceId != ActiveWorkspaceId)
            await SwitchWorkspaceAsync(targetWorkspaceId);
        else
            await ActivateTabAsync(moving[0]);
    }

    private void ShowWorkspaceMenu()
    {
        if (_isPrivate || IsDisposed) return;
        PopulateWorkspaceMenu();
        _workspaceMenu.Show(_workspaceButton, new Point(0, _workspaceButton.Height));
    }

    private void ShowCommandPalette()
    {
        if (IsDisposed || Disposing) return;
        using var palette = new CommandPaletteDialog(BuildCommandPaletteEntries(), _theme, GuiScale);
        if (palette.ShowDialog(this) == DialogResult.OK)
            palette.SelectedEntry?.Action();
    }

    private IReadOnlyList<CommandPaletteEntry> BuildCommandPaletteEntries()
    {
        var entries = new List<CommandPaletteEntry>
        {
            new("Action", "New tab", "Ctrl+T", () => _ = RequestNewTabAsync()),
            new("Action", "Toggle split view", "Ctrl+Shift+S · show two tabs side by side", () => _ = ToggleSplitWithNextTabAsync()),
            new("Action", "New private window", "Ctrl+Shift+N", OpenPrivateWindow),
            new("Action", "Show downloads", "Ctrl+J", ShowDownloadsMenu),
            new("Action", "Show history", "Ctrl+H", ShowHistoryMenu),
            new("Action", "Show bookmarks", "Saved pages", ShowLibraryMenu),
            new("Action", "Browser settings", "Search, privacy, performance", ShowAccountsMenu),
            new("Action", "Toggle fullscreen", "F11", RequestFullscreenToggle)
        };

        if (!_isPrivate)
        {
            foreach (var workspace in _settings.Workspaces)
            {
                var target = workspace;
                entries.Add(new CommandPaletteEntry(
                    "Workspace",
                    $"{target.Icon} {target.Name}",
                    target.Id == ActiveWorkspaceId ? "Current workspace" : "Switch workspace",
                    () => _ = SwitchWorkspaceAsync(target.Id)));
            }
        }

        foreach (var tab in _tabs.Where(tab => !tab.IsClosing).Take(60))
        {
            var target = tab;
            var workspaceName = _isPrivate ? "Private" : GetWorkspace(target.WorkspaceId)?.Name ?? "Workspace";
            entries.Add(new CommandPaletteEntry(
                "Tab",
                target.Title,
                $"{workspaceName} · {GetTabHost(target) ?? (target.IsHome ? "Tugle Home" : "New tab")}",
                () => _ = ActivateTabAsync(target)));
        }

        foreach (var visit in _history.RecentVisits.Take(12))
        {
            var target = visit;
            entries.Add(new CommandPaletteEntry(
                "History",
                target.Title,
                target.Url,
                () => NavigateToHistoryEntry(target.Url)));
        }

        return entries;
    }

    private void PopulateWorkspaceMenu()
    {
        _workspaceMenu.Items.Clear();
        if (_isPrivate) return;

        var active = _settings.ActiveWorkspace;
        var activeTabCount = GetWorkspaceTabCount(active.Id);
        var summary = new ToolStripMenuItem($"Current: {active.Name} · {activeTabCount} tab{(activeTabCount == 1 ? string.Empty : "s")}") { Enabled = false };
        _workspaceMenu.Items.Add(summary);
        _workspaceMenu.Items.Add(new ToolStripSeparator());

        foreach (var workspace in _settings.Workspaces)
        {
            var workspaceId = workspace.Id;
            var tabCount = GetWorkspaceTabCount(workspaceId);
            var item = new ToolStripMenuItem($"{workspace.Icon}  {workspace.Name}  ·  {tabCount} tab{(tabCount == 1 ? string.Empty : "s")}")
            {
                Checked = workspaceId == ActiveWorkspaceId,
                ToolTipText = GetWorkspaceMemorySummary(workspaceId)
            };
            item.Click += async (_, _) => await SwitchWorkspaceAsync(workspaceId);
            _workspaceMenu.Items.Add(item);
        }

        _workspaceMenu.Items.Add(new ToolStripSeparator());
        var newWorkspace = new ToolStripMenuItem("New workspace…")
        {
            Enabled = _settings.Workspaces.Count < 12
        };
        newWorkspace.Click += (_, _) => PromptCreateWorkspace();
        _workspaceMenu.Items.Add(newWorkspace);

        var templates = new ToolStripMenuItem("New from template");
        templates.Enabled = _settings.Workspaces.Count < 12;
        foreach (var template in WorkspaceTemplates)
        {
            var selectedTemplate = template;
            var item = new ToolStripMenuItem($"{selectedTemplate.Icon}  {selectedTemplate.Name}");
            item.Click += (_, _) => PromptCreateWorkspace(selectedTemplate);
            templates.DropDownItems.Add(item);
        }
        _workspaceMenu.Items.Add(templates);

        var manage = new ToolStripMenuItem("Manage current workspace");
        var rename = new ToolStripMenuItem("Rename…");
        rename.Click += (_, _) => RenameWorkspace(active.Id);
        manage.DropDownItems.Add(rename);

        var color = new ToolStripMenuItem("Color");
        foreach (var choice in WorkspaceColors)
        {
            var colorValue = TugleSettings.ToHex(choice.Color);
            var item = new ToolStripMenuItem(choice.Name)
            {
                Checked = string.Equals(active.Color, colorValue, StringComparison.OrdinalIgnoreCase)
            };
            item.Click += (_, _) => SetWorkspaceColor(active.Id, colorValue);
            color.DropDownItems.Add(item);
        }
        color.DropDownItems.Add(new ToolStripSeparator());
        var customColor = new ToolStripMenuItem("Custom color…");
        customColor.Click += (_, _) => ChooseWorkspaceColor(active.Id);
        color.DropDownItems.Add(customColor);
        manage.DropDownItems.Add(color);

        var icon = new ToolStripMenuItem("Icon");
        foreach (var iconValue in WorkspaceIcons)
        {
            var item = new ToolStripMenuItem(iconValue)
            {
                Checked = string.Equals(active.Icon, iconValue, StringComparison.Ordinal)
            };
            item.Click += (_, _) => SetWorkspaceIcon(active.Id, iconValue);
            icon.DropDownItems.Add(item);
        }
        manage.DropDownItems.Add(icon);
        manage.DropDownItems.Add(new ToolStripSeparator());

        var useCurrent = new ToolStripMenuItem("Use current page at startup");
        useCurrent.Click += (_, _) => SetWorkspaceStartupToCurrentPage(active.Id);
        useCurrent.Enabled = TryGetPageDetails(_activeTab, out _, out _, out _);
        manage.DropDownItems.Add(useCurrent);
        var useHome = new ToolStripMenuItem("Use Tugle Home at startup") { Checked = string.IsNullOrWhiteSpace(active.StartupPageUrl) };
        useHome.Click += (_, _) => SetWorkspaceStartupPage(active.Id, null);
        manage.DropDownItems.Add(useHome);

        var preferences = new ToolStripMenuItem("Workspace preferences");
        preferences.DropDownItems.Add("Search engine", null, (_, _) => ShowActionFlyout(ActionFlyoutMode.Search));
        preferences.DropDownItems.Add("Privacy", null, (_, _) => ShowActionFlyout(ActionFlyoutMode.Privacy));
        preferences.DropDownItems.Add("Memory controls", null, (_, _) => ShowActionFlyout(ActionFlyoutMode.Performance));
        manage.DropDownItems.Add(preferences);
        _workspaceMenu.Items.Add(manage);

        var rules = new ToolStripMenuItem("Auto-group sites…");
        var activeRules = active.Rules.Where(rule => rule.Enabled).OrderBy(rule => rule.HostPattern).ToArray();
        if (activeRules.Length == 0)
        {
            rules.DropDownItems.Add(new ToolStripMenuItem("Use a tab’s Groups menu to add one") { Enabled = false });
        }
        else
        {
            foreach (var rule in activeRules)
            {
                var ruleCopy = rule;
                var target = _settings.TabGroups.FirstOrDefault(group => group.Id == ruleCopy.TargetGroupId);
                var item = new ToolStripMenuItem($"{ruleCopy.HostPattern} → {target?.Name ?? "Missing group"}")
                {
                    ToolTipText = "Click to remove this rule"
                };
                item.Click += (_, _) =>
                {
                    active.Rules.Remove(ruleCopy);
                    SaveSettings();
                };
                rules.DropDownItems.Add(item);
            }
        }
        var routing = new ToolStripMenuItem("Always open sites in…");
        PopulateWorkspaceRoutingMenu(routing);
        var advanced = new ToolStripMenuItem("More workspace settings");
        advanced.DropDownItems.Add(rules);
        advanced.DropDownItems.Add(routing);
        advanced.DropDownItems.Add(new ToolStripSeparator());
        var exportItem = new ToolStripMenuItem("Export workspaces…");
        exportItem.Click += (_, _) => ExportWorkspaceBackup();
        var importItem = new ToolStripMenuItem("Import workspace backup…");
        importItem.Click += async (_, _) => await ImportWorkspaceBackupAsync();
        advanced.DropDownItems.Add(exportItem);
        advanced.DropDownItems.Add(importItem);
        _workspaceMenu.Items.Add(advanced);
    }

    private void PopulateWorkspaceRoutingMenu(ToolStripMenuItem menu)
    {
        menu.DropDownItems.Clear();
        if (_isPrivate) return;

        var host = GetTabHost(_activeTab);
        if (host is not null)
        {
            var currentSite = new ToolStripMenuItem($"Always open {host} in");
            var existing = _settings.WorkspaceRoutes.FirstOrDefault(route =>
                string.Equals(route.HostPattern, host, StringComparison.OrdinalIgnoreCase));
            foreach (var workspace in _settings.Workspaces)
            {
                var targetWorkspace = workspace;
                var workspaceItem = new ToolStripMenuItem($"{targetWorkspace.Icon}  {targetWorkspace.Name}")
                {
                    Checked = existing?.TargetWorkspaceId == targetWorkspace.Id && existing.TargetGroupId is null
                };
                var groups = _settings.TabGroups.Where(group => group.WorkspaceId == targetWorkspace.Id)
                    .OrderBy(group => group.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
                if (groups.Length == 0)
                    workspaceItem.Click += (_, _) => ToggleWorkspaceRoute(host, targetWorkspace.Id, null);
                else
                {
                    var ungrouped = new ToolStripMenuItem("Workspace only")
                    {
                        Checked = workspaceItem.Checked
                    };
                    ungrouped.Click += (_, _) => ToggleWorkspaceRoute(host, targetWorkspace.Id, null);
                    workspaceItem.DropDownItems.Add(ungrouped);
                    workspaceItem.DropDownItems.Add(new ToolStripSeparator());
                }

                foreach (var group in groups)
                {
                    var targetGroup = group;
                    var groupItem = new ToolStripMenuItem($"{targetGroup.Icon}  {targetGroup.Name}")
                    {
                        Checked = existing?.TargetWorkspaceId == targetWorkspace.Id && existing.TargetGroupId == targetGroup.Id
                    };
                    groupItem.Click += (_, _) => ToggleWorkspaceRoute(host, targetWorkspace.Id, targetGroup.Id);
                    workspaceItem.DropDownItems.Add(groupItem);
                }

                currentSite.DropDownItems.Add(workspaceItem);
            }
            menu.DropDownItems.Add(currentSite);
        }
        else
        {
            menu.DropDownItems.Add(new ToolStripMenuItem("Open a website to create a route") { Enabled = false });
        }

        var routes = _settings.WorkspaceRoutes.Where(route => route.Enabled)
            .OrderBy(route => route.HostPattern, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (routes.Length == 0) return;

        menu.DropDownItems.Add(new ToolStripSeparator());
        foreach (var route in routes)
        {
            var routeCopy = route;
            var item = new ToolStripMenuItem($"{routeCopy.HostPattern} → {DescribeWorkspaceRoute(routeCopy)}");
            var remove = new ToolStripMenuItem("Remove route");
            remove.Click += (_, _) =>
            {
                _settings.WorkspaceRoutes.Remove(routeCopy);
                SaveSettings();
            };
            item.DropDownItems.Add(remove);
            menu.DropDownItems.Add(item);
        }
    }

    private string DescribeWorkspaceRoute(TugleWorkspaceRoute route)
    {
        var workspace = GetWorkspace(route.TargetWorkspaceId);
        var group = route.TargetGroupId is { } groupId
            ? _settings.TabGroups.FirstOrDefault(item => item.Id == groupId && item.WorkspaceId == route.TargetWorkspaceId)
            : null;
        return group is null
            ? workspace?.Name ?? "Missing workspace"
            : $"{workspace?.Name ?? "Workspace"} / {group.Name}";
    }

    private async Task SwitchWorkspaceAsync(Guid workspaceId)
    {
        if (_isPrivate || GetWorkspace(workspaceId) is null || workspaceId == ActiveWorkspaceId) return;
        if (HasSplitView) CloseSplitView(revealActive: false);

        var previous = _settings.ActiveWorkspace;
        if (_activeTab is { IsClosing: false } current && current.WorkspaceId == previous.Id)
            previous.LastActiveTabId = current.Id;
        _settings.CaptureActiveWorkspace();
        _settings.ActiveWorkspaceId = workspaceId;
        _settings.ApplyActiveWorkspace();
        _tabScrollOffset = 0;
        ClearTabSelection();
        UpdateWorkspaceSwitcher();
        await ApplyActiveWorkspaceTrackingPreventionAsync();

        var activeWorkspace = _settings.ActiveWorkspace;
        var tab = activeWorkspace.LastActiveTabId is { } rememberedId
            ? _tabs.FirstOrDefault(item => item.WorkspaceId == workspaceId && item.Id == rememberedId && !item.IsClosing)
            : null;
        tab ??= _tabs.FirstOrDefault(item => item.WorkspaceId == workspaceId && !item.IsClosing);
        if (tab is not null && GetTabGroup(tab) is { IsCollapsed: true } group)
            tab = GetCollapsedGroupRepresentative(group.Id) ?? tab;
        if (tab is null)
            await OpenWorkspaceStartupTabAsync();
        else
            await ActivateTabAsync(tab);

        CapturePreviousSession();
        SaveSettings();
    }

    private async Task OpenWorkspaceStartupTabAsync()
    {
        var workspace = _isPrivate ? null : _settings.ActiveWorkspace;
        var destination = workspace?.StartupPageUrl;
        var tab = await OpenNewTabAsync(
            showHome: false,
            activate: false,
            tabWorkspaceId: workspace?.Id);
        if (tab?.View.CoreWebView2 is null) return;

        if (string.IsNullOrWhiteSpace(destination))
            ShowHome(tab);
        else
        {
            tab.InitialNavigationTarget = destination;
            tab.View.CoreWebView2.Navigate(destination);
        }
        await ActivateTabAsync(tab);
    }

    private async Task ApplyActiveWorkspaceTrackingPreventionAsync()
    {
        if (_isPrivate) return;
        var profile = _tabs.Select(tab => tab.View.CoreWebView2?.Profile).FirstOrDefault(item => item is not null);
        if (profile is null) return;
        try
        {
            profile.PreferredTrackingPreventionLevel = Enum.Parse<CoreWebView2TrackingPreventionLevel>(_settings.TrackingPrevention);
        }
        catch
        {
            await Task.CompletedTask;
        }
    }

    private void PromptCreateWorkspace(WorkspaceTemplate? template = null)
    {
        if (_isPrivate || _settings.Workspaces.Count >= 12) return;
        var suggestedName = template?.Name ?? $"Workspace {_settings.Workspaces.Count + 1}";
        using var dialog = new WorkspaceNameDialog("New workspace", suggestedName, _theme);
        if (dialog.ShowDialog(this) != DialogResult.OK || !TryNormalizeWorkspaceName(dialog.WorkspaceName, out var name)) return;
        if (HasWorkspaceName(name))
        {
            MessageBox.Show(this, "That workspace name is already in use.", "Workspace", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var color = template?.Color ?? WorkspaceColors[_settings.Workspaces.Count % WorkspaceColors.Length].Color;
        var icon = template?.Icon ?? WorkspaceIcons[_settings.Workspaces.Count % WorkspaceIcons.Length];
        var workspace = new TugleWorkspace
        {
            Id = Guid.NewGuid(),
            Name = name,
            Color = TugleSettings.ToHex(color),
            Icon = icon,
            SearchEngine = _settings.SearchEngine,
            TrackingPrevention = _settings.TrackingPrevention,
            LowMemoryMode = _settings.LowMemoryMode,
            DiscardInactiveTabs = _settings.DiscardInactiveTabs,
            UseWorkspaceMemorySettings = true
        };
        _settings.Workspaces.Add(workspace);
        if (template is not null)
        {
            foreach (var groupName in template.GroupNames.Take(12))
            {
                if (_settings.TabGroups.Count >= 48) break;
                var groupIndex = _settings.TabGroups.Count(group => group.WorkspaceId == workspace.Id);
                _settings.TabGroups.Add(new TugleTabGroup
                {
                    Id = Guid.NewGuid(),
                    WorkspaceId = workspace.Id,
                    Name = groupName,
                    Color = TugleSettings.ToHex(WorkspaceColors[groupIndex % WorkspaceColors.Length].Color),
                    Icon = WorkspaceIcons[groupIndex % WorkspaceIcons.Length]
                });
            }
        }
        SaveSettings();
        _ = SwitchWorkspaceAsync(workspace.Id);
    }

    private bool RenameWorkspace(Guid workspaceId)
    {
        var workspace = GetWorkspace(workspaceId);
        if (workspace is null) return false;
        using var dialog = new WorkspaceNameDialog("Rename workspace", workspace.Name, _theme);
        if (dialog.ShowDialog(this) != DialogResult.OK || !TryNormalizeWorkspaceName(dialog.WorkspaceName, out var name)) return false;
        if (HasWorkspaceName(name, workspaceId))
        {
            MessageBox.Show(this, "That workspace name is already in use.", "Workspace", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return false;
        }
        workspace.Name = name;
        UpdateWorkspaceSwitcher();
        SaveSettings();
        return true;
    }

    private bool SetWorkspaceColor(Guid workspaceId, string color)
    {
        var workspace = GetWorkspace(workspaceId);
        if (workspace is null || !TryParseTabGroupColor(color, out var parsed)) return false;
        workspace.Color = TugleSettings.ToHex(parsed);
        UpdateWorkspaceSwitcher();
        SaveSettings();
        return true;
    }

    private void ChooseWorkspaceColor(Guid workspaceId)
    {
        var workspace = GetWorkspace(workspaceId);
        if (workspace is null) return;
        using var dialog = new ColorPickerDialog(TugleSettings.FromHex(workspace.Color, _accent), "Workspace color", _theme);
        if (dialog.ShowDialog(this) == DialogResult.OK)
            SetWorkspaceColor(workspaceId, TugleSettings.ToHex(dialog.SelectedColor));
    }

    private bool SetWorkspaceIcon(Guid workspaceId, string icon)
    {
        var workspace = GetWorkspace(workspaceId);
        if (workspace is null || !WorkspaceIcons.Contains(icon, StringComparer.Ordinal)) return false;
        workspace.Icon = icon;
        UpdateWorkspaceSwitcher();
        SaveSettings();
        return true;
    }

    private void SetWorkspaceStartupToCurrentPage(Guid workspaceId)
    {
        if (!TryGetPageDetails(_activeTab, out var url, out _, out _)) return;
        SetWorkspaceStartupPage(workspaceId, url);
    }

    private bool SetWorkspaceStartupPage(Guid workspaceId, string? url)
    {
        var workspace = GetWorkspace(workspaceId);
        if (workspace is null) return false;
        if (!string.IsNullOrWhiteSpace(url) &&
            (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https" or "file")))
            return false;
        workspace.StartupPageUrl = string.IsNullOrWhiteSpace(url) ? null : url.Trim();
        SaveSettings();
        return true;
    }

    private static bool TryNormalizeWorkspaceName(string? value, out string name)
    {
        name = value?.Trim() ?? string.Empty;
        if (name.Length == 0) return false;
        if (name.Length > 40) name = name[..40];
        return true;
    }

    private bool HasWorkspaceName(string? value, Guid? exceptWorkspaceId = null) =>
        TryNormalizeWorkspaceName(value, out var name) && _settings.Workspaces.Any(workspace =>
            workspace.Id != exceptWorkspaceId && string.Equals(workspace.Name, name, StringComparison.CurrentCultureIgnoreCase));

    private string GetWorkspaceMemorySummary(Guid workspaceId)
    {
        var tabs = _tabs.Where(tab => !tab.IsClosing && tab.WorkspaceId == workspaceId).ToArray();
        var suspended = tabs.Count(tab => tab.IsSuspended);
        var discarded = tabs.Count(tab => tab.IsDiscarded);
        return tabs.Length == 0
            ? "No open tabs"
            : $"{tabs.Length} tab{(tabs.Length == 1 ? string.Empty : "s")} · savings: {suspended} paused, {discarded} unloaded";
    }

    private int GetWorkspaceTabCount(Guid workspaceId) =>
        _tabs.Count(tab => !tab.IsClosing && tab.WorkspaceId == workspaceId);

    private void ExportWorkspaceBackup()
    {
        if (_isPrivate) return;
        CapturePreviousSession();
        var backup = new TugleWorkspaceBackup
        {
            FormatVersion = 2,
            Workspaces = _settings.Workspaces.Select(workspace => new TugleWorkspace
            {
                Id = workspace.Id,
                Name = workspace.Name,
                Color = workspace.Color,
                Icon = workspace.Icon,
                StartupPageUrl = workspace.StartupPageUrl,
                SearchEngine = workspace.SearchEngine,
                TrackingPrevention = workspace.TrackingPrevention,
                UseWorkspaceMemorySettings = workspace.UseWorkspaceMemorySettings,
                LowMemoryMode = workspace.LowMemoryMode,
                DiscardInactiveTabs = workspace.DiscardInactiveTabs,
                LastActiveTabId = workspace.LastActiveTabId,
                Rules = workspace.Rules.Select(rule => new TugleWorkspaceRule
                {
                    Id = rule.Id,
                    WorkspaceId = rule.WorkspaceId,
                    TargetGroupId = rule.TargetGroupId,
                    HostPattern = rule.HostPattern,
                    Enabled = rule.Enabled
                }).ToList()
            }).ToList(),
            TabGroups = _settings.TabGroups.Select(group => new TugleTabGroup
            {
                Id = group.Id,
                WorkspaceId = group.WorkspaceId,
                Name = group.Name,
                Color = group.Color,
                Icon = group.Icon,
                IsCollapsed = group.IsCollapsed
            }).ToList(),
            Routes = _settings.WorkspaceRoutes.Select(route => new TugleWorkspaceRoute
            {
                Id = route.Id,
                HostPattern = route.HostPattern,
                TargetWorkspaceId = route.TargetWorkspaceId,
                TargetGroupId = route.TargetGroupId,
                Enabled = route.Enabled
            }).ToList(),
            Tabs = _settings.PreviousSessionTabs.Select(tab => new TugleSessionTab
            {
                Id = tab.Id,
                WorkspaceId = tab.WorkspaceId,
                Url = tab.Url,
                IsHome = tab.IsHome,
                IsActive = tab.IsActive,
                IsPinned = tab.IsPinned,
                GroupId = tab.GroupId,
                Title = tab.Title
            }).ToList()
        };
        using var dialog = new SaveFileDialog
        {
            Title = "Export workspaces",
            Filter = "Tugle workspace backup (*.tugle-workspace.json)|*.tugle-workspace.json|JSON files (*.json)|*.json",
            FileName = "Tugle-workspaces.tugle-workspace.json",
            AddExtension = true,
            DefaultExt = "json",
            OverwritePrompt = true
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            File.WriteAllText(dialog.FileName, JsonSerializer.Serialize(backup, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Tugle could not export that backup.\n\n" + ex.Message, "Export workspaces", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    private async Task ImportWorkspaceBackupAsync()
    {
        if (_isPrivate || _settings.Workspaces.Count >= 12) return;
        using var dialog = new OpenFileDialog
        {
            Title = "Import workspace backup",
            Filter = "Tugle workspace backup (*.tugle-workspace.json;*.json)|*.tugle-workspace.json;*.json|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        TugleWorkspaceBackup? backup;
        try
        {
            backup = JsonSerializer.Deserialize<TugleWorkspaceBackup>(File.ReadAllText(dialog.FileName), new JsonSerializerOptions(JsonSerializerDefaults.Web));
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "That file is not a readable Tugle workspace backup.\n\n" + ex.Message, "Import workspaces", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (backup is null || backup.FormatVersion is not (1 or 2))
        {
            MessageBox.Show(this, "This workspace backup uses an unsupported format.", "Import workspaces", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var importedWorkspaceIds = await ImportWorkspaceBackupAsync(backup);
        if (importedWorkspaceIds.Count == 0)
        {
            MessageBox.Show(this, "No usable workspaces were found in that backup.", "Import workspaces", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        await SwitchWorkspaceAsync(importedWorkspaceIds[0]);
        CapturePreviousSession();
        SaveSettings();
        MessageBox.Show(this, $"Imported {importedWorkspaceIds.Count} workspace{(importedWorkspaceIds.Count == 1 ? string.Empty : "s")}.", "Import workspaces", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private async Task<IReadOnlyList<Guid>> ImportWorkspaceBackupAsync(TugleWorkspaceBackup backup)
    {
        var workspaceMap = new Dictionary<Guid, Guid>();
        var imported = new List<Guid>();
        foreach (var source in (backup.Workspaces ?? []).Take(12))
        {
            if (_settings.Workspaces.Count >= 12 || source.Id == Guid.Empty || !TryNormalizeWorkspaceName(source.Name, out var name) ||
                !TryParseTabGroupColor(source.Color, out var color) || workspaceMap.ContainsKey(source.Id)) continue;
            var uniqueName = name;
            var suffix = 2;
            while (HasWorkspaceName(uniqueName)) uniqueName = $"{name} ({suffix++})";
            var workspace = new TugleWorkspace
            {
                Id = Guid.NewGuid(),
                Name = uniqueName,
                Color = TugleSettings.ToHex(color),
                Icon = WorkspaceIcons.Contains(source.Icon, StringComparer.Ordinal) ? source.Icon : "●",
                StartupPageUrl = IsWorkspaceRestorableAddress(source.StartupPageUrl) ? source.StartupPageUrl!.Trim() : null,
                SearchEngine = SearchProvider.Normalize(source.SearchEngine),
                TrackingPrevention = source.TrackingPrevention is "Basic" or "Balanced" or "Strict" ? source.TrackingPrevention : "Balanced",
                UseWorkspaceMemorySettings = source.UseWorkspaceMemorySettings,
                LowMemoryMode = source.LowMemoryMode,
                DiscardInactiveTabs = source.DiscardInactiveTabs
            };
            _settings.Workspaces.Add(workspace);
            workspaceMap[source.Id] = workspace.Id;
            imported.Add(workspace.Id);
        }

        var groupMap = new Dictionary<Guid, Guid>();
        foreach (var source in (backup.TabGroups ?? []).Take(48))
        {
            if (source.Id == Guid.Empty || !workspaceMap.TryGetValue(source.WorkspaceId, out var workspaceId) ||
                groupMap.ContainsKey(source.Id) || !TryNormalizeTabGroupName(source.Name, out var name) ||
                !TryParseTabGroupColor(source.Color, out var color) ||
                _settings.TabGroups.Count >= 48 || _settings.TabGroups.Count(group => group.WorkspaceId == workspaceId) >= 12) continue;
            var uniqueName = name;
            var suffix = 2;
            while (HasTabGroupName(uniqueName, workspaceId: workspaceId)) uniqueName = $"{name} ({suffix++})";
            var group = new TugleTabGroup
            {
                Id = Guid.NewGuid(),
                WorkspaceId = workspaceId,
                Name = uniqueName,
                Color = TugleSettings.ToHex(color),
                Icon = WorkspaceIcons.Contains(source.Icon, StringComparer.Ordinal) ? source.Icon : "●",
                IsCollapsed = source.IsCollapsed
            };
            _settings.TabGroups.Add(group);
            groupMap[source.Id] = group.Id;
        }

        foreach (var source in backup.Workspaces ?? [])
        {
            if (!workspaceMap.TryGetValue(source.Id, out var workspaceId) || GetWorkspace(workspaceId) is not { } workspace) continue;
            foreach (var rule in (source.Rules ?? []).Take(48))
            {
                if (!groupMap.TryGetValue(rule.TargetGroupId, out var targetGroupId) ||
                    !IsSafeWorkspaceHostPattern(rule.HostPattern)) continue;
                workspace.Rules.Add(new TugleWorkspaceRule
                {
                    Id = Guid.NewGuid(),
                    WorkspaceId = workspaceId,
                    TargetGroupId = targetGroupId,
                    HostPattern = rule.HostPattern.Trim().ToLowerInvariant(),
                    Enabled = rule.Enabled
                });
            }
        }

        foreach (var source in (backup.Routes ?? []).Take(36))
        {
            if (_settings.WorkspaceRoutes.Count >= 36 || !workspaceMap.TryGetValue(source.TargetWorkspaceId, out var targetWorkspaceId) ||
                !IsSafeWorkspaceHostPattern(source.HostPattern) ||
                _settings.WorkspaceRoutes.Any(route => string.Equals(route.HostPattern, source.HostPattern, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var targetGroupId = source.TargetGroupId is { } originalGroup && groupMap.TryGetValue(originalGroup, out var mappedGroup)
                ? mappedGroup
                : (Guid?)null;
            _settings.WorkspaceRoutes.Add(new TugleWorkspaceRoute
            {
                Id = Guid.NewGuid(),
                HostPattern = source.HostPattern.Trim().ToLowerInvariant(),
                TargetWorkspaceId = targetWorkspaceId,
                TargetGroupId = targetGroupId,
                Enabled = source.Enabled
            });
        }

        foreach (var source in (backup.Tabs ?? []).Take(80))
        {
            if (!workspaceMap.TryGetValue(source.WorkspaceId, out var workspaceId) ||
                (!source.IsHome && !IsWorkspaceRestorableAddress(source.Url))) continue;
            var groupId = source.GroupId is { } originalGroup && groupMap.TryGetValue(originalGroup, out var mappedGroup)
                ? mappedGroup
                : (Guid?)null;
            var tab = await OpenNewTabAsync(
                showHome: false,
                activate: false,
                deferInitialization: true,
                workspaceId: groupId,
                tabWorkspaceId: workspaceId);
            if (tab is null) continue;
            tab.Id = source.Id == Guid.Empty || _tabs.Any(existing => !ReferenceEquals(existing, tab) && existing.Id == source.Id)
                ? Guid.NewGuid()
                : source.Id;
            tab.Title = string.IsNullOrWhiteSpace(source.Title) ? tab.Title : source.Title.Trim()[..Math.Min(120, source.Title.Trim().Length)];
            tab.IsPinned = source.IsPinned;
            tab.IsHome = source.IsHome;
            tab.DeferredNavigation = true;
            tab.InitialNavigationTarget = source.Url;
            UpdateTabButton(tab);
        }
        return imported;
    }

    private static bool IsWorkspaceRestorableAddress(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" or "file";

    private static bool IsSafeWorkspaceHostPattern(string? value)
    {
        var pattern = value?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(pattern) || pattern.Contains('/') || pattern.Contains(':') || pattern.Contains('*', StringComparison.Ordinal) && !pattern.StartsWith("*.", StringComparison.Ordinal))
            return false;
        var host = pattern.StartsWith("*.", StringComparison.Ordinal) ? pattern[2..] : pattern;
        if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)) return !pattern.StartsWith("*.", StringComparison.Ordinal);
        var type = Uri.CheckHostName(host);
        return (type is UriHostNameType.Dns or UriHostNameType.IPv4 or UriHostNameType.IPv6) &&
            (!pattern.StartsWith("*.", StringComparison.Ordinal) || type == UriHostNameType.Dns && host.Contains('.'));
    }

    private async Task DuplicateTabAsync(BrowserTab source)
    {
        if (source.IsClosing || !_tabs.Contains(source)) return;

        var sourceUrl = source.DeferredNavigation ? source.InitialNavigationTarget : source.View.CoreWebView2?.Source;
        Uri? destination = null;
        if (!source.IsHome && Uri.TryCreate(sourceUrl, UriKind.Absolute, out var parsed) &&
            parsed.Scheme is "http" or "https" or "file")
            destination = parsed;
        var showHome = destination is null;
        var sourceIndex = _tabs.IndexOf(source);
        var duplicate = await OpenNewTabAsync(
            showHome,
            workspaceId: _isPrivate ? null : source.GroupId,
            tabWorkspaceId: _isPrivate ? null : source.WorkspaceId);
        if (duplicate?.View.CoreWebView2 is null) return;

        // Keep the duplicate beside its source, then let the normal navigation
        // lifecycle reveal it once the copied page is ready.
        _tabs.Remove(duplicate);
        _tabs.Insert(Math.Min(sourceIndex + 1, _tabs.Count), duplicate);
        OrderPinnedTabs();

        if (!showHome && destination is not null)
        {
            duplicate.InitialNavigationTarget = destination.AbsoluteUri;
            duplicate.View.CoreWebView2.Navigate(destination.AbsoluteUri);
        }
    }

    private void ToggleTabPinned(BrowserTab tab)
    {
        if (tab.IsClosing || !_tabs.Contains(tab)) return;
        tab.IsPinned = !tab.IsPinned;
        OrderPinnedTabs();
        CapturePreviousSession();
        SaveSettings();
        UpdateTabButton(tab);
    }

    private void OrderPinnedTabs()
    {
        Guid[] workspaceOrder = _isPrivate
            ? [Guid.Empty]
            : _settings.Workspaces.Select(workspace => workspace.Id)
                .Concat(_tabs.Where(tab => GetWorkspace(tab.WorkspaceId) is null).Select(tab => tab.WorkspaceId))
                .Distinct()
                .ToArray();
        var ordered = workspaceOrder
            .SelectMany(workspaceId => _tabs.Where(tab => tab.WorkspaceId == workspaceId && tab.IsPinned)
                .Concat(_tabs.Where(tab => tab.WorkspaceId == workspaceId && !tab.IsPinned)))
            .ToArray();
        if (ordered.SequenceEqual(_tabs))
        {
            LayoutTabs();
            return;
        }

        _tabs.Clear();
        _tabs.AddRange(ordered);
        LayoutTabs();
    }

    private async Task SavePageAsPdfAsync(BrowserTab? pageTab = null)
    {
        var tab = pageTab ?? _activeTab;
        if (tab is null) return;
        var core = tab.View.CoreWebView2;
        if (core is null) return;

        var safeTitle = string.Concat((tab.Title ?? "page")
            .Select(character => Path.GetInvalidFileNameChars().Contains(character) ? '_' : character));
        using var dialog = new SaveFileDialog
        {
            Title = "Save page as PDF",
            Filter = "PDF files (*.pdf)|*.pdf",
            FileName = string.IsNullOrWhiteSpace(safeTitle) ? "page.pdf" : safeTitle + ".pdf",
            AddExtension = true,
            DefaultExt = "pdf",
            OverwritePrompt = true
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            var saved = await core.PrintToPdfAsync(dialog.FileName);
            if (!saved)
                MessageBox.Show(this, "Tugle could not save this page as a PDF.", "Save page as PDF", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Tugle could not save this page as a PDF.\n\n" + ex.Message, "Save page as PDF", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    private void ToggleTabMute(BrowserTab tab)
    {
        if (tab.IsClosing || tab.View.CoreWebView2 is null) return;

        try
        {
            tab.View.CoreWebView2.IsMuted = !tab.View.CoreWebView2.IsMuted;
            tab.IsMuted = tab.View.CoreWebView2.IsMuted;
            UpdateTabButton(tab);
        }
        catch
        {
            // The WebView2 runtime may not expose audio controls on older builds.
        }
    }

    private void ShowHistoryMenu()
    {
        ShowActionFlyout(ActionFlyoutMode.History);
    }

    private void ShowLibraryMenu()
    {
        ShowActionFlyout(ActionFlyoutMode.Library);
    }

    private void ShowPrivacyMenu()
    {
        ShowActionFlyout(ActionFlyoutMode.Privacy);
    }

    private void ShowSearchMenu()
    {
        ShowActionFlyout(ActionFlyoutMode.Search);
    }

    private void RefreshHistoryFlyoutIfVisible()
    {
        if (_actionFlyout.Visible && _actionFlyoutMode == ActionFlyoutMode.History)
            PopulateActionFlyout();
    }

    private void RebuildHistoryMenu()
    {
        _historyMenu.Items.Clear();

        if (_history.RecentVisits.Count == 0)
        {
            _historyMenu.Items.Add(new ToolStripMenuItem("No history yet") { Enabled = false });
            return;
        }

        foreach (var visit in _history.RecentVisits.Take(12))
        {
            var label = string.IsNullOrWhiteSpace(visit.Title) ? visit.Url : visit.Title;
            var item = new ToolStripMenuItem(label)
            {
                ToolTipText = visit.Url
            };
            item.Click += (_, _) => NavigateToHistoryEntry(visit.Url);
            _historyMenu.Items.Add(item);
        }
    }

    private void NavigateToHistoryEntry(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var destination) ||
            (destination.Scheme != Uri.UriSchemeHttp && destination.Scheme != Uri.UriSchemeHttps)) return;
        _ = NavigateActiveTabAsync(destination.ToString());
    }

    private void NavigateToSearchEntry(string query)
    {
        if (ActiveWebView?.CoreWebView2 is null || string.IsNullOrWhiteSpace(query)) return;
        ActiveWebView.CoreWebView2.Navigate(BuildSearchUrl(query));
    }

    private void ShowGuiScaleMenu()
    {
        ShowActionFlyout(ActionFlyoutMode.GuiScale);
    }

    private void ShowThemeMenu()
    {
        ShowActionFlyout(ActionFlyoutMode.Theme);
    }

    private void ShowAccountsMenu()
    {
        ShowActionFlyout(ActionFlyoutMode.Accounts);
    }

    private void ShowBrowserSettings() => ShowActionFlyout(ActionFlyoutMode.BrowserSettings);

    private void ShowPerformanceMenu() => ShowActionFlyout(ActionFlyoutMode.Performance);

    private void ShowGoogleAccount()
    {
        ShowActionFlyout(ActionFlyoutMode.GoogleAccount);
        var core = _tabs.FirstOrDefault(tab => !tab.IsClosing && tab.View.CoreWebView2 is not null)?.View.CoreWebView2;
        if (core is not null) _ = RefreshGoogleConnectionStateAsync(core);
    }

    private async Task CheckForUpdatesAsync(bool showNoUpdateMessage)
    {
        if (_checkingForUpdates || IsDisposed) return;
        _checkingForUpdates = true;
        try
        {
            var release = await UpdateService.GetAvailableReleaseAsync();
            if (IsDisposed) return;
            if (release is null)
            {
                if (showNoUpdateMessage)
                    MessageBox.Show(this, "You already have the latest version.", "Tugle", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (!IsInstalledBuild || release.InstallerDownloadUrl is null)
            {
                var manualDownloadUrl = release.PortableDownloadUrl ?? release.InstallerDownloadUrl;
                var result = MessageBox.Show(
                    this,
                    $"Tugle {release.Label} is available. Open the download page?",
                    "Update available",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Information);
                if (result == DialogResult.Yes && manualDownloadUrl is not null)
                    Process.Start(new ProcessStartInfo(manualDownloadUrl) { UseShellExecute = true });
                return;
            }

            var installerPath = await UpdateService.DownloadInstallerAsync(release);
            if (IsDisposed) return;
            if (installerPath is null)
            {
                if (showNoUpdateMessage)
                    MessageBox.Show(this, "Tugle found an update but could not download it. Try again later.", "Tugle update", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var installNow = MessageBox.Show(
                this,
                $"Tugle {release.Label} is ready to install. Restart Tugle now?",
                "Update ready",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Information,
                MessageBoxDefaultButton.Button1);
            if (installNow == DialogResult.Yes)
                RestartThroughInstaller(installerPath);
        }
        catch
        {
            if (showNoUpdateMessage && !IsDisposed)
                MessageBox.Show(this, "Could not check for updates right now.", "Tugle", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        finally { _checkingForUpdates = false; }
    }

    private static bool IsInstalledBuild =>
        File.Exists(Path.Combine(AppContext.BaseDirectory, "unins000.exe")) ||
        IsUnderKnownInstallDirectory(AppContext.BaseDirectory, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)) ||
        IsUnderKnownInstallDirectory(AppContext.BaseDirectory, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs"));

    private static bool IsUnderKnownInstallDirectory(string path, string root)
    {
        if (string.IsNullOrWhiteSpace(root)) return false;
        var fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase);
    }

    private void RestartThroughInstaller(string installerPath)
    {
        try
        {
            var process = Process.Start(new ProcessStartInfo
            {
                FileName = installerPath,
                Arguments = "/SP- /CLOSEAPPLICATIONS /RESTARTAPPLICATIONS",
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(installerPath) ?? AppContext.BaseDirectory
            });
            if (process is null) return;

            foreach (var form in Application.OpenForms.Cast<Form>().ToArray())
                form.Close();
        }
        catch
        {
            MessageBox.Show(this, "Tugle downloaded the update but could not start the installer.", "Tugle update", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    private async void ConnectGoogleAccount()
    {
        await ConnectGoogleAccountAsync();
    }

    private async Task ConnectGoogleAccountAsync()
    {
        if (_openingGoogleAccount) return;
        _openingGoogleAccount = true;
        HideActionFlyout();
        try
        {
            var source = _googleAccountTab is { IsClosing: false } && !_googleAccountTab.View.IsDisposed
                ? _googleAccountTab.View.CoreWebView2?.Source : null;
            var reusable = Uri.TryCreate(source, UriKind.Absolute, out var uri) && uri.Scheme == "https" &&
                uri.Host is "accounts.google.com" or "myaccount.google.com";
            // Do not replace a page if the user has since navigated this tab elsewhere.
            if (_googleAccountTab is null || _googleAccountTab.IsClosing || !_tabs.Contains(_googleAccountTab) || !reusable)
                _googleAccountTab = await OpenNewTabAsync(showHome: false);
            else
                await ActivateTabAsync(_googleAccountTab);
            if (_googleAccountTab?.View.CoreWebView2 is null) return;
            _googleSignInBlocked = false;
            var destination = _googleSignedIn == true ? "https://myaccount.google.com/" : GoogleSession.SignInUrl;
            _googleAccountTab.InitialNavigationTarget = destination;
            _googleAccountTab.View.CoreWebView2.Navigate(destination);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Google could not be opened.\n\n" + ex.Message, "Google sign-in", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        finally { _openingGoogleAccount = false; }
    }

    private static bool IsGoogleHost(string? source)
    {
        return GoogleSession.IsGoogleUrl(source);
    }

    private async Task RefreshGoogleConnectionStateAsync(CoreWebView2 core)
    {
        try
        {
            var signedIn = await GoogleSession.IsConnectedAsync(core);
            var blocked = GoogleSession.IsAccountUrl(core.Source) && !signedIn && await GoogleSession.IsSignInBlockedAsync(core);
            if (IsDisposed) return;
            var changed = signedIn != _googleSignedIn || blocked != _googleSignInBlocked;
            _googleSignedIn = signedIn;
            _googleSignInBlocked = blocked;
            if (changed && _actionFlyout.Visible && _actionFlyoutMode == ActionFlyoutMode.GoogleAccount)
                PopulateActionFlyout();
        }
        catch
        {
            // Account detection is best effort and must never block browsing.
        }
    }

    private async void DisconnectGoogleAccount()
    {
        if (MessageBox.Show(this, "Sign out of Google websites in this Tugle profile?", "Sign out of Google", MessageBoxButtons.YesNo,
            MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
        try
        {
            var core = ActiveWebView?.CoreWebView2;
            if (core is not null)
            {
                if (!await GoogleSession.ClearCookiesAsync(core) && !IsDisposed)
                    MessageBox.Show(this, "Google is still signed in. Please try again.", "Google sign-out", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }
        catch
        {
            if (!IsDisposed)
                MessageBox.Show(this, "Google sign-out could not be completed. Please try again.", "Google sign-out", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        if (IsDisposed) return;
        var current = ActiveWebView?.CoreWebView2;
        if (current is not null) await RefreshGoogleConnectionStateAsync(current);
        if (_actionFlyoutMode == ActionFlyoutMode.GoogleAccount) PopulateActionFlyout();
    }

    private void ChooseCustomThemeColor()
    {
        using var dialog = new ColorPickerDialog(DefaultCustomColor, "Custom theme color", _theme);

        if (dialog.ShowDialog(this) == DialogResult.OK)
            SelectTheme(ThemePalette.FromAccent("Custom", "Custom accent color", dialog.SelectedColor));
    }

    private IEnumerable<(string Hex, Color Accent)> SavedCustomThemeColors()
    {
        foreach (var value in _settings.SavedCustomThemeAccents)
        {
            var accent = TugleSettings.FromHex(value, Color.Empty);
            if (accent == Color.Empty) continue;
            yield return (TugleSettings.ToHex(accent), accent);
        }
    }

    private void RememberCustomThemeAccent(Color accent)
    {
        var hex = TugleSettings.ToHex(accent);
        _settings.SavedCustomThemeAccents.RemoveAll(value =>
            string.Equals(value, hex, StringComparison.OrdinalIgnoreCase));
        _settings.SavedCustomThemeAccents.Insert(0, hex);
        if (_settings.SavedCustomThemeAccents.Count > 8)
            _settings.SavedCustomThemeAccents.RemoveRange(8, _settings.SavedCustomThemeAccents.Count - 8);
    }

    private void SelectSavedCustomThemeColor(string hex)
    {
        var accent = TugleSettings.FromHex(hex, DefaultCustomColor);
        SelectTheme(ThemePalette.FromAccent("Custom", "Custom accent color", accent));
    }

    private void ChooseCustomHomeBackground(BrowserTab tab)
    {
        using var dialog = new ColorPickerDialog(_homeBackground, "Background color", _theme);

        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        _homeBackground = dialog.SelectedColor;
        _homeBackgroundMode = "solid";
        _homeBackgroundMediaUrl = null;
        RefreshHomeBackground();
        SaveSettings();
    }

    private void ChooseGradientBackground()
    {
        using var firstDialog = new ColorPickerDialog(DefaultCustomColor, "Gradient start color", _theme);
        if (firstDialog.ShowDialog(this) != DialogResult.OK) return;

        using var secondDialog = new ColorPickerDialog(_theme.Accent, "Gradient end color", _theme);
        if (secondDialog.ShowDialog(this) != DialogResult.OK) return;

        _homeBackground = firstDialog.SelectedColor;
        _homeBackgroundSecondary = secondDialog.SelectedColor;
        _homeBackgroundMode = "gradient";
        _homeBackgroundMediaUrl = null;
        RefreshHomeBackground();
        SaveSettings();
    }

    private void ChooseBackgroundMedia(bool video)
    {
        using var dialog = new OpenFileDialog
        {
            Title = video ? "Choose a background video" : "Choose a background picture",
            Filter = video
                ? "Video files|*.mp4;*.webm;*.ogg;*.mov;*.m4v|All files|*.*"
                : "Picture files|*.png;*.jpg;*.jpeg;*.gif;*.webp;*.bmp|All files|*.*",
            Multiselect = false,
            CheckFileExists = true,
            CheckPathExists = true
        };
        if (dialog.ShowDialog(this) != DialogResult.OK || !File.Exists(dialog.FileName)) return;

        _homeBackgroundMode = video ? "video" : "image";
        _homeBackgroundMediaUrl = new Uri(dialog.FileName).AbsoluteUri;
        RefreshHomeBackground();
        SaveSettings();
    }

    private void ResetHomeBackground()
    {
        _homeBackground = _theme.ContentBackground;
        _homeBackgroundSecondary = _theme.Accent;
        _homeBackgroundMode = "solid";
        _homeBackgroundMediaUrl = null;
        RefreshHomeBackground();
        SaveSettings();
    }

    private void RefreshHomeBackground()
    {
        foreach (var homeTab in _tabs.Where(item => item.IsHome && !item.View.IsDisposed))
            _ = PopulateHomeAsync(homeTab);
    }

    private void RebuildGuiScaleMenu()
    {
        _guiScaleMenu.Items.Clear();
        foreach (var scale in GuiScales)
        {
            var label = $"{(int)Math.Round(scale * 100)}%";
            var item = new ToolStripMenuItem(label)
            {
                Checked = Math.Abs(scale - _guiScale) < 0.01f
            };
            item.Click += (_, _) => SetGuiScale(scale);
            _guiScaleMenu.Items.Add(item);
        }
    }

    private void ShowToolbarMenu(ContextMenuStrip menu, Control anchor, int minimumWidth)
    {
        var menuWidth = Math.Max(minimumWidth, menu.PreferredSize.Width);
        var x = -Math.Max(0, menuWidth - anchor.Width);
        menu.Show(anchor, new Point(x, anchor.Height));
    }

    private void ShowActionFlyout(ActionFlyoutMode mode)
    {
        if (_actionFlyout.Visible && _actionFlyoutMode == mode)
        {
            HideActionFlyout();
            return;
        }

        _actionFlyoutMode = mode;
        if (mode == ActionFlyoutMode.Library) { _bookmarkQuery = string.Empty; _bookmarkLimit = 30; _editingBookmark = null; }
        _actionFlyoutItems.AutoScrollPosition = Point.Empty;
        LayoutActionFlyout();
        PopulateActionFlyout();
        _actionFlyout.Visible = true;
        _actionFlyout.BringToFront();
    }

    private void HideActionFlyout()
    {
        _actionFlyout.Visible = false;
        _actionFlyoutMode = ActionFlyoutMode.None;
    }

    private void PopulateActionFlyout()
    {
        var scrollOffset = -_actionFlyoutItems.AutoScrollPosition.Y;
        _actionFlyoutItems.SuspendLayout();
        try
        {
        while (_actionFlyoutItems.Controls.Count > 0)
        {
            var control = _actionFlyoutItems.Controls[0];
            _actionFlyoutItems.Controls.RemoveAt(0);
            control.Dispose();
        }

        switch (_actionFlyoutMode)
        {
            case ActionFlyoutMode.History:
                _actionFlyoutTitle.Text = "History";
                var visits = _history.RecentVisits.Take(12).ToArray();
                var searches = _history.SearchSuggestions.Take(10).ToArray();
                if (visits.Length == 0 && searches.Length == 0)
                {
                    AddActionFlyoutInfo("No history yet");
                    break;
                }

                if (visits.Length > 0)
                {
                    AddActionFlyoutSectionLabel("RECENTLY VISITED", $"{visits.Length} pages");
                    foreach (var visit in visits)
                        AddHistoryFlyoutItem(visit, () => NavigateToHistoryEntry(visit.Url));
                }

                if (searches.Length > 0)
                {
                    AddActionFlyoutSectionLabel("RECENT SEARCHES", $"{searches.Length} queries");
                    foreach (var query in searches)
                        AddActionFlyoutButton($"Search  “{query}”", "Run this search again", () => NavigateToSearchEntry(query));
                }

                AddActionFlyoutButton(
                    "Delete browsing history",
                    "Delete recent pages, most used sites, and search suggestions",
                    ClearHistory,
                    prominent: true);
                break;

            case ActionFlyoutMode.Library:
                _actionFlyoutTitle.Text = "Bookmarks";
                if (_isPrivate)
                {
                    AddActionFlyoutInfo("Bookmark changes are disabled in a private window.");
                    break;
                }
                PopulateBookmarks();
                break;

            case ActionFlyoutMode.Privacy:
                _actionFlyoutTitle.Text = _isPrivate ? "Private browsing" : $"Privacy · {_settings.ActiveWorkspace.Name}";
                if (_isPrivate)
                {
                    AddActionFlyoutInfo("Tabs and browsing history aren’t saved. Downloads stay on your device.");
                    break;
                }

                AddActionFlyoutButton("‹  Browser settings", "Back", ShowBrowserSettings);
                AddActionFlyoutButton("New private window", "Ctrl+Shift+N", OpenPrivateWindow);
                AddActionFlyoutSectionLabel("Tracking prevention", "");
                AddCompactActions(new[] { "Basic", "Balanced", "Strict" }.Select(level =>
                    (level, (Action)(() => _ = SetTrackingPreventionAsync(level)), _settings.TrackingPrevention == level, false)).ToArray());
                AddActionFlyoutInfo(_settings.TrackingPrevention == "Strict"
                    ? "Blocks more trackers. Some sites may not work as expected."
                    : _settings.TrackingPrevention == "Basic" ? "Allows most trackers for fewer site interruptions." : "Blocks common trackers while keeping most sites working.");
                AddActionFlyoutSectionLabel("Protection", "");
                var adBlockControl = AddActionFlyoutButton(
                    "Ad blocker",
                    _settings.UBlockEnabled && !_uBlockEnabled
                        ? "The packaged extension is unavailable, so Tugle is using its conservative host fallback"
                        : "Enable or disable Tugle’s packaged ad blocker",
                    () => _ = SetProtectionEnabledAsync(ublock: true, !_settings.UBlockEnabled),
                    selected: false);
                adBlockControl.ValueText = !_settings.UBlockEnabled ? "Off" : _uBlockEnabled ? "On" : "Limited";
                var cookieControl = AddActionFlyoutButton(
                    "Cookie banners",
                    _settings.CookieGuardEnabled && !_cookieGuardEnabled
                        ? "This WebView2 runtime could not load the packaged cookie-banner helper"
                        : "Enable or disable Tugle’s packaged cookie-banner helper",
                    () => _ = SetProtectionEnabledAsync(ublock: false, !_settings.CookieGuardEnabled),
                    selected: false);
                cookieControl.ValueText = !_settings.CookieGuardEnabled ? "Off" : _cookieGuardEnabled ? "Hidden" : "Unavailable";
                AddActionFlyoutSectionLabel("Browsing data", "");
                AddActionFlyoutButton("Clear browsing data…", "Choose which data to clear", () => ShowActionFlyout(ActionFlyoutMode.ClearData));
                break;

            case ActionFlyoutMode.ClearData:
                _actionFlyoutTitle.Text = "Clear browsing data";
                AddActionFlyoutButton("‹  Privacy", "Back to privacy", ShowPrivacyMenu);
                AddActionFlyoutInfo("Choose what to clear. You’ll be asked to confirm.");
                AddActionFlyoutButton("Clear browsing history", "Clear Tugle history and WebView2 page history", () => _ = ClearBrowserDataAsync(CoreWebView2BrowsingDataKinds.BrowsingHistory, "browsing history"), destructive: true);
                AddActionFlyoutButton("Clear last hour", "Clear the last hour of browser history and cache", () => _ = ClearBrowserDataAsync(CoreWebView2BrowsingDataKinds.BrowsingHistory | CoreWebView2BrowsingDataKinds.DiskCache, "history and cache", TimeSpan.FromHours(1)), destructive: true);
                AddActionFlyoutButton("Clear cache", "Clear cached page files", () => _ = ClearBrowserDataAsync(CoreWebView2BrowsingDataKinds.DiskCache, "cache"), destructive: true);
                AddActionFlyoutButton("Clear cookies and site data", "Signs out of sites and removes their stored data", () => _ = ClearBrowserDataAsync(CoreWebView2BrowsingDataKinds.AllSite, "cookies and site data"), destructive: true);
                AddActionFlyoutButton("Clear download history", "Clear browser download history; downloaded files stay on disk", () => _ = ClearBrowserDataAsync(CoreWebView2BrowsingDataKinds.DownloadHistory, "download history"), destructive: true);
                break;

            case ActionFlyoutMode.Search:
                _actionFlyoutTitle.Text = _isPrivate ? "Search engine" : $"Search · {_settings.ActiveWorkspace.Name}";
                AddActionFlyoutButton("‹  Browser settings", "Back", ShowBrowserSettings);
                foreach (var name in SearchProvider.Names)
                {
                    AddActionFlyoutButton(
                        name,
                        "Use " + name + " for searches",
                        () => SetSearchProvider(name),
                        selected: string.Equals(name, _settings.SearchEngine, StringComparison.Ordinal));
                }
                break;

            case ActionFlyoutMode.GuiScale:
                _actionFlyoutTitle.Text = "GUI scale";
                AddActionFlyoutSectionLabel("INTERFACE SIZE", "70% – 140%");
                foreach (var scale in GuiScales)
                {
                    var selected = Math.Abs(scale - _guiScale) < 0.01f;
                    AddActionFlyoutButton(
                        $"{(int)Math.Round(scale * 100)}%",
                        selected ? "Current GUI scale" : "Set GUI scale",
                        () =>
                        {
                            SetGuiScale(scale);
                            PopulateActionFlyout();
                        },
                        selected);
                }
                break;

            case ActionFlyoutMode.Theme:
                _actionFlyoutTitle.Text = "Theme";
                AddActionFlyoutSectionLabel("PRESET COLORS", $"{ThemePalettes.ColorOptions.Count} colors");
                foreach (var theme in ThemePalettes.ColorOptions)
                {
                    var selected = string.Equals(theme.Name, _selectedTheme.Name, StringComparison.OrdinalIgnoreCase);
                    AddActionFlyoutButton(
                        theme.Name,
                        theme.Description,
                        () =>
                        {
                            SelectTheme(theme);
                        },
                        selected,
                        swatch: theme.Accent);
                }
                var savedCustomColors = SavedCustomThemeColors().ToArray();
                if (savedCustomColors.Length > 0)
                {
                    AddActionFlyoutSectionLabel("SAVED CUSTOM COLORS", $"{savedCustomColors.Length} colors");
                    foreach (var (hex, accent) in savedCustomColors)
                    {
                        var selected = _selectedTheme.Name == "Custom" &&
                            string.Equals(TugleSettings.ToHex(_selectedTheme.Accent), hex, StringComparison.OrdinalIgnoreCase);
                        AddActionFlyoutButton(
                            hex,
                            selected ? "Current custom color" : "Use saved custom color",
                            () => SelectSavedCustomThemeColor(hex),
                            selected,
                            swatch: accent);
                    }
                }
                AddActionFlyoutButton(
                    "Add and save custom color…",
                    "Create a theme and keep it in your saved colors",
                    ChooseCustomThemeColor,
                    swatch: DefaultCustomColor);
                break;

            case ActionFlyoutMode.Downloads:
                _actionFlyoutTitle.Text = "Downloads";
                if (_downloads.Count == 0)
                {
                    AddActionFlyoutInfo("No downloads yet");
                }
                else
                {
                    AddActionFlyoutSectionLabel("RECENT DOWNLOADS", $"{_downloads.Count} items");
                    foreach (var download in _downloads)
                    {
                        AddDownloadFlyoutItem(download);
                        AddDownloadControls(download);
                    }
                }
                break;

            case ActionFlyoutMode.Accounts:
                _actionFlyoutTitle.Text = "Settings";
                AddActionFlyoutButton("Google account", "Google websites in Tugle", ShowGoogleAccount);
                AddActionFlyoutButton("Browser settings", "Search, privacy and updates", ShowBrowserSettings);
                break;

            case ActionFlyoutMode.GoogleAccount:
                _actionFlyoutTitle.Text = "Google account";
                AddActionFlyoutButton("‹  Settings", "Back", ShowAccountsMenu);
                AddActionFlyoutInfo(_googleSignInBlocked ? "Google blocked sign-in in this embedded browser." :
                    _googleSignedIn == true ? "Signed in to Google websites in Tugle." : "Sign in to use Google websites in Tugle.");
                AddActionFlyoutButton(_googleSignedIn == true ? "Manage account" : "Sign in to Google", "Open in a Tugle tab", ConnectGoogleAccount, prominent: true);
                if (_googleSignedIn == true)
                    AddActionFlyoutButton("Sign out", "Sign out of Google in this Tugle profile", DisconnectGoogleAccount);
                break;

            case ActionFlyoutMode.BrowserSettings:
                _actionFlyoutTitle.Text = "Browser settings";
                AddActionFlyoutButton("‹  Settings", "Back", ShowAccountsMenu);
                AddActionFlyoutButton("Search engine", "Used by Home and the address bar", ShowSearchMenu).ValueText = _settings.SearchEngine;
                AddActionFlyoutButton("Performance", "Memory saver for inactive tabs", ShowPerformanceMenu)
                    .ValueText = _settings.LowMemoryMode ? "On" : "Off";
                AddActionFlyoutButton("Privacy and protection", "Private windows, tracking controls, and browsing-data cleanup", ShowPrivacyMenu);
                AddActionFlyoutSectionLabel("Tugle", typeof(MainForm).Assembly.GetName().Version?.ToString(3) ?? "2.3.2");
                AddActionFlyoutButton(
                    "Check for updates",
                    "Download the latest Tugle package",
                    () => _ = CheckForUpdatesAsync(showNoUpdateMessage: true));
                break;

            case ActionFlyoutMode.Performance:
                _actionFlyoutTitle.Text = _isPrivate ? "Performance" : $"Performance · {_settings.ActiveWorkspace.Name}";
                AddActionFlyoutButton("‹  Browser settings", "Back", ShowBrowserSettings);
                var memorySaver = AddActionFlyoutButton(
                    "Memory saver",
                    "Pause inactive tabs to free memory",
                    () => SetMemorySaverEnabled(!_settings.LowMemoryMode));
                memorySaver.ValueText = _settings.LowMemoryMode ? "On" : "Off";
                var tabDiscarding = AddActionFlyoutButton(
                    "Discard inactive tabs",
                    "Reload tabs left inactive for 30 minutes",
                    () => SetTabDiscardingEnabled(!_settings.DiscardInactiveTabs));
                tabDiscarding.ValueText = _settings.DiscardInactiveTabs ? "On" : "Off";
                if (!_isPrivate)
                    AddActionFlyoutInfo(GetWorkspaceMemorySummary(ActiveWorkspaceId));
                AddActionFlyoutInfo(_settings.LowMemoryMode
                    ? _settings.DiscardInactiveTabs
                        ? "Inactive, unpinned tabs can be unloaded after 30 minutes. They reload when opened; unsaved form input may be lost."
                        : "Inactive tabs pause after a short time. Tabs playing audio stay awake."
                    : "All tabs stay active in the background. Turn this on to use less memory.");
                break;
        }
        }
        finally
        {
            _actionFlyoutItems.ResumeLayout(true);
            LayoutActionFlyout();
            _actionFlyoutItems.AutoScrollPosition = new Point(0, scrollOffset);
        }
    }

    private void AddActionFlyoutInfo(string text)
    {
        var infoFont = CreateUiFont(11F);
        var item = new Label
        {
            Text = text,
            Width = FlyoutItemWidth,
            Height = TextRenderer.MeasureText(text, infoFont, new Size(FlyoutItemWidth, 0), TextFormatFlags.WordBreak).Height + Ui(12),
            Margin = UiPadding(4, 6, 4, 4),
            Font = infoFont,
            ForeColor = _muted,
            TextAlign = ContentAlignment.MiddleLeft
        };
        item.Disposed += (_, _) => infoFont.Dispose();
        _actionFlyoutItems.Controls.Add(item);
    }

    private void AddActionFlyoutSectionLabel(string label, string detail)
    {
        var item = new FlyoutSectionLabel
        {
            LabelText = System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(label.ToLowerInvariant()),
            DetailText = detail,
            Width = FlyoutItemWidth,
            Height = Ui(28),
            Margin = UiPadding(4, 12, 4, 4),
            UiScale = GuiScale
        };
        _actionFlyoutItems.Controls.Add(item);
    }

    private void AddHistoryFlyoutItem(HistoryVisit visit, Action action)
    {
        var item = new HistoryFlyoutItem
        {
            Text = string.IsNullOrWhiteSpace(visit.Title) ? visit.Url : visit.Title,
            Detail = $"{GetHistorySiteLabel(visit.Url)}  ·  {FormatHistoryTimestamp(visit.VisitedAt)}",
            SiteName = GetHistorySiteLabel(visit.Url),
            Width = FlyoutItemWidth,
            Height = Ui(58),
            Margin = UiPadding(4, 0, 4, 5),
            Font = CreateUiFont(12.5F),
            ForeColor = _text,
            UiScale = GuiScale,
            FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand,
            AccessibleName = $"History: {visit.Title}",
            AccessibleDescription = $"{GetHistorySiteLabel(visit.Url)}, visited {FormatHistoryTimestamp(visit.VisitedAt)}"
        };
        item.FlatAppearance.BorderSize = 0;
        _tooltips.SetToolTip(item, visit.Url);
        item.Click += (_, _) => action();
        item.Disposed += (_, _) => item.Font.Dispose();
        _actionFlyoutItems.Controls.Add(item);
        _ = LoadHistoryIconAsync(item, visit.IconUrl);
    }

    private void AddDownloadFlyoutItem(DownloadItem download)
    {
        var available = download.State == CoreWebView2DownloadState.Completed && File.Exists(download.Path);
        var stateText = download.State switch
        {
            CoreWebView2DownloadState.InProgress => FormatDownloadProgress(download),
            CoreWebView2DownloadState.Completed => "Completed",
            _ => download.Operation?.CanResume == true ? "Paused · Resume available" : "Interrupted"
        };
        var item = new DownloadFlyoutItem
        {
            Text = Path.GetFileName(download.Path),
            Detail = stateText,
            FilePath = download.Path,
            FileIcon = TryGetDownloadIcon(download.Path),
            Width = FlyoutItemWidth,
            Height = Ui(58),
            Margin = UiPadding(4, 0, 4, 5),
            Font = CreateUiFont(12.5F),
            ForeColor = _text,
            UiScale = GuiScale,
            Enabled = available,
            Cursor = available ? Cursors.Hand : Cursors.Default,
            AccessibleName = $"Download: {Path.GetFileName(download.Path)}",
            AccessibleDescription = available ? "Click to open, or drag to move or copy this file." : stateText
        };
        _tooltips.SetToolTip(item, available ? $"{download.Path}\nClick to open or drag to move/copy" : download.Path);
        if (available) item.Click += (_, _) => OpenDownloadedFile(download.Path);
        _actionFlyoutItems.Controls.Add(item);
        download.Row = item;
    }

    private void AddDownloadControls(DownloadItem download)
    {
        var operation = download.Operation;
        if (operation is null || download.State == CoreWebView2DownloadState.Completed) return;

        if (download.State == CoreWebView2DownloadState.InProgress)
        {
            AddCompactActions(("Pause", operation.Pause, false, false), ("Cancel", operation.Cancel, false, true));
        }
        else if (operation.CanResume)
        {
            AddCompactActions(("Resume", operation.Resume, false, false), ("Cancel", operation.Cancel, false, true));
        }
    }

    private static string FormatDownloadProgress(DownloadItem download)
    {
        if (download.TotalBytes <= 0) return $"{FormatFileSize(download.BytesReceived)} downloaded";
        var percent = Math.Clamp((int)Math.Round(download.BytesReceived * 100D / download.TotalBytes), 0, 100);
        return $"{percent}% · {FormatFileSize(download.BytesReceived)} / {FormatFileSize(download.TotalBytes)}";
    }

    private static string FormatFileSize(long bytes)
    {
        if (bytes < 1024) return $"{Math.Max(0, bytes)} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024D:0.#} KB";
        if (bytes < 1024L * 1024 * 1024) return $"{bytes / 1024D / 1024D:0.#} MB";
        return $"{bytes / 1024D / 1024D / 1024D:0.#} GB";
    }

    private static Image? TryGetDownloadIcon(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            using var icon = Icon.ExtractAssociatedIcon(path);
            return icon?.ToBitmap();
        }
        catch
        {
            return null;
        }
    }

    private static async Task LoadHistoryIconAsync(HistoryFlyoutItem item, string? iconUrl)
    {
        if (!Uri.TryCreate(iconUrl, UriKind.Absolute, out var iconUri) ||
            (iconUri.Scheme != Uri.UriSchemeHttp && iconUri.Scheme != Uri.UriSchemeHttps)) return;

        try
        {
            var bytes = await HistoryIconClient.GetByteArrayAsync(iconUri);
            using var stream = new MemoryStream(bytes);
            using var decoded = Image.FromStream(stream);
            var icon = new Bitmap(decoded);
            if (item.IsDisposed)
            {
                icon.Dispose();
                return;
            }

            item.SiteIcon = icon;
        }
        catch
        {
            // The monogram fallback remains visible when a favicon is unavailable.
        }
    }

    private void ClearHistory()
    {
        if (_history.RecentVisits.Count == 0 && _history.SearchSuggestions.Count == 0 && _history.MostUsedSites.Count == 0) return;

        var result = MessageBox.Show(
            this,
            "Delete recent pages, most used sites, and search suggestions?",
            "Clear browsing history",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);
        if (result != DialogResult.Yes) return;

        _history.Clear();
        RefreshHistoryFlyoutIfVisible();
        if (_activeTab?.IsHome == true)
            _ = PopulateHomeAsync(_activeTab);
    }

    private bool IsCurrentPageBookmarked() => IsPageBookmarked(_activeTab);

    private bool IsPageBookmarked(BrowserTab? tab)
    {
        return !_isPrivate && TryGetPageDetails(tab, out var url, out _, out _) && _library.IsBookmarked(url);
    }

    private void ToggleCurrentBookmark() => ToggleBookmark(_activeTab);

    private void ToggleBookmark(BrowserTab? tab)
    {
        if (_isPrivate || !TryGetPageDetails(tab, out var url, out var title, out var iconUrl)) return;
        _library.ToggleBookmark(url, title, iconUrl);
        CaptureBookmarkIcon(tab!);
        UpdateBookmarkButton();
    }

    private void CaptureBookmarkIcon(BrowserTab tab)
    {
        if (_isPrivate || tab.IsClosing || tab.FaviconImage is null ||
            !TryGetPageDetails(tab, out var url, out _, out var iconUrl)) return;
        // Do not save a previous page's favicon while the next one is loading.
        if (!string.Equals(tab.FaviconImageUri, iconUrl, StringComparison.Ordinal)) return;
        var entry = _library.Bookmarks.FirstOrDefault(item => item.Url == url);
        if (entry is null) return;
        try
        {
            var png = BookmarkIcons.Encode(tab.FaviconImage);
            if (!_library.UpdateIcon(entry.Id, url, iconUrl, png)) return;
            foreach (var row in _actionFlyoutItems.Controls.OfType<HistoryFlyoutItem>().Where(row => Equals(row.Tag, entry.Id)))
                row.SiteIcon = BookmarkIcons.Decode(png);
        }
        catch { /* A transient icon failure must not prevent saving a bookmark. */ }
    }

    private bool TryGetActivePageDetails(out string url, out string title, out string? iconUrl) =>
        TryGetPageDetails(_activeTab, out url, out title, out iconUrl);

    private static bool TryGetPageDetails(BrowserTab? tab, out string url, out string title, out string? iconUrl)
    {
        url = string.Empty;
        title = string.Empty;
        iconUrl = null;
        var core = tab?.View.CoreWebView2;
        if (tab is null || tab.IsHome ||
            !Uri.TryCreate(tab.DeferredNavigation ? tab.InitialNavigationTarget : core?.Source, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https")) return false;

        url = uri.AbsoluteUri;
        title = string.IsNullOrWhiteSpace(core?.DocumentTitle) ? tab.Title : core.DocumentTitle.Trim();
        iconUrl = core?.FaviconUri;
        return true;
    }

    private void AddLibraryFlyoutItem(LibraryEntry entry)
    {
        var detail = GetHistorySiteLabel(entry.Url);
        var item = new HistoryFlyoutItem
        {
            Text = entry.Title,
            Detail = detail,
            SiteName = GetHistorySiteLabel(entry.Url),
            Width = FlyoutItemWidth,
            Height = Ui(58),
            Margin = UiPadding(4, 0, 4, 4),
            Font = CreateUiFont(12F),
            ForeColor = _text,
            UiScale = GuiScale,
            Cursor = Cursors.Hand,
            AccessibleName = entry.Title,
            AccessibleDescription = detail
        };
        item.Tag = entry.Id;
        item.SiteIcon = BookmarkIcons.Decode(entry.IconPng);
        item.TrailingSpace = 30;
        item.Click += (_, _) => NavigateToLibraryEntry(entry);
        item.Disposed += (_, _) => item.Font.Dispose();
        _actionFlyoutItems.Controls.Add(item);
        var menu = new ContextMenuStrip();
        ConfigureToolbarMenu(menu);
        menu.Items.Add("Open in new tab", null, async (_, _) =>
        {
            var tab = await OpenNewTabAsync(showHome: false);
            if (tab?.View.CoreWebView2 is not null) tab.View.CoreWebView2.Navigate(entry.Url);
            HideActionFlyout();
        });
        menu.Items.Add("Edit bookmark", null, (_, _) => { _editingBookmark = entry.Id; PopulateActionFlyout(); });
        var removeItem = new ToolStripMenuItem("Remove");
        removeItem.Click += (_, _) =>
        {
            _library.Remove(entry.Id);
            UpdateBookmarkButton();
            PopulateActionFlyout();
        };
        menu.Items.Add(removeItem);
        item.ContextMenuStrip = menu;
        var manage = new FlyoutItemButton
        {
            Text = "…", CenterText = true, Width = Ui(28), Height = Ui(28),
            Left = item.Width - Ui(30), Top = (item.Height - Ui(28)) / 2,
            Font = CreateUiFont(13F), ForeColor = _iconVisible, BackColor = _chrome,
            UiScale = GuiScale, Cursor = Cursors.Hand, AccessibleName = "Manage " + entry.Title
        };
        manage.Click += (_, _) => menu.Show(manage, new Point(0, manage.Height));
        manage.Disposed += (_, _) => manage.Font.Dispose();
        item.Controls.Add(manage);
        item.Disposed += (_, _) => menu.Dispose();
        _tooltips.SetToolTip(item, $"{entry.Url}\nRight-click to manage");
        if (item.SiteIcon is null) _ = LoadBookmarkIconAsync(item, entry);
    }

    private async Task LoadBookmarkIconAsync(HistoryFlyoutItem item, LibraryEntry entry)
    {
        var pageUrl = entry.Url;
        var previousPng = entry.IconPng;
        var iconUrl = string.IsNullOrWhiteSpace(entry.IconUrl) ? new Uri(new Uri(pageUrl), "/favicon.ico").AbsoluteUri : entry.IconUrl;
        if (!_bookmarkIconLoads.TryGetValue(iconUrl, out var request))
        {
            // Failed requests are cached too, avoiding repeated retries while searching.
            if (_bookmarkIconLoads.Count >= 64)
            {
                var completed = _bookmarkIconLoads.FirstOrDefault(pair => pair.Value.IsCompleted).Key;
                if (completed is null) return;
                _bookmarkIconLoads.Remove(completed);
            }
            request = BookmarkIcons.DownloadAsync(iconUrl, _bookmarkIconCancellation.Token);
            _bookmarkIconLoads[iconUrl] = request;
        }
        var png = await request;
        if (IsDisposed || Disposing || _bookmarkIconCancellation.IsCancellationRequested || png is null || entry.Url != pageUrl) return;
        // A WebView icon arriving during the download is preferable (supports SVG and authenticated icons).
        if (entry.IconPng == previousPng) _library.UpdateIcon(entry.Id, pageUrl, iconUrl, png);
        if (!item.IsDisposed) item.SiteIcon = BookmarkIcons.Decode(entry.IconPng ?? png);
    }

    private void UpdateBookmarkButton()
    {
        var saved = IsCurrentPageBookmarked();
        _bookmarksButton.ForeColor = saved ? _accent : _iconVisible;
        _tooltips.SetToolTip(_bookmarksButton, saved ? "Bookmarks · This page is saved" : "Bookmarks");
        _bookmarksButton.Invalidate();
    }

    private TextBox AddBookmarkField(string placeholder, string value)
    {
        var surface = new RoundedSurface
        {
            Width = FlyoutItemWidth, Height = Ui(38), UiScale = GuiScale,
            Margin = UiPadding(4, 4, 4, 6)
        };
        var input = new TextBox
        {
            BorderStyle = BorderStyle.None, Font = CreateUiFont(12F),
            BackColor = _theme.Surface, ForeColor = _text,
            PlaceholderText = placeholder, AccessibleName = placeholder, Text = value,
            Left = Ui(10), Width = surface.Width - Ui(20), MaxLength = 4096
        };
        input.Top = (surface.Height - input.PreferredHeight) / 2;
        input.Disposed += (_, _) => input.Font.Dispose();
        input.Enter += (_, _) => { surface.FocusedField = true; surface.Invalidate(); };
        input.Leave += (_, _) => { surface.FocusedField = false; surface.Invalidate(); };
        surface.MouseDown += (_, _) => input.Focus();
        surface.Controls.Add(input);
        _actionFlyoutItems.Controls.Add(surface);
        return input;
    }

    private void PopulateBookmarks()
    {
        var editing = _library.Bookmarks.FirstOrDefault(entry => entry.Id == _editingBookmark);
        if (editing is not null)
        {
            _actionFlyoutTitle.Text = "Edit bookmark";
            AddActionFlyoutSectionLabel("Name", "");
            var title = AddBookmarkField("Bookmark name", editing.Title);
            AddActionFlyoutSectionLabel("Address", "");
            var url = AddBookmarkField("https://example.com", editing.Url);
            AddActionFlyoutInfo(string.Empty);
            var hint = (Label)_actionFlyoutItems.Controls[^1];
            hint.Height = 0;
            hint.Margin = Padding.Empty;
            AddCompactActions(
                ("Save", () =>
                {
                    if (!_library.Update(editing.Id, title.Text, url.Text))
                    {
                        hint.Text = "Enter a valid HTTP(S) address that isn’t already saved.";
                        hint.ForeColor = _theme.DangerText;
                        hint.Height = TextRenderer.MeasureText(hint.Text, hint.Font, new Size(hint.Width, 0), TextFormatFlags.WordBreak).Height + Ui(8);
                        LayoutActionFlyout();
                        url.Focus();
                        return;
                    }
                    _editingBookmark = null;
                    UpdateBookmarkButton();
                    PopulateActionFlyout();
                }, true, false),
                ("Cancel", () => { _editingBookmark = null; PopulateActionFlyout(); }, false, false));
            return;
        }

        if (TryGetActivePageDetails(out _, out _, out _))
        {
            var bookmarked = IsCurrentPageBookmarked();
            var saveButton = AddActionFlyoutButton(bookmarked ? "Bookmarked" : "Bookmark this page", bookmarked ? "Remove bookmark" : "Save this page (Ctrl+D)", () =>
            {
                ToggleCurrentBookmark();
                PopulateActionFlyout();
            }, selected: bookmarked, prominent: !bookmarked);
            saveButton.Height = Ui(46);
            saveButton.Margin = UiPadding(4, 0, 4, 10);
            saveButton.CenterText = true;
            saveButton.AccessibleName = bookmarked ? "Remove bookmark" : "Bookmark this page";
        }
        if (_library.Bookmarks.Count == 0) return;

        var search = AddBookmarkField("Search bookmarks", _bookmarkQuery);
        var firstResultIndex = _actionFlyoutItems.Controls.Count;
        var searchTimer = new System.Windows.Forms.Timer { Interval = 150 };
        void RenderResults()
        {
            searchTimer.Stop();
            if (search.IsDisposed || _actionFlyoutMode != ActionFlyoutMode.Library) return;
            _actionFlyoutItems.SuspendLayout();
            try
            {
                while (_actionFlyoutItems.Controls.Count > firstResultIndex)
                    _actionFlyoutItems.Controls[firstResultIndex].Dispose();
                var results = _library.Bookmarks.Where(entry =>
                    entry.Title.Contains(_bookmarkQuery, StringComparison.OrdinalIgnoreCase) ||
                    entry.Url.Contains(_bookmarkQuery, StringComparison.OrdinalIgnoreCase)).ToArray();
                foreach (var entry in results.Take(_bookmarkLimit)) AddLibraryFlyoutItem(entry);
                if (results.Length == 0) AddActionFlyoutInfo("No matching bookmarks");
                if (results.Length > _bookmarkLimit)
                    AddCompactActions(("Show more", () => { _bookmarkLimit += 30; RenderResults(); }, false, false));
            }
            finally
            {
                _actionFlyoutItems.ResumeLayout(true);
                LayoutActionFlyout();
            }
        }
        searchTimer.Tick += (_, _) => RenderResults();
        search.Disposed += (_, _) => searchTimer.Dispose();
        search.TextChanged += (_, _) =>
        {
            _bookmarkQuery = search.Text.Trim();
            _bookmarkLimit = 30;
            _actionFlyoutItems.AutoScrollPosition = Point.Empty;
            searchTimer.Stop();
            searchTimer.Start();
        };
        search.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape) { HideActionFlyout(); e.SuppressKeyPress = true; }
            if (e.KeyCode == Keys.Enter)
            {
                var match = _library.Bookmarks.FirstOrDefault(entry => entry.Title.Contains(_bookmarkQuery, StringComparison.OrdinalIgnoreCase) || entry.Url.Contains(_bookmarkQuery, StringComparison.OrdinalIgnoreCase));
                if (match is not null) NavigateToLibraryEntry(match);
                e.SuppressKeyPress = true;
            }
        };
        RenderResults();
    }

    private void NavigateToLibraryEntry(LibraryEntry entry)
    {
        if (ActiveWebView?.CoreWebView2 is null) return;
        ActiveWebView.CoreWebView2.Navigate(entry.Url);
        HideActionFlyout();
    }

    private void OpenPrivateWindow()
    {
        if (_isPrivate) return;
        var privateWindow = new MainForm(isPrivate: true);
        privateWindow.Show();
    }

    private async Task SetTrackingPreventionAsync(string level)
    {
        if (_isPrivate || level is not ("Basic" or "Balanced" or "Strict")) return;
        _settings.TrackingPrevention = level;
        SaveSettings();
        var profile = _tabs.Select(tab => tab.View.CoreWebView2?.Profile).FirstOrDefault(item => item is not null);
        if (profile is null)
        {
            if (_actionFlyout.Visible && _actionFlyoutMode == ActionFlyoutMode.Privacy)
                PopulateActionFlyout();
            return;
        }

        try
        {
            profile.PreferredTrackingPreventionLevel = Enum.Parse<CoreWebView2TrackingPreventionLevel>(level);
            if (_actionFlyout.Visible && _actionFlyoutMode == ActionFlyoutMode.Privacy)
                PopulateActionFlyout();
        }
        catch
        {
            await Task.CompletedTask;
        }
    }

    private async Task ClearBrowserDataAsync(CoreWebView2BrowsingDataKinds dataKinds, string label, TimeSpan? duration = null)
    {
        if (_isPrivate) return;
        var result = MessageBox.Show(
            this,
            $"Clear {label}{(duration is null ? string.Empty : " from the selected time range")}?",
            "Clear browsing data",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);
        if (result != DialogResult.Yes) return;

        var profile = _tabs.Select(tab => tab.View.CoreWebView2?.Profile).FirstOrDefault(item => item is not null);
        if (profile is null) return;
        try
        {
            if (duration is { } timeRange)
                await profile.ClearBrowsingDataAsync(dataKinds, DateTime.UtcNow - timeRange, DateTime.UtcNow);
            else
                await profile.ClearBrowsingDataAsync(dataKinds);

            if (dataKinds.HasFlag(CoreWebView2BrowsingDataKinds.BrowsingHistory))
            {
                if (duration is { } range) _history.ClearSince(DateTime.UtcNow - range);
                else _history.Clear();
            }
            if (dataKinds.HasFlag(CoreWebView2BrowsingDataKinds.DownloadHistory))
                _downloads.Clear();
            if (_actionFlyout.Visible && _actionFlyoutMode == ActionFlyoutMode.Privacy)
                PopulateActionFlyout();
        }
        catch
        {
            MessageBox.Show(this, "Tugle could not clear that data right now.", "Clear browsing data", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    private async Task SetProtectionEnabledAsync(bool ublock, bool enabled)
    {
        if (_isPrivate) return;
        if (ublock) _settings.UBlockEnabled = enabled;
        else _settings.CookieGuardEnabled = enabled;
        SaveSettings();

        foreach (var tab in _tabs.Where(tab => tab.View.CoreWebView2 is not null))
        {
            var core = tab.View.CoreWebView2!;
            if (ublock)
            {
                if (enabled)
                {
                    await EnsureUBlockAsync(core);
                    if (!_uBlockEnabled) EnableFallbackHostFilter(tab, core);
                }
                else await SetExtensionEnabledAsync(core, "uBlock Origin", false);
            }
            else
            {
                if (enabled) await EnsureCookieGuardAsync(core);
                else await SetExtensionEnabledAsync(core, "Tugle Cookie Guard", false);
            }
        }

        if (ublock) _uBlockEnabled = enabled && _uBlockExtension?.IsEnabled == true;
        else _cookieGuardEnabled = enabled && _cookieGuardExtension?.IsEnabled == true;
        if (_actionFlyout.Visible && _actionFlyoutMode == ActionFlyoutMode.Privacy)
            PopulateActionFlyout();
    }

    private async Task SetExtensionEnabledAsync(CoreWebView2 core, string name, bool enabled)
    {
        try
        {
            var extension = (await core.Profile.GetBrowserExtensionsAsync()).FirstOrDefault(item =>
                item.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (extension is not null && extension.IsEnabled != enabled)
                await extension.EnableAsync(enabled);
            if (name == "uBlock Origin") _uBlockExtension = extension;
            else _cookieGuardExtension = extension;
        }
        catch
        {
            // The static fallback remains available if the runtime does not expose extensions.
        }
    }

    private void SetSearchProvider(string provider)
    {
        if (!SearchProvider.Names.Contains(provider)) return;
        _settings.SearchEngine = provider;
        SaveSettings();
        foreach (var tab in ActiveWorkspaceTabs)
        {
            tab.SuggestionCancellation?.Cancel();
            if (tab.IsHome) _ = PopulateHomeAsync(tab);
        }
        if (_actionFlyout.Visible && _actionFlyoutMode == ActionFlyoutMode.Search)
            PopulateActionFlyout();
    }

    private void SetMemorySaverEnabled(bool enabled)
    {
        if (_isPrivate || _settings.LowMemoryMode == enabled) return;

        _settings.LowMemoryMode = enabled;
        var now = DateTime.UtcNow;
        foreach (var tab in ActiveWorkspaceTabs)
        {
            if (tab.IsClosing || tab.View.IsDisposed) continue;

            if (!enabled)
            {
                try
                {
                    if (tab.View.CoreWebView2?.IsSuspended == true)
                        tab.View.CoreWebView2.Resume();
                }
                catch
                {
                    // Resuming is best effort while a tab is closing.
                }
                tab.IsSuspended = false;
                tab.IsSuspending = false;
                tab.InactiveSinceUtc = null;
            }
            else if (!ReferenceEquals(tab, _activeTab) && tab.View.CoreWebView2 is not null)
            {
                tab.InactiveSinceUtc = now;
            }
        }

        SaveSettings();
        if (_actionFlyout.Visible && _actionFlyoutMode == ActionFlyoutMode.Performance)
            PopulateActionFlyout();
    }

    private void SetTabDiscardingEnabled(bool enabled)
    {
        if (_isPrivate || _settings.DiscardInactiveTabs == enabled) return;

        _settings.DiscardInactiveTabs = enabled;
        SaveSettings();
        if (_actionFlyout.Visible && _actionFlyoutMode == ActionFlyoutMode.Performance)
            PopulateActionFlyout();
    }

    private static string ColorToCss(Color color)
    {
        return $"#{color.R:X2}{color.G:X2}{color.B:X2}";
    }

    private static string GetHistorySiteLabel(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return url;
        return uri.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? uri.Host[4..] : uri.Host;
    }

    private static string FormatHistoryTimestamp(DateTime visitedAt)
    {
        var localTime = visitedAt.Kind == DateTimeKind.Utc ? visitedAt.ToLocalTime() : visitedAt;
        var elapsed = DateTime.Now - localTime;
        if (elapsed >= TimeSpan.Zero && elapsed < TimeSpan.FromMinutes(1)) return "Just now";
        if (elapsed >= TimeSpan.Zero && elapsed < TimeSpan.FromHours(1)) return $"{Math.Max(1, (int)elapsed.TotalMinutes)}m ago";
        if (localTime.Date == DateTime.Today) return $"Today · {localTime:t}";
        if (localTime.Date == DateTime.Today.AddDays(-1)) return $"Yesterday · {localTime:t}";
        return localTime.ToString("dd MMM · t");
    }

    private int FlyoutItemWidth => Math.Max(Ui(180), _actionFlyout.Width - Ui(58));

    private void AddCompactActions(params (string Label, Action Action, bool Selected, bool Destructive)[] actions)
    {
        var row = new TableLayoutPanel
        {
            Width = FlyoutItemWidth,
            Height = Ui(36),
            Margin = UiPadding(4, 0, 4, 6),
            ColumnCount = actions.Length,
            RowCount = 1,
            BackColor = _chrome
        };
        foreach (var action in actions)
        {
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F / actions.Length));
            var button = new FlyoutItemButton
            {
                Text = action.Label,
                Dock = DockStyle.Fill,
                Margin = new Padding(Ui(2), 0, Ui(2), 0),
                Font = CreateUiFont(10.5F),
                ForeColor = _text,
                UiScale = GuiScale,
                Selected = action.Selected,
                Destructive = action.Destructive,
                CenterText = true,
                Cursor = Cursors.Hand,
                AccessibleName = action.Label
            };
            button.Click += (_, _) => action.Action();
            button.Disposed += (_, _) => button.Font.Dispose();
            row.Controls.Add(button);
        }
        _actionFlyoutItems.Controls.Add(row);
    }

    private FlyoutItemButton AddActionFlyoutButton(
        string text,
        string tooltip,
        Action? action,
        bool selected = false,
        bool destructive = false,
        bool prominent = false,
        Color? swatch = null)
    {
        var item = new FlyoutItemButton
        {
            Text = text,
            Width = FlyoutItemWidth,
            Height = Ui(42),
            Margin = prominent ? UiPadding(4, 8, 4, 4) : UiPadding(4, 0, 4, 4),
            Padding = UiPadding(10, 0, 10, 0),
            Font = CreateUiFont(12.5F),
            ForeColor = _text,
            UiScale = GuiScale,
            Selected = selected,
            Destructive = destructive,
            Prominent = prominent,
            SwatchColor = swatch,
            FlatStyle = FlatStyle.Flat,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
            UseCompatibleTextRendering = false,
            Cursor = action is null ? Cursors.Default : Cursors.Hand,
            Enabled = action is not null
        };
        item.FlatAppearance.BorderSize = 0;
        item.FlatAppearance.MouseOverBackColor = _chromeLighter;
        item.FlatAppearance.MouseDownBackColor = _theme.Selection;
        _tooltips.SetToolTip(item, tooltip);
        if (action is not null) item.Click += (_, _) => action();
        item.Disposed += (_, _) => item.Font.Dispose();
        _actionFlyoutItems.Controls.Add(item);
        return item;
    }

    private void ShowDownloadsMenu()
    {
        ShowActionFlyout(ActionFlyoutMode.Downloads);
    }

    private void RebuildDownloadsMenu()
    {
        _downloadsMenu.Items.Clear();

        if (_downloads.Count == 0)
        {
            _downloadsMenu.Items.Add(new ToolStripMenuItem("No downloads yet") { Enabled = false });
        }
        else
        {
            foreach (var download in _downloads)
            {
                var stateText = download.State switch
                {
                    CoreWebView2DownloadState.InProgress => "Downloading…",
                    CoreWebView2DownloadState.Completed => "Completed",
                    _ => "Interrupted"
                };
                var item = new ToolStripMenuItem($"{Path.GetFileName(download.Path)} — {stateText}")
                {
                    ToolTipText = download.Path,
                    Tag = download.Path,
                    Enabled = download.State == CoreWebView2DownloadState.Completed && File.Exists(download.Path)
                };
                item.Click += (_, _) => OpenDownloadedFile(download.Path);
                _downloadsMenu.Items.Add(item);
            }
        }

    }

    private void UpdateDownloadsButton()
    {
        var activeCount = _downloads.Count(download => download.State == CoreWebView2DownloadState.InProgress);
        _downloadsButton.ForeColor = activeCount > 0 ? _accent : _iconVisible;
        _tooltips.SetToolTip(
            _downloadsButton,
            activeCount > 0 ? $"Downloads ({activeCount} active)" : "Downloads (Ctrl+J)");
    }

    private void SelectTheme(ThemePalette theme)
    {
        _selectedTheme = theme;
        SetTheme(theme);
    }

    private void SetTheme(ThemePalette theme, bool saveSettings = true)
    {
        if (ReferenceEquals(theme, _theme)) return;

        _theme = theme;
        _chrome = theme.Chrome;
        _chromeLighter = theme.ChromeLighter;
        _text = theme.Text;
        _muted = theme.Muted;
        _iconVisible = theme.Icon;
        _accent = theme.Accent;
        TugleTheme.Current = theme;

        SuspendLayout();
        try
        {
            BackColor = _chrome;
            if (_titleBar is not null) _titleBar.BackColor = _chrome;
            if (_titleArea is not null) _titleArea.BackColor = _chrome;
            _tabsFlow.BackColor = _chrome;
            if (_toolbar is not null) _toolbar.BackColor = _chrome;
            _navigation.BackColor = _chrome;
            _utilityActions.BackColor = _chrome;
            _contentHost.BackColor = theme.ContentBackground;
            _splitDivider.BackColor = theme.Chrome;
            _splitDivider.Invalidate();
            _addressSurface.BackColor = _chrome;
            _addressBar.BackColor = theme.Surface;
            _addressBar.ForeColor = _text;
            _actionFlyout.BackColor = _chrome;
            _actionFlyoutItems.BackColor = _chrome;
            _actionFlyoutTitle.ForeColor = _text;
            _actionFlyoutClose.BackColor = _chrome;
            _actionFlyoutClose.ForeColor = _iconVisible;

            _newTabButton.BackColor = theme.Surface;
            _newTabButton.ForeColor = _iconVisible;
            _newTabButton.FlatAppearance.BorderColor = theme.Border;
            _newTabButton.FlatAppearance.MouseOverBackColor = theme.SurfaceHover;
            _newTabButton.FlatAppearance.MouseDownBackColor = theme.SurfacePressed;
            _workspaceButton.BackColor = _chrome;
            _workspaceButton.ForeColor = _text;
            UpdateWorkspaceSwitcher();

            ConfigureToolbarButton(_backButton, string.Empty, "Back");
            ConfigureToolbarButton(_forwardButton, string.Empty, "Forward");
            ConfigureToolbarButton(_reloadButton, string.Empty, "Reload");
            ConfigureToolbarButton(_homeButton, string.Empty, "Home");
            ConfigureToolbarButton(_historyButton, string.Empty, "History");
            ConfigureToolbarButton(_guiScaleButton, string.Empty, "GUI scale");
            ConfigureToolbarButton(_themeButton, string.Empty, "Theme");
            ConfigureToolbarButton(_bookmarksButton, string.Empty, "Bookmarks");
            ConfigureToolbarButton(_downloadsButton, string.Empty, "Downloads");
            ConfigureToolbarButton(_accountButton, string.Empty, "Settings");
            _historyButton.UiScale = GuiScale;
            _guiScaleButton.UiScale = GuiScale;
            _themeButton.UiScale = GuiScale;
            _bookmarksButton.UiScale = GuiScale;
            _downloadsButton.UiScale = GuiScale;
            _accountButton.UiScale = GuiScale;
            LayoutToolbarActions();
            _tooltips.SetToolTip(_themeButton, $"Theme ({_theme.Name})");

            if (_titleBar is not null)
            {
                foreach (Control child in _titleBar.Controls)
                {
                    if (child is not FlowLayoutPanel windowButtons || child.Dock != DockStyle.Right) continue;
                    windowButtons.BackColor = _chrome;
                    foreach (Control button in windowButtons.Controls)
                    {
                        button.BackColor = _chrome;
                        button.ForeColor = _iconVisible;
                    }
                }
            }

            foreach (var tab in _tabs)
            {
                tab.Button.BackColor = theme.Surface;
                tab.View.DefaultBackgroundColor = theme.ContentBackground;
                UpdateTabButton(tab);
                if (tab.IsHome) _ = PopulateHomeAsync(tab);
            }

            ConfigureToolbarMenu(_downloadsMenu);
            ConfigureToolbarMenu(_historyMenu);
            ConfigureToolbarMenu(_guiScaleMenu);
            ConfigureToolbarMenu(_workspaceMenu);
            _tooltips.SetToolTip(_themeButton, $"Theme ({_theme.Name})");
            UpdateDownloadsButton();
            UpdateNavigationButtons();
            InvalidateThemeControls(this);
            if (_actionFlyout.Visible) PopulateActionFlyout();
        }
        finally
        {
            ResumeLayout(true);
            PerformLayout();
        }
        if (saveSettings) SaveSettings();
    }

    private static void InvalidateThemeControls(Control control)
    {
        control.Invalidate();
        foreach (Control child in control.Controls)
            InvalidateThemeControls(child);
    }

    private Size GetScaledMinimumWindowSize()
    {
        var screen = IsHandleCreated
            ? Screen.FromHandle(Handle).WorkingArea
            : Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1920, 1080);
        var minimumWidth = Math.Min(Math.Max(640, Ui(760)), Math.Max(640, screen.Width - Ui(48)));
        var minimumHeight = Math.Min(Math.Max(500, Ui(500)), Math.Max(500, screen.Height - Ui(48)));
        return new Size(minimumWidth, minimumHeight);
    }

    private void SetGuiScale(float scale)
    {
        scale = Math.Clamp(scale, 0.7f, 1.4f);
        if (Math.Abs(scale - _guiScale) < 0.01f) return;

        _guiScale = scale;
        ApplyGuiScale();
        SaveSettings();
    }

    private void ApplyGuiScale()
    {
        if (_applyingGuiScale || IsDisposed) return;
        _applyingGuiScale = true;
        var chromeContainers = new Control?[] { _titleBar, _titleArea, _windowButtons, _toolbar, _navigation, _utilityActions, _tabsFlow, _workspaceButton };
        SuspendLayout();
        foreach (var container in chromeContainers) container?.SuspendLayout();
        try
        {
            Padding = UiPadding(7);
            Font = CreateUiFont(13F);
            MinimumSize = GetScaledMinimumWindowSize();

            if (_titleBar is not null) _titleBar.Height = Ui(54);
            if (_windowButtons is not null)
            {
                _windowButtons.Width = Ui(138);
                _windowButtons.Padding = UiPadding(0, 2, 0, 0);
                foreach (Button button in _windowButtons.Controls)
                {
                    button.Size = new Size(Ui(46), Ui(36));
                    if (button is CloseGlyphButton close) close.UiScale = GuiScale;
                    var previousFont = button.Font;
                    button.Font = new Font("Segoe MDL2 Assets", 10F * GuiScale);
                    previousFont.Dispose();
                }
            }
            if (_toolbar is not null)
            {
                _toolbar.Height = Ui(56);
                _toolbar.Padding = UiPadding(5, 4, 8, 7);
                _toolbar.ColumnStyles[0].Width = Ui(168);
                _toolbar.ColumnStyles[2].Width = Ui(258);
            }

            _navigation.Padding = UiPadding(0, 2, 0, 0);
            _utilityActions.Padding = UiPadding(2, 2, 0, 0);

            ConfigureToolbarButton(_backButton, string.Empty, "Back");
            ConfigureToolbarButton(_forwardButton, string.Empty, "Forward");
            ConfigureToolbarButton(_reloadButton, string.Empty, "Reload");
            ConfigureToolbarButton(_homeButton, string.Empty, "Home");
            ConfigureToolbarButton(_historyButton, string.Empty, "History");
            ConfigureToolbarButton(_guiScaleButton, string.Empty, "GUI scale");
            ConfigureToolbarButton(_themeButton, string.Empty, "Theme");
            ConfigureToolbarButton(_bookmarksButton, string.Empty, "Bookmarks");
            ConfigureToolbarButton(_downloadsButton, string.Empty, "Downloads");
            ConfigureToolbarButton(_accountButton, string.Empty, "Settings");
            _historyButton.Margin = UiPadding(0, 0, 4, 0);
            _guiScaleButton.Margin = UiPadding(0, 0, 4, 0);
            _downloadsButton.Margin = Padding.Empty;
            _historyButton.UiScale = GuiScale;
            _guiScaleButton.UiScale = GuiScale;
            _themeButton.UiScale = GuiScale;
            _bookmarksButton.UiScale = GuiScale;
            _downloadsButton.UiScale = GuiScale;
            _accountButton.UiScale = GuiScale;

            _addressSurface.UiScale = GuiScale;
            _addressBar.Font = CreateUrlFont(13F);
            _addressSurface.Invalidate();
            LayoutToolbarActions();
            UpdateBookmarkButton();

            _newTabButton.Font = CreateUiFont(13F);
            _newTabButton.Size = new Size(Ui(36), Ui(32));
            _newTabButton.UiScale = GuiScale;
            _workspaceButton.Font = CreateUiFont(12F);
            _workspaceButton.UiScale = GuiScale;
            foreach (var tab in _tabs)
            {
                tab.Button.Font = CreateUiFont(13F);
                tab.Button.UiScale = GuiScale;
                tab.Button.Invalidate();
            }

            ConfigureToolbarMenu(_downloadsMenu);
            ConfigureToolbarMenu(_historyMenu);
            ConfigureToolbarMenu(_guiScaleMenu);
            ConfigureToolbarMenu(_workspaceMenu);
            UpdateDownloadsButton();
            _tooltips.SetToolTip(_guiScaleButton, $"GUI scale ({_guiScale:P0})");
            _actionFlyout.Padding = Padding.Empty;
            _actionFlyout.UiScale = GuiScale;
            _actionFlyout.RefreshShape();
            _actionFlyoutTitle.Font = CreateUiFont(14F);
            _actionFlyoutClose.Width = Ui(38);
            _actionFlyoutClose.Font = CreateUiFont(19F);
            _actionFlyoutClose.UiScale = GuiScale;
            _actionFlyoutItems.Padding = UiPadding(14, 6, 14, 14);
            if (_actionFlyoutTitle.Parent is Panel flyoutHeader)
            {
                flyoutHeader.Height = Ui(48);
                flyoutHeader.Padding = UiPadding(14, 0, 4, 0);
            }
            LayoutActionFlyout();
            if (_actionFlyout.Visible) PopulateActionFlyout();
            if (_titleArea is not null) LayoutTabStrip(_titleArea);
        }
        finally
        {
            foreach (var container in chromeContainers.Reverse()) container?.ResumeLayout(false);
            ResumeLayout(true);
            PerformLayout();
            _titleBar?.PerformLayout();
            _toolbar?.PerformLayout();
            _navigation.PerformLayout();
            _utilityActions.PerformLayout();
            if (_titleArea is not null) LayoutTabStrip(_titleArea);
            if (_activeTab is { IsClosing: false }) EnsureTabVisible(_activeTab);
            LayoutContentViews();
            LayoutActionFlyout();
            Invalidate(true);
            _applyingGuiScale = false;
        }
    }

    private static void OpenDownloadedFile(string path)
    {
        if (!File.Exists(path)) return;
        Process.Start(new ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true
        });
    }

    private static string GetDownloadsFolder()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Downloads");
    }

    private static string GetAvailableDownloadPath(string folder, string suggestedName)
    {
        var safeName = string.Join("_", suggestedName.Split(Path.GetInvalidFileNameChars()));
        if (string.IsNullOrWhiteSpace(safeName)) safeName = "download";

        var baseName = Path.GetFileNameWithoutExtension(safeName);
        var extension = Path.GetExtension(safeName);
        var candidate = Path.Combine(folder, safeName);
        var suffix = 2;
        while (File.Exists(candidate) || Directory.Exists(candidate))
        {
            candidate = Path.Combine(folder, $"{baseName} ({suffix++}){extension}");
        }
        return candidate;
    }

    private void ShowHome(BrowserTab? tab = null)
    {
        tab ??= _activeTab;
        if (tab?.View.CoreWebView2 is null) return;

        tab.IsHome = true;
        tab.Title = "Tugle";
        tab.FaviconUrl = null;
        var homePath = Path.Combine(AppContext.BaseDirectory, "TugleHome.html");
        tab.InitialNavigationTarget = new Uri(homePath).AbsoluteUri;
        tab.FaviconRequestVersion++;
        ReplaceTabFavicon(tab, null);
        if (ReferenceEquals(tab, _activeTab)) _addressBar.Clear();

        tab.View.CoreWebView2.Navigate(new Uri(homePath).AbsoluteUri);
        if (ReferenceEquals(tab, _activeTab)) UpdateNavigationButtons();
    }

    private bool HasSplitView =>
        _splitLeftTab is { IsClosing: false } left &&
        _splitRightTab is { IsClosing: false } right &&
        !ReferenceEquals(left, right) &&
        _tabs.Contains(left) && _tabs.Contains(right) &&
        left.WorkspaceId == right.WorkspaceId &&
        (_isPrivate || left.WorkspaceId == ActiveWorkspaceId);

    private bool IsSplitParticipant(BrowserTab tab) =>
        HasSplitView && (ReferenceEquals(tab, _splitLeftTab) || ReferenceEquals(tab, _splitRightTab));

    private async Task ToggleSplitWithNextTabAsync()
    {
        if (HasSplitView)
        {
            CloseSplitView();
            return;
        }

        var candidate = ActiveWorkspaceTabs.FirstOrDefault(tab => !ReferenceEquals(tab, _activeTab));
        if (candidate is not null) await OpenSplitViewAsync(candidate);
    }

    private async Task ToggleSplitViewAsync(BrowserTab requested)
    {
        if (IsSplitParticipant(requested))
        {
            CloseSplitView();
            return;
        }

        await OpenSplitViewAsync(requested);
    }

    private async Task OpenSplitViewAsync(BrowserTab requested)
    {
        if (requested.IsClosing || !_tabs.Contains(requested) || requested.WorkspaceId != ActiveWorkspaceId) return;

        var primary = _activeTab;
        if (primary is null || primary.IsClosing || primary.WorkspaceId != requested.WorkspaceId)
            primary = ActiveWorkspaceTabs.FirstOrDefault(tab => !ReferenceEquals(tab, requested));
        else if (ReferenceEquals(primary, requested))
            primary = ActiveWorkspaceTabs.FirstOrDefault(tab => !ReferenceEquals(tab, requested));
        if (primary is null || primary.IsClosing || ReferenceEquals(primary, requested)) return;

        CloseSplitView(revealActive: false);

        // Activate each tab once before presenting both. That preserves lazy
        // restoration and makes an unloaded tab safe to show beside a page.
        await ActivateTabAsync(requested);
        if (requested.IsClosing || primary.IsClosing || !_tabs.Contains(primary)) return;
        await ActivateTabAsync(primary);
        if (requested.IsClosing || primary.IsClosing || !_tabs.Contains(requested) || !_tabs.Contains(primary)) return;

        _splitLeftTab = primary;
        _splitRightTab = requested;
        _activeTab = primary;
        FocusSplitTab(primary);
        await RevealSplitTabsAsync();
    }

    private void CloseSplitView(bool revealActive = true)
    {
        var hadSplitView = HasSplitView;
        _splitLeftTab = null;
        _splitRightTab = null;
        _draggingSplitDivider = false;
        _splitDivider.Capture = false;
        _splitDivider.Visible = false;
        if (!hadSplitView) return;

        LayoutContentViews();
        if (revealActive && _activeTab is { IsClosing: false } active)
            _ = RevealTabAsync(active);
    }

    private void FocusSplitTab(BrowserTab tab)
    {
        if (!IsSplitParticipant(tab) || tab.IsClosing) return;
        _activeTab = tab;
        var now = DateTime.UtcNow;
        foreach (var item in _tabs)
        {
            var active = ReferenceEquals(item, tab);
            item.InactiveSinceUtc = active || IsSplitParticipant(item)
                ? null
                : item.InactiveSinceUtc ?? now;
            UpdateTabButton(item);
        }
        UpdateAddressBar();
        UpdateNavigationButtons();
    }

    private async Task RevealSplitTabsAsync()
    {
        if (!HasSplitView || _splitLeftTab is not { } left || _splitRightTab is not { } right) return;
        var focused = IsSplitParticipant(_activeTab ?? left) ? _activeTab! : left;

        _contentHost.SuspendLayout();
        try
        {
            LayoutContentViews();
            SetWebViewPresentation(left, true);
            SetWebViewPresentation(right, true);
            foreach (var tab in _tabs)
            {
                if (!ReferenceEquals(tab, left) && !ReferenceEquals(tab, right))
                    SetWebViewPresentation(tab, false);
            }
            _splitDivider.BringToFront();
        }
        finally
        {
            _contentHost.ResumeLayout(true);
        }

        FocusSplitTab(focused);
        await Task.CompletedTask;
    }

    private void LayoutContentViews()
    {
        if (!HasSplitView || _splitLeftTab is not { } left || _splitRightTab is not { } right)
        {
            _splitDivider.Visible = false;
            foreach (var tab in _tabs.Where(tab => !tab.View.IsDisposed))
            {
                tab.View.Dock = DockStyle.Fill;
                tab.View.Bounds = _contentHost.ClientRectangle;
                try { GetWebViewController(tab.View)?.NotifyParentWindowPositionChanged(); }
                catch { /* A WebView controller can be shutting down during resize. */ }
            }
            return;
        }

        var dividerWidth = Math.Max(Ui(7), 5);
        var availableWidth = Math.Max(1, _contentHost.ClientSize.Width - dividerWidth);
        var minimumPaneWidth = Math.Min(Math.Max(Ui(110), availableWidth / 5), Ui(260));
        var leftWidth = Math.Clamp((int)Math.Round(availableWidth * _splitRatio), minimumPaneWidth, Math.Max(minimumPaneWidth, availableWidth - minimumPaneWidth));
        var rightWidth = Math.Max(1, availableWidth - leftWidth);
        _splitRatio = (float)leftWidth / availableWidth;

        left.View.Dock = DockStyle.None;
        right.View.Dock = DockStyle.None;
        left.View.Bounds = new Rectangle(0, 0, leftWidth, _contentHost.ClientSize.Height);
        _splitDivider.SetBounds(leftWidth, 0, dividerWidth, _contentHost.ClientSize.Height);
        right.View.Bounds = new Rectangle(leftWidth + dividerWidth, 0, rightWidth, _contentHost.ClientSize.Height);
        _splitDivider.Visible = true;

        foreach (var tab in new[] { left, right })
        {
            try { GetWebViewController(tab.View)?.NotifyParentWindowPositionChanged(); }
            catch { /* A WebView controller can be shutting down during resize. */ }
        }
    }

    private void BeginSplitDividerDrag(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left || !HasSplitView) return;
        _draggingSplitDivider = true;
        _splitDivider.Capture = true;
    }

    private void MoveSplitDivider(object? sender, MouseEventArgs e)
    {
        if (!_draggingSplitDivider || !HasSplitView) return;
        var pointer = _contentHost.PointToClient(_splitDivider.PointToScreen(e.Location));
        var availableWidth = Math.Max(1, _contentHost.ClientSize.Width - _splitDivider.Width);
        _splitRatio = Math.Clamp((float)pointer.X / availableWidth, 0.2f, 0.8f);
        LayoutContentViews();
    }

    private void EndSplitDividerDrag(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        _draggingSplitDivider = false;
        _splitDivider.Capture = false;
    }

    private async void CloseTab(BrowserTab tab)
    {
        var index = _tabs.IndexOf(tab);
        if (index < 0 || tab.IsClosing) return;

        RememberClosedTab(tab);
        tab.IsClosing = true;
        if (!_tabs.Contains(tab)) return;

        index = _tabs.IndexOf(tab);

        var wasActive = ReferenceEquals(tab, _activeTab);
        if (IsSplitParticipant(tab)) CloseSplitView(revealActive: !wasActive);
        var replacement = wasActive
            ? _tabs.Skip(index + 1).FirstOrDefault(item => !item.IsClosing && item.WorkspaceId == tab.WorkspaceId) ??
              _tabs.Take(index).LastOrDefault(item => !item.IsClosing && item.WorkspaceId == tab.WorkspaceId)
            : null;

        // Keep the closing WebView on screen until its replacement is fully
        // presented. Removing it first leaves the host panel black for a frame.
        if (replacement is not null)
            await ActivateTabAsync(replacement);
        if (IsDisposed || tab.View.IsDisposed) return;

        _tabs.Remove(tab);
        _selectedTabs.Remove(tab);
        if (ReferenceEquals(_selectionAnchor, tab)) _selectionAnchor = null;
        _contentHost.Controls.Remove(tab.View);
        _tabsFlow.Controls.Remove(tab.Button);
        LayoutTabs();
        tab.SuggestionCancellation?.Cancel();
        tab.SuggestionCancellation?.Dispose();
        tab.FaviconRequestVersion++;
        tab.FaviconImage?.Dispose();
        tab.FaviconImage = null;
        tab.View.Dispose();
        tab.Button.ContextMenuStrip?.Dispose();
        tab.Button.Dispose();

        if (_tabs.Count == 0 || (! _isPrivate && !ActiveWorkspaceTabs.Any()))
        {
            _activeTab = null;
            await OpenWorkspaceStartupTabAsync();
            return;
        }
        CapturePreviousSession();
        SaveSettings();
    }

    private void ActivateTab(BrowserTab tab)
    {
        _ = ActivateTabAsync(tab);
    }

    private async Task ActivateTabAsync(BrowserTab tab)
    {
        if (!_tabs.Contains(tab) || tab.IsClosing) return;
        if (!_isPrivate && tab.WorkspaceId != ActiveWorkspaceId)
        {
            await SwitchWorkspaceAsync(tab.WorkspaceId);
            return;
        }
        if (IsSplitParticipant(tab))
        {
            FocusSplitTab(tab);
            return;
        }
        if (HasSplitView) CloseSplitView(revealActive: false);
        if (GetTabGroup(tab) is { IsCollapsed: true } group && !ReferenceEquals(tab, GetCollapsedGroupRepresentative(group.Id)))
            SetTabGroupCollapsed(group.Id, false);
        _activeTab = tab;
        var now = DateTime.UtcNow;

        if (tab.View.CoreWebView2?.IsSuspended == true)
            tab.View.CoreWebView2.Resume();
        tab.IsSuspended = false;
        tab.IsSuspending = false;

        foreach (var item in _tabs)
        {
            var active = ReferenceEquals(item, tab);
            item.InactiveSinceUtc = active ? null : item.InactiveSinceUtc ?? now;
            UpdateTabButton(item);
        }

        EnsureTabVisible(tab);
        UpdateAddressBar();
        try
        {
            await EnsureTabInitializedAsync(tab);
            if (tab.IsClosing || tab.View.IsDisposed || IsDisposed || tab.View.CoreWebView2 is null) return;
            if (tab.DeferredNavigation)
            {
                tab.DeferredNavigation = false;
                if (tab.IsHome || string.IsNullOrWhiteSpace(tab.InitialNavigationTarget)) ShowHome(tab);
                else tab.View.CoreWebView2.Navigate(tab.InitialNavigationTarget);
            }
        }
        catch (Exception ex)
        {
            if (tab.IsClosing || IsDisposed) return;
            tab.InitializationTask = null;
            MessageBox.Show(this, "This tab could not load. Select it to retry.\n\n" + ex.Message, "Tugle", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (!ReferenceEquals(tab, _activeTab)) return;
        await RevealTabAsync(tab);
        UpdateAddressBar();
        UpdateNavigationButtons();
        if (tab.IsHome) _ = PopulateHomeAsync(tab);
    }

    private async Task RevealTabAsync(BrowserTab tab)
    {
        if (tab.View.IsDisposed || !_tabs.Contains(tab) || !ReferenceEquals(tab, _activeTab) ||
            !tab.InitialNavigationReady) return;
        if (IsSplitParticipant(tab))
        {
            await RevealSplitTabsAsync();
            return;
        }

        var previous = _tabs.FirstOrDefault(item =>
            !ReferenceEquals(item, tab) && item.View.Visible && !item.View.IsDisposed);
        var revealVersion = ++_revealVersion;

        _contentHost.SuspendLayout();
        try
        {
            tab.View.Bounds = _contentHost.ClientRectangle;
            SetWebViewPresentation(tab, true);

            // Keep the current surface in front for one compositor handoff.
            // WebView2 can briefly expose a stale/blank surface when a hidden
            // child HWND is made visible and immediately reordered.
            if (previous is not null && _contentHost.Controls.GetChildIndex(previous.View) != 0)
                _contentHost.Controls.SetChildIndex(previous.View, 0);
        }
        finally
        {
            _contentHost.ResumeLayout(true);
        }

        if (previous is not null && !ReferenceEquals(previous, tab))
        {
            await Task.Delay(50);
            if (revealVersion != _revealVersion || tab.View.IsDisposed ||
                !ReferenceEquals(tab, _activeTab)) return;
        }

        _contentHost.SuspendLayout();
        try
        {
            if (tab.View.IsDisposed || !ReferenceEquals(tab, _activeTab)) return;

            if (_contentHost.Controls.GetChildIndex(tab.View) != 0)
                _contentHost.Controls.SetChildIndex(tab.View, 0);

            SetWebViewPresentation(tab, true);
            foreach (var item in _tabs)
            {
                if (!ReferenceEquals(item, tab))
                    SetWebViewPresentation(item, false);
            }
        }
        finally
        {
            _contentHost.ResumeLayout(true);
        }
    }

    private static void SetWebViewPresentation(BrowserTab tab, bool visible)
    {
        if (tab.View.IsDisposed) return;

        var controller = GetWebViewController(tab.View);
        if (!visible)
        {
            try
            {
                if (controller is not null) controller.IsVisible = false;
            }
            catch
            {
                // The controller may be closing at the same time as its tab.
            }

            tab.View.Visible = false;
            return;
        }

        tab.View.Visible = true;
        try
        {
            if (controller is not null)
            {
                controller.IsVisible = true;
                controller.NotifyParentWindowPositionChanged();
            }
        }
        catch
        {
            // A presentation refresh is best effort; WebView2 still owns the
            // normal WinForms visibility path if an older runtime rejects it.
        }

        tab.View.Invalidate(true);
        tab.View.Update();
    }

    private static CoreWebView2Controller? GetWebViewController(WebView2 view)
    {
        return typeof(WebView2)
            .GetField("_coreWebView2Controller",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            ?.GetValue(view) as CoreWebView2Controller;
    }

    private void BatchChromeUpdate(Action update)
    {
        _tabsFlow.SuspendLayout();
        _contentHost.SuspendLayout();
        try
        {
            update();
        }
        finally
        {
            _contentHost.ResumeLayout(true);
            _tabsFlow.ResumeLayout(true);
        }
    }

    private async Task SuspendInactiveTabsAsync()
    {
        if (_tabs.Count < 2 || _suspensionSweepRunning) return;

        _suspensionSweepRunning = true;
        try
        {
            var now = DateTime.UtcNow;
            foreach (var tab in _tabs.ToArray())
            {
                if (!UsesMemorySaver(tab) || ReferenceEquals(tab, _activeTab) || IsSplitParticipant(tab) ||
                    tab.IsClosing || tab.IsPlayingAudio ||
                    tab.IsSuspended ||
                    tab.IsSuspending ||
                    tab.InactiveSinceUtc is null ||
                    now - tab.InactiveSinceUtc.Value < InactiveTabDelay ||
                    tab.View.IsDisposed ||
                    tab.View.CoreWebView2 is null)
                {
                    continue;
                }

                tab.IsSuspending = true;
                try
                {
                    var core = tab.View.CoreWebView2;
                    if (ReferenceEquals(tab, _activeTab)) continue;

                    var suspended = await core.TrySuspendAsync();
                    tab.IsSuspended = suspended && core.IsSuspended;
                    if (ReferenceEquals(tab, _activeTab) && tab.IsSuspended)
                    {
                        core.Resume();
                        tab.IsSuspended = false;
                    }
                    UpdateTabButton(tab);
                }
                catch
                {
                    // Suspension is an optimization. A failed attempt must not affect browsing.
                }
                finally
                {
                    tab.IsSuspending = false;
                }
            }

            await DiscardInactiveTabsAsync();
        }
        finally
        {
            _suspensionSweepRunning = false;
        }
    }

    private Task DiscardInactiveTabsAsync()
    {
        if (_tabs.Count < 2)
            return Task.CompletedTask;

        var now = DateTime.UtcNow;
        foreach (var tab in _tabs.ToArray())
        {
            if (!UsesMemorySaver(tab) || !UsesTabDiscarding(tab) || ReferenceEquals(tab, _activeTab) || IsSplitParticipant(tab) || tab.IsClosing || tab.IsPinned ||
                tab.IsPlayingAudio || tab.IsSuspending || tab.InactiveSinceUtc is null ||
                now - tab.InactiveSinceUtc.Value < DiscardInactiveTabDelay ||
                tab.View.IsDisposed || tab.View.CoreWebView2 is null ||
                !TryGetRestorableTabUrl(tab, out var url))
            {
                continue;
            }

            // Replacing the controller is what releases renderer memory. This is
            // intentionally separate from suspension and only runs when the user
            // opted in to reloading long-idle tabs.
            var previousView = tab.View;
            tab.SuggestionCancellation?.Cancel();
            tab.SuggestionCancellation?.Dispose();
            tab.SuggestionCancellation = null;
            tab.FaviconRequestVersion++;
            tab.InitialNavigationTarget = url;
            tab.InitialNavigationStarted = false;
            tab.InitialNavigationReady = false;
            tab.InitializationTask = null;
            tab.DeferredNavigation = true;
            tab.IsSuspended = false;
            tab.IsSuspending = false;
            tab.IsDiscarded = true;
            tab.FallbackHostFilterRegistered = false;

            BatchChromeUpdate(() =>
            {
                _contentHost.Controls.Remove(previousView);
                tab.ReplaceView(CreateBrowserView());
                _contentHost.Controls.Add(tab.View);
                tab.View.Bounds = _contentHost.ClientRectangle;
            });
            previousView.Dispose();
            UpdateTabButton(tab);
        }

        return Task.CompletedTask;
    }

    private bool UsesMemorySaver(BrowserTab tab)
    {
        if (_isPrivate || tab.WorkspaceId == ActiveWorkspaceId) return _settings.LowMemoryMode;
        return GetWorkspace(tab.WorkspaceId)?.LowMemoryMode ?? _settings.LowMemoryMode;
    }

    private bool UsesTabDiscarding(BrowserTab tab)
    {
        if (_isPrivate || tab.WorkspaceId == ActiveWorkspaceId) return _settings.DiscardInactiveTabs;
        return GetWorkspace(tab.WorkspaceId)?.DiscardInactiveTabs ?? _settings.DiscardInactiveTabs;
    }

    private void UpdateTabButton(BrowserTab tab)
    {
        var active = ReferenceEquals(tab, _activeTab);
        var group = GetTabGroup(tab);
        var collapsedRepresentative = group is { IsCollapsed: true }
            ? GetCollapsedGroupRepresentative(group.Id)
            : null;
        var isCollapsedRepresentative = ReferenceEquals(tab, collapsedRepresentative);
        tab.Button.Text = isCollapsedRepresentative
            ? $"› {group!.Icon} {group.Name} ({GetGroupTabCount(group.Id)})"
            : tab.Title;
        tab.Button.Favicon = isCollapsedRepresentative ? null : tab.IsHome ? _homeTabIcon : tab.FaviconImage;
        tab.Button.Muted = tab.IsMuted;
        tab.Button.PlayingAudio = tab.IsPlayingAudio;
        tab.Button.Pinned = tab.IsPinned;
        tab.Button.GroupColor = group is null
            ? Color.Empty
            : TugleSettings.FromHex(group.Color, Color.Empty);
        tab.Button.Active = active;
        tab.Button.Selected = _selectedTabs.Contains(tab);
        tab.Button.ForeColor = active ? _text : _muted;
        tab.Button.AccessibleName = tab.Title +
            (group is null ? string.Empty : $", {group.Name} tab group") +
            (isCollapsedRepresentative ? $", collapsed group with {GetGroupTabCount(group!.Id)} tabs" : string.Empty) +
            (tab.IsPinned ? " pinned" : string.Empty) +
            (active ? " tab active" : " tab");
        tab.Button.AccessibleDescription = tab.IsMuted
            ? "This tab is muted. Use the speaker button before the X to unmute it."
            : tab.IsDiscarded
            ? "This inactive tab was unloaded to save memory. Select it to reload."
            : tab.IsSuspended
            ? "This inactive tab is suspended to save memory. Select it to resume."
            : tab.IsPlayingAudio ? "Playing audio. Use the speaker button to mute this tab."
            : "Click to switch to this tab. Use the X to close it.";
        _tooltips.SetToolTip(tab.Button, tab.Title +
            (group is null ? string.Empty : $" · {group.Icon} {group.Name}") +
            (isCollapsedRepresentative ? $" · {GetGroupTabCount(group!.Id)} tabs collapsed" : string.Empty) +
            (tab.IsMuted ? " · Muted" : tab.IsPlayingAudio ? " · Playing audio" : ""));
        tab.Button.Invalidate();
    }

    private BrowserTab? GetCollapsedGroupRepresentative(Guid groupId) =>
        _activeTab is { IsClosing: false } active && active.GroupId == groupId
            ? active
            : _tabs.FirstOrDefault(tab => !tab.IsClosing && tab.GroupId == groupId);

    private int GetGroupTabCount(Guid groupId) => _tabs.Count(tab => !tab.IsClosing && tab.GroupId == groupId);

    private IEnumerable<BrowserTab> GetDisplayedTabs()
    {
        foreach (var tab in ActiveWorkspaceTabs)
        {
            var group = GetTabGroup(tab);
            if (group is not { IsCollapsed: true } || ReferenceEquals(tab, GetCollapsedGroupRepresentative(group.Id)))
                yield return tab;
        }
    }

    private void LayoutTabs()
    {
        var scale = GuiScale * DeviceDpi / 96f;
        var gap = Math.Max(4, (int)(6 * scale));
        var height = Math.Max(1, _tabsFlow.ClientSize.Height - gap * 2);
        _tabViewportWidth = Math.Max(1, _tabsFlow.ClientSize.Width);
        var preferredWidth = Math.Max(1, (int)(190 * scale));
        var minimumWidth = Math.Max(1, (int)(128 * scale));
        // Keep usable tab controls instead of collapsing every tab into a tiny
        // icon. Extra tabs deliberately overflow so the wheel can reveal them.
        var tabWidth = Math.Max(minimumWidth, preferredWidth);

        var displayedTabs = GetDisplayedTabs().ToArray();
        _tabContentWidth = gap + displayedTabs.Length * (tabWidth + gap);
        var maximumScroll = Math.Max(0, _tabContentWidth - _tabViewportWidth);
        _tabScrollOffset = Math.Clamp(_tabScrollOffset, 0, maximumScroll);

        var x = gap - _tabScrollOffset;
        foreach (var tab in _tabs)
            tab.Button.Visible = false;
        foreach (var tab in displayedTabs)
        {
            tab.Button.SetBounds(x, gap, tabWidth, height);
            tab.Button.Visible = x < _tabViewportWidth && x + tabWidth > 0;
            tab.Button.IsCompact = false;
            x += tabWidth + gap;
        }

        PositionNewTabButton();
    }

    private void LayoutTabStrip(Control titleArea)
    {
        var scale = GuiScale * DeviceDpi / 96f;
        var gap = Math.Max(4, (int)(6 * scale));
        var plusWidth = Math.Max(1, (int)(40 * scale));
        var preferredWorkspaceWidth = Math.Max(Ui(104), (int)(148 * scale));
        var workspaceWidth = _isPrivate ? 0 : Math.Min(preferredWorkspaceWidth,
            Math.Max(Ui(76), titleArea.ClientSize.Width / 3));
        var tabLeft = _isPrivate ? 0 : workspaceWidth + gap;
        var viewportWidth = Math.Max(1, titleArea.ClientSize.Width - tabLeft - plusWidth - gap * 2);

        _workspaceButton.Visible = !_isPrivate;
        if (!_isPrivate)
            _workspaceButton.SetBounds(gap, gap, workspaceWidth, Math.Max(1, titleArea.ClientSize.Height - gap * 2));
        _tabsFlow.SetBounds(tabLeft, 0, viewportWidth, titleArea.ClientSize.Height);
        LayoutTabs();
    }

    private void PositionNewTabButton()
    {
        if (_titleArea is null) return;

        var scale = GuiScale * DeviceDpi / 96f;
        var gap = Math.Max(4, (int)(6 * scale));
        var plusWidth = Math.Max(1, (int)(40 * scale));
        var viewportWidth = Math.Max(1, _tabViewportWidth);

        // Put + immediately after the last tab when the strip fits. Once tabs
        // overflow, keep + at the right edge so it remains easy to reach.
        var plusX = _tabContentWidth <= viewportWidth
            ? _tabsFlow.Left + Math.Max(gap, _tabContentWidth)
            : _tabsFlow.Left + viewportWidth + gap;
        _newTabButton.SetBounds(
            plusX,
            gap,
            plusWidth,
            Math.Max(1, _titleArea.ClientSize.Height - gap * 2));
        // Keep the control visible even during the first zero-size layout pass.
        // The final position is recalculated again when the form is shown.
        _newTabButton.Visible = true;
        _newTabButton.BringToFront();
        _newTabButton.Invalidate();
    }

    private Font CreateUiFont(float size)
    {
        return new Font("Segoe UI", size * GuiScale, FontStyle.Regular);
    }

    private Font CreateUrlFont(float size)
    {
        return new Font("Segoe UI", size * GuiScale, FontStyle.Regular);
    }

    private void EnsureTabVisible(BrowserTab tab)
    {
        if (!_tabs.Contains(tab) || !GetDisplayedTabs().Contains(tab) || _tabContentWidth <= _tabViewportWidth) return;

        var left = tab.Button.Left;
        var right = tab.Button.Right;
        if (left < 0)
            _tabScrollOffset += left;
        else if (right > _tabViewportWidth)
            _tabScrollOffset += right - _tabViewportWidth;

        LayoutTabs();
    }

    private void ScrollTabs(int delta)
    {
        if (_tabContentWidth <= _tabViewportWidth) return;

        var step = Math.Max(24, Math.Abs(delta) / 2);
        _tabScrollOffset = Math.Clamp(
            _tabScrollOffset - Math.Sign(delta) * step,
            0,
            _tabContentWidth - _tabViewportWidth);
        LayoutTabs();
    }

    private bool IsTabStripUnderCursor()
    {
        return _tabsFlow.Visible && _tabsFlow.IsHandleCreated &&
            _tabsFlow.RectangleToScreen(_tabsFlow.ClientRectangle).Contains(Cursor.Position);
    }

    private void BeginTabDrag(BrowserTab tab)
    {
        if (tab.IsClosing) return;
        if (!_selectedTabs.Contains(tab)) SelectTabFromPointer(tab, Keys.None);
        _draggedTab = tab;
        _draggedTabs.Clear();
        _draggedTabs.AddRange(GetTabsForOperation(tab).OrderBy(item => _tabs.IndexOf(item)));
        _tabsFlow.Cursor = Cursors.SizeAll;
        foreach (var dragged in _draggedTabs) dragged.Button.IsDragging = true;
    }

    private void MoveDraggedTab(BrowserTab tab, Point screenLocation)
    {
        if (!ReferenceEquals(_draggedTab, tab) || tab.IsClosing) return;

        var pointer = _tabsFlow.PointToClient(screenLocation);
        var remaining = GetDisplayedTabs().Where(item => !_draggedTabs.Contains(item)).ToList();
        var targetIndex = remaining.Count;
        var groupTarget = remaining.FirstOrDefault(item => item.Button.Bounds.Contains(pointer));
        for (var index = 0; index < remaining.Count; index++)
        {
            var candidate = remaining[index].Button;
            if (pointer.X < candidate.Left + candidate.Width / 2)
            {
                targetIndex = index;
                break;
            }
        }
        SetDragGroupTarget(groupTarget is null ? null : GetTabGroup(groupTarget));

        var workspaceTabs = _tabs.Where(item => item.WorkspaceId == tab.WorkspaceId).ToList();
        var moving = workspaceTabs.Where(item => _draggedTabs.Contains(item)).ToList();
        if (moving.Count == 0) return;
        workspaceTabs.RemoveAll(item => _draggedTabs.Contains(item));
        targetIndex = Math.Clamp(targetIndex, 0, workspaceTabs.Count);
        if (targetIndex <= workspaceTabs.Count)
            workspaceTabs.InsertRange(targetIndex, moving);
        ReplaceWorkspaceTabOrder(tab.WorkspaceId, workspaceTabs);
        LayoutTabs();
    }

    private void SetDragGroupTarget(TugleTabGroup? group)
    {
        var newTarget = group is null || _draggedTabs.All(tab => tab.GroupId == group.Id) ? null :
            _tabs.FirstOrDefault(tab => tab.GroupId == group.Id && tab.WorkspaceId == group.WorkspaceId && !tab.IsClosing);
        if (ReferenceEquals(_dragGroupTarget, newTarget)) return;
        if (_dragGroupTarget is not null) _dragGroupTarget.Button.DropTarget = false;
        _dragGroupTarget = newTarget;
        if (_dragGroupTarget is not null) _dragGroupTarget.Button.DropTarget = true;
    }

    private void EndTabDrag(BrowserTab tab)
    {
        if (!ReferenceEquals(_draggedTab, tab)) return;

        var targetGroupId = _dragGroupTarget is null ? null : GetTabGroup(_dragGroupTarget)?.Id;
        var draggedCopy = _draggedTabs.ToArray();
        if (_dragGroupTarget is not null) _dragGroupTarget.Button.DropTarget = false;
        _dragGroupTarget = null;
        _draggedTab = null;
        _tabsFlow.Cursor = Cursors.Default;
        foreach (var dragged in _draggedTabs) dragged.Button.IsDragging = false;
        if (targetGroupId is { } groupId)
            AssignTabsToGroup(draggedCopy, groupId);
        _draggedTabs.Clear();
        OrderPinnedTabs();
        CapturePreviousSession();
        SaveSettings();
    }

    private void ReplaceWorkspaceTabOrder(Guid workspaceId, IReadOnlyList<BrowserTab> ordered)
    {
        var cursor = 0;
        for (var index = 0; index < _tabs.Count; index++)
        {
            if (_tabs[index].WorkspaceId != workspaceId) continue;
            _tabs[index] = ordered[cursor++];
        }
    }

    private void UpdateAddressBar()
    {
        UpdateBookmarkButton();
        if (_activeTab is { DeferredNavigation: true, IsHome: false })
            _addressBar.Text = _activeTab.InitialNavigationTarget ?? string.Empty;
        else if (ActiveWebView?.Source is not null && ActiveWebView.Source.Scheme is "http" or "https")
            _addressBar.Text = ActiveWebView.Source.ToString();
        else
            _addressBar.Clear();
    }

    private void UpdateNavigationButtons()
    {
        // Keep both arrows enabled and bright so they never disappear into the dark chrome.
        _backButton.Enabled = true;
        _forwardButton.Enabled = true;
        _backButton.ForeColor = _iconVisible;
        _forwardButton.ForeColor = _iconVisible;
    }

    private int Ui(float logicalPixels)
    {
        return Math.Max(0, (int)Math.Round(logicalPixels * GuiScale * DeviceDpi / 96f));
    }

    private Padding UiPadding(int all)
    {
        return new Padding(Ui(all));
    }

    private Padding UiPadding(int left, int top, int right, int bottom)
    {
        return new Padding(Ui(left), Ui(top), Ui(right), Ui(bottom));
    }

    private void SetLoadingState(bool loading)
    {
        _reloadButton.ForeColor = loading ? _accent : _iconVisible;
    }

    private string BuildSearchUrl(string query) => SearchProvider.BuildUrl(_settings.SearchEngine, query);

    private static string GetVisitTitle(string url, string? pageTitle)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            if (SearchProvider.GetQuery(url) is { Length: > 0 } query) return $"Search: {query}";

            return string.IsNullOrWhiteSpace(pageTitle) ? uri.Host : pageTitle.Trim();
        }

        return string.IsNullOrWhiteSpace(pageTitle) ? "Recent site" : pageTitle.Trim();
    }

    private void RecordSearchFromNavigation(string url)
    {
        var query = SearchProvider.GetQuery(url);
        if (!string.IsNullOrWhiteSpace(query))
        {
            _history.RecordSearch(query);
            RefreshHistoryFlyoutIfVisible();
        }
    }

    private static bool IsHomeSource(string? source)
    {
        if (string.IsNullOrWhiteSpace(source)) return false;
        var homeUri = new Uri(Path.Combine(AppContext.BaseDirectory, "TugleHome.html")).AbsoluteUri;
        return string.Equals(source, homeUri, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsBlankPage(string? source)
    {
        return !string.IsNullOrWhiteSpace(source) &&
            source.StartsWith("about:blank", StringComparison.OrdinalIgnoreCase);
    }

    private static string TrimTitle(string title)
    {
        title = title.Trim();
        return title.Length > 24 ? title[..24] + "…" : title;
    }

    private void ToggleMaximize()
    {
        if (_f11FullscreenMode) ToggleFullscreen();
        MaximizedBounds = Screen.FromHandle(Handle).WorkingArea;
        WindowState = WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized;
        FitRestoredWindowToWorkingArea();
    }

    private void FitRestoredWindowToWorkingArea()
    {
        if (_correctingRestoredBounds || !IsHandleCreated || _f11FullscreenMode || WindowState != FormWindowState.Normal)
            return;

        var area = Screen.FromHandle(Handle).WorkingArea;
        var maximumWidth = Math.Max(MinimumSize.Width, area.Width - 80);
        var maximumHeight = Math.Max(MinimumSize.Height, area.Height - 80);
        if (Bounds.Width <= maximumWidth && Bounds.Height <= maximumHeight)
            return;

        _correctingRestoredBounds = true;
        try
        {
            var width = Math.Min(Bounds.Width, maximumWidth);
            var height = Math.Min(Bounds.Height, maximumHeight);
            Bounds = new Rectangle(
                area.Left + (area.Width - width) / 2,
                area.Top + (area.Height - height) / 2,
                width,
                height);
        }
        finally { _correctingRestoredBounds = false; }
    }

    private void ToggleFullscreen()
    {
        if (_f11FullscreenMode)
        {
            _f11FullscreenMode = false;
            TopMost = _f11PreviousTopMost;
            WindowState = FormWindowState.Normal;
            Bounds = _f11PreviousBounds;
            FitRestoredWindowToWorkingArea();
            if (_f11PreviousWindowState == FormWindowState.Maximized)
                WindowState = FormWindowState.Maximized;
        }
        else
        {
            _f11FullscreenMode = true;
            _f11PreviousWindowState = WindowState;
            _f11PreviousBounds = Bounds;
            _f11PreviousTopMost = TopMost;

            // Use the full monitor bounds so F11 covers the Windows taskbar while
            // preserving Tugle's own custom chrome.
            WindowState = FormWindowState.Normal;
            TopMost = true;
            SetWindowToScreen();
        }
        PerformLayout();
    }

    private void SetWindowToScreen()
    {
        var screenBounds = Screen.FromHandle(Handle).Bounds;
        Bounds = screenBounds;
        SetWindowPos(
            Handle,
            new IntPtr(-1),
            screenBounds.X,
            screenBounds.Y,
            screenBounds.Width,
            screenBounds.Height,
            SwpNoActivate | SwpFrameChanged);
    }

    private void RequestFullscreenToggle()
    {
        var now = DateTime.UtcNow;
        if (now - _lastFullscreenToggleUtc < TimeSpan.FromMilliseconds(350)) return;
        _lastFullscreenToggleUtc = now;
        ToggleFullscreen();
    }

    private void OnWindowActivated()
    {
        RegisterF11HotKey();
        if (!_f11FullscreenMode) return;

        TopMost = true;
        SetWindowToScreen();
    }

    private void OnWindowDeactivated()
    {
        UnregisterF11HotKey();

        // Let Alt+Tab and newly opened apps appear above Tugle. Fullscreen is
        // restored automatically when the user returns to this window.
        if (_f11FullscreenMode)
            TopMost = false;
    }

    private bool TryHandleBrowserShortcut(Keys keyData)
    {
        if (keyData == (Keys.Control | Keys.Shift | Keys.P))
        {
            BeginInvoke(ShowCommandPalette);
            return true;
        }

        if (keyData == (Keys.Control | Keys.Shift | Keys.S))
        {
            _ = ToggleSplitWithNextTabAsync();
            return true;
        }

        if (!_isPrivate && keyData == (Keys.Control | Keys.Shift | Keys.W))
        {
            BeginInvoke(ShowWorkspaceMenu);
            return true;
        }

        if (!_isPrivate && keyData == (Keys.Control | Keys.Alt | Keys.Left))
        {
            SwitchWorkspaceByOffset(-1);
            return true;
        }

        if (!_isPrivate && keyData == (Keys.Control | Keys.Alt | Keys.Right))
        {
            SwitchWorkspaceByOffset(1);
            return true;
        }

        if (!_isPrivate && (keyData & (Keys.Control | Keys.Alt)) == (Keys.Control | Keys.Alt))
        {
            var keyCode = keyData & Keys.KeyCode;
            if (keyCode is >= Keys.D1 and <= Keys.D9)
            {
                var index = (int)keyCode - (int)Keys.D1;
                if (index < _settings.Workspaces.Count)
                    _ = SwitchWorkspaceAsync(_settings.Workspaces[index].Id);
                return true;
            }
        }

        if (keyData == (Keys.Control | Keys.T))
        {
            _ = RequestNewTabAsync();
            return true;
        }

        if (keyData == (Keys.Control | Keys.J))
        {
            BeginInvoke(ShowDownloadsMenu);
            return true;
        }

        if (keyData == (Keys.Control | Keys.H))
        {
            BeginInvoke(ShowHistoryMenu);
            return true;
        }

        if (keyData == (Keys.Control | Keys.D))
        {
            ToggleCurrentBookmark();
            if (!_isPrivate) BeginInvoke(ShowLibraryMenu);
            return true;
        }

        if (keyData == (Keys.Control | Keys.Shift | Keys.T))
        {
            _ = ReopenClosedTabAsync();
            return true;
        }

        if (keyData == (Keys.Control | Keys.Shift | Keys.N))
        {
            OpenPrivateWindow();
            return true;
        }

        if (keyData == (Keys.Control | Keys.P))
        {
            _ = SavePageAsPdfAsync();
            return true;
        }

        if (keyData == (Keys.Control | Keys.L))
        {
            _addressBar.Focus();
            _addressBar.SelectAll();
            return true;
        }

        if (keyData == (Keys.Control | Keys.W))
        {
            if (_activeTab is not null) CloseTab(_activeTab);
            return true;
        }

        if (keyData == Keys.F11)
        {
            RequestFullscreenToggle();
            return true;
        }

        return false;
    }

    private void SwitchWorkspaceByOffset(int offset)
    {
        if (_isPrivate || _settings.Workspaces.Count < 2) return;
        var currentIndex = _settings.Workspaces.FindIndex(workspace => workspace.Id == ActiveWorkspaceId);
        if (currentIndex < 0) currentIndex = 0;
        var nextIndex = (currentIndex + offset + _settings.Workspaces.Count) % _settings.Workspaces.Count;
        _ = SwitchWorkspaceAsync(_settings.Workspaces[nextIndex].Id);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        return TryHandleBrowserShortcut(keyData) || base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        base.OnFormClosing(e);
        if (e.Cancel) return;

        _suspendTimer.Stop();
        Application.RemoveMessageFilter(_tabWheelFilter);
        if (!_isPrivate)
        {
            CapturePreviousSession();
            SaveSettings();
        }
    }

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int x,
        int y,
        int cx,
        int cy,
        uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private void DragTitle(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left || e.Clicks > 1) return;
        if (_f11FullscreenMode) ToggleFullscreen();
        ReleaseCapture();
        SendMessage(Handle, 0xA1, (IntPtr)2, IntPtr.Zero);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WmHotKey && m.WParam.ToInt32() == F11HotKeyId)
        {
            RequestFullscreenToggle();
            return;
        }

        base.WndProc(ref m);
        if (m.Msg != 0x84 || WindowState != FormWindowState.Normal || _f11FullscreenMode) return;

        var packed = m.LParam.ToInt64();
        var point = PointToClient(new Point(
            (short)(packed & 0xffff),
            (short)((packed >> 16) & 0xffff)));
        var edge = Padding.Left;
        bool left = point.X < edge;
        bool right = point.X >= ClientSize.Width - edge;
        bool top = point.Y < edge;
        bool bottom = point.Y >= ClientSize.Height - edge;
        int hit = top && left ? 13 : top && right ? 14 : bottom && left ? 16 : bottom && right ? 17 :
            left ? 10 : right ? 11 : top ? 12 : bottom ? 15 : 0;
        if (hit != 0) m.Result = (IntPtr)hit;
    }

    private void RegisterF11HotKey()
    {
        if (IsHandleCreated && !_f11HotKeyRegistered)
            _f11HotKeyRegistered = RegisterHotKey(Handle, F11HotKeyId, 0, (uint)Keys.F11);
    }

    private void UnregisterF11HotKey()
    {
        if (IsHandleCreated && _f11HotKeyRegistered)
        {
            UnregisterHotKey(Handle, F11HotKeyId);
            _f11HotKeyRegistered = false;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            UnregisterF11HotKey();
            _suspendTimer.Dispose();
            _bookmarkIconCancellation.Cancel();
            _history.Dispose();
            _tooltips.Dispose();
            foreach (var tab in _tabs)
            {
                tab.FaviconImage?.Dispose();
                tab.View.Dispose();
            }
            _homeTabIcon?.Dispose();
            _privateFonts.Dispose();
        }
        base.Dispose(disposing);
    }
}

internal sealed class TabDragEventArgs(Point screenLocation) : EventArgs
{
    public Point ScreenLocation { get; } = screenLocation;
}

internal enum NavigationIcon
{
    Back,
    Forward,
    Reload,
    Home
}

internal sealed class CloseGlyphButton : Button
{
    private bool _hovered;

    public float UiScale { get; set; } = 0.9f;

    public CloseGlyphButton()
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        UseVisualStyleBackColor = false;
        Cursor = Cursors.Hand;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(Parent?.BackColor ?? TugleTheme.Current.Chrome);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

        var diameter = Math.Min(Ui(30), Math.Min(Width, Height) - Ui(6));
        if (diameter <= 0) return;

        var circle = new Rectangle((Width - diameter) / 2, (Height - diameter) / 2, diameter, diameter);
        if (_hovered)
        {
            using var fill = new SolidBrush(TugleTheme.Current.Danger);
            e.Graphics.FillEllipse(fill, circle);
        }

        var centerX = circle.Left + circle.Width / 2F;
        var centerY = circle.Top + circle.Height / 2F;
        var arm = Ui(5);
        using var pen = new Pen(_hovered ? Color.White : TugleTheme.Current.Icon, Math.Max(1.6F, Ui(1.7F)))
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round
        };
        e.Graphics.DrawLine(pen, centerX - arm, centerY - arm, centerX + arm, centerY + arm);
        e.Graphics.DrawLine(pen, centerX + arm, centerY - arm, centerX - arm, centerY + arm);
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        _hovered = true;
        base.OnMouseEnter(e);
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hovered = false;
        base.OnMouseLeave(e);
        Invalidate();
    }

    private int Ui(float logicalPixels)
    {
        return Math.Max(1, (int)Math.Round(logicalPixels * UiScale * DeviceDpi / 96f));
    }
}

internal sealed class WorkspaceSwitchButton : Button
{
    public float UiScale { get; set; } = 0.9f;
    public string WorkspaceName
    {
        get => _workspaceName;
        set
        {
            _workspaceName = string.IsNullOrWhiteSpace(value) ? "Personal" : value.Trim();
            Invalidate();
        }
    }
    public string WorkspaceIcon
    {
        get => _workspaceIcon;
        set
        {
            _workspaceIcon = string.IsNullOrWhiteSpace(value) ? "●" : value.Trim();
            Invalidate();
        }
    }
    public Color WorkspaceColor
    {
        get => _workspaceColor;
        set
        {
            _workspaceColor = value;
            Invalidate();
        }
    }

    private string _workspaceName = "Personal";
    private string _workspaceIcon = "●";
    private Color _workspaceColor = Color.FromArgb(96, 149, 229);
    private bool _hovered;

    public WorkspaceSwitchButton()
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        UseVisualStyleBackColor = false;
        Text = string.Empty;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var theme = TugleTheme.Current;
        e.Graphics.Clear(Parent?.BackColor ?? BackColor);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var bounds = new RectangleF(.5f, .5f, Math.Max(0, Width - 1), Math.Max(0, Height - 1));
        var radius = Math.Min(Ui(10), bounds.Height / 2f);
        using var path = new GraphicsPath();
        path.AddArc(bounds.Left, bounds.Top, radius * 2, radius * 2, 180, 90);
        path.AddArc(bounds.Right - radius * 2, bounds.Top, radius * 2, radius * 2, 270, 90);
        path.AddArc(bounds.Right - radius * 2, bounds.Bottom - radius * 2, radius * 2, radius * 2, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - radius * 2, radius * 2, radius * 2, 90, 90);
        path.CloseFigure();
        using var fill = new SolidBrush(_hovered ? theme.SurfaceHover : theme.Surface);
        using var border = new Pen(_hovered ? theme.BorderStrong : theme.Border);
        e.Graphics.FillPath(fill, path);
        e.Graphics.DrawPath(border, path);

        var dot = new Rectangle(Ui(9), Math.Max(Ui(4), (Height - Ui(12)) / 2), Ui(12), Ui(12));
        using var dotFill = new SolidBrush(WorkspaceColor.IsEmpty ? theme.Accent : WorkspaceColor);
        e.Graphics.FillEllipse(dotFill, dot);
        var iconBounds = new Rectangle(dot.Right + Ui(6), 0, Ui(16), Height);
        TextRenderer.DrawText(e.Graphics, WorkspaceIcon, Font, iconBounds, ForeColor,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        var chevronWidth = Ui(16);
        var textBounds = new Rectangle(iconBounds.Right + Ui(3), 0,
            Math.Max(1, Width - iconBounds.Right - chevronWidth - Ui(10)), Height);
        TextRenderer.DrawText(e.Graphics, WorkspaceName, Font, textBounds, ForeColor,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        var chevronX = Width - Ui(12);
        var centerY = Height / 2f;
        using var chevron = new Pen(theme.Icon, Math.Max(1f, Ui(1.2f)))
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round
        };
        e.Graphics.DrawLine(chevron, chevronX - Ui(3), centerY - Ui(2), chevronX, centerY + Ui(1));
        e.Graphics.DrawLine(chevron, chevronX, centerY + Ui(1), chevronX + Ui(3), centerY - Ui(2));
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        _hovered = true;
        base.OnMouseEnter(e);
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hovered = false;
        base.OnMouseLeave(e);
        Invalidate();
    }

    private int Ui(float logicalPixels) => Math.Max(1, (int)Math.Round(logicalPixels * UiScale * DeviceDpi / 96f));
}

internal sealed class TabButton : Button
{
    public float UiScale { get; set; } = 0.9f;

    public bool Active { get; set; }
    public bool Selected
    {
        get => _selected;
        set
        {
            if (_selected == value) return;
            _selected = value;
            Invalidate();
        }
    }
    public bool Muted { get; set; }
    public bool PlayingAudio { get; set; }
    private bool ShowMute => PlayingAudio || Muted;
    public Color GroupColor
    {
        get => _groupColor;
        set
        {
            if (_groupColor.ToArgb() == value.ToArgb()) return;
            _groupColor = value;
            Invalidate();
        }
    }
    public bool Pinned
    {
        get => _pinned;
        set
        {
            if (_pinned == value) return;
            _pinned = value;
            Invalidate();
        }
    }
    public bool IsCompact
    {
        get => _isCompact;
        set
        {
            if (_isCompact == value) return;
            _isCompact = value;
            Invalidate();
        }
    }
    public bool IsDragging { get; set; }
    public bool DropTarget
    {
        get => _dropTarget;
        set
        {
            if (_dropTarget == value) return;
            _dropTarget = value;
            Invalidate();
        }
    }
    public Image? Favicon
    {
        get => _favicon;
        set
        {
            if (ReferenceEquals(_favicon, value)) return;
            _favicon = value;
            Invalidate();
        }
    }
    public event EventHandler? CloseRequested;
    public event EventHandler? MuteRequested;
    public event EventHandler? DragStarted;
    public event EventHandler<TabDragEventArgs>? DragMoved;
    public event EventHandler? DragEnded;

    private Image? _favicon;
    private Color _groupColor = Color.Empty;
    private bool _hovered;
    private bool _hoverClose;
    private bool _hoverMute;
    private bool _closingPress;
    private bool _mutePress;
    private bool _dragging;
    private bool _isCompact;
    private bool _pinned;
    private bool _selected;
    private bool _dropTarget;
    private Point _dragStart;

    private Rectangle CloseBounds
    {
        get
        {
            var closeSize = Ui(IsCompact ? 24 : 28);
            var rightInset = Ui(IsCompact ? 4 : 8);
            return new(Math.Max(0, Width - rightInset - closeSize), Math.Max(2, (Height - closeSize) / 2), closeSize, closeSize);
        }
    }

    private Rectangle MuteBounds
    {
        get
        {
            if (!ShowMute) return Rectangle.Empty;
            var size = Ui(26);
            return new(Math.Max(0, CloseBounds.Left - Ui(1) - size), Math.Max(0, (Height - size) / 2), size, size);
        }
    }

    // Keep the workspace accent away from the close/mute controls so it never
    // changes their hit target or makes the tab strip visually busy.
    public Rectangle GroupIndicatorBounds
    {
        get
        {
            if (GroupColor.IsEmpty) return Rectangle.Empty;
            var left = Ui(10);
            var right = Math.Max(left + Ui(12), CloseBounds.Left - Ui(8));
            return new Rectangle(left, Ui(3), Math.Max(1, right - left), Math.Max(1, Ui(2)));
        }
    }

    public TabButton()
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        UseVisualStyleBackColor = false;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(Parent?.BackColor ?? TugleTheme.Current.Chrome);

        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var bounds = new RectangleF(.5f, .5f, Width - 1, Height - 1);
        var diameter = Math.Min(Ui(24), bounds.Height);
        if (diameter <= 0) return;

        using var path = new GraphicsPath();
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();

        var theme = TugleTheme.Current;
        var fillColor = Active ? theme.ActiveTab : Selected ? theme.Selection : _hovered ? theme.TabHover : theme.Surface;
        if (IsDragging) fillColor = theme.TabDragging;
        using var fill = new SolidBrush(fillColor);
        using var border = new Pen(Active ? theme.BorderStrong : theme.Border);
        e.Graphics.FillPath(fill, path);
        e.Graphics.DrawPath(border, path);
        if (DropTarget)
        {
            using var dropBorder = new Pen(GroupColor.IsEmpty ? theme.Accent : GroupColor, Math.Max(1f, Ui(2)));
            e.Graphics.DrawPath(dropBorder, path);
        }

        var groupIndicator = GroupIndicatorBounds;
        if (!groupIndicator.IsEmpty)
        {
            using var groupPen = new Pen(GroupColor, Math.Max(1f, groupIndicator.Height))
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round
            };
            var indicatorY = groupIndicator.Top + groupIndicator.Height / 2f;
            e.Graphics.DrawLine(groupPen, groupIndicator.Left, indicatorY, groupIndicator.Right, indicatorY);
        }

        var iconSize = Math.Min(Ui(IsCompact ? 14 : 16), Math.Max(1, Height - Ui(10)));
        var iconBounds = new Rectangle(Ui(IsCompact ? 6 : 12), (Height - iconSize) / 2, iconSize, iconSize);
        if (Favicon is not null)
        {
            var interpolation = e.Graphics.InterpolationMode;
            e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            e.Graphics.DrawImage(Favicon, iconBounds);
            e.Graphics.InterpolationMode = interpolation;
        }
        else
        {
            using var sitePen = new Pen(theme.Icon, Math.Max(1f, Ui(1)));
            e.Graphics.DrawEllipse(sitePen, iconBounds);
            e.Graphics.DrawArc(sitePen, iconBounds.Left + iconBounds.Width / 4, iconBounds.Top,
                iconBounds.Width / 2, iconBounds.Height, 90, 180);
            e.Graphics.DrawLine(sitePen, iconBounds.Left + Ui(2), iconBounds.Top + iconBounds.Height / 2,
                iconBounds.Right - Ui(2), iconBounds.Top + iconBounds.Height / 2);
        }

        if (!IsCompact)
        {
            var titleLeft = iconBounds.Right + Ui(7);
            if (Pinned)
            {
                var pinX = titleLeft + Ui(3);
                var pinY = Height / 2f;
                using var pinPen = new Pen(theme.Accent, Math.Max(1f, Ui(1.2f)))
                {
                    StartCap = LineCap.Round,
                    EndCap = LineCap.Round
                };
                e.Graphics.DrawEllipse(pinPen, pinX, pinY - Ui(4), Ui(6), Ui(5));
                e.Graphics.DrawLine(pinPen, pinX + Ui(3), pinY + Ui(1), pinX + Ui(3), pinY + Ui(6));
                titleLeft += Ui(13);
            }
            var titleRight = ShowMute ? MuteBounds.Left : CloseBounds.Left;
            var titleBounds = new Rectangle(titleLeft, 0, Math.Max(1, titleRight - titleLeft - Ui(6)), Height);
            TextRenderer.DrawText(
                e.Graphics,
                Text,
                Font,
                titleBounds,
                ForeColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        }

        if (ShowMute) DrawAudioGlyph(e.Graphics, theme);

        if (_hoverClose)
        {
            using var closeFill = new SolidBrush(theme.Danger);
            e.Graphics.FillEllipse(closeFill, CloseBounds);
        }

        var cx = CloseBounds.Left + CloseBounds.Width / 2f;
        var cy = Height / 2f;
        var arm = Ui(4);
        using var closePen = new Pen(_hoverClose ? Color.White : theme.Icon, Math.Max(1.2f, 1.6f * UiScale * DeviceDpi / 96f)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        e.Graphics.DrawLine(closePen, cx - arm, cy - arm, cx + arm, cy + arm);
        e.Graphics.DrawLine(closePen, cx + arm, cy - arm, cx - arm, cy + arm);
    }

    private void DrawAudioGlyph(Graphics graphics, ThemePalette theme)
    {
        var mute = MuteBounds;
        if (_hoverMute)
        {
            using var hoverFill = new SolidBrush(theme.Selection);
            graphics.FillEllipse(hoverFill, mute);
        }
        var state = graphics.Save();
        var scale = 18f / 24f * UiScale * DeviceDpi / 96f;
        graphics.TranslateTransform(mute.Left + mute.Width / 2f - 12 * scale, mute.Top + mute.Height / 2f - 12 * scale);
        graphics.ScaleTransform(scale, scale);
        using (var mutePen = new Pen(Muted ? theme.Accent : theme.Icon, 1.8f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round
        })
        {
            graphics.DrawPolygon(mutePen, (PointF[])[new(3, 9), new(7, 9), new(12, 5), new(12, 19), new(7, 15), new(3, 15)]);
            if (Muted)
            {
                graphics.DrawLine(mutePen, 16, 9, 21, 15);
                graphics.DrawLine(mutePen, 21, 9, 16, 15);
            }
            else
            {
                graphics.DrawArc(mutePen, 10, 8, 7, 8, -60, 120);
                graphics.DrawArc(mutePen, 10, 4, 12, 16, -60, 120);
            }
        }

        graphics.Restore(state);
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        _hovered = true;
        base.OnMouseEnter(e);
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hovered = false;
        _hoverClose = false;
        _hoverMute = false;
        Cursor = Cursors.Default;
        base.OnMouseLeave(e);
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        _mutePress = e.Button == MouseButtons.Left && MuteBounds.Contains(e.Location);
        _closingPress = !_mutePress && e.Button == MouseButtons.Left && CloseBounds.Contains(e.Location);
        if (_closingPress || _mutePress) Capture = true;
        if (!_closingPress && !_mutePress && e.Button == MouseButtons.Left)
            _dragStart = e.Location;
        if (!_closingPress && !_mutePress) base.OnMouseDown(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var closeHover = CloseBounds.Contains(e.Location);
        var muteHover = !closeHover && MuteBounds.Contains(e.Location);
        if (_hoverClose != closeHover || _hoverMute != muteHover)
        {
            _hoverClose = closeHover;
            _hoverMute = muteHover;
            Cursor = closeHover || muteHover ? Cursors.Hand : Cursors.Default;
            Invalidate();
        }
        base.OnMouseMove(e);
        if (_dragging)
        {
            if (e.Button == MouseButtons.Left)
                DragMoved?.Invoke(this, new TabDragEventArgs(PointToScreen(e.Location)));
            return;
        }

        if (_closingPress || _mutePress || e.Button != MouseButtons.Left) return;

        var deltaX = Math.Abs(e.X - _dragStart.X);
        var deltaY = Math.Abs(e.Y - _dragStart.Y);
        if (deltaX < SystemInformation.DragSize.Width && deltaY < SystemInformation.DragSize.Height) return;

        _dragging = true;
        Capture = true;
        IsDragging = true;
        Invalidate();
        DragStarted?.Invoke(this, EventArgs.Empty);
        DragMoved?.Invoke(this, new TabDragEventArgs(PointToScreen(e.Location)));
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (_closingPress)
        {
            Capture = false;
            var shouldClose = e.Button == MouseButtons.Left && CloseBounds.Contains(e.Location);
            _closingPress = false;
            if (shouldClose) CloseRequested?.Invoke(this, EventArgs.Empty);
            return;
        }

        if (_mutePress)
        {
            Capture = false;
            var shouldMute = e.Button == MouseButtons.Left && MuteBounds.Contains(e.Location);
            _mutePress = false;
            if (shouldMute) MuteRequested?.Invoke(this, EventArgs.Empty);
            return;
        }

        if (_dragging)
        {
            _dragging = false;
            Capture = false;
            IsDragging = false;
            Invalidate();
            DragEnded?.Invoke(this, EventArgs.Empty);
            return;
        }

        base.OnMouseUp(e);
    }

    private int Ui(float logicalPixels)
    {
        return Math.Max(1, (int)Math.Round(logicalPixels * UiScale * DeviceDpi / 96f));
    }
}

internal sealed class TabAddButton : Button
{
    public float UiScale { get; set; } = 0.9f;

    private bool _hovered;

    public TabAddButton()
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        UseVisualStyleBackColor = false;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(Parent?.BackColor ?? TugleTheme.Current.Chrome);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var bounds = new RectangleF(.5f, .5f, Width - 1, Height - 1);
        var diameter = Math.Min(Ui(20), bounds.Height);
        if (diameter <= 0) return;

        using var path = new GraphicsPath();
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();

        using var fill = new SolidBrush(_hovered ? TugleTheme.Current.SurfaceHover : TugleTheme.Current.Surface);
        using var border = new Pen(TugleTheme.Current.Border);
        e.Graphics.FillPath(fill, path);
        e.Graphics.DrawPath(border, path);
        var cx = Width / 2f;
        var cy = Height / 2f;
        var arm = Ui(5);
        using var pen = new Pen(ForeColor, 1.6f);
        e.Graphics.DrawLine(pen, cx - arm, cy, cx + arm, cy);
        e.Graphics.DrawLine(pen, cx, cy - arm, cx, cy + arm);
    }

    private int Ui(float logicalPixels)
    {
        return Math.Max(1, (int)Math.Round(logicalPixels * UiScale * DeviceDpi / 96f));
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        _hovered = true;
        base.OnMouseEnter(e);
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hovered = false;
        base.OnMouseLeave(e);
        Invalidate();
    }
}

internal abstract class ToolbarActionButton : Button
{
    private bool _hovered;

    public float UiScale { get; set; } = 0.9f;

    protected ToolbarActionButton()
    {
        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw,
            true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        UseVisualStyleBackColor = false;
        Cursor = Cursors.Hand;
    }

    protected int Ui(float logicalPixels)
    {
        return Math.Max(1, (int)Math.Round(logicalPixels * UiScale * DeviceDpi / 96f));
    }

    protected void PaintGlyph(PaintEventArgs e, ToolbarGlyph glyph)
    {
        PaintActionBackground(e);
        var state = e.Graphics.Save();
        try
        {
            // Keep geometry and stroke widths on one fractional 24-unit grid.
            // Rounding each coordinate separately distorts small / compact icons.
            var scale = Math.Min(UiScale * DeviceDpi / 96f, Math.Min(Width, Height) / 26f);
            if (scale <= 0) return;
            e.Graphics.TranslateTransform(Width / 2f - 12f * scale, Height / 2f - 12f * scale);
            e.Graphics.ScaleTransform(scale, scale);
            ToolbarGlyphs.Draw(e.Graphics, glyph, ForeColor, Enabled);
        }
        finally
        {
            e.Graphics.Restore(state);
        }
    }

    protected void PaintActionBackground(PaintEventArgs e)
    {
        e.Graphics.Clear(BackColor);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        if (!_hovered) return;

        using var hover = new SolidBrush(TugleTheme.Current.SurfaceHover);
        using var path = CreateRoundedPath(new RectangleF(.5f, .5f, Width - 1, Height - 1), Ui(12));
        e.Graphics.FillPath(hover, path);
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        _hovered = true;
        base.OnMouseEnter(e);
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hovered = false;
        base.OnMouseLeave(e);
        Invalidate();
    }

    private static GraphicsPath CreateRoundedPath(RectangleF bounds, float diameter)
    {
        var path = new GraphicsPath();
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}

internal enum ToolbarGlyph
{
    Back,
    Forward,
    Reload,
    Home,
    History,
    GuiScale,
    Theme,
    Bookmark,
    Download,
    Account
}

internal static class ToolbarGlyphs
{
    public static void Draw(Graphics graphics, ToolbarGlyph glyph, Color foreground, bool enabled)
    {
        var color = enabled ? foreground : Color.FromArgb(110, foreground);
        using var pen = new Pen(color, 1.75f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round
        };
        using var path = new GraphicsPath();

        switch (glyph)
        {
            case ToolbarGlyph.Back:
            case ToolbarGlyph.Forward:
            {
                // Mirrored geometry keeps both navigation arrows optically identical.
                var direction = glyph == ToolbarGlyph.Back ? 1f : -1f;
                PointF Point(float x, float y) => new(12f + (x - 12f) * direction, y);
                path.AddLines((PointF[])[Point(10, 6), Point(4, 12), Point(10, 18)]);
                path.StartFigure();
                path.AddLine(Point(4, 12), Point(20, 12));
                break;
            }
            case ToolbarGlyph.Reload:
                // Two continuous curved arrows, with clear gaps between their ends.
                path.AddBezier(4, 9, 6.1f, 3.4f, 15.5f, 1.6f, 20, 9);
                path.AddLine(20, 9, 20, 4.5f);
                path.StartFigure();
                path.AddLine(15.5f, 9, 20, 9);
                path.StartFigure();
                path.AddBezier(20, 15, 17.9f, 20.6f, 8.5f, 22.4f, 4, 15);
                path.AddLine(4, 15, 4, 19.5f);
                path.StartFigure();
                path.AddLine(4, 15, 8.5f, 15);
                break;
            case ToolbarGlyph.Home:
                path.AddLines((PointF[])[new(3, 10.5f), new(12, 3), new(21, 10.5f)]);
                path.StartFigure();
                path.AddLines((PointF[])[
                    new(5.5f, 8.5f), new(5.5f, 20), new(9.5f, 20),
                    new(9.5f, 13.5f), new(14.5f, 13.5f), new(14.5f, 20),
                    new(18.5f, 20), new(18.5f, 8.5f)
                ]);
                break;
            case ToolbarGlyph.History:
                path.AddEllipse(3.5f, 3.5f, 17, 17);
                path.StartFigure();
                path.AddLines((PointF[])[new(12, 7), new(12, 12), new(16, 14.5f)]);
                break;
            case ToolbarGlyph.GuiScale:
                // Draw the letterforms as outlines so Aa shares the icon stroke and
                // baseline at every DPI, independent of font hinting / substitution.
                path.AddLines((PointF[])[new(2.5f, 18), new(7.5f, 5.5f), new(12.5f, 18)]);
                path.StartFigure();
                path.AddLine(4.2f, 13.75f, 10.8f, 13.75f);
                path.StartFigure();
                path.AddBezier(20.5f, 12, 18.9f, 9.3f, 14.5f, 10, 14.5f, 14.2f);
                path.AddBezier(14.5f, 14.2f, 14.5f, 18.4f, 18.9f, 19.1f, 20.5f, 16.4f);
                path.StartFigure();
                path.AddLine(20.5f, 10.5f, 20.5f, 18);
                break;
            case ToolbarGlyph.Theme:
            {
                path.AddEllipse(3.5f, 3.5f, 17, 17);
                var theme = TugleTheme.Current;
                // Center each swatch on the same grid, leaving equal rim clearance.
                DrawSwatch(9, 9, theme.Accent);
                DrawSwatch(15, 9, theme.BorderStrong);
                DrawSwatch(9, 15, theme.Detail);
                void DrawSwatch(float x, float y, Color swatch)
                {
                    using var fill = new SolidBrush(enabled ? swatch : Color.FromArgb(110, swatch));
                    graphics.FillEllipse(fill, x - 1.5f, y - 1.5f, 3, 3);
                }
                break;
            }
            case ToolbarGlyph.Download:
                path.AddLine(12, 3.5f, 12, 14.5f);
                path.StartFigure();
                path.AddLines((PointF[])[new(7.5f, 10), new(12, 14.5f), new(16.5f, 10)]);
                path.StartFigure();
                path.AddLine(4, 15.5f, 4, 18.5f);
                path.AddBezier(4, 18.5f, 4, 19.3f, 4.7f, 20, 5.5f, 20);
                path.AddLine(5.5f, 20, 18.5f, 20);
                path.AddBezier(18.5f, 20, 19.3f, 20, 20, 19.3f, 20, 18.5f);
                path.AddLine(20, 18.5f, 20, 15.5f);
                break;
            case ToolbarGlyph.Bookmark:
                path.AddLines((PointF[])[
                    new(12, 3), new(14.7f, 8.4f), new(20.7f, 9.3f), new(16.35f, 13.55f),
                    new(17.38f, 19.55f), new(12, 16.72f), new(6.62f, 19.55f), new(7.65f, 13.55f),
                    new(3.3f, 9.3f), new(9.3f, 8.4f), new(12, 3)
                ]);
                break;
            case ToolbarGlyph.Account:
                path.AddEllipse(4, 3.5f, 16, 16);
                path.StartFigure();
                path.AddEllipse(9, 7, 6, 6);
                path.StartFigure();
                path.AddBezier(7, 18, 7.5f, 14.5f, 16.5f, 14.5f, 17, 18);
                break;
        }

        graphics.DrawPath(pen, path);
    }
}

internal sealed class NavigationButton(NavigationIcon icon) : ToolbarActionButton
{
    protected override void OnPaint(PaintEventArgs e) => PaintGlyph(e, icon switch
    {
        NavigationIcon.Back => ToolbarGlyph.Back,
        NavigationIcon.Forward => ToolbarGlyph.Forward,
        NavigationIcon.Reload => ToolbarGlyph.Reload,
        NavigationIcon.Home => ToolbarGlyph.Home,
        _ => throw new ArgumentOutOfRangeException(nameof(icon))
    });
}

internal sealed class HistoryButton : ToolbarActionButton
{
    protected override void OnPaint(PaintEventArgs e) => PaintGlyph(e, ToolbarGlyph.History);
}

internal sealed class GuiScaleButton : ToolbarActionButton
{
    protected override void OnPaint(PaintEventArgs e) => PaintGlyph(e, ToolbarGlyph.GuiScale);
}

internal sealed class ThemeButton : ToolbarActionButton
{
    protected override void OnPaint(PaintEventArgs e) => PaintGlyph(e, ToolbarGlyph.Theme);
}

internal sealed class BookmarkButton : ToolbarActionButton
{
    protected override void OnPaint(PaintEventArgs e) => PaintGlyph(e, ToolbarGlyph.Bookmark);
}

internal sealed class DownloadButton : ToolbarActionButton
{
    protected override void OnPaint(PaintEventArgs e) => PaintGlyph(e, ToolbarGlyph.Download);
}

internal sealed class AccountButton : ToolbarActionButton
{
    protected override void OnPaint(PaintEventArgs e) => PaintGlyph(e, ToolbarGlyph.Account);
}

internal sealed class FlyoutItemButton : Button
{
    private bool _hovered;

    public float UiScale { get; set; } = 0.9f;
    public bool Selected { get; set; }
    public bool Destructive { get; set; }
    public bool Prominent { get; set; }
    public Color? SwatchColor { get; set; }
    public string ValueText { get; set; } = string.Empty;
    public bool CenterText { get; set; }

    public FlyoutItemButton()
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        UseVisualStyleBackColor = false;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(Parent?.BackColor ?? TugleTheme.Current.Chrome);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var bounds = new RectangleF(.5f, .5f, Width - 1, Height - 1);
        var diameter = Math.Min(Ui(12), bounds.Height);
        if (diameter <= 0) return;

        if (Prominent || Selected || _hovered)
        {
            using var path = CreateRoundedPath(bounds, diameter);
            using var fill = new SolidBrush(
                Prominent
                    ? (_hovered ? TugleTheme.Current.ProminentHover : TugleTheme.Current.Prominent)
                    : Destructive
                    ? Color.FromArgb(55, TugleTheme.Current.Danger)
                    : Selected ? TugleTheme.Current.Selection : TugleTheme.Current.SurfaceHover);
            e.Graphics.FillPath(fill, path);
        }

        var textLeft = SwatchColor.HasValue ? Ui(34) : Ui(10);
        if (SwatchColor.HasValue)
        {
            using var swatch = new SolidBrush(SwatchColor.Value);
            e.Graphics.FillEllipse(swatch, new Rectangle(Ui(10), (Height - Ui(14)) / 2, Ui(14), Ui(14)));
        }

        var valueWidth = string.IsNullOrEmpty(ValueText) ? 0 : TextRenderer.MeasureText(ValueText, Font).Width + Ui(16);
        var textBounds = new Rectangle(textLeft, 0, Math.Max(1, Width - textLeft - Ui(10) - valueWidth), Height);
        var textColor = Prominent
            ? TugleTheme.Current.ProminentText
            : Destructive
            ? TugleTheme.Current.DangerText
            : Enabled ? ForeColor : TugleTheme.Current.Muted;
        TextRenderer.DrawText(
            e.Graphics,
            Text,
            Font,
            textBounds,
            textColor,
            (CenterText ? TextFormatFlags.HorizontalCenter : TextFormatFlags.Left) | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        if (valueWidth > 0)
            TextRenderer.DrawText(e.Graphics, ValueText, Font,
                new Rectangle(Width - valueWidth - Ui(10), 0, valueWidth, Height), TugleTheme.Current.Muted,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        if (Focused && ShowFocusCues)
            ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(ClientRectangle, -Ui(4), -Ui(4)), ForeColor, BackColor);
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        _hovered = true;
        base.OnMouseEnter(e);
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hovered = false;
        base.OnMouseLeave(e);
        Invalidate();
    }

    private GraphicsPath CreateRoundedPath(RectangleF bounds, float diameter)
    {
        var path = new GraphicsPath();
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    private int Ui(float logicalPixels)
    {
        return Math.Max(1, (int)Math.Round(logicalPixels * UiScale * DeviceDpi / 96f));
    }
}

internal sealed class FlyoutSectionLabel : Control
{
    public float UiScale { get; set; } = 0.9f;
    public string LabelText { get; set; } = string.Empty;
    public string DetailText { get; set; } = string.Empty;

    public FlyoutSectionLabel()
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(Parent?.BackColor ?? TugleTheme.Current.Chrome);
        using var labelFont = new Font("Segoe UI", Math.Max(8F, Ui(10)), FontStyle.Bold, GraphicsUnit.Pixel);
        using var detailFont = new Font("Segoe UI", Math.Max(8F, Ui(10)), FontStyle.Regular, GraphicsUnit.Pixel);
        var left = new Rectangle(Ui(2), 0, Math.Max(1, Width - Ui(100)), Height);
        var right = new Rectangle(Math.Max(Ui(2), Width - Ui(104)), 0, Ui(100), Height);
        TextRenderer.DrawText(
            e.Graphics, LabelText, labelFont, left, TugleTheme.Current.Section,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        TextRenderer.DrawText(
            e.Graphics, DetailText, detailFont, right, TugleTheme.Current.Section,
            TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
    }

    private int Ui(float logicalPixels)
    {
        return Math.Max(1, (int)Math.Round(logicalPixels * UiScale * DeviceDpi / 96f));
    }
}

internal sealed class HistoryFlyoutItem : Button
{
    private bool _hovered;
    private Image? _siteIcon;

    public float UiScale { get; set; } = 0.9f;
    public string Detail { get; set; } = string.Empty;
    public string SiteName { get; set; } = string.Empty;
    public int TrailingSpace { get; set; }
    public Image? SiteIcon
    {
        get => _siteIcon;
        set
        {
            if (ReferenceEquals(_siteIcon, value)) return;
            _siteIcon?.Dispose();
            _siteIcon = value;
            Invalidate();
        }
    }

    public HistoryFlyoutItem()
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        UseVisualStyleBackColor = false;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(Parent?.BackColor ?? TugleTheme.Current.Chrome);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var bounds = new RectangleF(.5f, .5f, Width - 1, Height - 1);
        if (bounds.Width <= 1 || bounds.Height <= 1) return;

        if (_hovered)
        {
            using var path = CreateRoundedPath(bounds, Ui(12));
            using var fill = new SolidBrush(TugleTheme.Current.SurfaceHover);
            e.Graphics.FillPath(fill, path);
        }

        var iconSize = Ui(26);
        var iconBounds = new Rectangle(Ui(10), Math.Max(Ui(5), (Height - iconSize) / 2), iconSize, iconSize);
        if (SiteIcon is not null)
        {
            var interpolation = e.Graphics.InterpolationMode;
            e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            e.Graphics.DrawImage(SiteIcon, iconBounds);
            e.Graphics.InterpolationMode = interpolation;
        }
        else
        {
            using var iconFill = new SolidBrush(GetFallbackColor(SiteName));
            using var iconPath = CreateRoundedPath(iconBounds, Ui(8));
            e.Graphics.FillPath(iconFill, iconPath);
            using var monogramFont = new Font("Segoe UI", Math.Max(8F, Ui(11)), FontStyle.Bold, GraphicsUnit.Pixel);
            var monogram = string.IsNullOrWhiteSpace(SiteName) ? "?" : SiteName[..1].ToUpperInvariant();
            TextRenderer.DrawText(
                e.Graphics,
                monogram,
                monogramFont,
                iconBounds,
                Color.White,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }

        var titleLeft = iconBounds.Right + Ui(10);
        var titleBounds = new Rectangle(titleLeft, Ui(7), Math.Max(1, Width - titleLeft - Ui(12 + TrailingSpace)), Ui(22));
        var detailBounds = new Rectangle(titleLeft, Ui(30), Math.Max(1, Width - titleLeft - Ui(12 + TrailingSpace)), Ui(18));
        TextRenderer.DrawText(
            e.Graphics, Text, Font, titleBounds, Enabled ? ForeColor : TugleTheme.Current.Muted,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        using var detailFont = new Font(Font.FontFamily, Math.Max(8F, Font.SizeInPoints - 2F), FontStyle.Regular);
        TextRenderer.DrawText(
            e.Graphics, Detail, detailFont, detailBounds, TugleTheme.Current.Detail,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        _hovered = true;
        base.OnMouseEnter(e);
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hovered = false;
        base.OnMouseLeave(e);
        Invalidate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _siteIcon?.Dispose();
            _siteIcon = null;
        }
        base.Dispose(disposing);
    }

    private static Color GetFallbackColor(string value)
    {
        unchecked
        {
            uint hash = 2166136261;
            foreach (var character in value)
                hash = (hash ^ character) * 16777619;

            var palette = new[]
            {
                Color.FromArgb(51, 112, 145),
                Color.FromArgb(92, 78, 145),
                Color.FromArgb(133, 83, 100),
                Color.FromArgb(52, 126, 112),
                Color.FromArgb(127, 99, 53)
            };
            return palette[hash % (uint)palette.Length];
        }
    }

    private static GraphicsPath CreateRoundedPath(RectangleF bounds, float diameter)
    {
        var path = new GraphicsPath();
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    private int Ui(float logicalPixels)
    {
        return Math.Max(1, (int)Math.Round(logicalPixels * UiScale * DeviceDpi / 96f));
    }
}

internal sealed class DownloadFlyoutItem : Button
{
    private bool _hovered;
    private bool _suppressClick;
    private Point _dragStart;
    private Image? _fileIcon;

    public float UiScale { get; set; } = 0.9f;
    public string Detail { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public Image? FileIcon
    {
        get => _fileIcon;
        set
        {
            if (ReferenceEquals(_fileIcon, value)) return;
            _fileIcon?.Dispose();
            _fileIcon = value;
            Invalidate();
        }
    }

    public DownloadFlyoutItem()
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        UseVisualStyleBackColor = false;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(Parent?.BackColor ?? TugleTheme.Current.Chrome);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var bounds = new RectangleF(.5f, .5f, Width - 1, Height - 1);
        if (bounds.Width <= 1 || bounds.Height <= 1) return;

        if (_hovered && Enabled)
        {
            using var path = CreateRoundedPath(bounds, Ui(12));
            using var fill = new SolidBrush(TugleTheme.Current.SurfaceHover);
            e.Graphics.FillPath(fill, path);
        }

        var iconSize = Ui(28);
        var iconBounds = new Rectangle(Ui(10), Math.Max(Ui(5), (Height - iconSize) / 2), iconSize, iconSize);
        if (FileIcon is not null)
        {
            var interpolation = e.Graphics.InterpolationMode;
            e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            e.Graphics.DrawImage(FileIcon, iconBounds);
            e.Graphics.InterpolationMode = interpolation;
        }
        else
        {
            using var iconFill = new SolidBrush(TugleTheme.Current.IconBackground);
            using var iconPen = new Pen(TugleTheme.Current.Icon, Math.Max(1F, Ui(1.2F)));
            using var iconPath = CreateRoundedPath(iconBounds, Ui(6));
            e.Graphics.FillPath(iconFill, iconPath);
            e.Graphics.DrawPath(iconPen, iconPath);
            var fold = Ui(7);
            e.Graphics.DrawLine(iconPen, iconBounds.Right - fold, iconBounds.Top, iconBounds.Right, iconBounds.Top + fold);
            e.Graphics.DrawLine(iconPen, iconBounds.Left + Ui(7), iconBounds.Top + Ui(15), iconBounds.Right - Ui(6), iconBounds.Top + Ui(15));
            e.Graphics.DrawLine(iconPen, iconBounds.Left + Ui(7), iconBounds.Top + Ui(20), iconBounds.Right - Ui(6), iconBounds.Top + Ui(20));
        }

        var titleLeft = iconBounds.Right + Ui(10);
        var titleBounds = new Rectangle(titleLeft, Ui(7), Math.Max(1, Width - titleLeft - Ui(12)), Ui(22));
        var detailBounds = new Rectangle(titleLeft, Ui(30), Math.Max(1, Width - titleLeft - Ui(12)), Ui(18));
        var titleColor = ForeColor;
        TextRenderer.DrawText(
            e.Graphics, Text, Font, titleBounds, titleColor,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        using var detailFont = new Font(Font.FontFamily, Math.Max(8F, Font.SizeInPoints - 2F), FontStyle.Regular);
        TextRenderer.DrawText(
            e.Graphics, Detail, detailFont, detailBounds, Enabled ? TugleTheme.Current.Detail : Color.FromArgb(112, 123, 140),
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        _hovered = true;
        base.OnMouseEnter(e);
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hovered = false;
        base.OnMouseLeave(e);
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left && Enabled)
            _dragStart = e.Location;
        base.OnMouseDown(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!Enabled || e.Button != MouseButtons.Left || string.IsNullOrWhiteSpace(FilePath) || !File.Exists(FilePath)) return;

        var deltaX = Math.Abs(e.X - _dragStart.X);
        var deltaY = Math.Abs(e.Y - _dragStart.Y);
        if (deltaX < SystemInformation.DragSize.Width && deltaY < SystemInformation.DragSize.Height) return;

        _suppressClick = true;
        var data = new DataObject();
        data.SetData(DataFormats.FileDrop, new[] { FilePath });
        DoDragDrop(data, DragDropEffects.Copy | DragDropEffects.Move);
    }

    protected override void OnClick(EventArgs e)
    {
        if (_suppressClick)
        {
            _suppressClick = false;
            return;
        }
        base.OnClick(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _fileIcon?.Dispose();
            _fileIcon = null;
        }
        base.Dispose(disposing);
    }

    private static GraphicsPath CreateRoundedPath(RectangleF bounds, float diameter)
    {
        var path = new GraphicsPath();
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    private int Ui(float logicalPixels)
    {
        return Math.Max(1, (int)Math.Round(logicalPixels * UiScale * DeviceDpi / 96f));
    }
}

internal sealed class RoundedFlyoutPanel : Panel
{
    public float UiScale { get; set; } = 0.9f;

    public RoundedFlyoutPanel()
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
    }

    public void RefreshShape()
    {
        UpdateShape();
        Invalidate();
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        UpdateShape();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
    }

    private void UpdateShape()
    {
        if (Width <= 1 || Height <= 1) return;

        using var path = CreateRoundedPath(ClientRectangle, Ui(14));
        var previous = Region;
        Region = new Region(path);
        previous?.Dispose();
    }

    private GraphicsPath CreateRoundedPath(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        var diameter = Math.Min(Math.Max(2, radius), Math.Min(bounds.Width, bounds.Height));
        var rectangle = new RectangleF(.5f, .5f, Math.Max(1, bounds.Width - 1), Math.Max(1, bounds.Height - 1));
        path.AddArc(rectangle.Left, rectangle.Top, diameter, diameter, 180, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Top, diameter, diameter, 270, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rectangle.Left, rectangle.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    private int Ui(float logicalPixels)
    {
        return Math.Max(1, (int)Math.Round(logicalPixels * UiScale * DeviceDpi / 96f));
    }
}

internal sealed class RoundedSurface : Panel
{
    public float UiScale { get; set; } = 0.9f;

    public bool FocusedField { get; set; }

    public RoundedSurface()
    {
        DoubleBuffered = true;
        BackColor = TugleTheme.Current.Chrome;
        ResizeRedraw = true;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var bounds = new RectangleF(.5f, .5f, Width - 1, Height - 1);
        var diameter = Math.Min(Ui(24), bounds.Height);
        if (diameter <= 0) return;

        using var path = new GraphicsPath();
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();

        using var fill = new SolidBrush(TugleTheme.Current.Surface);
        using var border = new Pen(
            FocusedField ? TugleTheme.Current.BorderStrong : TugleTheme.Current.Border);
        e.Graphics.FillPath(fill, path);
        e.Graphics.DrawPath(border, path);
    }

    private int Ui(float logicalPixels)
    {
        return Math.Max(1, (int)Math.Round(logicalPixels * UiScale * DeviceDpi / 96f));
    }
}

internal sealed class TugleColorTable : ProfessionalColorTable
{
    public override Color ToolStripDropDownBackground => TugleTheme.Current.Surface;
    public override Color MenuBorder => TugleTheme.Current.Border;
    public override Color MenuItemBorder => TugleTheme.Current.BorderStrong;
    public override Color MenuItemSelected => TugleTheme.Current.Selection;
    public override Color MenuItemSelectedGradientBegin => TugleTheme.Current.Selection;
    public override Color MenuItemSelectedGradientEnd => TugleTheme.Current.Selection;
    public override Color MenuItemPressedGradientBegin => TugleTheme.Current.ActiveTab;
    public override Color MenuItemPressedGradientMiddle => TugleTheme.Current.ActiveTab;
    public override Color MenuItemPressedGradientEnd => TugleTheme.Current.ActiveTab;
    public override Color SeparatorDark => TugleTheme.Current.Border;
    public override Color SeparatorLight => TugleTheme.Current.Surface;
}
