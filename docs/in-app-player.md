# In-app player — engine notes

Forward-looking notes on the in-app video player. Aimed at future-me staring
at a runtime issue six months from now, not at end users. Pairs with the
0.4.x changelog entries that explain the migration history.

## What we use

The in-app player is **`Sinaxxr.FFME.Windows` 8.0.361-sinaxxr.2**, an actively-
maintained community fork of [`unosquare/ffmediaelement`](https://github.com/unosquare/ffmediaelement)
that tracks **FFmpeg 8.x**. Upstream FFME's most recent release at the time of
this migration (`FFME.Windows 7.0.361-beta.1`, June 2024) was hard-pinned to
FFmpeg 7 — the Sinaxxr fork was picked specifically so we can keep using the
single FFmpeg 8 shared-binary bundle in `tools/ffmpeg/` for both FFprobe (CLI,
metadata pipeline) and the in-app player. One copy of FFmpeg on disk; one
version to think about.

The control is `<ffme:MediaElement>` — a real WPF visual that renders decoded
frames to a `WriteableBitmap`. There is no `HwndHost`, no Win32 child window,
no DXGI swap chain bleeding through XAML. `Background` and `Stretch` behave
like on any other WPF element. The earlier interop saga that lived in
`Helpers/Interop/HwndHostBackgroundFix.cs` and the custom `AspectRatioPanel`
existed to work around problems that *only the previous engine* had; both are
gone.

## Engine wiring

- **NuGet:** `Sinaxxr.FFME.Windows` 8.0.361-sinaxxr.2 in `VideoArchiveManager.App.csproj`.
- **FFmpeg directory:** `App.xaml.cs` sets `Unosquare.FFME.Library.FFmpegDirectory`
  to `<install>/tools/ffmpeg/` and gates `App.IsPlayerAvailable` on the directory
  containing `avcodec-62.dll`. If that file is missing, `App.PlayerInitError`
  is set and the player UI degrades gracefully.
- **Build-time copy:** `VideoArchiveManager.App.csproj` has an
  `AfterTargets="Build"` MSBuild target that mirrors `tools/ffmpeg/` into the
  build output so local debug runs work. The target is skipped when
  `IsPublishing=true` so `publish.ps1`'s existing copy step (and its
  `-SkipBundleFfmpeg` switch) stays authoritative for release artifacts.
- **Where the player lives in code:**
  - XAML: `MainWindow.xaml`, the `<ffme:MediaElement x:Name="VideoPlayer">` inside the
    Grid.Column="2" black `Border`.
  - Code-behind: `MainWindow.xaml.cs`, the `MediaElement_*` handlers and the
    async `Open(Uri)` / `Close()` / `Play()` / `Pause()` / `Seek(TimeSpan)`
    pipeline driven by `Detail_PropertyChanged`.

## How errors surface today

FFME does not raise a `MediaFailed` event; failures show up in two places:

1. **Exceptions** thrown out of `VideoPlayer.Open(uri)` / `Play()` / `Seek()`.
   `MainWindow.xaml.cs` catches these in `Detail_PropertyChanged` and writes
   the first 20 characters of `ex.Message` into `PlayerDurationText`, plus
   "error" into `PlayerCurrentTimeText`.
2. **`MediaElement.MessageLogged`** events with
   `MessageType == MediaLogMessageType.Error` — typically internal FFmpeg
   diagnostics that didn't bubble up as an exception. `MainWindow.xaml.cs`
   subscribes to this and falls back to the same minimal "error / --:--" inline
   display in the player toolbar.

This is intentionally minimal — enough to know *something* failed without
spamming the UI. **If you want richer error visibility** (e.g. a status-bar
entry that mirrors `MainViewModel.LastSaveStatus`, or a popup with the FFmpeg
log message), the place to expand it is the `MediaElement_MessageLogged`
handler at the bottom of `MainWindow.xaml.cs`. The argument has `e.Message`,
`e.MessageType`, and the originating media context — plumb whichever is most
useful into a viewmodel-level status property.

## What to do if Sinaxxr.FFME.Windows goes dormant

The fork is a single maintainer's effort and isn't on a release cadence. If
its NuGet feed stops getting updates and there's a real reason to move
(security advisory, breaking FFmpeg API change in a future version, the fork
gets archived), the migration back to **upstream FFME 7.0** is short:

1. **Swap the package** in `src/VideoArchiveManager.App/VideoArchiveManager.App.csproj`:

   ```xml
   <!-- before -->
   <PackageReference Include="Sinaxxr.FFME.Windows" Version="8.0.361-sinaxxr.2" />
   <!-- after -->
   <PackageReference Include="FFME.Windows" Version="7.0.361-beta.1" />
   ```

2. **Downgrade `tools/ffmpeg/`** from FFmpeg 8.x (avcodec-62) to **FFmpeg 7.x**
   (avcodec-61) shared binaries. Get them from
   [BtbN/FFmpeg-Builds](https://github.com/BtbN/FFmpeg-Builds/releases) — the
   `ffmpeg-n7.x-latest-win64-gpl-shared` zip's `bin/` folder contents replace
   what's currently in `tools/ffmpeg/`. Both `ffmpeg.exe` and `ffprobe.exe`
   stay forward/backward compatible at the JSON output level, so the FFprobe
   metadata pipeline keeps working with no code change.

3. **Update the avcodec sentinel** in `App.xaml.cs` from `avcodec-62.dll` to
   `avcodec-61.dll`. Search for the string literal — it only appears in one
   place. (The other DLL major-version numbers will also shift; the sentinel
   is just the one we check for "is FFmpeg present at all".)

4. **Rebuild + retest playback.** No XAML, viewmodel, or event-handler changes
   are needed because both forks expose the same `Unosquare.FFME` namespace
   and `MediaElement` API surface.

This is meaningfully easier than the original migration FROM LibVLCSharp because
both forks are API-compatible. Estimate ~15 minutes of focused work.

## What we'd avoid going back to

The history is in the changelog, but for the avoidance of doubt:

- **`LibVLCSharp.WPF`** — `HwndHost`-based, white flashes around the picture
  were architectural and not fixable from the WPF side. The 0.4.x entries in
  `CHANGELOG.md` document five iterations of failed workarounds before the
  engine swap.
- **WPF `MediaElement`** — would require the user to install the Microsoft
  HEVC Video Extension from the Store ($0.99 or OEM-bundled) to play DJI
  HEVC clips. Codec coverage is too narrow for this app's use case.
- **WebView2 + HTML5 `<video>`** — works, but adds a ~120MB Edge runtime
  dependency for a control that we ship one of. Not worth it.

If a third FFmpeg-shared-DLL-loading + WPF-native player ever appears with
better maintenance posture than either fork, that's worth re-evaluating —
the engine surface here is small (`Open`/`Close`/`Play`/`Pause`/`Seek`,
`Position`/`NaturalDuration`/`IsPlaying`/`IsSeekable`, `MediaOpened`/`MediaEnded`/
`PropertyChanged`/`MessageLogged`), so swapping it out a third time would
again be a focused change rather than a rewrite.
