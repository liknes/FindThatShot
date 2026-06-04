namespace VideoArchiveManager.Core.Services;

public interface IThumbnailService
{
    bool IsAvailable();

    string ResolveExecutablePath();

    string GetThumbnailPath(int videoId);

    Task<string?> GenerateAsync(int videoId, string videoFilePath, double? durationSeconds, CancellationToken cancellationToken = default);

    // Deletes app-generated thumbnail cache files for the given video ids.
    // Only canonical "{id}.jpg" files inside the configured thumbnail
    // directory are touched. Returns the number of files actually deleted.
    int DeleteForVideos(IEnumerable<int> videoIds);

    // Canonical cache path for a moment thumbnail: a "{momentId}.jpg" file
    // inside a "Moments" subfolder of the configured thumbnail directory, kept
    // separate from whole-clip thumbnails so the two id spaces can't collide.
    string GetMomentThumbnailPath(int momentId);

    // Extracts a single frame at an explicit timestamp (seconds from the start
    // of the clip) for a moment, writing it to GetMomentThumbnailPath(momentId).
    // Unlike GenerateAsync this seeks to a caller-supplied position rather than
    // a fixed 15% of the duration. Returns the output path on success, null on
    // failure. Source video file is never modified.
    Task<string?> GenerateAtAsync(int momentId, string videoFilePath, double seekSeconds, CancellationToken cancellationToken = default);

    // Deletes cached "{momentId}.jpg" files for the given moment ids from the
    // "Moments" subfolder. Returns the number of files actually deleted.
    int DeleteForMoments(IEnumerable<int> momentIds);
}
