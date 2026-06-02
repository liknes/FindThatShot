# Third-party notices

Find That Shot itself is licensed under the GNU General Public License v3 (see `LICENSE` at the root of this repository). Source code is available at https://github.com/liknes/FindThatShot.

The installed application bundles or links to several third-party components. Their licenses, sources, and obligations are listed below. Where a component is GPL-licensed, the corresponding source is either bundled by upstream or available at the URL given here; you may also request the source through the project's GitHub repository.

---

## FFmpeg

- **Version:** 8.1.1 ("full" build by Gyan Doshi, https://www.gyan.dev/ffmpeg/builds/)
- **License:** GNU General Public License v3 (because of `--enable-gpl --enable-version3` configuration; bundles libx264, libx265, libxavs2, libxvid, libvidstab, librubberband, and others under GPL terms)
- **Project home:** https://ffmpeg.org/
- **Source code:** https://ffmpeg.org/download.html#get-sources, plus the matching upstream tarball and configure options listed at https://www.gyan.dev/ffmpeg/builds/
- **How it is used:** Two ways, both against the same single bundled FFmpeg copy in `tools/ffmpeg/`:
  - `ffmpeg.exe` and `ffprobe.exe` are invoked as separate processes for metadata extraction and thumbnail generation.
  - The matching FFmpeg shared-library DLLs (`avcodec-*.dll`, `avformat-*.dll`, `avutil-*.dll`, `swscale-*.dll`, `swresample-*.dll`, etc.) are loaded in-process by FFME for in-app video playback, via the FFmpeg.AutoGen bindings.
  All FFmpeg binaries are shipped unmodified.

## FFME (Sinaxxr fork)

- **Version:** 8.0.361-sinaxxr.2 (via the `Sinaxxr.FFME.Windows` NuGet package)
- **License:** Microsoft Public License (Ms-PL)
- **Project home:** https://github.com/sinaxxr/ffmediaelement (fork of https://github.com/unosquare/ffmediaelement)
- **How it is used:** WPF MediaElement-style control (`<ffme:MediaElement>`) used as the in-app video player. Loads the bundled FFmpeg shared libraries from `tools/ffmpeg/` at runtime to decode video and renders frames to a `WriteableBitmap`. Shipped unmodified as a NuGet binary dependency.

## FFmpeg.AutoGen

- **Version:** 4.4.0 (transitive dependency of `Sinaxxr.FFME.Windows`)
- **License:** GNU Lesser General Public License v3
- **Project home:** https://github.com/Ruslan-B/FFmpeg.AutoGen
- **How it is used:** Auto-generated C# bindings to FFmpeg's C API. FFME uses these bindings to load and call the bundled FFmpeg shared libraries (`avcodec-*.dll`, `avformat-*.dll`, etc.) for in-app video playback. Shipped unmodified as a NuGet binary dependency.

## mpv / libmpv

- **Component:** `libmpv-2.dll` (mpv client library), shinchiro "mpv-dev" Windows build (statically links its own copy of FFmpeg)
- **License:** GNU General Public License v2 or later (the bundled build enables GPL features — its reported feature list includes `gpl` — so the combined work is distributed under the GPL)
- **Project home:** https://mpv.io/ (source: https://github.com/mpv-player/mpv; Windows build scripts: https://github.com/shinchiro/mpv-winbuild-cmake)
- **Source code:** https://github.com/mpv-player/mpv plus the matching FFmpeg sources at https://ffmpeg.org/download.html#get-sources; you may also request the corresponding source through this project's GitHub repository.
- **How it is used:** Loaded in-process via P/Invoke (`Helpers/Player/MpvInterop.cs`) as the GPU-rendered in-app video player. When `tools/mpv/libmpv-2.dll` is present the app renders video on the GPU through mpv (`vo=gpu`) into a child window; absent it, the app falls back to the FFME player. Shipped unmodified.

## Velopack

- **License:** MIT
- **Project home:** https://github.com/velopack/velopack
- **How it is used:** Installer, update packaging, and in-app update delivery.

## CommunityToolkit.Mvvm

- **License:** MIT
- **Project home:** https://github.com/CommunityToolkit/dotnet
- **How it is used:** Observable objects and relay commands for the MVVM layer.

## ModernWpfUI

- **License:** MIT
- **Project home:** https://github.com/Kinnara/ModernWpf
- **How it is used:** Modern-looking control styles for the WPF UI.

## Entity Framework Core and Microsoft.Data.Sqlite

- **License:** MIT
- **Project home:** https://github.com/dotnet/efcore
- **How it is used:** Object-relational mapping and SQLite access for the catalog database.

## SQLite

- **License:** Public domain (https://www.sqlite.org/copyright.html)
- **Project home:** https://www.sqlite.org/
- **How it is used:** Embedded database engine for the catalog.

## Microsoft.Extensions.*

- **License:** MIT
- **Project home:** https://github.com/dotnet/runtime
- **How it is used:** Configuration binding, dependency injection, logging, and the generic host.

## Microsoft.Web.WebView2

- **License:** Microsoft Software License Terms (proprietary, royalty-free for distribution), https://aka.ms/webview2/license
- **Project home:** https://developer.microsoft.com/microsoft-edge/webview2/
- **How it is used:** Embeds a Chromium-based browser surface inside the right-sidebar editor's `LocationMapView` user control to render the interactive GPS preview map. The `Microsoft.Web.WebView2` NuGet binaries (`WebView2Loader.dll` etc.) ship inside the installer; the underlying Chromium runtime is the **WebView2 Evergreen Runtime**, a Windows component pre-installed on Windows 10 21H1+ and Windows 11. On older systems the runtime can be installed for free from the project home above. Shipped unmodified as a NuGet binary dependency.

## Leaflet

- **Version:** 1.9.4
- **License:** BSD 2-Clause, https://github.com/Leaflet/Leaflet/blob/main/LICENSE
- **Project home:** https://leafletjs.com/
- **How it is used:** Open-source JavaScript map library used by the embedded `LocationMapView` to render the interactive OpenStreetMap tile pane and the GPS marker. **Bundled, not fetched from a CDN:** the library's CSS and JS (and the default marker PNG images) are embedded in the application assembly and inlined into the WebView2 page at runtime, so the map library works fully offline. Only the OpenStreetMap *tiles* require network access. Shipped unmodified from the upstream 1.9.4 `dist/` distribution.

## OpenStreetMap / Nominatim

- **License of data:** Open Database License (ODbL), https://www.openstreetmap.org/copyright
- **Service home:** https://www.openstreetmap.org/, Nominatim at https://nominatim.org/
- **How it is used:** Two ways, both subject to the ODbL:
  - **Map tiles** for the right-sidebar `LocationMapView` preview are fetched from OpenStreetMap's public tile servers by the embedded Leaflet page. The "© OpenStreetMap contributors" attribution is rendered inline on the map per the OSM tile usage policy and is clickable (opens the upstream copyright page in the system default browser).
  - **Reverse-geocoding** of GPS coordinates to place names. The application sends optional, user-initiated queries to the public Nominatim instance (the bulk *Fill missing locations from GPS…* command and the new in-sidebar manual GPS picker both route through this).

## Oxygen Icons

- **License:** GNU Lesser General Public License v3 (LGPL-3.0), https://www.gnu.org/licenses/lgpl-3.0.html
- **Designer:** Oxygen Team / KDE
- **Project home:** https://github.com/KDE/oxygen-icons
- **Specific icon used:** `Apps-kmplayer` from the Oxygen icon set, redistributed by IconArchive (https://www.iconarchive.com/show/oxygen-icons-by-oxygen-icons.org/Apps-kmplayer-icon.html).
- **How it is used:** Shipped unmodified as the application icon (`AppIcon.ico` / `AppIcon.png`) for the main window, secondary windows, the executable, and the Velopack installer / Start-menu shortcut.

---

If you redistribute Find That Shot, you must keep this file and the `LICENSE` file alongside the executable. If you modify the application and distribute the result, you must do so under the GNU General Public License v3.
