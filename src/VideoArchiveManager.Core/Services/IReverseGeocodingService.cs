// Find That Shot - organize and search a large local video archive.
// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Ingve Moss Liknes <findthatshot@ingve.no>
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.
namespace VideoArchiveManager.Core.Services;

public sealed class GeocodeResult
{
    public required string LocationShort { get; init; }
    public string? DisplayName { get; init; }
    public string? Country { get; init; }
    public string? Region { get; init; }
}

public interface IReverseGeocodingService
{
    /// <summary>
    /// Resolve a GPS coordinate to a human-readable place name. Implementations
    /// MUST cache results so that calling this repeatedly with the same (or very
    /// nearby) coordinates does not generate repeated network calls.
    /// </summary>
    /// <returns><c>null</c> if the coordinate could not be resolved.</returns>
    Task<GeocodeResult?> LookupAsync(
        double latitude,
        double longitude,
        CancellationToken cancellationToken = default);
}
