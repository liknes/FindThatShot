namespace VideoArchiveManager.Core.Services;

public interface IThumbnailService
{
    bool IsAvailable();

    string ResolveExecutablePath();

    string GetThumbnailPath(int videoId);

    Task<string?> GenerateAsync(int videoId, string videoFilePath, double? durationSeconds, CancellationToken cancellationToken = default);
}
