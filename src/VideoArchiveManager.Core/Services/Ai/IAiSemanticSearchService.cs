namespace VideoArchiveManager.Core.Services.Ai;

public class SemanticSearchHit
{
    public int VideoItemId { get; init; }
    public double Score { get; init; }
    // Best-matching timestamp (seconds) within the clip, when frame-level
    // resolution is available — lets the UI jump to where the subject appears.
    public double? BestFrameSeconds { get; init; }
}

// Natural-language ("drone shot over snowy mountains at sunset") search over the
// stored CLIP clip embeddings. Returns clip ids ranked by similarity; the
// caller hydrates them into VideoItems.
public interface IAiSemanticSearchService
{
    bool IsAvailable { get; }

    Task<IReadOnlyList<SemanticSearchHit>> SearchAsync(
        string query,
        int maxResults,
        double minScore,
        CancellationToken cancellationToken = default);

    // Drops the in-memory embedding cache (e.g. after a tagging pass adds rows).
    void InvalidateCache();
}
