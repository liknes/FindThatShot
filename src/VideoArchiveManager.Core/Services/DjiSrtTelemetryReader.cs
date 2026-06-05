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
using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace VideoArchiveManager.Core.Services;

/// <summary>
/// Reads DJI's standard flight-data <c>.SRT</c> companion file (written by the
/// DJI Fly app next to every recorded clip). The format is a normal SubRip
/// subtitle file whose text payload carries per-frame telemetry like:
/// <code>
/// [iso: 400] [shutter: 1/100] [fnum: 1.8] [latitude: 60.391263]
/// [longitude: 5.322054] [rel_alt: 5.1 abs_alt: 29.054]
/// </code>
/// We scan forward and return the first non-zero <c>latitude/longitude</c> pair
/// (the GPS values start at exactly 0.0/0.0 before the drone gets a fix). For
/// indoor flights where no fix is ever obtained, the method returns <c>null</c>.
/// </summary>
public sealed class DjiSrtTelemetryReader : IDjiSrtTelemetryReader
{
    // Matches "[latitude: <num>] [longitude: <num>]" with the standard DJI
    // bracket layout. Whitespace between the two brackets is allowed.
    private static readonly Regex LatLonPattern = new(
        @"\[latitude:\s*(?<lat>-?\d+(?:\.\d+)?)\][^\[]*\[longitude:\s*(?<lon>-?\d+(?:\.\d+)?)\]",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // Matches the DJI "[rel_alt: 107.400 abs_alt: 47.605]" bracket; we lift
    // rel_alt (height above takeoff, in metres) for the endpoint tooltips.
    private static readonly Regex RelAltPattern = new(
        @"\[rel_alt:\s*(?<alt>-?\d+(?:\.\d+)?)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // Matches the absolute-altitude half of the same DJI bracket
    // ("… abs_alt: 47.605]"), surfaced alongside rel_alt in the player readout.
    private static readonly Regex AbsAltPattern = new(
        @"abs_alt:\s*(?<alt>-?\d+(?:\.\d+)?)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // Camera-exposure brackets DJI writes per frame, e.g.
    // "[iso: 100] [shutter: 1/2000.0] [fnum: 1.7] [ev: 0] [focal_len: 24.00]".
    // Brackets are optional / labels are case-insensitive so we still parse the
    // handful of older firmware layouts that drop the surrounding "[ ]".
    private static readonly Regex IsoPattern = new(
        @"iso:\s*(?<v>\d+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex ShutterPattern = new(
        @"shutter:\s*(?<v>[\d./]+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex FnumPattern = new(
        @"fnum:\s*(?<v>\d+(?:\.\d+)?)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex EvPattern = new(
        @"ev:\s*(?<v>-?\d+(?:\.\d+)?)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex FocalPattern = new(
        @"focal_len:\s*(?<v>\d+(?:\.\d+)?)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    // SubRip cue timing line: "00:00:01,033 --> 00:00:01,066" (comma or dot
    // decimal separator). Only the start / end are captured.
    private static readonly Regex CueTimePattern = new(
        @"(?<sh>\d+):(?<sm>\d+):(?<ss>\d+)[,.](?<sms>\d+)\s*-->\s*(?<eh>\d+):(?<em>\d+):(?<es>\d+)[,.](?<ems>\d+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // Strips the "<font …>" / "</font>" wrappers DJI sometimes puts around the
    // telemetry text so the field regexes see clean content.
    private static readonly Regex FontTagPattern = new(
        @"</?font[^>]*>",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    // 1e-6 ≈ 0.1 m at the equator — anything smaller is "still at zero".
    private const double NearZero = 1e-6;

    private readonly ILogger<DjiSrtTelemetryReader>? _logger;

    public DjiSrtTelemetryReader(ILogger<DjiSrtTelemetryReader>? logger = null)
    {
        _logger = logger;
    }

    public async Task<DjiTelemetrySummary?> TryReadAsync(
        string videoPath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(videoPath)) return null;

        var srtPath = TryResolveCompanion(videoPath);
        if (srtPath is null) return null;

        try
        {
            // Stream the file line-by-line so we never pay to load a multi-MB
            // SRT into memory; we usually bail out within the first few hundred
            // lines once the GPS gets a fix.
            using var stream = new FileStream(
                srtPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 8192,
                options: FileOptions.SequentialScan | FileOptions.Asynchronous);
            using var reader = new StreamReader(stream);

            string? line;
            while ((line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false)) is not null)
            {
                if (!TryParseLatLon(line, out var lat, out var lon)) continue;

                return new DjiTelemetrySummary { Latitude = lat, Longitude = lon };
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "DJI SRT parse failed for {Path}", srtPath);
            return null;
        }

        return null;
    }

    public async Task<IReadOnlyList<GeoPoint>?> TryReadFlightPathAsync(
        string videoPath,
        int maxPoints = 600,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(videoPath)) return null;
        if (maxPoints < 2) maxPoints = 2;

        var srtPath = TryResolveCompanion(videoPath);
        if (srtPath is null) return null;

        // Collect the raw, ordered fixes first, then downsample once at the
        // end. DJI logs ~30 samples/second, so a multi-minute clip can carry
        // tens of thousands of identical-ish points — far more than the map
        // needs. Coalescing consecutive duplicate coordinates while reading
        // keeps the working set small for long hovers.
        var raw = new List<GeoPoint>();
        try
        {
            using var stream = new FileStream(
                srtPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 8192,
                options: FileOptions.SequentialScan | FileOptions.Asynchronous);
            using var reader = new StreamReader(stream);

            string? line;
            while ((line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false)) is not null)
            {
                if (!TryParseLatLon(line, out var lat, out var lon)) continue;

                // Drop exact consecutive repeats (the drone hovering / GPS not
                // refreshing) so the polyline isn't thousands of stacked points.
                if (raw.Count > 0)
                {
                    var last = raw[^1];
                    if (Math.Abs(last.Latitude - lat) < NearZero &&
                        Math.Abs(last.Longitude - lon) < NearZero)
                    {
                        continue;
                    }
                }

                raw.Add(new GeoPoint(lat, lon, TryParseRelAlt(line)));
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "DJI SRT flight-path parse failed for {Path}", srtPath);
            return null;
        }

        return Downsample(raw, maxPoints);
    }

    public async Task<IReadOnlyList<DjiTelemetrySample>?> TryReadTelemetryTrackAsync(
        string videoPath,
        int maxSamples = 200_000,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(videoPath)) return null;
        if (maxSamples < 1) maxSamples = 1;

        var srtPath = TryResolveCompanion(videoPath);
        if (srtPath is null) return null;

        var samples = new List<DjiTelemetrySample>();
        try
        {
            using var stream = new FileStream(
                srtPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 8192,
                options: FileOptions.SequentialScan | FileOptions.Asynchronous);
            using var reader = new StreamReader(stream);

            // Parse the SRT one cue at a time. A cue is "<index>\n<time -->
            // time>\n<text…>\n\n"; we treat any line carrying "-->" as the cue
            // boundary, then accumulate following non-blank lines as the text
            // payload until the blank-line separator (or EOF). This is tolerant
            // of the index line being absent / reordered, which some exporters do.
            TimeSpan? start = null;
            TimeSpan? end = null;
            var text = new System.Text.StringBuilder();

            string? line;
            while ((line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false)) is not null)
            {
                var timeMatch = CueTimePattern.Match(line);
                if (timeMatch.Success)
                {
                    // A new timing line starts a new cue — flush the one we were
                    // building (if it had both a window and some text).
                    FlushCue(samples, start, end, text);
                    start = ParseCueTime(timeMatch, "sh", "sm", "ss", "sms");
                    end = ParseCueTime(timeMatch, "eh", "em", "es", "ems");
                    text.Clear();
                    continue;
                }

                if (string.IsNullOrWhiteSpace(line))
                {
                    FlushCue(samples, start, end, text);
                    start = end = null;
                    text.Clear();
                    continue;
                }

                // Anything that isn't a timing line, a blank separator, or a
                // bare numeric index line is telemetry text for the open cue.
                if (start is not null && !IsIndexLine(line))
                {
                    if (text.Length > 0) text.Append(' ');
                    text.Append(line);
                }

                if (samples.Count >= maxSamples) break;
            }

            // Flush a trailing cue with no terminating blank line (EOF).
            FlushCue(samples, start, end, text);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "DJI SRT telemetry parse failed for {Path}", srtPath);
            return null;
        }

        return samples.Count > 0 ? samples : null;
    }

    // Builds a DjiTelemetrySample from an accumulated cue and appends it when it
    // carries a valid time window and at least one parseable telemetry field.
    private static void FlushCue(
        List<DjiTelemetrySample> samples,
        TimeSpan? start,
        TimeSpan? end,
        System.Text.StringBuilder text)
    {
        if (start is null || end is null || text.Length == 0) return;

        var payload = FontTagPattern.Replace(text.ToString(), string.Empty);

        var sample = new DjiTelemetrySample(
            Start: start.Value,
            End: end.Value,
            Iso: TryParseInt(IsoPattern, payload),
            Shutter: TryParseString(ShutterPattern, payload),
            FNumber: TryParseDouble(FnumPattern, payload),
            Ev: TryParseDouble(EvPattern, payload),
            FocalLength: TryParseDouble(FocalPattern, payload),
            RelativeAltitude: TryParseRelAlt(payload),
            AbsoluteAltitude: TryParseDouble(AbsAltPattern, payload),
            Latitude: TryParseLatLon(payload, out var lat, out _) ? lat : null,
            Longitude: TryParseLatLon(payload, out _, out var lon) ? lon : null);

        // Skip cues that parsed nothing useful (e.g. a stray text block).
        if (sample is { Iso: null, Shutter: null, FNumber: null, Ev: null,
                FocalLength: null, RelativeAltitude: null, AbsoluteAltitude: null,
                Latitude: null, Longitude: null })
        {
            return;
        }

        samples.Add(sample);
    }

    private static bool IsIndexLine(string line)
    {
        var trimmed = line.Trim();
        if (trimmed.Length == 0) return false;
        foreach (var c in trimmed)
        {
            if (!char.IsDigit(c)) return false;
        }
        return true;
    }

    private static TimeSpan ParseCueTime(Match m, string h, string min, string s, string ms)
    {
        var hours = int.Parse(m.Groups[h].Value, CultureInfo.InvariantCulture);
        var minutes = int.Parse(m.Groups[min].Value, CultureInfo.InvariantCulture);
        var seconds = int.Parse(m.Groups[s].Value, CultureInfo.InvariantCulture);
        // SubRip uses 3-digit milliseconds; be defensive about other widths.
        var millisRaw = m.Groups[ms].Value;
        var millis = int.Parse(millisRaw, CultureInfo.InvariantCulture);
        if (millisRaw.Length == 2) millis *= 10;
        else if (millisRaw.Length == 1) millis *= 100;
        else if (millisRaw.Length > 3) millis /= (int)Math.Pow(10, millisRaw.Length - 3);
        return new TimeSpan(0, hours, minutes, seconds, millis);
    }

    private static int? TryParseInt(Regex pattern, string text)
    {
        var m = pattern.Match(text);
        return m.Success && int.TryParse(m.Groups["v"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v)
            ? v
            : null;
    }

    private static double? TryParseDouble(Regex pattern, string text)
    {
        var m = pattern.Match(text);
        return m.Success && double.TryParse(m.Groups[m.Groups["v"].Success ? "v" : "alt"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)
            ? v
            : null;
    }

    private static string? TryParseString(Regex pattern, string text)
    {
        var m = pattern.Match(text);
        if (!m.Success) return null;
        var value = m.Groups["v"].Value.Trim();
        return value.Length > 0 ? value : null;
    }

    // Uniformly thins the points down to <= maxPoints while always preserving
    // the first and last fix so the takeoff and landing markers stay accurate.
    private static IReadOnlyList<GeoPoint> Downsample(List<GeoPoint> points, int maxPoints)
    {
        if (points.Count <= maxPoints) return points;

        var result = new List<GeoPoint>(maxPoints);
        // Step across the source so the last sampled index lands on the final
        // point; the explicit add of points[^1] below guarantees the endpoint.
        var step = (double)(points.Count - 1) / (maxPoints - 1);
        for (int i = 0; i < maxPoints - 1; i++)
        {
            result.Add(points[(int)(i * step)]);
        }
        result.Add(points[^1]);
        return result;
    }

    // Shared lat/lon extraction used by both the single-fix and full-path
    // readers. Returns true only for an in-range, past-the-no-fix-zero sample.
    private static bool TryParseLatLon(string line, out double lat, out double lon)
    {
        lat = 0;
        lon = 0;

        var match = LatLonPattern.Match(line);
        if (!match.Success) return false;

        if (!double.TryParse(
                match.Groups["lat"].Value,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out lat)) return false;
        if (!double.TryParse(
                match.Groups["lon"].Value,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out lon)) return false;

        // Skip uninitialised / no-fix readings (exactly 0.0/0.0 before lock).
        if (Math.Abs(lat) < NearZero && Math.Abs(lon) < NearZero) return false;

        // Drop anything outside the valid WGS84 envelope (parser glitches,
        // corrupt SRT, etc.).
        if (lat is < -90 or > 90) return false;
        if (lon is < -180 or > 180) return false;

        return true;
    }

    // Pulls the DJI rel_alt (metres above takeoff) from a telemetry line, or
    // null when the line doesn't carry one / it doesn't parse.
    private static double? TryParseRelAlt(string line)
    {
        var match = RelAltPattern.Match(line);
        if (!match.Success) return null;
        return double.TryParse(
            match.Groups["alt"].Value,
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out var alt) ? alt : null;
    }

    /// <summary>
    /// Looks for a sibling SRT file with the same base name as the video, using
    /// the platform's case-sensitivity. DJI writes uppercase ".SRT" so we try
    /// that first, then fall back to lowercase for Linux/macOS users.
    /// </summary>
    private static string? TryResolveCompanion(string videoPath)
    {
        var upper = Path.ChangeExtension(videoPath, ".SRT");
        if (File.Exists(upper)) return upper;

        var lower = Path.ChangeExtension(videoPath, ".srt");
        if (File.Exists(lower)) return lower;

        return null;
    }
}
