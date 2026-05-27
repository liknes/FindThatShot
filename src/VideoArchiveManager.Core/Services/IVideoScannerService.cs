using VideoArchiveManager.Core.Models;

namespace VideoArchiveManager.Core.Services;

public interface IVideoScannerService
{
    Task ScanAsync(
        IEnumerable<RootFolder> rootFolders,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default);

    Task UpdateFileAvailabilityAsync(CancellationToken cancellationToken = default);
}
