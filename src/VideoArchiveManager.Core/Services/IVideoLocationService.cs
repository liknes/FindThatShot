using VideoArchiveManager.Core.Models;

namespace VideoArchiveManager.Core.Services;

public interface IVideoLocationService
{
    /// <summary>
    /// For each video that has GPS coordinates but no <c>LocationText</c>,
    /// reverse-geocode the coordinates and fill in <c>LocationText</c>. Existing
    /// (non-empty) location values are left untouched.
    /// </summary>
    /// <returns>The number of videos whose <c>LocationText</c> was filled in.</returns>
    Task<int> FillMissingLocationsAsync(
        IProgress<GeocodeProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
