namespace VideoArchiveManager.Core.Services;

public interface IFfprobeService
{
    bool IsAvailable();

    string ResolveExecutablePath();

    Task<FfprobeResult?> ProbeAsync(string filePath, CancellationToken cancellationToken = default);
}
