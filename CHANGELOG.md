# Changelog

All notable changes to **Video Archive Manager** are recorded in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [0.3.0] - 2026-05-29

### Added

- **Review mode for the in-app video player.** Clicking *Play in app* now expands the player to the centre of the main window: the root-folder sidebar and the catalog grid collapse, the video takes the central area, and the tag / notes / rating / status editor docks on the right at full height. Curate while the clip plays, pause to add tags, resume, close — all without leaving the layout.
- **Seek slider** with current-time / duration readouts. Drag the thumb or click anywhere on the track to jump.
- **±5s skip buttons** and a single **Play / Pause** toggle button (replaces the old separate Pause / Resume buttons).
- **Space** keyboard shortcut toggles play/pause while the player is open, except when keyboard focus is on a text input (typing tag names / notes is unaffected).
- **Help → About Video Archive Manager…** dialog showing the running version, GPLv3 license, source-code link, third-party credits, and a *View third-party notices…* button.
- **THIRD-PARTY-NOTICES.md** at repo root and shipped next to the executable. Documents FFmpeg (Gyan.dev 8.1.1, GPLv3), VLC / LibVLC (LGPLv2.1+ with GPL plugins), LibVLCSharp, Velopack, ModernWpfUI, CommunityToolkit.Mvvm, Entity Framework Core, Microsoft.Data.Sqlite, SQLite, OpenStreetMap / Nominatim, and the rest of the Microsoft.Extensions stack with their project URLs and corresponding source-code links.
- **Window title shows the running version**, e.g. `Video Archive Manager — 0.3.0`. Trivial change, but the easiest possible confirmation that an in-app update succeeded.
- README section on **Licensing & attribution**.

### Changed

- The thumbnail preview in the editor column is hidden while Review mode is active (the live video is already showing in its own column).
- `scripts/publish.ps1` now copies `LICENSE` and `THIRD-PARTY-NOTICES.md` into the publish folder as a belt-and-braces step on top of the linked `<None>` items in the App `.csproj`, so the GPL/LGPL attribution always ships next to the executable.

### Notes

- Switching to a different video in the catalog while in Review mode automatically closes the player and returns the layout to normal. Click *Play in app* on the new video to re-enter Review mode.
- Review mode requires LibVLC to have initialised successfully at startup. If it didn't, the *Play in app* button stays disabled and the editor shows the underlying error; *Play externally* always works regardless.

## [0.2.0] - 2026-05-29

First public release.

### Added

- **Catalog backup & restore** — automatic timestamped backups of `catalog.db` on startup, on-demand backups from Settings, configurable retention, staged restore via an app-restart so the live database is never modified while in use.
- **Sidecar JSON files (opt-in)** — write `.findthatshot.json` next to source videos so tags / ratings / notes / status travel with the footage. Atomic writes, graceful handling of read-only / offline drives, live *Sidecars: ON/OFF* indicator in the status bar, granular per-save status messages.
- **In-app update checker** — *Help → Check for updates…* looks for newer GitHub Releases and applies them via Velopack. Only takes effect when running from an installed build (`Setup.exe`).
- **Bundled FFmpeg** — `ffmpeg.exe` and `ffprobe.exe` (Gyan.dev "full" build) ship inside the installer for zero-config metadata extraction and thumbnail generation.
- **Video location services** — GPS extraction from supported video formats with reverse-geocoding via OpenStreetMap's Nominatim service. Bulk *Fill missing locations from GPS…* command on the Catalog menu.
- **Catalog cleanup** — remove individual videos, offline videos, or entire root folders (catalog only — source video files are never touched).
- **Bulk edit** — apply status, rating, append notes, or add a tag to multiple selected videos at once.

### Changed

- Responsive default window size; date pickers and the *Play externally* button are no longer clipped at common screen widths.

[Unreleased]: https://github.com/liknes/FindThatShot/compare/v0.3.0...HEAD
[0.3.0]: https://github.com/liknes/FindThatShot/compare/v0.2.0...v0.3.0
[0.2.0]: https://github.com/liknes/FindThatShot/releases/tag/v0.2.0
