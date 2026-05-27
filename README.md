# FindThatShot - Video Archive Manager

A Windows desktop application for organizing a large local video archive across internal and external drives. Files are never moved, renamed, or modified; the app only catalogs them, generates thumbnails, and stores searchable metadata, tags, notes, ratings, and workflow status.

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
  VideoArchiveManager.Data/   EF Core DbContext, entity configurations, migrations, TagService, SearchService, VideoScannerService
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

## Roadmap (not in v1)

The database already contains an `AiTagSuggestions` table for future AI tagging (sea, fog, ships, birds, parrots, cars, people, beach, forest, mountains, city, snow, etc.). The current MVP focuses on manual tagging and searchable metadata only.
