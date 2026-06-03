using VideoArchiveManager.Core.Models;

namespace VideoArchiveManager.Core.Services;

/// <summary>
/// Computes aggregate metrics across the whole catalog for the statistics
/// dashboard. Read-only: it only queries the catalog database and never reads,
/// moves, or modifies any source video file.
/// </summary>
public interface ICatalogStatisticsService
{
    Task<CatalogStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default);
}
