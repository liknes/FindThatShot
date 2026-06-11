# Find That Shot

A Windows desktop application for organizing a large local video archive across internal and external drives. **Source video files are never moved, renamed, deleted, or modified by this app under any circumstances** — it only catalogs them, generates thumbnails, and stores searchable metadata, tags, notes, ratings, and workflow status. The files the app writes or removes on disk are:

- its own SQLite catalog, settings, generated thumbnail JPGs, and rotating catalog backups inside its data directory; and
- optionally, when **Write sidecar JSON files next to videos** is enabled in Settings, a small `<videoname>.<ext>.findthatshot.json` companion file next to each video. Source video files themselves are still never modified.

## Tech stack

- .NET 8
- WPF (`net8.0-windows`) with ModernWpfUI
- MVVM via `CommunityToolkit.Mvvm`
- SQLite via `Microsoft.EntityFrameworkCore.Sqlite`
- FFmpeg / FFprobe for metadata extraction, thumbnail generation, and in-app video playback (a single bundled FFmpeg used for both the CLI tools and the player's shared-library DLLs)
- FFME (Sinaxxr fork tracking FFmpeg 8) as the WPF-native in-app video player
- ONNX Runtime (CPU) for the optional, local CLIP-based AI auto-tagging and natural-language search (model produced via `scripts/export-clip-onnx.py` or dropped in; never committed)

## Solution layout

```
FindThatShot.sln
src/
  VideoArchiveManager.Core/   Models, enums, service interfaces, FfprobeService, ThumbnailService, FileSystemService, FolderNameParser, MediaSafetyGuard, AppSettings
  VideoArchiveManager.Data/   EF Core DbContext, entity configurations, migrations, TagService, SearchService, MomentService, VideoScannerService, VideoLibraryService
  VideoArchiveManager.App/    WPF UI, Views, ViewModels, App startup, DI container
tests/
  VideoArchiveManager.Core.Tests/   xUnit tests for Core (media-immutability guard, sidecar, backup, parsing, tokenizer)
  VideoArchiveManager.Data.Tests/   xUnit tests for Data (durability, cascade scope, removal safety, search, migrations)
```

## Prerequisites

- Windows 10/11
- .NET 8 SDK (`dotnet --version` should be `8.0.x`)
- FFmpeg + FFprobe (https://ffmpeg.org). Used for two things:
  - **Scanning + thumbnails:** `ffmpeg.exe` and `ffprobe.exe` are invoked as separate processes. Easiest: `winget install Gyan.FFmpeg` so they end up on `PATH`. Or configure the exact `.exe` paths inside the app via **Settings**. Any modern FFmpeg version works.
  - **In-app video playback:** the player (FFME) loads FFmpeg's **shared-library DLLs** (`avcodec-*.dll`, `avformat-*.dll`, etc.) at runtime from `tools/ffmpeg/` next to the executable. Use a **shared** FFmpeg build (e.g. `ffmpeg-release-full-shared` from gyan.dev or a `gpl-shared` build from BtbN), not a static-only build. The FFmpeg version must match what FFME is built against — currently **FFmpeg 8.x** (avcodec-62.dll). For more on the engine and the migration path back to upstream FFME 7 if needed, see [`docs/in-app-player.md`](./docs/in-app-player.md).

## First run

```powershell
dotnet build FindThatShot.sln
dotnet run --project src/VideoArchiveManager.App/VideoArchiveManager.App.csproj
```

On first launch the app creates:

- `%LOCALAPPDATA%\VideoArchiveManager\catalog.db` — SQLite catalog
- `%LOCALAPPDATA%\VideoArchiveManager\Thumbnails\` — generated JPG thumbnails
- `%LOCALAPPDATA%\VideoArchiveManager\Backups\` — rotating catalog database backups (the most recent N copies; configurable in Settings)
- `%APPDATA%\VideoArchiveManager\settings.json` — user overrides (only when you save Settings)

If FFmpeg / FFprobe is not on `PATH`, open **Settings** and point the app at `ffmpeg.exe` and `ffprobe.exe`.

## Using the app

1. Click **Add folder** and pick a root directory (e.g. `D:\VideoArchive\`). Add as many root folders as you like.
2. Click **Scan**. The scanner recursively walks each root, runs `ffprobe` for technical metadata, parses the folder name (e.g. `2025-05-20 - Carapicuiba Sao Paulo - City flight`), and queues thumbnail generation in the background.
3. Use the search box and sidebar filters to find shots:
   - Tokens are AND-matched across filename, folder path, location, context, notes, camera, codec, and tag names.
   - Filter by status, camera, tag, minimum rating, date range, and online/offline availability.
   - Navigate the **Folders** tree in the sidebar (drive → registered root → subfolder) to scope the catalog to any folder on disk; recursive video counts appear next to each node.
   - **Save a search** (Smart Collections): once you've dialled in a filter combination, click **Save** in the sidebar's **SAVED SEARCHES** panel to name and pin it. Clicking a saved search re-applies every filter and re-runs the query live, so it always reflects the current catalog (newly-scanned matching clips appear automatically). Rename or delete saved searches inline — deleting only removes the saved filter, never any videos or catalog data.
4. Select a card to bring the clip into the right-hand editor — an interactive **map** of the clip's GPS location at the top (or a *Set location…* picker if the clip has no GPS yet), followed by review state, tags, notes, and the play / open-location actions. Click **Save** to persist edits. For a comprehensive read-only dump of every captured field (file, video, camera, location, catalog, internal), press **Alt+Enter** or right-click the card and choose *Show clip info…* — opens a non-modal *Clip info* window with copy-to-clipboard on every row.
5. To preview a clip without leaving the app, click **Play in app** in the detail panel. This opens **Review mode**: the Folders tree and the video grid collapse, the embedded FFME player takes the main area, and the tag / notes / rating / status editor docks on the right at full height so you can tag while watching. The embedded player handles `.mp4 / .mov / .mxf / .mkv / .avi` and modern codecs (H.264/H.265/ProRes/DNxHD/etc.) out of the box via FFmpeg.

   Review-mode controls:
   - **Play / Pause** toggle button (or press **Space** when keyboard focus is not in a text box).
   - **Stop**, **−5s** / **+5s** skip, draggable **seek slider** with current time and duration.
   - **Mark in** / **Mark out** buttons — or press **I** then **O** while watching — to capture a timestamped moment (see **Moments** below).
   - **Close player** restores the normal three-pane layout.
   - The right-hand editor stays fully functional — add tags, type notes, change rating or status, click **Save** without leaving Review mode.

   **Play externally** always works and hands the file off to the OS default player; it doesn't enter Review mode.
6. Select multiple cards (Ctrl/Shift+click) and click **Bulk edit** to apply status, rating, append notes, or add a tag to all of them.
7. Missing files (e.g. external drive unplugged) stay searchable; an *Offline* badge appears on their card. Click **Refresh** to re-check availability.
8. Clean up the catalog when you no longer need certain entries:
   - **Remove single / multiple videos**: select one or more cards, then either right-click → *Remove from database…* or press <kbd>Delete</kbd>. Confirms first.
   - **Remove offline**: toolbar button that drops every catalog row whose source file is no longer on disk (after the latest *Refresh* / scan).
   - **Remove a root folder**: in the sidebar, select a root folder and click *Remove selected…*. The dialog tells you how many imported videos are under it and removes them along with the folder entry.

   All three flows only affect the catalog database and the app's thumbnail cache — **source video files on disk are never touched**.

## Moments (in/out markers & sub-clips)

The app is called *Find That Shot* — and a shot is rarely a whole file. **Moments** let you mark a timestamped in/out range inside a clip and give it its own label, rating, notes, and tags, completely independent of the parent file's metadata.

**Capturing moments** happens in **Review mode** while a clip is playing:

- Press **I** to drop an in-point at the current playhead, then **O** to set the out-point and create the moment (the **Mark in** / **Mark out** transport buttons do the same thing). Press **O** without an in-point to record a single-timestamp marker.
- A thumbnail is grabbed at the in-point automatically, so every moment has a visual at the exact frame you marked.
- New moments appear in the **MOMENTS** list in the right-hand editor. Select one to edit its label, rating (0–5), notes, and tags in the compact moment editor; **Jump to** seeks the player straight to that timestamp, and **Delete** removes just the moment (never the file). Cards in the grid show a small badge with each clip's moment count.

**Finding moments** across the whole archive: open **Catalog → Find moments…** for a dedicated search window. It matches on moment label, notes, and tags, with a minimum-rating filter and an online-only toggle. Each result shows the in-point thumbnail, the time range, the parent file, and its tags — click **Jump to** to open the parent clip in Review mode and seek directly to that moment (e.g. *jump to 00:01:32*).

Moments are stored in the catalog alongside everything else and, when sidecars are enabled, travel with the footage too (see below). As with all curation data, capturing or deleting a moment only ever edits the catalog and the thumbnail cache — **the source video file is never modified**.

## Catalog statistics

Open **Catalog → Catalog statistics…** for a read-only dashboard that summarises the whole catalog at a glance — handy for understanding the shape of your archive and tracking how much of it you've reviewed:

- **Overview cards**: total clips (with online/offline split), combined file size and runtime, distinct folders, camera models, tag count, and the percentage of clips that are reviewed and geotagged.
- **Breakdowns**: proportional bars for clips by status, rating, resolution (8K / 4K / 1440p / 1080p / 720p / SD), and year, plus your top cameras, codecs, and tags. Each bar shows its count and its share of the catalog.

The numbers are computed on demand straight from the catalog database (a **Refresh** button recomputes them). Like the rest of the app, building the dashboard never reads, moves, or modifies any source video file.

## Browse on map

Open **Catalog → Browse on map…** for a whole-archive map that turns location into a primary navigation axis — the "where did I shoot that?" entry point for a geo-heavy drone archive. Every geotagged clip is plotted as a clustered marker on the same bundled-offline Leaflet stack the per-clip map uses (only the OpenStreetMap tiles need a connection; the map chrome, markers, and clustering all work offline).

- **Scope.** By default the map plots **every** geotagged clip in the catalog. Untick **Whole archive** to instead mirror the **current grid filters**, so the map shows only the clips your sidebar search/filters are currently matching.
- **Click a cluster → scope the grid.** Clicking a cluster zooms into it *and* scopes the main catalog grid to exactly those clips. A **Map selection: N clips** pill appears in the status bar; click it (or change any normal filter) to clear the scoping and show all matches again.
- **Filter grid to this view.** The toolbar button scopes the grid to every clip currently inside the map viewport — pan/zoom to a region, then filter to it.
- **Click a marker → preview a clip.** Clicking a single marker fills the side panel with that clip's thumbnail, location, coordinates, and online state, and selects it in the grid. From there, **Show in grid** brings it forward in the catalog and **Play in app** opens it in the in-app player.

The window is read-only over the catalog and reuses the lightweight projection the grid already builds — plotting thousands of points stays cheap, and **no source video file is read, moved, or modified**.

## Browse by date

Open **Catalog → Browse by date…** for a whole-archive **year-by-month heatmap** that turns time into a navigation axis — the "when did I shoot that?" companion to *Browse on map*. Each year present in the catalog is a row of twelve month cells, tinted by clip volume (brighter = more clips) with the per-month count and per-year total shown inline. Clips are bucketed by **effective shoot date** — the parsed folder/shoot date when available, otherwise the file's modified date — so the heatmap reflects when footage was *captured*, not when it was imported.

- **Scope.** By default the heatmap counts **every** clip in the catalog. Untick **Whole archive** to instead mirror the **current grid filters**.
- **Click a month → list its clips.** Clicking a month fills the side panel with that month's clips (thumbnail, filename, date, online state, rating).
- **Preview and jump.** Selecting a clip previews it; **Show in grid** brings it forward in the catalog and **Play in app** opens it in the in-app player.
- **Filter grid to this month.** The toolbar button scopes the main catalog grid to the selected month's date range.

The window is read-only over the catalog and reuses lightweight aggregate queries, so charting a large archive stays cheap and **no source video file is read, moved, or modified**.

## Finding duplicates

Open **Catalog → Find duplicates…** to track down clips that exist in more than one place — typically the same footage copied to a backup or external drive. The finder groups clips by a **metadata fingerprint** the scanner already captured — **exact file size + duration + resolution** — so it's instant, needs nothing read off disk, and finds duplicates even among **offline** clips.

Each duplicate set is shown as a card listing its copies (thumbnail, filename, folder, size/resolution/codec/camera, and an Online/Offline badge). One copy is flagged **Suggested keep** (preferring an online, well-curated, original import), but nothing is selected for you:

- Tick exactly which redundant copies to remove, or use **Select all redundant** to mark everything except the suggested keep in each set (then fine-tune).
- A running total shows how many copies are selected and how much disk would no longer be cataloged.
- **Remove selected from database…** confirms first — and warns if you've selected *every* copy of a clip — then removes only those catalog entries and their cached thumbnails.

As everywhere else in the app, this only ever edits the catalog: **your source video files are never moved, renamed, or deleted.** Removing duplicate entries just forgets them from the database; the files (including the copies you chose to "remove") remain untouched on disk.

## AI auto-tagging & natural-language search (opt-in)

The app can optionally use a local **CLIP** model to (a) propose subject tags for your clips and (b) let you search by plain-English description. Everything runs **locally and offline** on the CPU (ONNX Runtime); the model only ever **reads frames** — your source files are never modified.

Because a single thumbnail is a weak signal for a whole video, the scoring pass **samples several frames across each clip** via the same ffmpeg pipeline that makes thumbnails, then **max-pools** results across those frames — so a tag or a search matches if the subject appears in *any* part of the clip, not just on average. Sampling is **duration-proportional**: it takes roughly one frame every N seconds (default 20s), clamped to a configurable min/max, so a 30-second clip gets a few frames while a 13-minute clip scales up to the cap instead of being under-sampled by a flat count.

Two capabilities come from the one model:

- **Auto-tag suggestions.** Each clip's frames are scored against a scene/subject vocabulary (sea, fog, ships, beach, forest, mountains, city, snow, sunset, plus coastal/drone/aerial terms like coastline, cliffs, marina, lighthouse, reef, kayak, and terrain/urban terms like canyon, glacier, skyscrapers, wind turbines, …). Above-threshold labels become suggestions you triage in **Catalog → Review AI suggestions…** as accept/reject chips. **Accept** promotes a suggestion to a real tag on the clip; **Reject** dismisses it (and is remembered, so a later re-run won't keep re-proposing it). Nothing is ever auto-applied. The candidate list lives in `AiLabelVocabulary` — add a row and **Re-score all clips** to detect new subjects. Optionally, **adaptive thresholds** (on by default) learn a per-label confidence cutoff from your accept/reject history, so suggestions sharpen toward your footage over time without ever changing the CLIP model.
- **Natural-language search.** Turn on **View → Search by description (AI)** (or the inline **AI search** toggle beside the search box) and type something like *"drone shot over snowy mountains at sunset"* — clips are ranked by visual similarity to your phrase rather than by literal text matching. The default bundle is **multilingual**: the query text is encoded by a multilingual DistilBERT aligned to the CLIP ViT-B/32 image space, so search works in **50+ languages** (Norwegian, Brazilian Portuguese, German, …). (Auto-tag suggestion labels are still applied from the English `AiLabelVocabulary`; localizing the displayed tag names is a separate concern.)

### Enabling it

The feature is **off by default**, and — like the bundled FFmpeg/mpv binaries — the **model is not shipped in the repo** (it's large, and gitignored under `tools/models/`). You supply it once; there is **no official hosted download**. Pick one of:

- **Produce the bundle with the prep script (recommended).** Run the included exporter once to download the public weights and assemble the exact bundle the app expects into `tools/models/clip-multilingual-v1`. The default exporter builds the **multilingual** bundle (OpenAI CLIP ViT-B/32 image encoder + `sentence-transformers/clip-ViT-B-32-multilingual-v1` text encoder + multilingual BERT vocab):

```bash
python -m pip install "torch" "transformers" "sentence-transformers" "onnx" "onnxscript"
python scripts/export-mclip-onnx.py
```

  The app resolves `tools/models/clip-multilingual-v1` automatically (it's also copied into the build/publish output, like FFmpeg), so it's then **zero-config**. (An English-only bundle can still be produced with `scripts/export-clip-onnx.py`; the app detects which tokenizer to use from the bundle's `manifest.json`.)

- **Drop in a bundle you already have.** Put the files (below) in any folder and set **Settings → AI tagging → Model folder** to it (handy offline).

- **Self-host + download-on-demand (recommended for distribution; the shipped default).** The default **AI model download URL** points at this repo's GitHub Release asset (`models-v2/clip-multilingual-v1.zip`), so the in-app **Download model** button fetches + extracts the bundle into `%LOCALAPPDATA%\VideoArchiveManager\Models\` on first use — no Python for end users. To (re)publish that asset: run `python scripts/export-mclip-onnx.py --zip` (writes `tools/models/clip-multilingual-v1.zip`), then upload it to the GitHub Release under the **`models-v2`** tag with that exact filename. Publish the app with `-SkipBundleModel` to keep the installer small; the ~450 MB only downloads for users who actually opt in. (If you re-export the model, reuse the same tag/filename or bump `AppSettings.AiModelDownloadUrl`.)

Then, in **Settings → AI tagging**:

1. Tick **Enable AI auto-tagging and natural-language search**.
2. Confirm the status shows the model as **Ready** (it resolves a configured **Model folder**, then a bundled `tools/models/clip-multilingual-v1`, then the managed app-data folder).
3. Run **Catalog → Generate AI tags…** to score your clips (progress shows in the status bar), then open **Catalog → Review AI suggestions…**.

**Catalog → Generate AI tags…** scores only clips that don't have AI embeddings yet (the incremental pass), while **Catalog → Re-score all clips with AI…** re-runs scoring on every clip — use it after changing the sampling settings, label vocabulary, or model. Re-scoring is idempotent: it replaces prior embeddings and refreshes pending suggestions, but never resurrects tags you've already accepted or rejected.

Tuning (seconds-per-frame sampling interval, min/max frames per clip, suggestion confidence threshold) lives in the same Settings pane. **Clear AI data** purges all embeddings and pending suggestions to reclaim space — accepted tags and source files are untouched. Turning the feature back off makes the whole subsystem inert (no model load, no menu commands, no extra writes); existing data simply stops being read.

A model bundle directory is expected to contain `image_encoder.onnx`, `text_encoder.onnx`, the CLIP BPE vocab (`bpe_simple_vocab_16e6.txt.gz`), and an optional `manifest.json` (tensor names, image size, normalization constants — sensible CLIP ViT-B/32 defaults are used when absent). `scripts/export-clip-onnx.py` writes all of these for you.

## Catalog backup

All curation work — tags, ratings, notes, statuses, and workflow state — lives in `catalog.db`. The app protects that data with rotating backups:

- **Automatic on startup** (on by default). At launch the app copies `catalog.db` into the configured **Backup directory** (`%LOCALAPPDATA%\VideoArchiveManager\Backups\` by default) with a timestamped filename like `catalog-20260528-093017.db`.
- **On demand**. Open **Settings → Catalog backup → Back up now** to make an immediate backup.
- **Retention**. Configure how many recent backups to keep (default: 7). Older backups are pruned automatically after each new backup.
- **Restore**. Open **Settings → Catalog backup**, pick a backup from the list, click **Restore selected…** and confirm. The app stages the restore (no file is overwritten while the DB might be in use), prompts you to restart, and on the next startup atomically swaps the catalog into place before any database connection is opened. The current catalog is automatically copied first into the Backups folder as a `catalog-pre-restore-<timestamp>.db` safety snapshot so you can roll back if needed. Backups can also be copied off to external storage as a manual offsite backup.

Backups only ever copy the SQLite catalog file. Source video files are never read, copied, modified, or referenced during backup.

## Sidecar files (opt-in)

By default the app keeps everything inside its own SQLite catalog and never writes to your video folders. If you'd like your tags / rating / notes / status to **travel with the footage** (e.g. when you move a drive to another computer or want a portable record of your curation), turn on **Settings → Sidecar files → Write sidecar JSON files next to videos**.

When enabled, every save in the detail panel and every Bulk Edit operation writes a companion file next to each affected video:

```
D:\Footage\2025-05-20 City flight\clip.mov
D:\Footage\2025-05-20 City flight\clip.mov.findthatshot.json   <-- sidecar
```

The JSON contains your tags, rating (0-5), status, notes, location/context text, folder date, any **moments** (timestamped in/out ranges with their own label, rating, notes, and tags), and a small snapshot of the technical metadata (codec, resolution, duration, camera). The sidecar schema is `findthatshot/v2`; older `v1` sidecars without moments are still read.

Behavior:

- The source video file itself is **never** modified, moved, or renamed.
- Sidecars are written atomically (temp file + rename) so they're never left half-written if the process is killed.
- Failures on read-only or offline drives (e.g. NAS unavailable, drive write-protected) are logged and skipped silently — the catalog save in `catalog.db` always succeeds regardless.
- The sidecar extension `.findthatshot.json` is intentionally distinctive so other software (DaVinci Resolve, Premiere Pro, Lightroom, Final Cut Pro, Avid, Sony Catalyst, camera utilities, Explorer, etc.) doesn't try to read or write it. It is **not** an Adobe XMP sidecar.

## Supported extensions

`.mp4 .mov .mxf .avi .mkv`

Adjust by editing `appsettings.json` or `%APPDATA%\VideoArchiveManager\settings.json`.

## Working with EF Core migrations

The Data project is its own startup target for EF tooling. From the repo root:

```powershell
dotnet ef migrations add <Name> \
  --project src/VideoArchiveManager.Data/VideoArchiveManager.Data.csproj \
  --startup-project src/VideoArchiveManager.Data/VideoArchiveManager.Data.csproj \
  --output-dir Migrations

dotnet ef database update \
  --project src/VideoArchiveManager.Data/VideoArchiveManager.Data.csproj \
  --startup-project src/VideoArchiveManager.Data/VideoArchiveManager.Data.csproj
```

`DesignTimeDbContextFactory` writes to `%LOCALAPPDATA%\VideoArchiveManager\catalog.db` by default, matching the runtime path. The application also calls `Database.MigrateAsync()` on startup so any new migrations are applied automatically.

## Testing

The two non-negotiable invariants — **original media is never touched** and **user curation is never silently destroyed** — are protected by an automated test suite (xUnit + NSubstitute + FluentAssertions), plus code-level runtime guardrails.

Run the suites from the repo root:

```powershell
dotnet test tests/VideoArchiveManager.Core.Tests/VideoArchiveManager.Core.Tests.csproj
dotnet test tests/VideoArchiveManager.Data.Tests/VideoArchiveManager.Data.Tests.csproj
```

What's covered:

- **Media immutability (P0).** A `MediaSnapshot` helper fingerprints every file in a fixture tree (SHA-256 + length + last-write time); operations run, then it asserts **zero diffs** on the source media. Covers catalog removal (by id / under root), thumbnail-cache cleanup, sidecar writes (off by default → no file appears; on → only the `.findthatshot.json` appears and the video bytes are unchanged), and read-only ffprobe/thumbnail paths.
- **Data durability (P0).** Cascade scope (deleting a clip removes its `VideoTag` / `VideoMoment` / `MomentTag` rows but never the global `Tag` vocabulary), targeted detach/delete, `AiTaggingService.ClearAllAiDataAsync` only touching AI tables, catalog backup/restore round-trip, and additive migrations (re-running `Migrate` over a populated catalog preserves data). Tests use a **real file-backed SQLite catalog** migrated with the production migrations, so cascade deletes and `ExecuteDeleteAsync` behave exactly as in production.
- **Functional (P1).** `SearchService` filters/sort/paging, `FolderNameParser`, `VectorMath`, `ClipTokenizer`, `DuplicateDetectionService`, `SavedSearchService`, `TagService`, `MomentService`, and `NominatimReverseGeocodingService` (with a stubbed `HttpClient`).

Runtime guardrails enforced in code (and locked in by tests):

- **`MediaSafetyGuard`** (Core) — a last-line-of-defence that refuses to delete any path with a protected media/sidecar extension or outside the app's cache directory. The thumbnail-cache cleanup routes its deletes through it.
- **Mandatory safety backup** — every bulk catalog removal (`RemoveByIds` / `RemoveOffline` / `RemoveUnderRoot` / `RemoveRootFolder`) takes a best-effort catalog snapshot first, so a mistaken removal is always recoverable.
- **Atomic remove-root** — removing a root folder and its video records happens in a single database transaction, so a partial failure can never half-delete the catalog.

CI runs both suites on a Windows runner for every push / pull request (`.github/workflows/tests.yml`).

## Distribution (Velopack)

The app is packaged for end users with [Velopack](https://velopack.io). A self-contained `win-x64` build is bundled into a Windows installer (`Setup.exe`) and a portable zip; the installer also wires up auto-updates so demo users get future versions without reinstalling.

### One-time setup (developer machine)

The Velopack CLI is pinned as a local dotnet tool. Restore it once after cloning:

```powershell
dotnet tool restore
```

### Building a release

Run the publish script from the repo root. Optionally pass `-Version` to override the version in the App `.csproj`.

```powershell
pwsh ./scripts/publish.ps1
pwsh ./scripts/publish.ps1 -Version 0.2.0
```

What the script does:

1. Restores local dotnet tools (`vpk`).
2. `dotnet publish` of `VideoArchiveManager.App` in `Release` for `win-x64`, self-contained, into `./publish`.
3. If `./tools/ffmpeg/` exists at the repo root, copies `ffmpeg.exe` / `ffprobe.exe` (and any sibling files) into the publish output so the released app picks them up via the relative defaults in `appsettings.json`. Pass `-SkipBundleFfmpeg` to opt out. When no bundled FFmpeg is present the app falls back to whatever `ffmpeg` / `ffprobe` is on the user's `PATH`.
4. Runs `dotnet vpk pack` against the publish folder.

Artifacts land in `./releases/`:

| File | Purpose |
| --- | --- |
| `VideoArchiveManager-win-Setup.exe` | Single-file installer for demo users. |
| `VideoArchiveManager-win-Portable.zip` | Portable build (just unzip and run `VideoArchiveManager.exe`). |
| `VideoArchiveManager-<version>-full.nupkg` | Velopack release package used by the auto-updater. |
| `RELEASES`, `releases.win.json`, `assets.win.json` | Release manifests; publish these alongside the nupkg if/when you wire up auto-update. |

### Bundling FFmpeg (optional)

To give demo users a zero-setup experience, drop a Windows FFmpeg build into `./tools/ffmpeg/` so the directory looks like:

```
tools/
  ffmpeg/
    ffmpeg.exe
    ffprobe.exe
    ... (any other FFmpeg dlls if you use a shared build)
```

The next `./scripts/publish.ps1` run will bundle these into the installer. The app's default settings already point at `tools\ffmpeg\ffmpeg.exe` and `tools\ffmpeg\ffprobe.exe` (relative to the install directory) so it works automatically; users can still override the paths in **Settings**.

The same `tools\ffmpeg\` shared-library DLLs (`avcodec-*.dll`, `avformat-*.dll`, etc.) are also what the **in-app player** loads at runtime — see [`docs/in-app-player.md`](./docs/in-app-player.md) for the engine choice, the community-fork situation, the error-surfacing model, and a step-by-step plan for switching back to upstream FFME if that ever becomes necessary.

### Releasing a new version

The release pipeline targets a **GitHub Release** as the update host. Required tools on the dev machine: `dotnet`, `pwsh`, and the [GitHub CLI](https://cli.github.com/) (`gh`) authenticated against your account.

1. Bump `<Version>` in `src/VideoArchiveManager.App/VideoArchiveManager.App.csproj` (or use `-Version` on the script). Use semver, e.g. `0.3.0`.
2. Build the artifacts:
   ```powershell
   pwsh ./scripts/publish.ps1 -Version 0.3.0
   ```
   This populates `./releases/` with the installer, portable zip, nupkgs, and the update feeds (`releases.win.json`, `assets.win.json`, `RELEASES`).
3. Tag and create a GitHub Release with the artifacts attached.

   > **Do not** use `./releases/*` — that folder accumulates **every** historical `*.nupkg`, so the glob would upload dozens of stale full packages (gigabytes) and is slow/wrong. Attach only **this** version's files plus the three feed manifests. The two JSON feeds are **mandatory**: Velopack 1.1.x reads `releases.win.json` from the *latest* release to detect updates. If they're missing, installed apps report "you're on the latest version" even though a newer release exists.

   ```powershell
   $v = "0.3.0"
   git tag "v$v"; git push origin "v$v"
   gh release create "v$v" --title "v$v" --notes "Release notes…" `
     ".\releases\VideoArchiveManager-win-Setup.exe" `
     ".\releases\VideoArchiveManager-win-Portable.zip" `
     ".\releases\VideoArchiveManager-$v-full.nupkg" `
     ".\releases\VideoArchiveManager-$v-delta.nupkg" `
     ".\releases\releases.win.json" `
     ".\releases\assets.win.json" `
     ".\releases\RELEASES"
   ```

   After publishing, sanity-check that the live feed reports the new version:
   ```powershell
   (Invoke-RestMethod "https://github.com/liknes/FindThatShot/releases/latest/download/releases.win.json").Assets |
     Where-Object Type -eq Full | ForEach-Object { [version]$_.Version } | Sort-Object | Select-Object -Last 1
   ```
4. On any installed copy of the app (laptop, multimedia PC), open **Help → Check for updates…** to pull the new version. The app verifies the version, downloads the delta or full package, applies the update, and restarts on the new version. Your catalog database, settings, sidecar files, and backups are not touched.

### In-app update flow

`VelopackApp.Build().Run()` is wired in `App.Main`, and the app exposes a manual updater via **Help → Check for updates…**. It is configured by:

```jsonc
// appsettings.json (or user-overridable settings.json)
"UpdateRepoUrl": "https://github.com/liknes/FindThatShot"
```

Behaviour:

- Only the **public** GitHub Releases of that repo are considered (no access token required at runtime).
- Updates are detected via the `releases.win.json` feed attached to the latest release (with `assets.win.json` / `RELEASES` alongside it). All three must be uploaded with every release — see the warning under *Releasing a new version*.
- The check is **manual** — nothing happens on app startup. Click **Help → Check for updates…**.
- The flow is: check → confirmation dialog → progress in the status bar → app exits → Velopack applies the swap → app relaunches on the new version.
- Running from `dotnet run` or a raw `dotnet publish` output (i.e. not from the `Setup.exe` installer) shows a friendly "This build is not an installed copy" message instead of trying to apply.
- Source video files, the catalog database, settings, thumbnails, and sidecars are never read or modified by the update process — only the app's own binaries inside its install directory.

### Tracking adoption (visitors & downloads)

Two independent signals, since one tool can't cover both cleanly:

- **Landing-page visitors & referrers** — GoatCounter (cookieless, no consent banner) is wired into `docs/index.html`. Register the code `findthatshot` at https://www.goatcounter.com/ so the snippet's `findthatshot.goatcounter.com` URL resolves, then view stats at https://findthatshot.goatcounter.com/. This counts page views and referrers only (you'll see Facebook as a referrer once you start posting). It does not count downloads — GitHub does that (below).
- **Actual app downloads** — GitHub counts every release-asset download server-side (far more accurate than click-tracking). Quick view per release:
  ```powershell
  gh api repos/liknes/FindThatShot/releases --jq '.[] | "\(.tag_name): " + ([.assets[] | select(.name|test("Setup.exe|Portable.zip")) | "\(.name)=\(.download_count)"] | join("  "))'
  ```
  Note: in-app updates pull the `*-delta.nupkg` / `*-full.nupkg` assets, so the `Setup.exe` count reflects *new* installs while the nupkg counts reflect *updates*.

### Code signing (not enabled)

The installer and binaries are currently unsigned. End users will see a SmartScreen "Unknown publisher" warning the first time. For personal / internal use this is acceptable. If you ever distribute publicly, look into a code-signing certificate and `vpk pack --signParams …`.

## Localization & translating the app

The Windows app is fully localizable. English is the source language in `src/VideoArchiveManager.App/Localization/Strings.resx`; each translation is a sibling file `Strings.<culture>.resx` (for example `Strings.nb-NO.resx` for Norwegian Bokmål). Pick your language under **Settings → General → Language**; the choice is saved in your user settings.

**Community translations** are managed on [Crowdin](https://crowdin.com/). The repository root contains a [`crowdin.yml`](./crowdin.yml) file that tells Crowdin which files to sync with GitHub.

### For translators

1. Open the Find That Shot project on Crowdin (link from the project **About** page or README once the maintainer has published it).
2. Choose a target language (or request a new one if the project allows it).
3. Translate strings in the Crowdin editor. Context comments from `Strings.resx` appear when present.
4. When translations are approved, Crowdin opens a pull request on GitHub with the updated `.resx` files. After that PR is merged, the new language appears automatically in the in-app language picker — no code changes required.

Placeholders such as `{0}` must be preserved exactly; they are filled at runtime with numbers, paths, or other dynamic text.

### For maintainers (Crowdin ↔ GitHub)

If you have already connected Crowdin to this repository:

1. Merge or push [`crowdin.yml`](./crowdin.yml) to the `main` branch.
2. In Crowdin: **Integrations → GitHub → Edit** — confirm the configuration file is `crowdin.yml` and sync mode is **Source and translation files**.
3. Set the source language to **English** and add target languages (Norwegian Bokmål is already in the repo as `nb-NO`).
4. Under **Settings → Translations**, enable **Allow users to suggest new languages** if you want volunteers to propose additional locales.
5. Configure when Crowdin should open PRs (on each update, on a schedule, or manually).

The landing page (`docs/index.html`) still uses inline strings in `docs/i18n.js` (English + Norwegian only). App UI strings and the website can be split into separate Crowdin file groups later if needed.

## Licensing & attribution

> Copyright (C) 2026 Ingve Moss Liknes &lt;findthatshot@ingve.no&gt;
>
> Find That Shot is free software: you can redistribute it and/or modify it under the terms of the **GNU General Public License v3** (or, at your option, any later version) as published by the Free Software Foundation. It is distributed **without any warranty** — see the GNU General Public License for details. A copy ships in [`LICENSE`](./LICENSE); if not, see <https://www.gnu.org/licenses/>.

Find That Shot itself is licensed under the **GNU General Public License v3** (see `LICENSE`). The installed application bundles several third-party components with their own licenses:

- **FFmpeg** (Gyan.dev "full" build, version 8.1.1) — GPLv3, https://ffmpeg.org/. Used in two ways: (a) invoked as separate `ffmpeg.exe` / `ffprobe.exe` processes for scanning and thumbnails, and (b) the same FFmpeg shared-library DLLs are loaded in-process by the in-app player (FFME).
- **FFME — Sinaxxr fork** — Ms-PL, https://github.com/sinaxxr/ffmediaelement (fork of https://github.com/unosquare/ffmediaelement). WPF-native video player control used for in-app playback.
- **FFmpeg.AutoGen** — LGPLv3, https://github.com/Ruslan-B/FFmpeg.AutoGen. C# bindings FFME uses to call into the bundled FFmpeg shared libraries.
- **Velopack** — MIT, https://github.com/velopack/velopack.
- **ONNX Runtime** — MIT, https://github.com/microsoft/onnxruntime. Powers the optional local CLIP inference for AI auto-tagging / natural-language search. CLIP itself (the downloaded model weights) originates from OpenAI's CLIP; model files are fetched at runtime and are not distributed with the app.
- **OpenStreetMap / Nominatim** — map data © OpenStreetMap contributors, ODbL.
- Plus a number of MIT-licensed .NET libraries (CommunityToolkit.Mvvm, ModernWpfUI, Entity Framework Core, Microsoft.Data.Sqlite, Microsoft.Extensions.\*).

The full attribution list, including project URLs and links to the corresponding source code, ships with the application as `THIRD-PARTY-NOTICES.md` next to `VideoArchiveManager.exe`. It is also accessible from inside the app via **Help → About Find That Shot… → View third-party notices…**.

If you redistribute the app (e.g. by sharing the installer with others), keep `LICENSE` and `THIRD-PARTY-NOTICES.md` alongside it. If you modify the source and distribute the result, you must do so under the GPLv3.

## Roadmap (not in v1)

AI auto-tagging (the `AiTagSuggestions` table) and natural-language search are now implemented as an opt-in, fully local CLIP feature — see [AI auto-tagging & natural-language search](#ai-auto-tagging--natural-language-search-opt-in) above. A natural next step is letting a strong per-frame search match spin directly into a timestamped **Moment** (jump-to-the-shot), since the per-frame embeddings and best-match timestamps are already captured.
