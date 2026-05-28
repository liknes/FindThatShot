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
}
