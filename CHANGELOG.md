# Changelog

All notable changes to **Video Archive Manager** are recorded in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Fixed

- **Tag picker no longer adds a phantom second chip when virtualization is on.** The 0.4.1 fix for "one click → multiple chips" caught the *synchronous* `SelectionChanged` echo (the picker's own `lb.SelectedItem = null` and the `Clear`/`Add` rebuild of `FilteredTags` re-firing the event on the same call stack) but missed a *deferred* path: with `VirtualizingPanel.IsVirtualizing="True"`, the picker recycles containers asynchronously when its `ItemsSource` changes, and WPF dispatches a focus/selection restore at a lower priority that fires `SelectionChanged` *after* our `finally` block returns. By that point the guard had already been reset, so whatever tag now sat at the previously-selected index (e.g. *Birds* after clicking *AQS*, since *AQS* was excluded from the rebuilt list) got promoted to a chip too, producing impossible AND-filters with empty result sets. The handler now releases the re-entrancy guard via `Dispatcher.BeginInvoke` at `DispatcherPriority.Background` so the deferred echo arrives while the guard is still set and is ignored. One click → exactly one chip, regardless of virtualization.

### Added

- **"Open file location" in the catalog right-click menu.** Right-clicking a thumbnail in the catalog grid now offers an *Open file location* item alongside the existing *Remove from database…* — picks reveal the source video file in Windows Explorer with the file pre-selected. Reuses the existing `VideoDetailViewModel.OpenFileLocationCommand` (the same command behind the editor pane's *Open location* button), so it goes through the existing `IFileSystemService.RevealInExplorer` path. As with the Remove command, it operates on the currently selected clip — make sure the right-clicked thumbnail is actually selected first.

### Changed

- **Cleaner thumbnail card metadata.** Each catalog card now shows just resolution, camera, and tag summary (in addition to the thumbnail, duration overlay, and offline badge). Filename, status, and rating were dropped to reduce visual noise on the grid: filenames are long and decorative on a visual browser, status is best edited in the right-hand panel where it lives anyway, and ratings are typically managed in DaVinci / Lightroom / Bridge.
- **Catalog thumbnails now fill the entire space between the sidebars.** The video grid used to be a `WrapPanel` with hard-coded `Width="240"` cards, so any leftover horizontal space (often 100–200px on smaller windows) showed up as empty gutter on the right and the column count jumped between 2 and 4 in coarse steps as you resized. The grid now uses a custom `AdaptiveWrapPanel` (in `Helpers/Controls/`) that picks `columns = floor(availableWidth / MinItemWidth)` (240px minimum) and gives every cell in a row the same width so each row exactly spans the catalog column at any window size. Cards in the last partial row left-align at the same width as the rest, matching the look of standard photo grids. Thumbnail tiles also keep a true 16:9 shape as cards stretch (a small `WidthToAspectHeightConverter` binds the tile's `Height` to its own `ActualWidth × 9/16`) instead of being locked at 135px regardless of card width.

## [0.4.1] - 2026-05-30

### Added

- **Application icon.** The executable, every window's title bar / Alt-Tab / taskbar entry, and the Velopack installer + Start-menu shortcut now all use a proper application icon (`Assets/AppIcon.ico` for the EXE / installer, `Assets/AppIcon.png` for the WPF windows). Previously every surface fell back to the generic .NET WPF icon. The artwork is the `Apps-kmplayer` icon from the Oxygen icon set (KDE, LGPL-3.0); attribution added to `THIRD-PARTY-NOTICES.md`.

### Fixed

- **Stop button no longer flashes the player white.** Clicking *Stop* used to call `MediaPlayer.Stop()`, which tears down VLC's decoder; after that, Windows repaints the underlying `HwndHost` surface with the window class's default `WhiteBrush` — a hard bright flash against the cinematic black backdrop. The button now pauses playback and seeks to 0 instead, preserving the "stopped at the beginning" user model while leaving the first frame on screen.
- **More pale chrome around the in-app player.** Followed up on the 0.4.0 border cleanup with three more sources of the "thin white line / white block around the video" report:
  - The top chrome row's 1px bottom separator (`SystemControlForegroundBaseLowBrush`) now collapses to `BorderThickness="0"` while review mode is active, so it can't paint a pale line directly above the cinematic black backdrop.
  - The editor column's 1px left separator does the same in review mode (it still divides the catalog grid from the editor in normal mode).
  - The player transport row was painted in `SystemControlBackgroundChromeMediumLowBrush` — near-white in light theme — which read as a bright panel under the picture. It's now solid black to match the surrounding column, and the time-readout `TextBlock`s are explicitly `Foreground="White"` so they stay legible.
- **Newly created tags now appear in the sidebar picker immediately.** Adding a brand-new tag via the editor (or as part of a Bulk Edit) used to require an app restart before the tag showed up in the sidebar's Tags filter list. `VideoDetailViewModel` now raises a `TagCatalogChanged` event that `MainViewModel` listens for, inserting the new tag into `AllTags` at its alphabetised position (matching `TagService.GetAllAsync` ordering) and refreshing the picker. Bulk Edit additionally calls `ReloadFiltersAsync` after the dialog closes so its newly created tags / cameras land in the sidebar the same way.
- **Clicking one tag in the sidebar picker no longer adds several chips.** The picker's `SelectionChanged` handler used to read `ListBox.SelectedItem` and call `AddTagFilterCommand`, which synchronously rebuilds `FilteredTags` (`Clear` + `Add`). The mutation re-fired `SelectionChanged` with whatever item now sat at the previously-selected index, cascading into multiple chips per click. The handler now captures the clicked tag from `e.AddedItems`, clears the selection *before* the chip-add, and guards against re-entry — one click adds exactly one chip.

## [0.4.0] - 2026-05-30

### Added

- **Press Enter to add a tag.** The "New tag" textbox in the editor now accepts Enter as a shortcut for the *Add* button. Type a tag, press Enter, and the field clears so you can immediately type the next one — much faster when entering several tags in a row. The *Add* button still works exactly as before.
- **Multi-tag filtering with live search in the sidebar.** The Tags filter in the left sidebar has been redesigned to scale to hundreds of tags:
  - **Type-to-filter input** above the tag list narrows the list as you type (case-insensitive substring match).
  - **Click a tag** to add it as a removable chip; the tag disappears from the picker list while it's selected.
  - **Multiple tag filters combine with AND** — videos must carry every selected tag to appear in the result grid. Useful for queries like *Birds* ∩ *DJI Mini5Pro*.
  - **Clear** button next to the *Tags* heading drops all chips in one click; it auto-disables when nothing is selected.
  - The catalog Clear Filters command also resets the chip selection and the filter input.
- **"Start review session" workflow.** New top-toolbar button (accent-styled, next to *Clear filters*), a matching *View → Start review session* menu item, and a *Show only unreviewed* sidebar checkbox / view-menu toggle surface clips that haven't been reviewed yet:
  - **`Ctrl+Shift+R` shortcut** triggers *Start review session* from anywhere in the main window (mirrors `Ctrl+Shift+L` for *Clear filters*).
  - **Union signal.** A clip counts as "unreviewed" if EITHER its status is still the default *Unreviewed*, OR it has no tags. Catches both forgetful workflows (status never bumped) and behavioural ones (never tagged) with one filter.
  - **One-click batch start.** *Start review session* clears all other filters (search text, status, camera, dates, tag chips, root folder), turns the unreviewed filter on, runs the search, and pre-selects the first result so you can press *Play in app* immediately. Shows *"Review session: N clip(s) waiting"* or *"… you're caught up."* in the status bar.
  - **Persistent toggle.** The sidebar *Show only unreviewed* checkbox is a normal filter — combine it with the tag chips, date pickers, camera, etc. for narrower review queues (e.g. "unreviewed DJI Mini5Pro clips from May").
  - **Implemented as a new `SearchQuery.OnlyUnreviewed` flag** in the data layer, applied in `SearchService` as `Status == Unreviewed OR !Tags.Any()`.

### Changed

- **Status now auto-promotes from *Unreviewed* to *Keep* on the first tag.** Adding the first tag to a clip whose status is still the default *Unreviewed* now silently bumps it to *Keep* (status bar shows *"Tag added · status → Keep · …"* so you notice the first time). Removes one chore from the review loop and keeps the new *Show only unreviewed* filter honest over time — no more reviewed-but-still-listed clips because you forgot to flip the dropdown.
- **Tag filter is now multi-select.** The sidebar's *Tags* list used to be a single-select list that scrolled through every tag in the catalog. Replaced with the chip + search pattern above. The hidden top-bar *Tag* dropdown (which used `SelectedTagFilter`) remains commented out in the XAML for reference but is no longer wired up — the sidebar serves this purpose and scales better.

### Fixed

- **Removed the thin light borders around the in-app video player.** The player column and toolbar were drawing 1px chrome lines (`SystemControlForegroundBaseLowBrush`) on the sides of the video and between the video and the transport controls. Dropped the borders so review mode shows a clean, edge-to-edge frame against the black backdrop.

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

[Unreleased]: https://github.com/liknes/FindThatShot/compare/v0.4.1...HEAD
[0.4.1]: https://github.com/liknes/FindThatShot/compare/v0.4.0...v0.4.1
[0.4.0]: https://github.com/liknes/FindThatShot/compare/v0.3.0...v0.4.0
[0.3.0]: https://github.com/liknes/FindThatShot/compare/v0.2.0...v0.3.0
[0.2.0]: https://github.com/liknes/FindThatShot/releases/tag/v0.2.0
