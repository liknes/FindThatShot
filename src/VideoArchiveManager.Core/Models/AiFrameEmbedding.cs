namespace VideoArchiveManager.Core.Models;

// One L2-normalized CLIP image embedding for a single frame sampled from a
// clip at TimeSeconds. Multiple rows per clip. Tag scoring max-pools across
// these (so "does this clip ever show snow?" is answered by the best frame,
// not the average), and they let search resolve the exact second a subject
// appears for jump-to / future Moment creation.
public class AiFrameEmbedding
{
    public int Id { get; set; }

    public int VideoItemId { get; set; }
    public VideoItem VideoItem { get; set; } = null!;

    // Position (seconds) in the clip the frame was decoded from.
    public double TimeSeconds { get; set; }

    // Float32 vector, little-endian, length == Dim. Stored as a BLOB.
    public byte[] Vector { get; set; } = Array.Empty<byte>();

    public int Dim { get; set; }

    public string ModelId { get; set; } = string.Empty;
}
