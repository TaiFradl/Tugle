# What’s new

## 1.2.1 — Public update

- Published the complete 1.2 feature and polish update, including the Windows installer and portable download.
- Bumped the version so earlier 1.2.0 builds can detect this update.
- Release packaging keeps the application and installer versions aligned with the release tag.

## 1.2.0 — Library, privacy, and productivity

- Reworked bookmarks with search, editing, and page actions. Removed Read later while preserving its saved links as bookmarks.
- Audio controls appear only on playing or muted tabs, using a clearer speaker icon and larger hit target.
- Most used sites replace recent sites on Home, ranked by real visits rather than favicon updates.
- Restored background tabs load on selection. History writes are batched off the UI thread, and Home no longer recreates unchanged video backgrounds.
- Tightened toolbar spacing and made short panels fit their content. Added Slate to the browser and setup theme choices.
- Added pinned tabs, recently closed tabs (`Ctrl+Shift+T`), and session restore that preserves tab order, pinned state, and the selected tab.
- Added private windows (`Ctrl+Shift+N`) using WebView2’s off-the-record mode. Private tabs, history, library changes, and settings are not written to disk.
- Added Privacy and protection controls for tracking-prevention level, packaged uBlock Origin and Cookie Guard state, and clearing history, cache, cookies/site data, and download history.
- Downloads now show byte progress and offer pause, resume, and cancel when the WebView2 runtime supports them.
- Added Save page as PDF (`Ctrl+P`) and address-bar search choices for Google, DuckDuckGo, Bing, Brave, or a custom `{query}` URL.
- Installed copies now download updates in the background and offer an automatic installer restart; portable copies retain the release download flow.

## 1.1.5 — Installation and session restore

- Added `Tugle-Setup.exe`, a per-user Windows installer with Start-menu and desktop shortcuts, upgrade support, an uninstaller, and a release-workflow artifact.
- Tugle now saves the open HTTP(S), file, and home tabs on normal exit, then restores them with the previously active tab selected when it reopens.
- In-app update checks now prefer the installer and retain the portable ZIP as a fallback.

## 1.1.0 — Tab, scale, and privacy update

- Fixed GUI scaling so browser chrome uses the selected scale consistently and keeps a usable minimum window size.
- Added tab duplication, an always-visible mute control beside each tab’s close button, and mouse-wheel scrolling anywhere over overflowing tabs.
- Removed adaptive “Match site colors” from setup and theme settings.
- Added a saved custom-color palette and restored full packaged uBlock Origin filtering with a built-in fallback.
- Windows taskbar pinning remains an explicit user choice; the installer supplies Start-menu and optional desktop shortcuts instead.

## 1.0.1 — Local maintenance update

- Simplified setup: the Google, Theme, and Background steps are centered, with setup branding removed. Added the Coral theme color.
- Added **Match site colors** in setup and the Theme menu. Tugle uses a site’s declared color when available and a stable site-specific color otherwise.
- Kept the home page on the selected base theme, Ocean by default.
- Made the blocker safer: page documents are always allowed, and the old full uBlock ruleset is disabled. Tugle now filters only requests to dedicated advertising hosts.
- Removed the unused bundled uBlock payload from new packages to reduce their size.
- Fixed adaptive-theme settings so they persist across restarts.

## 1.0.0 — First public release

- Added the clean Google → Theme → Background setup flow.
- Added Ocean and the first Ocean gradient as the default appearance.
- Fixed restored-window sizing after leaving fullscreen while preserving Windows snapping.
- Added GitHub Releases packages and in-app update checks.
- Added a portable Windows package for sharing with other PCs.
