# Tugle

Tugle is a lightweight Windows browser with a focused, custom interface.

## What is in this prototype

- A Firefox-inspired dark browser toolbar
- Browser tabs with inactive-tab suspension
- Address bar that accepts URLs or search text
- Back, forward, reload, and home buttons
- History menu with the 12 most recent visits (`Ctrl+H`)
- Searchable local bookmarks with page icons, editing, removal, and open-in-new-tab controls (`Ctrl+D` saves the current page). Small icon thumbnails are cached locally. Former Read later links remain available as bookmarks.
- Pinned tabs and recently closed tabs (`Ctrl+Shift+T`)
- GUI-scale menu for compact through large browser chrome (70% to 140%)
- Downloads menu with progress plus pause, resume, and cancel controls when the runtime supports them (`Ctrl+J`)
- Private windows (`Ctrl+Shift+N`), browser-data cleanup, tracking-prevention levels, and visible blocker status
- PDF saving (`Ctrl+P`) and configurable address-bar search
- Two-section Settings panel with Google website sign-in and browser preferences
- Update checks through GitHub Releases
- Three-step first-run setup: Google account, theme, and background
- Persistent settings and a persistent WebView2 profile across restarts
- A clean Tugle start page
- A privacy indicator
- Full uBlock Origin filtering with a conservative built-in fallback
- Saved custom theme colors

## Workspaces

Workspaces are separate tab sets. Click the workspace pill in the tab bar or press `Ctrl+Shift+W` to switch; each row shows its current tab count. Use `New workspace…` for a simple workspace, or choose a template such as School, Work, or Research. The `Manage current workspace` menu handles its name, color, icon, startup page, and preferences. Right-click a tab and choose `Move to workspace` to move one or several selected tabs. Less common tools such as automatic grouping, site routing, and backups are under `More workspace settings`.

`Ctrl+Alt+Left/Right` moves between workspaces, and `Ctrl+Alt+1` through `Ctrl+Alt+9` opens a workspace directly. Workspaces do not share tabs, but they continue to use the same browser profile and saved bookmarks.

The start page is stored locally in `TugleHome.html` and loaded as a normal page. This avoids injecting a large image-embedded HTML string into WebView2, which caused the earlier blank-page bug.

## Search engine

Text that is not a URL is sent to the selected search provider. Settings → Browser settings → Search engine offers Google, DuckDuckGo, or Bing. Home and the address bar use the same choice. Brave and custom-engine settings from older versions migrate to Google without resetting other preferences. Direct URLs still open directly. Google autocomplete is only requested when Google is selected; DuckDuckGo and Bing use local suggestions.

The search behavior is in `MainForm.cs`, inside `NavigateFromAddressBar()`.

## Technology

- C# and .NET 8 Windows Forms
- Microsoft Edge WebView2 for displaying websites
- One WebView2 instance per open tab
- Generated Tugle app icon in `assets\tugle-icon.png` and `assets\tugle-icon.ico`

## Could Tugle use C++?

Yes. WebView2 supports native Win32 C++ applications. C++ may be a good long-term choice if Tugle needs deeper control over Windows integration, process behavior, or a custom browser engine. For this first polish pass, C# keeps the interface changes faster to build and easier to iterate. C++ by itself would not guarantee lower RAM usage because most memory is used by the web engine and the pages being displayed.

## Memory and startup approach

Tugle keeps memory use down by having no preloading, suspending inactive tabs, and using a persistent single profile with lightweight WebView2 startup flags. The web pages themselves can still use significant memory; video-heavy sites and large web apps are controlled by the page, not the toolbar.

The first-run setup stores its choices in `%LOCALAPPDATA%\Tugle\settings.json`. Appearance, GUI scale, and start-page background choices are saved there as well. Website cookies and sign-ins stay in `%LOCALAPPDATA%\Tugle\WebView2Profile`.

Tugle starts maximized and supports native Windows snapping, resizing, and the maximize-button Snap Layouts menu. F11 toggles taskbar-covering fullscreen and restores the previous window state. Setup starts maximized with standard Windows window controls. Its Google → Theme → Background steps keep one kind of choice on each page, with a live home-page preview beside the current controls or above them in narrower windows.

Setup version 7 uses the main browser palette, Tugle branding, and minimal text. Theme has its own page with named color choices and a custom color picker. Background has its own page with Color, Gradient, Picture, and Video options beside a live home-page preview. Custom theme accents and selected media paths persist across restarts. Closing setup leaves it incomplete so it appears again on next launch. Done saves appearance before opening the home page. Existing history and website sessions are preserved.

Google sign-in opens inside Tugle using its persistent website profile. Setup advances only when that profile has a Google authentication cookie. Closing the sign-in window does not mark the account as connected. Google may refuse sign-in in an embedded browser; Tugle shows this restriction and lets setup continue without signing in. It does not spoof another browser or copy system-browser cookies.

Tugle checks `TaiFradl/Tugle` GitHub Releases after launch. Installed copies download a newer Windows installer in the background and ask to restart when it is ready; the installer closes Tugle, preserves the profile, and launches the updated app. Portable copies open the release download instead, since a running portable folder cannot safely replace itself. The same check is available from Settings → Browser settings → Check for updates.

Pushing a tag such as `v2.3.2` builds and publishes both `Tugle-Setup.exe` and `Tugle-browser.zip` through the GitHub Actions release workflow. Update `RELEASE_NOTES.md` before tagging.

For isolated development checks, `TUGLE_PROFILE_DIRECTORY` can point to a separate profile folder. Settings, history, and WebView2 data then use that folder. Leave it unset for normal use.

## Accounts and Google sign-in

Settings has two top-level choices: Google account and Browser settings. Google account opens or reuses a normal Tugle tab for signing in to Google websites, managing an existing session, or signing out of this profile. Connection status is checked from Tugle's own WebView2 cookies rather than a saved boolean. This is website sign-in, not a Tugle sync account. Credentials are entered only on Google's HTTPS page, never in a Tugle password form. A fresh private window keeps its Google session separate from the regular profile. Google can still block embedded-browser sign-in; this is not bypassed or represented as a successful connection.

## Ad blocking

Tugle loads its packaged uBlock Origin extension for filter-list based blocking. If a WebView2 runtime cannot load extensions, the browser still blocks requests to a conservative list of dedicated advertising hosts and never blocks page documents.

## Launching Tugle

Run `Tugle-Setup.exe` for the normal installation. It installs Tugle for the current Windows user, adds a Start-menu shortcut, and creates a desktop shortcut by default. It does not delete `%LOCALAPPDATA%\Tugle`, so an upgrade or uninstall keeps the user’s settings, tab session, history, and WebView2 website data.

The portable ZIP remains available for people who prefer to run Tugle without installing it. Windows requires the user to choose taskbar pinning; after installation, right-click Tugle in Start and choose **Pin to taskbar** if wanted.

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

## Regression checks

Run `dotnet run --project tests/Tugle.Checks.csproj -- --live` on Windows. The checks use a fresh temporary profile and a local test server, validate bookmark migration and visit counts, render chrome at 70%, 90%, and 140%, and exercise real WebView2 audio, deferred session restoration, Home, and setup. Test audio is muted. PNG previews are saved under `tests/bin/Debug/net8.0-windows/renders`.

## Current limitations

- Bookmarks, most-used site counts, and tab sessions are local-only; there is no sync service.
- The small built-in blocker fallback covers dedicated advertising hosts only when the packaged uBlock extension cannot load.

## Next sensible step

Polish the toolbar and start page after visual feedback, then add a filter-list updater while measuring memory use against Edge.
