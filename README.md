# Tugle

Tugle is the first prototype of a lightweight, single-tab Windows browser.

## What is in this prototype

- A Firefox-inspired dark browser toolbar
- One active tab named Tugle
- Address bar that accepts URLs or search text
- Back, forward, reload, and home buttons
- History menu with the 12 most recent visits (`Ctrl+H`)
- GUI-scale menu for compact through large browser chrome (80% to 120%)
- Downloads menu with active and completed-download status (`Ctrl+J`)
- Accounts panel with Google sign-in handoff and connection status
- Update checks through GitHub Releases
- Three-step first-run setup: Google account, theme, and background
- Persistent settings and a persistent WebView2 profile across restarts
- A clean Tugle start page
- A privacy indicator
- A small built-in blocker for common advertising and analytics hosts

The start page is stored locally in `TugleHome.html` and loaded as a normal page. This avoids injecting a large image-embedded HTML string into WebView2, which caused the earlier blank-page bug.

## Search engine: Google

Google is the current search destination. Tugle does not have its own search engine yet, so text that is not a URL is sent to Google. Direct URLs still open directly. The search provider can be made configurable later.

The search behavior is in `MainForm.cs`, inside `NavigateFromAddressBar()`.

## Technology

- C# and .NET 8 Windows Forms
- Microsoft Edge WebView2 for displaying websites
- One WebView2 instance, keeping the first prototype simple and avoiding extra tab processes
- Generated Tugle app icon in `assets\tugle-icon.png` and `assets\tugle-icon.ico`

## Could Tugle use C++?

Yes. WebView2 supports native Win32 C++ applications. C++ may be a good long-term choice if Tugle needs deeper control over Windows integration, process behavior, or a custom browser engine. For this first polish pass, C# keeps the interface changes faster to build and easier to iterate. C++ by itself would not guarantee lower RAM usage because most memory is used by the web engine and the pages being displayed.

## Memory and startup approach

Tugle keeps memory use down by having no preloading, suspending inactive tabs, and using a persistent single profile with lightweight WebView2 startup flags. The web pages themselves can still use significant memory; video-heavy sites and large web apps are controlled by the page, not the toolbar.

The first-run setup stores its choices in `%LOCALAPPDATA%\Tugle\settings.json`. Appearance, GUI scale, and start-page background choices are saved there as well. Website cookies and sign-ins stay in `%LOCALAPPDATA%\Tugle\WebView2Profile`.

Tugle starts maximized and supports native Windows snapping, resizing, and the maximize-button Snap Layouts menu. F11 toggles taskbar-covering fullscreen and restores the previous window state. Setup starts maximized with standard Windows window controls. Its Google → Theme → Background steps keep one kind of choice on each page, with a live home-page preview beside the current controls or above them in narrower windows.

Setup version 6 uses the main browser palette, Tugle branding, and minimal text. Theme has its own page with named color choices and a custom color picker. Background has its own page with Color, Gradient, Picture, and Video options beside a live home-page preview. Custom theme accents and selected media paths persist across restarts. Closing setup leaves it incomplete so it appears again on next launch. Done saves appearance before opening the home page. Existing history and website sessions are preserved.

Google sign-in opens in the system browser instead of an embedded WebView. Setup provides an explicit Continue button after the user returns. Existing Google sessions already present in Tugle are recognized, but system-browser cookies are intentionally not copied into WebView2. Full Google account linking would require a registered OAuth desktop client and is not claimed by this handoff. No Google password is collected by setup.

Tugle checks `TaiFradl/Tugle` GitHub Releases after launch. A newer release offers its portable ZIP download; the same check is available from Accounts → Check for updates.

For isolated development checks, `TUGLE_PROFILE_DIRECTORY` can point to a separate profile folder. Settings, history, and WebView2 data then use that folder. Leave it unset for normal use.

## Accounts and Google sign-in

The Accounts toolbar button opens Google in the system browser and shows the account panel when you return. Tugle does not collect or store a Google password. Existing sessions inside Tugle are detected from its own WebView2 profile; system-browser cookies are not copied into Tugle. A future sync service would need an explicit provider and separate encryption design.

## Ad blocking status

The current blocker is intentionally small and built into the prototype. It blocks a short list of known advertising and analytics hosts before their resources load. It is not yet a complete uBlock-style filter engine, and it does not yet download or update filter lists.

## Design

The current interface is dark from top to bottom: dark browser chrome, dark address bar, dark start page, mint/blue accent colors, and icon-style navigation controls.

## Run Tugle

Double-click:

`exe\Tugle.exe`

Or run this from PowerShell in the project folder:

```powershell
dotnet run --project .\\Tugle.csproj
```

Microsoft Edge WebView2 Runtime must be installed on the computer.

## Current limitations

- Single tab only
- No bookmarks or full-history page yet
- No filter-list updater yet
- No installer yet
- No custom search-engine setting yet

## Next sensible step

Polish the toolbar and start page after visual feedback, then replace the small host list with a maintained filter-list system while measuring memory use against Edge.
