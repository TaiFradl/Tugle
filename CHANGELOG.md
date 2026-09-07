# What’s new

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
