# Changelog

All notable changes to **Video Archive Manager** are recorded in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [0.6.0] - 2026-05-31

### Added

- **Clip-info popup** (`Views/VideoInfoWindow.xaml/.cs`). Non-modal "Get info" / "Properties"-style window that surfaces every piece of metadata the catalog has captured for a clip — a lot more than the inline panel ever showed:
  - **File**: name, full path, folder, extension, size (humanised + raw bytes), modified, created, on-disk / offline.
  - **Video**: duration, resolution, aspect ratio (nominal label like *16:9* + reduced ratio in parens), frame rate, codec.
  - **Camera**: make/model (section hidden when not present).
  - **Location**: folder-derived location text, GPS coords + *Open in map* link, folder date (section hidden when none of these are set).
  - **Catalog**: status, rating (rendered as `★★★☆☆` so 4/5 reads at a glance), tags as inline read-only chips, notes preview (240-char trimmed), sidecar status (*Written* / *Not written yet* / *Disabled* / *Unavailable*) with the resolved sidecar path on the second line, added-to-catalog timestamp, last-updated timestamp.
  - **Internal**: catalog ID, thumbnail path.
  Each row uses a new `InfoRow` templated control (`Helpers/Controls/InfoRow.cs` + `Resources/Components/InfoRow.xaml`) with a copy-to-clipboard ghost button that's only visible on hover — the popup reads as quiet reference data when idle, reveals affordances on intent, and quietly absorbs the *Copy path* use case so the inline button stays gone. Window is non-modal so it can stay open while you keep browsing; reopening for the same clip just brings the existing instance to the front (handled by `MainWindow`'s instance tracking) instead of stacking duplicates.
- **Three entry points to the popup**, registered in priority order:
  1. **`Alt+Enter`** key binding on `MainWindow` — Windows-wide convention for *Properties* (Explorer, Task Manager, etc.).
  2. **Right-click on the editor-pane thumbnail** — primary discovery affordance.
  3. **Right-click on a catalog card** — *Show clip info...* sits at the top of the existing card context menu, above *Open file location*.
  All three route through a single `ShowInfoCommand` on `VideoDetailViewModel`, which fires a `ShowInfoRequested` event that `MainWindow` subscribes to and turns into a window. Keeping the VM out of window construction keeps the layer split clean.
- **Cinematic design system.** Replaced the default ModernWpfUI look with a custom dark, content-first design inspired by DaVinci Resolve's media pool and Adobe Bridge — the audience this app actually serves. New design tokens live under `src/VideoArchiveManager.App/Resources/Theme/`:
  - `Colors.xaml` — layered near-black surfaces (`App.Background.Base/Elevated/Highest` at `#16161A`/`#1E1E22`/`#26262C`), three foreground tiers (Primary/Secondary/Tertiary) that replace every former `Opacity="0.7"` hierarchy hack, and a single warm-amber accent (`#F5A623`) chosen specifically *not* to be the default Windows blue every WPF app ships with. Overrides on `SystemControlBackgroundChromeMediumLowBrush` / `SystemAccentColor*` so existing references upgrade for free; `ModernWpf.ThemeManager.Current.AccentColor` is also set in `App.xaml.cs` so accent-derived brushes resolved at template-build time pick it up belt-and-braces.
  - `Typography.xaml` — Display / H1 / H2 / Section / Body / BodyMuted / BodySmall / Caption / Mono on Segoe UI Variable. The new `App.Type.Section` (11px SemiBold UPPERCASE) replaces the old 13px+0.8 opacity `SectionHeader` and visibly anchors sidebar groups as rail labels rather than competing-with-content headings.
  - `Spacing.xaml` — `App.Space.1..7` rhythm (4 / 8 / 12 / 16 / 24 / 32 / 48) plus named radii (chip 12 = full pill on a 24h chip, button 4, card 8, panel 10) and standard control heights.
  - `Icons.xaml` — 40+ Segoe Fluent Icons string-resource keys (`Icon.Play`, `Icon.Pause`, `Icon.Search`, `Icon.Folder`, `Icon.Trash`, etc.) with a Segoe MDL2 Assets fallback for older Win10. Free font, zero asset cost.
- **Button system** (`Resources/Components/Buttons.xaml`): `App.Button.Primary` (accent fill, used for Search / Scan / Save / Play in app / Start review session), `Secondary` (subtle border, neutral actions), `Ghost` (no chrome, inline tertiary), `Destructive` (red ghost → red fill on hover, used for Remove / Stop). Every button shares one icon-on-left `ControlTemplate`; the icon glyph is carried via a `helpers:Theme.Icon` attached property on the `Button` itself, so call sites read as `<Button helpers:Theme.Icon="{StaticResource Icon.Play}" Content="Play" />` without composing `StackPanel + TextBlock + ContentPresenter` per call. Empty `Theme.Icon` collapses the glyph automatically so the same style works for icon+label or label-only buttons. Plus `App.Button.IconOnly` (toolbar circles for ±5s skip) and `App.Button.ChipClose` (16x16 ghost for tag-chip close glyphs).
- **Cards with hover/select states** (`Resources/Components/Cards.xaml`). Card chrome now lives on the `ListBoxItem`'s `ControlTemplate` instead of an inner `Border` in the `DataTemplate`, so `IsSelected` / `IsMouseOver` / `IsKeyboardFocused` triggers wire directly into the visual without `AncestorType` lookups. Rest: 8px radius on `App.Background.Elevated`, no border. Hover: `App.Accent.Subtle` border + drop shadow lift (Black 14/2, 45% opacity). Selected: 2px accent border on `App.Background.Highest` background. Selected + hover: an accent-amber inner glow. Thumbnail tile gained a subtle bottom-up gradient so the duration badge stays legible over light frames, plus a Fluent warning glyph next to "Offline" instead of plain text.
- **Tag chips** (`Resources/Components/TagChip.xaml`): dark-on-dark pill (24h, full-radius) with accent-amber `Medium`-weight label and a U+00D7 multiplication-sign close glyph instead of the literal lowercase `x`. Hover lifts the border to `App.Accent.Subtle`. Replaces both former `TagChipBorder` sites (sidebar tag-filter chips and editor pane tag chips) — `Foreground="Black"` on a pale-blue Win-XP-style chip is gone.
- **Empty states** (`Resources/Components/EmptyState.xaml` + `Helpers/Controls/EmptyState.cs`). Templated control with `Glyph` / `Headline` / `Subtext` / `CtaText` / `CtaIcon` / `CtaCommand` dependency properties; `HasCta` is a read-only computed property that auto-collapses the CTA button when no command is set. Three sites:
  - Editor pane "No clip selected" placeholder (replaces the old `Opacity=0.6` floating gray text).
  - Catalog zero-results overlay with a *Clear filters* CTA, sitting in the same grid cell as the catalog `ListBox` and bound to `Videos.Count` via the new `CountToVisibilityConverter` (handles `int`, `ICollection`, `IEnumerable`).
  - First-run sidebar zero-folders panel with an *Add folder* primary CTA wired to the existing `AddRootFolderCommand`. Replaces the empty `ListBox` + disabled *Remove selected* combo that previously confronted new users.
- **Status bar redesign.**
  - 2px accent progress strip pinned to `VerticalAlignment="Top"` across the entire window via `Grid.RowSpan="3"` + `Panel.ZIndex="100"`. Determinate fill while `ProgressMaximum > 0`; flips to indeterminate during the file-enumeration phase via a `DataTrigger` on `ProgressMaximum=0`.
  - Bottom row replaced with: status text (left, secondary tier), a *Scanning · 00:42* pill (only while `IsScanning`, accent-amber border + accent-subtle fill, rotating sync glyph driven by an infinite `DoubleAnimation` on `RotateTransform.Angle`), and an always-visible *Sidecars: ON/OFF* pill with a 6px state dot that flips to `App.Success` green via a `DataTrigger` matching the bound text.
- **Splash window** (`Views/SplashWindow.xaml/.cs`). 480×320 borderless rounded panel shown immediately on startup — accent strip on the left edge, brand mark, version, rotating Fluent sync glyph + "Loading" label. `App.xaml.cs` subscribes once to `MainWindow.Loaded` and dismisses the splash via a 400ms `QuadraticEase EaseIn` opacity fade-out so the handoff doesn't flash. Failures during construction or `Show()` are swallowed; the app proceeds without the splash rather than gating startup on it.
- **Customised title bar.** ModernWpfUI's `TitleBar.Background/Foreground/InactiveBackground/InactiveForeground/IsIconVisible` attached properties are now bound to our color tokens so the chrome reads as part of the app rather than generic Windows. A brand-mark anchor (3×20 accent vertical bar + tracked "VIDEO ARCHIVE" 11px SemiBold + 1px subtle separator) sits at the start of the menu row to give the surface an identity beyond the system title bar.
- **Custom slim scrollbars and player seek slider** (`Resources/Components/Sliders.xaml`). 12px implicit `ScrollBar` style applied app-wide: 8px rounded thumb on transparent track, accent-amber on hover, accent-hover while dragging. The player seek bar is now a 4px pill track with accent-amber fill on the played portion (driven by `Track.DecreaseRepeatButton` sized to the current value) and a 14px circular accent thumb that gains a 6px accent glow on hover (25% opacity) and a brighter glow while dragging (45%).
- **Subtle motion.** Player column cross-fades `Opacity` 0→1 over 240ms `CubicEase EaseOut` when entering review mode, and 0.16s back to 0 when leaving — driven by a `DataTrigger` on `Detail.IsPlayerVisible` with EnterActions/ExitActions storyboards. The grid column-width swap stays instantaneous (animating `GridLength` requires a custom `AnimationTimeline`; the cross-fade gives 90% of the perceived smoothness without that maintenance cost). The scan pill's sync glyph spins continuously while a scan is running.
- **Dialog re-skin.** `AboutWindow`, `SettingsWindow`, and `BulkEditDialog` all adopt the same title-bar tokens, App.Type.H1 + secondary subtext header pattern, sectioned settings panels (`App.Background.Elevated` cards on `App.Border.Subtle`), accent-coloured hyperlinks, and the new button system — `Save` / `Apply` are `App.Button.Primary` with a save icon, `Cancel` is `App.Button.Ghost`, *Browse* / *Restore* are `App.Button.Secondary` with appropriate icons.

### Changed

- **Editor pane METADATA block removed.** The inline 8-row grid (Duration / Resolution / Camera / Codec / Size / Folder / Location / GPS) under the thumbnail is gone. The right sidebar now reads as a *workspace* (review state, tags, notes, action buttons), not a *workspace + reference dump* — matching how DaVinci Resolve, Adobe Bridge, Final Cut Pro, and Premiere Pro split inspection from action. Reference data lives in the new clip-info popup, which holds roughly 2.5× as many fields as the old block did.
- **Editor action row trimmed.** Removed *Copy path* — the catalog right-click *Open file location* command and the editor's *Open location* button cover the realistic workflows (reveal in Explorer, then drag from there). The action row is now Play in app / Play externally / Open location, primary-secondary-secondary in importance order. The `CopyFilePathCommand` itself remains on `VideoDetailViewModel` so re-adding the button is a one-line XAML change if anyone misses it.

### Fixed

- **`ScrollBar` style crash on first window paint.** The implicit dark-scrollbar style initially used `Style.Triggers` on `Orientation` to swap the entire `ScrollBar` `Style` between `App.ScrollBar.Vertical` and `App.ScrollBar.Horizontal`. WPF forbids a Style from setting the `Style` property of the element it's being applied to (it would re-trigger application infinitely), and the runtime catches it during `Style.Seal()` as `ArgumentException: Style object is not allowed to affect the Style property of the object to which it applies` — surfaced as a `XamlParseException` on `ScrollBar` initialisation the moment any `ScrollViewer` realised its scrollbars. Fix: the per-orientation chrome now lives in two standalone `ControlTemplate` resources (`App.ScrollBar.VerticalTemplate`, `App.ScrollBar.HorizontalTemplate`); the implicit `Style TargetType="ScrollBar"` defaults to vertical and switches `Template` (allowed) — not `Style` (forbidden) — via a single `Trigger` on `Orientation="Horizontal"`. Same trigger flips `Width` / `Height` / `MinWidth` / `MinHeight` so the bar measures correctly along either axis.

## [0.5.0] - 2026-05-31

### Fixed

- **In-app player migrated from LibVLCSharp to FFME (FFmpeg-backed WPF MediaElement).** White flashes around the video — letterbox at steady state and brief transition flashes during media swap / first-frame load — were a fundamental consequence of `LibVLCSharp.WPF`'s rendering model: the `VideoView` is an `HwndHost` wrapping a Win32 child window that VLC paints into via Direct3D11. The HwndHost surface is *opaque* to WPF — its background is whatever Win32 / VLC says it is, not what the surrounding XAML says — and the DXGI swap chain VLC writes to isn't cleared between frames, so any stretch of pixels VLC hasn't actively painted (letterbox bars, the moment between media swap and first frame, the gap during seek) inherits whatever was on the surface previously. That defaulted to white via the host window class's `COLOR_WINDOW` brush. Successive iterations of fixes (class-brush patch via `SetClassLongPtr`, `WM_PARENTNOTIFY` hook, `WM_ERASEBKGND` subclass via `SetWindowSubclass`, `RedrawWindow` flush, swapping the vout backend to `direct3d9`, sizing the player area to the video aspect via a custom `AspectRatioPanel`) successively reduced the symptom but never eliminated it because none of them could reach pixels that VLC's DXGI swap chain owned. Replacing the engine cuts the root cause: FFME's `MediaElement` is a real WPF visual that renders decoded frames to a `WriteableBitmap`. Its `Background`, `Stretch`, and z-order behave like any other WPF element. There is no `HwndHost`, no class brush, no `WM_ERASEBKGND`, no DXGI swap chain — the entire failure category is gone. Specifically: removed `LibVLCSharp.WPF` 3.9.7.1 and `VideoLAN.LibVLC.Windows` 3.0.23.1 from `VideoArchiveManager.App.csproj`; added `Sinaxxr.FFME.Windows` 8.0.361-sinaxxr.2 (an actively-maintained fork of upstream FFME that targets FFmpeg 8 — picked over upstream FFME 7.0 to keep using the FFmpeg 8 shared binaries already bundled in `tools/ffmpeg/` for FFprobe, so player and metadata pipeline share one FFmpeg). `App.xaml.cs` no longer initialises `LibVLCSharp.Shared.Core`; it points `Unosquare.FFME.Library.FFmpegDirectory` at `<install>/tools/ffmpeg/` and gates `IsPlayerAvailable` on the directory containing `avcodec-62.dll`. The DI registration of the `LibVLC` singleton is gone — FFME's `MediaElement` IS the player. `MainWindow.xaml` swaps `<vlc:VideoView>` (wrapped in our `AspectRatioPanel`) for `<ffme:MediaElement Stretch="Uniform" Background="Black" LoadedBehavior="Manual" UnloadedBehavior="Manual" ScrubbingEnabled="True" VerticalSyncEnabled="True">`. `MainWindow.xaml.cs` is rewritten to use FFME's API surface: async `Open(Uri)/Close()/Play()/Pause()/Seek(TimeSpan)`, `Position` (TimeSpan), `NaturalDuration` (TimeSpan?), `IsPlaying`, `IsSeekable`. `MediaOpened` replaces VLC's `LengthChanged`; `MediaEnded` replaces `EndReached`; `INotifyPropertyChanged` filtered to `Position`/`MediaState`/`IsPlaying`/`IsPaused` replaces VLC's `TimeChanged`/`PositionChanged`/`Playing`/`Paused`/`Stopped`; `MessageLogged` filtered to `Error` severity replaces `EncounteredError`. Deleted `Helpers/Controls/AspectRatioPanel.cs` and the now-redundant `VideoItemViewModel.AspectRatio` property — FFME's built-in `Stretch="Uniform"` does the equivalent natively because the control is a real WPF visual and no longer needs an external panel to constrain it. Added an `AfterTargets="Build"` MSBuild target to `VideoArchiveManager.App.csproj` that mirrors `tools/ffmpeg/` into the build output for local debug runs (production install layout is unchanged — `publish.ps1`'s existing copy step still owns release artifacts and its `-SkipBundleFfmpeg` switch still works because the new target skips when `IsPublishing=true`). The earlier saga of GDI / DXGI workarounds — `HwndHostBackgroundFix`'s 290 lines of P/Invoke, the 4 `HwndHostBackgroundFix.Apply` call sites, the `MediaPlayer.Vout` subscription, the `--vout=direct3d9` override, the `AspectRatioPanel` plus its viewmodel binding, and the brief detour through forcing a dark `Window.Background` on every window — was net code rot in service of a problem the wrong engine was always going to have. None of it remains.
- **Tag picker no longer adds a phantom second chip when virtualization is on.** The 0.4.1 fix for "one click → multiple chips" caught the *synchronous* `SelectionChanged` echo (the picker's own `lb.SelectedItem = null` and the `Clear`/`Add` rebuild of `FilteredTags` re-firing the event on the same call stack) but missed a *deferred* path: with `VirtualizingPanel.IsVirtualizing="True"`, the picker recycles containers asynchronously when its `ItemsSource` changes, and WPF dispatches a focus/selection restore at a lower priority that fires `SelectionChanged` *after* our `finally` block returns. By that point the guard had already been reset, so whatever tag now sat at the previously-selected index (e.g. *Birds* after clicking *AQS*, since *AQS* was excluded from the rebuilt list) got promoted to a chip too, producing impossible AND-filters with empty result sets. The handler now releases the re-entrancy guard via `Dispatcher.BeginInvoke` at `DispatcherPriority.Background` so the deferred echo arrives while the guard is still set and is ignored. One click → exactly one chip, regardless of virtualization.

### Added

- **"Open file location" in the catalog right-click menu.** Right-clicking a thumbnail in the catalog grid now offers an *Open file location* item alongside the existing *Remove from database…* — picks reveal the source video file in Windows Explorer with the file pre-selected. Reuses the existing `VideoDetailViewModel.OpenFileLocationCommand` (the same command behind the editor pane's *Open location* button), so it goes through the existing `IFileSystemService.RevealInExplorer` path. As with the Remove command, it operates on the currently selected clip — make sure the right-clicked thumbnail is actually selected first.

### Changed

- **Cleaner thumbnail card metadata.** Each catalog card now shows just resolution, camera, and tag summary (in addition to the thumbnail, duration overlay, and offline badge). Filename, status, and rating were dropped to reduce visual noise on the grid: filenames are long and decorative on a visual browser, status is best edited in the right-hand panel where it lives anyway, and ratings are typically managed in DaVinci / Lightroom / Bridge.
- **Catalog thumbnails now fill the entire space between the sidebars.** The video grid used to be a `WrapPanel` with hard-coded `Width="240"` cards, so any leftover horizontal space (often 100–200px on smaller windows) showed up as empty gutter on the right and the column count jumped between 2 and 4 in coarse steps as you resized. The grid now uses a custom `AdaptiveWrapPanel` (in `Helpers/Controls/`) that picks `columns = floor(availableWidth / MinItemWidth)` (240px minimum) and gives every cell in a row the same width so each row exactly spans the catalog column at any window size. Cards in the last partial row left-align at the same width as the rest, matching the look of standard photo grids. Thumbnail tiles also keep a true 16:9 shape as cards stretch (a small `WidthToAspectHeightConverter` binds the tile's `Height` to its own `ActualWidth × 9/16`) instead of being locked at 135px regardless of card width.
- **About dialog attribution updated to reflect the FFME migration.** *Help → About* used to credit "VLC / LibVLC (LGPLv2.1+)" alongside FFmpeg in the third-party components paragraph — accurate up to 0.4.1, stale from this release onward. The dialog now credits FFmpeg (GPLv3), FFME / Sinaxxr fork (Ms-PL), and FFmpeg.AutoGen (LGPLv3), with hyperlinks to each project page. `THIRD-PARTY-NOTICES.md` was already updated as part of the migration; this just brings the in-app About dialog in sync. The publish-script comment that lists "bundled dependencies (FFmpeg, LibVLC)" is corrected the same way.

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

[Unreleased]: https://github.com/liknes/FindThatShot/compare/v0.6.0...HEAD
[0.6.0]: https://github.com/liknes/FindThatShot/compare/v0.5.0...v0.6.0
[0.5.0]: https://github.com/liknes/FindThatShot/compare/v0.4.1...v0.5.0
[0.4.1]: https://github.com/liknes/FindThatShot/compare/v0.4.0...v0.4.1
[0.4.0]: https://github.com/liknes/FindThatShot/compare/v0.3.0...v0.4.0
[0.3.0]: https://github.com/liknes/FindThatShot/compare/v0.2.0...v0.3.0
[0.2.0]: https://github.com/liknes/FindThatShot/releases/tag/v0.2.0
