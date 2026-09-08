using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using System.Text.Json;
using System.Runtime.InteropServices;
using System.Diagnostics;

namespace Tugle;

internal sealed class FirstRunSetupForm : Form
{
    public const int CurrentVersion = 7;
    private readonly WebView2 _view = new() { Dock = DockStyle.Fill, DefaultBackgroundColor = Color.FromArgb(248, 249, 252) };
    private readonly Func<Task<CoreWebView2Environment>> _getEnvironment;
    private readonly string _pageUrl = new Uri(Path.Combine(AppContext.BaseDirectory, "TugleSetup.html")).AbsoluteUri;
    private bool _signingIn;
    private bool _picking;
    private readonly Dictionary<string, string> _media = new();
    private ThemePalette? _customTheme;
    public bool ConnectGoogle { get; private set; }
    public ThemePalette SelectedTheme { get; private set; } = ThemePalettes.Ocean;
    public string SelectedThemeName => SelectedTheme.Name;
    public string? SelectedBackgroundMediaUrl { get; private set; }
    public string SelectedBackgroundMode { get; private set; } = "gradient";
    public Color SelectedBackground { get; private set; } = Color.FromArgb(7, 21, 38);
    public Color SelectedBackgroundSecondary { get; private set; } = Color.FromArgb(32, 61, 91);

    public FirstRunSetupForm(Func<Task<CoreWebView2Environment>> getEnvironment, ThemePalette initialTheme)
    {
        _getEnvironment = getEnvironment;
        SelectedTheme = initialTheme;
        if (initialTheme.Name == "Custom") _customTheme = initialTheme;
        var palette = initialTheme;
        _view.DefaultBackgroundColor = palette.ContentBackground;
        Text = "Tugle setup";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(1060, 780);
        MinimumSize = new Size(560, 500);
        FormBorderStyle = FormBorderStyle.Sizable;
        WindowState = FormWindowState.Maximized;
        KeyPreview = true;
        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.F11)
            {
                if (FormBorderStyle == FormBorderStyle.None)
                {
                    FormBorderStyle = FormBorderStyle.Sizable;
                    WindowState = FormWindowState.Maximized;
                }
                else
                {
                    WindowState = FormWindowState.Normal;
                    FormBorderStyle = FormBorderStyle.None;
                    Bounds = Screen.FromHandle(Handle).Bounds;
                }
                e.Handled = true;
            }
        };
        BackColor = _view.DefaultBackgroundColor;
        HandleCreated += (_, _) =>
        {
            var dark = 1;
            DwmSetWindowAttribute(Handle, 20, ref dark, sizeof(int));
            var chrome = palette.Chrome.R | (palette.Chrome.G << 8) | (palette.Chrome.B << 16);
            DwmSetWindowAttribute(Handle, 35, ref chrome, sizeof(int));
        };
        Font = new Font("Segoe UI", 10);
        var icon = Path.Combine(AppContext.BaseDirectory, "assets", "tugle-icon.ico");
        if (File.Exists(icon)) Icon = new Icon(icon);
        Controls.Add(_view);
        Shown += async (_, _) => await InitializeAsync();
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

    private static object PaletteData(ThemePalette t) => new { name = t.Name, accent = TugleSettings.ToHex(t.Accent), chrome = TugleSettings.ToHex(t.Chrome), surface = TugleSettings.ToHex(t.Surface), background = TugleSettings.ToHex(t.ContentBackground), text = TugleSettings.ToHex(t.Text), muted = TugleSettings.ToHex(t.Muted), border = TugleSettings.ToHex(t.Border) };
    private void Post(object data) => _view.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(data));
    private ThemePalette? FindTheme(string? name) => name == "Custom" ? _customTheme : ThemePalettes.ColorOptions.FirstOrDefault(t => t.Name == name);
    private void UpdateTitleColor()
    {
        var color = SelectedTheme.Chrome;
        var chrome = color.R | (color.G << 8) | (color.B << 16);
        DwmSetWindowAttribute(Handle, 35, ref chrome, sizeof(int));
    }

    private void PickColor(JsonElement root, bool theme)
    {
        if (_picking) return;
        var target = theme ? "theme" : root.GetProperty("target").GetString();
        if (target is not ("theme" or "primary" or "secondary")) return;
        _picking = true;
        try
        {
            var initial = theme ? SelectedTheme.Accent : TugleSettings.FromHex(root.GetProperty("color").GetString(), SelectedBackground);
            using var dialog = new ColorPickerDialog(initial, theme ? "Custom theme color" : "Background color", SelectedTheme);
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            if (theme)
            {
                _customTheme = ThemePalette.FromAccent("Custom", "Custom accent color", dialog.SelectedColor);
                SelectedTheme = _customTheme;
                UpdateTitleColor();
                Post(new { type = "customTheme", theme = PaletteData(_customTheme) });
            }
            else Post(new { type = "color", target, color = TugleSettings.ToHex(dialog.SelectedColor) });
        }
        finally { _picking = false; }
    }

    private void PickMedia(JsonElement root)
    {
        if (_picking) return;
        var mode = root.GetProperty("mode").GetString();
        if (mode is not ("image" or "video")) return;
        _picking = true;
        try
        {
            using var dialog = new OpenFileDialog
            {
                Title = mode == "video" ? "Choose a background video" : "Choose a background picture",
                Filter = mode == "video" ? "Video files|*.mp4;*.webm;*.ogg;*.mov;*.m4v" : "Picture files|*.png;*.jpg;*.jpeg;*.gif;*.webp;*.bmp",
                Multiselect = false, CheckFileExists = true, CheckPathExists = true
            };
            if (dialog.ShowDialog(this) != DialogResult.OK || !File.Exists(dialog.FileName)) return;
            var url = new Uri(dialog.FileName).AbsoluteUri;
            _media[mode] = url;
            Post(new { type = "media", mode, url, name = Path.GetFileName(dialog.FileName) });
        }
        finally { _picking = false; }
    }

    private async Task InitializeAsync()
    {
        try
        {
            await _view.EnsureCoreWebView2Async(await _getEnvironment());
            if (IsDisposed) return;
            var core = _view.CoreWebView2;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.AreDevToolsEnabled = false;
            core.NavigationStarting += (_, e) => { if (e.Uri != _pageUrl) e.Cancel = true; };
            core.NewWindowRequested += (_, e) => e.Handled = true;
            core.WebMessageReceived += ReceiveMessage;
            core.Navigate(_pageUrl);
        }
        catch (Exception ex)
        {
            if (IsDisposed) return;
            MessageBox.Show(this, "Setup could not start. Please reopen Tugle to try again.\n\n" + ex.Message, "Tugle setup", MessageBoxButtons.OK, MessageBoxIcon.Information);
            Close();
        }
    }

    private void ReceiveMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (e.Source != _pageUrl || IsDisposed) return;
        var json = e.WebMessageAsJson;
        // Leave the WebView2 callback before opening a modal native picker.
        BeginInvoke(new Action(async () => await ProcessMessageAsync(json)));
    }

    private async Task ProcessMessageAsync(string json)
    {
        if (IsDisposed) return;
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            switch (root.GetProperty("action").GetString())
            {
                case "ready":
                    Post(new { type = "themes", themes = ThemePalettes.ColorOptions.Select(PaletteData), custom = _customTheme is null ? null : PaletteData(_customTheme), selected = SelectedThemeName });
                    ConnectGoogle = await GoogleSession.IsConnectedAsync(_view.CoreWebView2);
                    if (!IsDisposed) Post(new { type = "account", connected = ConnectGoogle, initial = true });
                    break;
                case "close":
                    Close();
                    break;
                case "theme":
                    var name = root.GetProperty("name").GetString();
                    var selected = FindTheme(name);
                    if (selected is not null) { SelectedTheme = selected; UpdateTitleColor(); }
                    break;
                case "pickTheme":
                    PickColor(root, true);
                    break;
                case "pickColor":
                    PickColor(root, false);
                    break;
                case "pickMedia":
                    PickMedia(root);
                    break;
                case "google":
                    if (_signingIn) return;
                    _signingIn = true;
                    try
                    {
                        using var signIn = new GoogleExternalSignInForm();
                        signIn.ShowDialog(this);
                        // The system browser keeps its own cookie jar. Only report a
                        // real Tugle session as connected; the handoff itself never
                        // pretends to authenticate the app.
                        ConnectGoogle = await GoogleSession.IsConnectedAsync(_view.CoreWebView2);
                        if (!IsDisposed)
                            _view.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new
                            {
                                type = "account",
                                connected = ConnectGoogle,
                                message = signIn.Returned ? "Return to Tugle to continue" : ""
                            }));
                    }
                    finally { _signingIn = false; }
                    break;
                case "finish":
                    var themeName = root.GetProperty("theme").GetString();
                    var finishTheme = FindTheme(themeName);
                    if (finishTheme is null) return;
                    var background = root.GetProperty("background");
                    var mode = background.GetProperty("mode").GetString();
                    if (mode is not ("solid" or "gradient" or "image" or "video")) return;
                    if (mode is "image" or "video")
                    {
                        if (!_media.TryGetValue(mode, out var url) || !File.Exists(new Uri(url).LocalPath))
                        { Post(new { type = "error", message = "Choose a file first." }); return; }
                        SelectedBackgroundMediaUrl = url;
                    }
                    else SelectedBackgroundMediaUrl = null;
                    SelectedTheme = finishTheme;
                    SelectedBackgroundMode = mode;
                    SelectedBackground = ColorTranslator.FromHtml(background.GetProperty("primary").GetString()!);
                    SelectedBackgroundSecondary = ColorTranslator.FromHtml(background.GetProperty("secondary").GetString()!);
                    DialogResult = DialogResult.OK;
                    Close();
                    break;
            }
        }
        catch (Exception ex)
        {
            if (!IsDisposed)
            {
                Post(new { type = "error", message = "Please try again." });
                MessageBox.Show(this, "That step could not be completed. Please try again.\n\n" + ex.Message, "Tugle setup");
            }
        }
    }
}

internal static class GoogleSession
{
    internal static bool IsGoogleUrl(string? source) =>
        Uri.TryCreate(source, UriKind.Absolute, out var uri) && uri.Scheme == "https" &&
        (uri.Host.Equals("google.com", StringComparison.OrdinalIgnoreCase) ||
         uri.Host.EndsWith(".google.com", StringComparison.OrdinalIgnoreCase));

    internal static async Task<bool> IsConnectedAsync(CoreWebView2 core)
    {
        var cookies = await core.CookieManager.GetCookiesAsync("https://accounts.google.com/");
        // Companion/preference cookies alone do not establish a signed-in session.
        return cookies.Any(c => (c.Name is "SID" or "__Secure-1PSID" or "__Secure-3PSID") &&
            !string.IsNullOrWhiteSpace(c.Value) && (c.IsSession || c.Expires > DateTime.UtcNow));
    }
}

internal sealed class GoogleExternalSignInForm : Form
{
    private const string SignInUrl = "https://accounts.google.com/ServiceLogin?continue=https%3A%2F%2Fmyaccount.google.com%2F&service=accountsettings";
    public bool Returned { get; private set; }

    public GoogleExternalSignInForm()
    {
        Text = "Google sign-in · Tugle";
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(460, 230);
        MinimumSize = new Size(420, 210);
        Font = new Font("Segoe UI", 10);
        BackColor = Color.FromArgb(24, 29, 40);
        ForeColor = Color.FromArgb(235, 240, 248);
        var message = new Label
        {
            Dock = DockStyle.Fill,
            Text = "Google opened in your browser.\nReturn here when you are done.",
            TextAlign = ContentAlignment.MiddleCenter,
            Padding = new Padding(24),
            Font = new Font("Segoe UI", 11)
        };
        var footer = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 62,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(10),
            BackColor = Color.FromArgb(24, 29, 40)
        };
        var done = new Button { Text = "Continue", Width = 110, Height = 36, FlatStyle = FlatStyle.Flat };
        var cancel = new Button { Text = "Back", Width = 90, Height = 36, FlatStyle = FlatStyle.Flat };
        done.Click += (_, _) => { Returned = true; DialogResult = DialogResult.OK; Close(); };
        cancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        footer.Controls.Add(done);
        footer.Controls.Add(cancel);
        Controls.Add(message);
        Controls.Add(footer);
        Shown += (_, _) =>
        {
            try
            {
                Process.Start(new ProcessStartInfo { FileName = SignInUrl, UseShellExecute = true });
            }
            catch
            {
                message.Text = "Could not open Google.\nClose this window and try again.";
                done.Enabled = false;
            }
        };
    }
}
