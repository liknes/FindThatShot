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

    string GetSidecarPathFor(string videoPath);
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
