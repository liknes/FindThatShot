using VideoArchiveManager.Core.Models;

namespace VideoArchiveManager.Core.Services;

public interface ISidecarService
{
    bool IsEnabled { get; }

    Task<SidecarWriteResult> WriteAsync(
        VideoItem video,
        IEnumerable<Tag> tags,
        CancellationToken cancellationToken = default);

    Task<SidecarBatchResult> WriteManyAsync(
        IEnumerable<(VideoItem Video, IReadOnlyList<Tag> Tags)> items,
        CancellationToken cancellationToken = default);

    // Reads and parses the sidecar sitting next to a video, if one exists.
    // Returns null when there is no sidecar or it can't be read/parsed —
    // a sidecar is best-effort metadata, never a hard dependency, so a bad
    // file must not break a scan. Used to rehydrate a freshly-imported
    // clip (tags, rating, status, …) from a sidecar written by a previous
    // install.
    Task<SidecarData?> TryReadAsync(
        string videoPath,
        CancellationToken cancellationToken = default);

    string GetSidecarPathFor(string videoPath);

    // True when a sidecar file already exists on disk for this video. Used to
    // keep an existing sidecar in sync with the catalog even when writing NEW
    // sidecars is disabled — the presence of the file is itself the opt-in.
    bool SidecarExistsFor(string videoPath);

    // True when this video should be written: either new-sidecar writing is
    // enabled, or a sidecar already exists and must be kept current.
    bool ShouldWriteFor(string videoPath);
}

public class SidecarWriteResult
{
    public bool Skipped { get; init; }

    public bool Written { get; init; }

    public string? Path { get; init; }

    public string? ErrorMessage { get; init; }

    public static SidecarWriteResult DisabledOrSkipped() =>
        new() { Skipped = true };

    public static SidecarWriteResult Ok(string path) =>
        new() { Written = true, Path = path };

    public static SidecarWriteResult Failure(string message, string? path = null) =>
        new() { Written = false, Skipped = false, ErrorMessage = message, Path = path };
}

public class SidecarBatchResult
{
    public int Attempted { get; init; }

    public int Written { get; init; }

    public int Failed { get; init; }

    public int Skipped { get; init; }

    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();
}

// Parsed, transport-agnostic view of a sidecar's catalog metadata. Tag
// type/status are kept as raw strings here so the Core layer stays free of
// JSON concerns; callers map them onto the enums.
public sealed class SidecarData
{
    public int Rating { get; init; }

    public string? Status { get; init; }

    public string? Notes { get; init; }

    public string? LocationText { get; init; }

    public string? ContextText { get; init; }

    public DateTime? FolderDate { get; init; }

    public IReadOnlyList<SidecarTagData> Tags { get; init; } = Array.Empty<SidecarTagData>();
}

public sealed class SidecarTagData
{
    public string Name { get; init; } = string.Empty;

    public string? Type { get; init; }
}
