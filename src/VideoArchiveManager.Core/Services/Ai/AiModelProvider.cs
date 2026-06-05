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
using System.IO.Compression;
using System.Net.Http;
using Microsoft.Extensions.Logging;
using VideoArchiveManager.Core.Configuration;

namespace VideoArchiveManager.Core.Services.Ai;

public class AiModelProvider : IAiModelProvider
{
    private readonly ISettingsStore _settings;
    private readonly ILogger<AiModelProvider> _logger;
    private readonly Func<HttpClient>? _httpClientFactory;
    private readonly object _loadLock = new();

    private IClipModel? _model;
    private volatile bool _downloading;

    public AiModelProvider(
        ISettingsStore settings,
        ILogger<AiModelProvider> logger,
        Func<HttpClient>? httpClientFactory = null)
    {
        _settings = settings;
        _logger = logger;
        _httpClientFactory = httpClientFactory;
    }

    // Managed default lives alongside the catalog/thumbnails in app-data. This is
    // also the download target for EnsureDownloadedAsync.
    private static string ManagedDirectory =>
        Path.Combine(AppSettings.DefaultBaseDirectory, "Models", "clip-vit-b32");

    // Bundled location shipped next to the executable, mirroring how ffmpeg/libmpv
    // are distributed (tools\ffmpeg, tools\mpv). The publish/build copy step and the
    // scripts/export-clip-onnx.py prep script both target this folder, so a model
    // dropped here works with zero configuration.
    private static string BundledDirectory =>
        Path.Combine(AppContext.BaseDirectory, "tools", "models", "clip-vit-b32");

    public string ModelDirectory
    {
        get
        {
            // Resolution order: an explicitly configured directory wins, then a
            // bundled folder next to the exe, then the managed app-data folder
            // (which doubles as the download target).
            var configured = _settings.Current.AiModelDirectory;
            if (!string.IsNullOrWhiteSpace(configured) && IsInstalledAt(configured))
                return configured;
            if (IsInstalledAt(BundledDirectory))
                return BundledDirectory;
            return ManagedDirectory;
        }
    }

    public AiModelStatus GetStatus()
    {
        if (!_settings.Current.EnableAiTagging) return AiModelStatus.Disabled;
        if (_downloading) return AiModelStatus.Downloading;
        return IsModelInstalled() ? AiModelStatus.Ready : AiModelStatus.NotInstalled;
    }

    public bool IsModelInstalled() => IsInstalledAt(ModelDirectory);

    private static bool IsInstalledAt(string? dir)
    {
        if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir)) return false;

        var manifest = ClipModelManifest.LoadOrDefault(Path.Combine(dir, "manifest.json"));
        return File.Exists(Path.Combine(dir, manifest.ImageEncoderFile))
            && File.Exists(Path.Combine(dir, manifest.TextEncoderFile))
            && File.Exists(Path.Combine(dir, manifest.VocabFile));
    }

    public async Task<bool> EnsureDownloadedAsync(IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        if (IsModelInstalled()) return true;

        var url = _settings.Current.AiModelDownloadUrl;
        if (string.IsNullOrWhiteSpace(url))
        {
            _logger.LogWarning("AI model not installed and no download URL configured.");
            return false;
        }

        if (_httpClientFactory is null)
        {
            _logger.LogWarning("No HTTP client available to download the AI model.");
            return false;
        }

        _downloading = true;
        var targetDir = ManagedDirectory;
        var tempZip = Path.Combine(Path.GetTempPath(), $"fts-clip-{Guid.NewGuid():N}.zip");
        try
        {
            Directory.CreateDirectory(targetDir);
            using var http = _httpClientFactory();
            using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var total = response.Content.Headers.ContentLength ?? -1L;
            await using (var src = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
            await using (var dst = File.Create(tempZip))
            {
                var buffer = new byte[81920];
                long readTotal = 0;
                int read;
                while ((read = await src.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    await dst.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    readTotal += read;
                    if (total > 0) progress?.Report(Math.Clamp((double)readTotal / total, 0, 1));
                }
            }

            ZipFile.ExtractToDirectory(tempZip, targetDir, overwriteFiles: true);
            FlattenSingleSubdirectory(targetDir);

            var ok = IsModelInstalled();
            if (!ok) _logger.LogWarning("AI model archive extracted but expected files are missing in {Dir}.", targetDir);
            return ok;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to download / extract the AI model bundle.");
            return false;
        }
        finally
        {
            _downloading = false;
            try { if (File.Exists(tempZip)) File.Delete(tempZip); } catch { /* best effort */ }
        }
    }

    public IClipModel GetModel()
    {
        if (_model != null) return _model;
        lock (_loadLock)
        {
            if (_model != null) return _model;
            if (!IsModelInstalled())
                throw new InvalidOperationException("The CLIP model is not installed.");
            _model = new ClipOnnxModel(ModelDirectory);
            _logger.LogInformation("Loaded CLIP model '{ModelId}' from {Dir}.", _model.ModelId, ModelDirectory);
            return _model;
        }
    }

    public void Unload()
    {
        lock (_loadLock)
        {
            _model?.Dispose();
            _model = null;
        }
    }

    // If the archive extracted into a single nested folder (common when zipping
    // a directory), lift its contents up so the model files sit directly in the
    // managed directory where IsModelInstalled() looks for them.
    private static void FlattenSingleSubdirectory(string dir)
    {
        var files = Directory.GetFiles(dir);
        var subDirs = Directory.GetDirectories(dir);
        if (files.Length != 0 || subDirs.Length != 1) return;

        var inner = subDirs[0];
        foreach (var f in Directory.GetFiles(inner))
            File.Move(f, Path.Combine(dir, Path.GetFileName(f)), overwrite: true);
        foreach (var d in Directory.GetDirectories(inner))
            Directory.Move(d, Path.Combine(dir, Path.GetFileName(d)));
        try { Directory.Delete(inner, recursive: true); } catch { /* best effort */ }
    }
}
