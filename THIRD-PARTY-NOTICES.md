# Third-party notices

Video Archive Manager itself is licensed under the GNU General Public License v3 (see `LICENSE` at the root of this repository). Source code is available at https://github.com/liknes/FindThatShot.

The installed application bundles or links to several third-party components. Their licenses, sources, and obligations are listed below. Where a component is GPL-licensed, the corresponding source is either bundled by upstream or available at the URL given here; you may also request the source through the project's GitHub repository.

---

## FFmpeg

- **Version:** 8.1.1 ("full" build by Gyan Doshi, https://www.gyan.dev/ffmpeg/builds/)
- **License:** GNU General Public License v3 (because of `--enable-gpl --enable-version3` configuration; bundles libx264, libx265, libxavs2, libxvid, libvidstab, librubberband, and others under GPL terms)
- **Project home:** https://ffmpeg.org/
- **Source code:** https://ffmpeg.org/download.html#get-sources, plus the matching upstream tarball and configure options listed at https://www.gyan.dev/ffmpeg/builds/
- **How it is used:** Video Archive Manager invokes `ffmpeg.exe` and `ffprobe.exe` as separate processes for metadata extraction and thumbnail generation. The binaries are shipped unmodified.

## VLC / LibVLC

- **Version:** 3.0.x (via the `VideoLAN.LibVLC.Windows` NuGet package)
- **License:** GNU Lesser General Public License v2.1 or later for `libvlc.dll` and `libvlccore.dll`; some bundled plugins are licensed under the GNU General Public License v2 or later.
- **Project home:** https://www.videolan.org/vlc/
- **Source code:** https://www.videolan.org/vlc/download-sources.html
- **How it is used:** Video Archive Manager loads `libvlc.dll` to render in-app video previews. The libraries are shipped unmodified.

## LibVLCSharp

- **License:** GNU Lesser General Public License v2.1 or later
- **Project home:** https://code.videolan.org/videolan/LibVLCSharp
- **How it is used:** Managed .NET bindings for LibVLC.

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

## OpenStreetMap / Nominatim

- **License of data:** Open Database License (ODbL), https://www.openstreetmap.org/copyright
- **Service:** Nominatim, https://nominatim.org/
- **How it is used:** Reverse-geocoding GPS coordinates to place names. The application sends optional, user-initiated queries to the public Nominatim instance. Returned data is subject to the ODbL.

## Oxygen Icons

- **License:** GNU Lesser General Public License v3 (LGPL-3.0), https://www.gnu.org/licenses/lgpl-3.0.html
- **Designer:** Oxygen Team / KDE
- **Project home:** https://github.com/KDE/oxygen-icons
- **Specific icon used:** `Apps-kmplayer` from the Oxygen icon set, redistributed by IconArchive (https://www.iconarchive.com/show/oxygen-icons-by-oxygen-icons.org/Apps-kmplayer-icon.html).
- **How it is used:** Shipped unmodified as the application icon (`AppIcon.ico` / `AppIcon.png`) for the main window, secondary windows, the executable, and the Velopack installer / Start-menu shortcut.

---

If you redistribute Video Archive Manager, you must keep this file and the `LICENSE` file alongside the executable. If you modify the application and distribute the result, you must do so under the GNU General Public License v3.
