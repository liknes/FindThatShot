using System.ComponentModel.DataAnnotations;

namespace VideoArchiveManager.Core.Models;

/// <summary>
/// Cached result of a reverse-geocoding lookup. We round the coordinates to a
/// fixed precision so that nearby clips collapse to the same cache key, which
/// keeps the API call count tiny and behaves politely toward free services
/// such as OpenStreetMap Nominatim.
/// </summary>
public class GeocodeCacheEntry
{
    public int Id { get; set; }

    /// <summary>Latitude rounded to <see cref="CoordinateDecimals"/> decimals.</summary>
    public double LatRounded { get; set; }

    /// <summary>Longitude rounded to <see cref="CoordinateDecimals"/> decimals.</summary>
    public double LonRounded { get; set; }

    /// <summary>Short name we'd put in <c>VideoItem.LocationText</c>, e.g. "Hardangervidda".</summary>
    [Required]
    public string LocationShort { get; set; } = string.Empty;

    /// <summary>The full human-readable place name returned by the geocoder, kept for reference.</summary>
    public string? DisplayName { get; set; }

    public string? Country { get; set; }

    public string? Region { get; set; }

    /// <summary>UTC timestamp of when this cache entry was created.</summary>
    public DateTime LookedUpAt { get; set; } = DateTime.UtcNow;

    /// <summary>The provider used (e.g. "nominatim"). Allows future swap-in of other geocoders.</summary>
    [Required]
    public string Provider { get; set; } = "nominatim";

    /// <summary>
    /// How many decimals we round coordinates to when building the cache key.
    /// 4 decimals ≈ 11 metres at the equator, which is well within "same place"
    /// for our purposes and keeps lookup counts small.
    /// </summary>
    public const int CoordinateDecimals = 4;

    public static double Round(double coordinate)
        => Math.Round(coordinate, CoordinateDecimals, MidpointRounding.AwayFromZero);
}
