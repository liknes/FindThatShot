namespace VideoArchiveManager.Core.Services.Ai;

public class AiTaggingProgress
{
    public int Total { get; init; }
    public int Processed { get; init; }
    public int Tagged { get; init; }
    public int Failed { get; init; }
    public int Skipped { get; init; }
    public string? CurrentFile { get; init; }
    public string Message { get; init; } = string.Empty;
    public bool IsComplete { get; init; }
}

// Runs the CLIP scoring pass over clips: samples frames, stores embeddings, and
// writes AiTagSuggestion rows. Never reads or modifies source files beyond the
// frame decode, and never auto-applies tags.
public interface IAiTaggingService
{
    // Number of online clips that still need embedding for the current model
    // (or all online clips when reprocessAll is true).
    Task<int> CountPendingAsync(bool reprocessAll, CancellationToken cancellationToken = default);

    Task GenerateAsync(
        bool reprocessAll,
        IProgress<AiTaggingProgress>? progress = null,
        CancellationToken cancellationToken = default);

    // Removes all AI-generated data (embeddings + suggestions) from the catalog.
    // Source files and real tags are untouched.
    Task<int> ClearAllAiDataAsync(CancellationToken cancellationToken = default);
}
