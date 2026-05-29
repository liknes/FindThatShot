# FindThatShot - Video Archive Manager

A Windows desktop application for organizing a large local video archive across internal and external drives. **Source video files are never moved, renamed, deleted, or modified by this app under any circumstances** — it only catalogs them, generates thumbnails, and stores searchable metadata, tags, notes, ratings, and workflow status. The files the app writes or removes on disk are:

- its own SQLite catalog, settings, generated thumbnail JPGs, and rotating catalog backups inside its data directory; and
- optionally, when **Write sidecar JSON files next to videos** is enabled in Settings, a small `<videoname>.<ext>.findthatshot.json` companion file next to each video. Source video files themselves are still never modified.

## Tech stack

- .NET 8
- WPF (`net8.0-windows`) with ModernWpfUI
- MVVM via `CommunityToolkit.Mvvm`
- SQLite via `Microsoft.EntityFrameworkCore.Sqlite`
- FFmpeg / FFprobe (external) for metadata extraction and thumbnail generation
- LibVLCSharp + bundled VLC native libraries for in-app video playback

## Solution layout

```
FindThatShot.sln
src/
  VideoArchiveManager.Core/   Models, enums, service interfaces, FfprobeService, ThumbnailService, FileSystemService, FolderNameParser, AppSettings
  VideoArchiveManager.Data/   EF Core DbContext, entity configurations, migrations, TagService, SearchService, VideoScannerService, VideoLibraryService
  VideoArchiveManager.App/    WPF UI, Views, ViewModels, App startup, DI container
```

## Prerequisites

- Windows 10/11
- .NET 8 SDK (`dotnet --version` should be `8.0.x`)
- FFmpeg + FFprobe (https://ffmpeg.org) for **scanning + thumbnails**. Easiest: `winget install Gyan.FFmpeg` so `ffmpeg.exe` and `ffprobe.exe` are on `PATH`. Or configure the exact `.exe` paths inside the app via **Settings**. Any modern FFmpeg version works.
- **In-app video playback** uses LibVLC, which is bundled via the `VideoLAN.LibVLC.Windows` NuGet package. No additional install or configuration is required.

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
   - Filter by status, camera, tag, minimum rating, date range, root folder, and online/offline availability.
4. Select a card to view full metadata in the right panel. Edit notes, rating, status, and tags there. Click **Save** to persist.
5. To preview a clip without leaving the app, click **Play in app** in the detail panel — the embedded VLC player handles `.mp4 / .mov / .mxf / .mkv / .avi` and modern codecs (H.264/H.265/ProRes/DNxHD/etc.) out of the box. Use **Pause** / **Resume** / **Stop** / **Close player** to control playback. **Play externally** always works and hands the file off to the OS default player.
6. Select multiple cards (Ctrl/Shift+click) and click **Bulk edit** to apply status, rating, append notes, or add a tag to all of them.
7. Missing files (e.g. external drive unplugged) stay searchable; an *Offline* badge appears on their card. Click **Refresh** to re-check availability.
8. Clean up the catalog when you no longer need certain entries:
   - **Remove single / multiple videos**: select one or more cards, then either right-click → *Remove from database…* or press <kbd>Delete</kbd>. Confirms first.
   - **Remove offline**: toolbar button that drops every catalog row whose source file is no longer on disk (after the latest *Refresh* / scan).
   - **Remove a root folder**: in the sidebar, select a root folder and click *Remove selected…*. The dialog tells you how many imported videos are under it and removes them along with the folder entry.

   All three flows only affect the catalog database and the app's thumbnail cache — **source video files on disk are never touched**.

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

The JSON contains your tags, rating (0-5), status, notes, location/context text, folder date, and a small snapshot of the technical metadata (codec, resolution, duration, camera).

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

### Releasing a new version

The release pipeline targets a **GitHub Release** as the update host. Required tools on the dev machine: `dotnet`, `pwsh`, and the [GitHub CLI](https://cli.github.com/) (`gh`) authenticated against your account.

1. Bump `<Version>` in `src/VideoArchiveManager.App/VideoArchiveManager.App.csproj` (or use `-Version` on the script). Use semver, e.g. `0.3.0`.
2. Build the artifacts:
   ```powershell
   pwsh ./scripts/publish.ps1 -Version 0.3.0
   ```
   This populates `./releases/` with the installer, portable zip, nupkg, and `RELEASES` manifest.
3. Tag and create a GitHub Release with the artifacts attached:
   ```powershell
   git tag v0.3.0
   git push origin v0.3.0
   gh release create v0.3.0 ./releases/* --title "v0.3.0" --notes "Release notes…"
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
- Updates are detected via the `RELEASES` manifest attached to the latest release.
- The check is **manual** — nothing happens on app startup. Click **Help → Check for updates…**.
- The flow is: check → confirmation dialog → progress in the status bar → app exits → Velopack applies the swap → app relaunches on the new version.
- Running from `dotnet run` or a raw `dotnet publish` output (i.e. not from the `Setup.exe` installer) shows a friendly "This build is not an installed copy" message instead of trying to apply.
- Source video files, the catalog database, settings, thumbnails, and sidecars are never read or modified by the update process — only the app's own binaries inside its install directory.

### Code signing (not enabled)

The installer and binaries are currently unsigned. End users will see a SmartScreen "Unknown publisher" warning the first time. For personal / internal use this is acceptable. If you ever distribute publicly, look into a code-signing certificate and `vpk pack --signParams …`.

## Roadmap (not in v1)

The database already contains an `AiTagSuggestions` table for future AI tagging (sea, fog, ships, birds, parrots, cars, people, beach, forest, mountains, city, snow, etc.). The current MVP focuses on manual tagging and searchable metadata only.
