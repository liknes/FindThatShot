namespace VideoArchiveManager.Core.Models;

// One pooled, L2-normalized CLIP embedding per clip (the mean of its sampled
// frame embeddings, re-normalized). Small and cacheable in memory, this is the
// vector natural-language search ranks against. The richer per-frame vectors
// live in AiFrameEmbedding for max-pool tagging and timestamp precision.
public class AiClipEmbedding
{
    public int Id { get; set; }

    public int VideoItemId { get; set; }
    public VideoItem VideoItem { get; set; } = null!;

    // Float32 vector, little-endian, length == Dim. Stored as a BLOB.
    public byte[] Vector { get; set; } = Array.Empty<byte>();

    public int Dim { get; set; }

    // Number of frames that were sampled and pooled into this vector.
    public int FrameCount { get; set; }

    // Identifies the model that produced the vector so a model upgrade can
    // invalidate / re-embed stale rows.
    public string ModelId { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
