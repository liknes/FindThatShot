// Find That Shot - organize and search a large local video archive.
// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Ingve Moss Liknes <findthatshot@ingve.no>
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.
namespace VideoArchiveManager.Core.Configuration;

public class AppSettings
{
    // UI language as a .NET culture name (e.g. "en", "nb-NO"). Null/empty means
    // "follow the operating system". Applied at startup before the first window
    // paints and switchable live from Settings → General → Language.
    public string? Language { get; set; }

    public string? FfmpegPath { get; set; }

    public string? FfprobePath { get; set; }

    public string? ThumbnailDirectory { get; set; }

    public string? DatabasePath { get; set; }

    public string? BackupDirectory { get; set; }

    public bool AutoBackupOnStartup { get; set; } = true;

    public int BackupRetentionCount { get; set; } = 7;

    public bool WriteSidecarFiles { get; set; } = false;

    // When true, the in-app player substitutes the matching DaVinci Resolve
    // proxy (sibling "Proxy" folder, same base name as the hero) for the
    // original clip on Play in app. Falls back to the hero file when no proxy
    // is found. Defaults to false so new users see no surprise substitution —
    // opt in via Settings once a proxy workflow is established. The catalog,
    // thumbnails, ffprobe metadata and "Play externally" command always
    // continue to point at the hero file regardless of this flag.
    public bool PreferProxyForPlayback { get; set; } = false;

    // When true, the sidebar map plots the full DJI flight path (read live
    // from a sibling ".SRT" companion) as a polyline with start / end dots.
    // This is purely a *display* toggle: turning it off only suppresses the
    // on-map track — clips are still geotagged with their single GPS fix
    // (extracted at scan time and persisted to the catalog / sidecars), and
    // the single-location marker still shows. Defaults to on to preserve the
    // existing behaviour; pilots who'd rather not have a route drawn on a map
    // (e.g. while screen-sharing) can switch it off without losing geotags.
    public bool ShowDroneFlightPaths { get; set; } = true;

    // When true, the in-app player shows a formatted telemetry strip under the
    // video for DJI clips (ISO / shutter / aperture / focal length / altitude /
    // GPS, read live from the sibling ".SRT" companion and synced to the current
    // playback position). Purely a display toggle — the underlying SRT is still
    // used for the flight-path map regardless. Defaults to on; the player has an
    // inline toggle that round-trips to this value, so the choice survives a
    // restart.
    public bool ShowPlayerTelemetry { get; set; } = true;

    // GitHub repo (https://github.com/owner/repo) that hosts Velopack
    // release artifacts. The app's "Check for updates" command reads this
    // value at runtime. Empty / null disables the check.
    public string? UpdateRepoUrl { get; set; } = "https://github.com/liknes/FindThatShot";

    public int MaxScanParallelism { get; set; } = 4;

    public int PageSize { get; set; } = 200;

    // --- Hover-scrub previews (catalog cards) -----------------------------
    // When true, hovering a catalog card and sweeping the pointer horizontally
    // scrubs through a handful of preview frames sampled across the clip — fast
    // visual triage without opening the player. Generation is LAZY: frames are
    // extracted (via the same ffmpeg the thumbnail pipeline uses) the first
    // time a clip is hovered and then cached on disk, so a clip you never hover
    // costs nothing. Defaults ON; it's cheap because it never runs as a batch.
    public bool EnableHoverScrubPreview { get; set; } = true;

    // How many evenly-spaced frames to sample across a clip for the hover
    // scrub. More frames = finer scrubbing at a higher one-time decode cost
    // per hovered clip. Clamped to a sane band at use sites.
    public int HoverScrubFrameCount { get; set; } = 12;

    // --- AI auto-tagging & natural-language search (opt-in) ---------------
    // Master switch for the whole CLIP subsystem. When false, no model is
    // downloaded or loaded, no scoring pass runs, the AI menu commands are
    // hidden, and natural-language search is disabled. Defaults OFF so nobody
    // pays the model download or the CPU cost unless they opt in (mirrors how
    // sidecars and proxy-substitution default off).
    public bool EnableAiTagging { get; set; } = false;

    // Optional drop-in location for the CLIP ONNX model bundle (image encoder,
    // text encoder, tokenizer vocab, manifest). When empty the app uses its
    // managed model directory under app-data and can download the bundle on
    // demand. A power user / offline machine can pre-place the files and point
    // here to skip the download entirely (mirrors the tools/ffmpeg pattern).
    public string? AiModelDirectory { get; set; }

    // URL the model bundle (.zip) is fetched from on first use
    // (download-on-demand) by the in-app "Download model" button. Points at the
    // GitHub Release asset for this repo; the archive is produced by
    // scripts/export-mclip-onnx.py --zip and uploaded under the models-v2 tag.
    // This is the MULTILINGUAL bundle (CLIP ViT-B/32 image encoder + a
    // multilingual DistilBERT text encoder), so natural-language search works in
    // 50+ languages. If you re-export the model, upload it under the SAME
    // tag/filename or bump this default. Power users can still override via
    // AiModelDirectory (a drop-in folder) or by editing this in appsettings.json.
    public string? AiModelDownloadUrl { get; set; } =
        "https://github.com/liknes/FindThatShot/releases/download/models-v2/clip-multilingual-v1.zip";

    // Frame sampling is duration-proportional: we aim for one sampled frame
    // every AiSecondsPerFrame seconds of footage, then clamp the count to
    // [AiMinFramesPerClip, AiMaxFramesPerClip]. A single thumbnail is a poor
    // signal for a whole video, and a flat frame count under-samples long clips
    // while over-sampling short ones, so a 30s clip lands on the floor and a
    // 13-minute clip scales up to the cap. Frames are max-pooled for tag/search
    // scores, so more frames = better recall on what a clip "ever shows" at
    // higher decode/inference cost.
    public double AiSecondsPerFrame { get; set; } = 20d;

    // Lower bound so even very short clips get a few viewpoints (start/mid/end).
    public int AiMinFramesPerClip { get; set; } = 4;

    // Upper bound so a feature-length clip can't explode the per-clip cost
    // (each frame is one ffmpeg decode + one CLIP image inference).
    public int AiMaxFramesPerClip { get; set; } = 24;

    // Cosine-similarity floor (CLIP image↔text) above which a vocabulary label
    // becomes an AiTagSuggestion. CLIP matches typically score ~0.22–0.35, so
    // ~0.26 is a sensible default; raise for fewer/cleaner suggestions.
    public double AiSuggestionThreshold { get; set; } = 0.26;

    // Cap on how many suggestions a single clip can produce (the top-N labels
    // by confidence above the threshold), so the review queue stays focused.
    public int AiMaxSuggestionsPerClip { get; set; } = 6;

    // When true, the scoring pass learns a per-label confidence threshold from
    // your own accept/reject history instead of applying the single global
    // AiSuggestionThreshold to every label. Labels you keep rejecting get a
    // higher bar (suppressed), labels you consistently accept get a lower one
    // (surfaced sooner) — so suggestions sharpen toward your footage over time.
    // Inert until a label has enough decided suggestions; falls back to the
    // global threshold otherwise. CLIP weights are never touched.
    public bool AiAdaptiveThresholds { get; set; } = true;

    // Persisted main-window sidebar layout. Width is the user's last
    // dragged width of the FOLDERS / TAGS / CAMERAS rail; the three
    // *Expanded flags persist Lightroom-style panel collapse state so
    // the user's layout choices survive an app restart. All four
    // honour their defaults if absent or out-of-range from settings.json.
    public double SidebarWidth { get; set; } = 260d;

    public bool SidebarFoldersExpanded { get; set; } = true;

    public bool SidebarTagsExpanded { get; set; } = true;

    public bool SidebarCamerasExpanded { get; set; } = true;

    public bool SidebarDateExpanded { get; set; } = true;

    public bool SidebarSavedSearchesExpanded { get; set; } = true;

    // Persisted collapse state for the right-hand clip editor's Lightroom-style
    // panels (MAP / TAGS / NOTES & RATING / MOMENTS). The map opens by default
    // because it's the at-a-glance "where was this shot" panel; the rest start
    // collapsed so the editor opens compact and the user reveals what they
    // work with, after which their choices survive a restart.
    public bool DetailMapPanelExpanded { get; set; } = true;

    public bool DetailTagsPanelExpanded { get; set; } = false;

    public bool DetailNotesPanelExpanded { get; set; } = false;

    public bool DetailMomentsPanelExpanded { get; set; } = false;

    // Persisted main-window placement so the window reopens at the size and
    // position the user left it. All four geometry values are nullable: null
    // means "never saved" and the XAML defaults (1400x880, centered) apply.
    // Position (Left/Top) is validated against the current monitor layout on
    // restore, so a window saved on a since-removed monitor falls back to the
    // centered default rather than opening off-screen. WindowMaximized is
    // restored on top of the saved normal-mode geometry, so un-maximizing
    // lands back on the user's last floating size.
    public double? WindowLeft { get; set; }

    public double? WindowTop { get; set; }

    public double? WindowWidth { get; set; }

    public double? WindowHeight { get; set; }

    public bool WindowMaximized { get; set; }

    // Up to 10 tags bound to number-key hotkeys (1-9 then 0) in review mode.
    // Pressing the digit toggles that tag on / off for the current clip, so a
    // user with a stable tag vocabulary can rate-and-tag a review queue without
    // leaving the keyboard. Each entry carries its slot (0-9; see PinnedTag),
    // so empty slots are simply absent rather than represented by gaps. Empty
    // by default — the feature is opt-in via Settings → Review hotkeys.
    public IReadOnlyList<PinnedTag> PinnedTags { get; set; } = Array.Empty<PinnedTag>();

    public IReadOnlyList<string> SupportedExtensions { get; set; } = new[]
    {
        ".mp4", ".mov", ".mxf", ".avi", ".mkv"
    };

    public IReadOnlyList<string> ExcludedFolderNames { get; set; } = new[]
    {
        "proxy",
        "proxies",
        "edits",
        "_proxy",
        "_edits",
        "_originals",
        "@eaDir",
        "@Recycle",
        ".Trash",
        "$RECYCLE.BIN",
        "System Volume Information"
    };

    public IReadOnlyList<string> ExcludedFileNamePatterns { get; set; } = new[]
    {
        "*.LRV",
        "*.THM"
    };

    public static string DefaultBaseDirectory =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "VideoArchiveManager");

    public static string DefaultDatabasePath =>
        Path.Combine(DefaultBaseDirectory, "catalog.db");

    public static string DefaultThumbnailDirectory =>
        Path.Combine(DefaultBaseDirectory, "Thumbnails");

    public static string DefaultBackupDirectory =>
        Path.Combine(DefaultBaseDirectory, "Backups");

    public static string DefaultLogDirectory =>
        Path.Combine(DefaultBaseDirectory, "Logs");

    public static string UserSettingsPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "VideoArchiveManager",
            "settings.json");

    public string EffectiveDatabasePath =>
        string.IsNullOrWhiteSpace(DatabasePath) ? DefaultDatabasePath : DatabasePath;

    public string EffectiveThumbnailDirectory =>
        string.IsNullOrWhiteSpace(ThumbnailDirectory) ? DefaultThumbnailDirectory : ThumbnailDirectory;

    public string EffectiveBackupDirectory =>
        string.IsNullOrWhiteSpace(BackupDirectory) ? DefaultBackupDirectory : BackupDirectory;
}
