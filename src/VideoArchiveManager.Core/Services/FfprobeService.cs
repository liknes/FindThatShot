using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using VideoArchiveManager.Core.Configuration;

namespace VideoArchiveManager.Core.Services;

public class FfprobeService : IFfprobeService
{
    private readonly ISettingsStore _settings;
    private readonly ILogger<FfprobeService> _logger;

    public FfprobeService(ISettingsStore settings, ILogger<FfprobeService> logger)
    {
        _settings = settings;
        _logger = logger;
    }

    public bool IsAvailable()
    {
        var path = ResolveExecutablePath();
        if (string.IsNullOrEmpty(path)) return false;
        if (File.Exists(path)) return true;
        return IsOnPath(path);
    }

    public string ResolveExecutablePath()
    {
        var configured = _settings.Current.FfprobePath;
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
        {
            return configured;
        }
        return "ffprobe";
    }

    public async Task<FfprobeResult?> ProbeAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return null;
        }

        var exe = ResolveExecutablePath();
        var psi = new ProcessStartInfo
        {
            FileName = exe,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8
        };

        psi.ArgumentList.Add("-v");
        psi.ArgumentList.Add("quiet");
        psi.ArgumentList.Add("-print_format");
        psi.ArgumentList.Add("json");
        psi.ArgumentList.Add("-show_format");
        psi.ArgumentList.Add("-show_streams");
        psi.ArgumentList.Add(filePath);

        try
        {
            using var process = new Process { StartInfo = psi };
            if (!process.Start())
            {
                return null;
            }

            var stdout = await process.StandardOutput.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

            if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(stdout))
            {
                return null;
            }

            return Parse(stdout);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ffprobe failed for {FilePath}", filePath);
            return null;
        }
    }

    private static bool IsOnPath(string executable)
    {
        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(pathEnv)) return false;

        foreach (var dir in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(dir, executable);
                if (File.Exists(candidate)) return true;
                if (OperatingSystem.IsWindows() && File.Exists(candidate + ".exe")) return true;
            }
            catch
            {
                // ignore inaccessible PATH entries
            }
        }
        return false;
    }

    private static FfprobeResult? Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            double? duration = null;
            string? camera = null;
            double? gpsLat = null;
            double? gpsLon = null;

            if (root.TryGetProperty("format", out var format))
            {
                if (format.TryGetProperty("duration", out var durElement) &&
                    durElement.ValueKind == JsonValueKind.String &&
                    double.TryParse(durElement.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
                {
                    duration = d;
                }

                if (format.TryGetProperty("tags", out var fmtTags))
                {
                    camera ??= GetTag(fmtTags, "make", "model", "device_make", "device_model", "encoder");
                    var make = GetTag(fmtTags, "make", "com.apple.quicktime.make");
                    var model = GetTag(fmtTags, "model", "com.apple.quicktime.model");
                    if (!string.IsNullOrWhiteSpace(make) || !string.IsNullOrWhiteSpace(model))
                    {
                        camera = string.Join(' ', new[] { make, model }.Where(s => !string.IsNullOrWhiteSpace(s)));
                    }

                    var location = GetTag(fmtTags, "location", "com.apple.quicktime.location.ISO6709", "location-eng");
                    if (!string.IsNullOrWhiteSpace(location))
                    {
                        if (TryParseIso6709(location, out var lat, out var lon))
                        {
                            gpsLat = lat;
                            gpsLon = lon;
                        }
                    }
                }
            }

            int? width = null, height = null;
            double? frameRate = null;
            string? codec = null;

            if (root.TryGetProperty("streams", out var streams) && streams.ValueKind == JsonValueKind.Array)
            {
                foreach (var stream in streams.EnumerateArray())
                {
                    if (stream.TryGetProperty("codec_type", out var ct) &&
                        ct.ValueKind == JsonValueKind.String &&
                        ct.GetString() == "video")
                    {
                        if (stream.TryGetProperty("width", out var w) && w.TryGetInt32(out var wi)) width = wi;
                        if (stream.TryGetProperty("height", out var h) && h.TryGetInt32(out var hi)) height = hi;
                        if (stream.TryGetProperty("codec_name", out var cn) && cn.ValueKind == JsonValueKind.String)
                            codec = cn.GetString();
                        if (stream.TryGetProperty("avg_frame_rate", out var fr) && fr.ValueKind == JsonValueKind.String)
                            frameRate = ParseRational(fr.GetString());
                        if ((frameRate == null || frameRate == 0) &&
                            stream.TryGetProperty("r_frame_rate", out var rfr) && rfr.ValueKind == JsonValueKind.String)
                            frameRate = ParseRational(rfr.GetString());

                        if (stream.TryGetProperty("tags", out var streamTags))
                        {
                            camera ??= GetTag(streamTags, "encoder", "handler_name");
                        }
                        break;
                    }
                }
            }

            return new FfprobeResult
            {
                DurationSeconds = duration,
                Width = width,
                Height = height,
                FrameRate = frameRate,
                Codec = codec,
                Camera = camera?.Trim(),
                GpsLatitude = gpsLat,
                GpsLongitude = gpsLon
            };
        }
        catch
        {
            return null;
        }
    }

    private static string? GetTag(JsonElement tags, params string[] keys)
    {
        if (tags.ValueKind != JsonValueKind.Object) return null;
        foreach (var key in keys)
        {
            foreach (var prop in tags.EnumerateObject())
            {
                if (string.Equals(prop.Name, key, StringComparison.OrdinalIgnoreCase) &&
                    prop.Value.ValueKind == JsonValueKind.String)
                {
                    var v = prop.Value.GetString();
                    if (!string.IsNullOrWhiteSpace(v)) return v;
                }
            }
        }
        return null;
    }

    private static double? ParseRational(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var parts = text.Split('/');
        if (parts.Length == 2 &&
            double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var num) &&
            double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var den) &&
            den != 0)
        {
            return num / den;
        }
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
        {
            return v;
        }
        return null;
    }

    private static bool TryParseIso6709(string value, out double lat, out double lon)
    {
        lat = 0; lon = 0;
        if (string.IsNullOrWhiteSpace(value)) return false;

        var trimmed = value.TrimEnd('/');
        var match = System.Text.RegularExpressions.Regex.Match(
            trimmed,
            @"^(?<lat>[+-]\d+(?:\.\d+)?)(?<lon>[+-]\d+(?:\.\d+)?)(?<alt>[+-]\d+(?:\.\d+)?)?$");
        if (!match.Success) return false;

        return double.TryParse(match.Groups["lat"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out lat) &&
               double.TryParse(match.Groups["lon"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out lon);
    }
}
