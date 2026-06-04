namespace VideoArchiveManager.Core.Services.Ai;

public enum AiModelStatus
{
    // The feature is switched off in Settings.
    Disabled,
    // Enabled, but no model files are present yet (offer to download).
    NotInstalled,
    // A download / extraction is in progress.
    Downloading,
    // Model files are present and loadable.
    Ready,
    // Something went wrong loading or downloading.
    Error
}

// Resolves where the CLIP model bundle lives, reports whether it's installed,
// downloads it on demand, and hands out a loaded (cached) IClipModel. Honours a
// user-supplied drop-in folder first, then a managed app-data location.
public interface IAiModelProvider
{
    // Directory the model bundle is expected in (drop-in if configured and
    // present, otherwise the managed app-data location).
    string ModelDirectory { get; }

    AiModelStatus GetStatus();

    bool IsModelInstalled();

    // Downloads + extracts the model bundle into the managed directory. No-op
    // (returns true) if already installed. Reports 0..1 progress.
    Task<bool> EnsureDownloadedAsync(IProgress<double>? progress = null, CancellationToken cancellationToken = default);

    // Returns a loaded, cached model. Throws if not installed / load fails.
    IClipModel GetModel();

    // Releases the loaded model + its native sessions (e.g. on disable).
    void Unload();
}
