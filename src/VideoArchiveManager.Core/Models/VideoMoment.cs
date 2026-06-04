using System.ComponentModel.DataAnnotations;

namespace VideoArchiveManager.Core.Models;

// A timestamped sub-clip ("moment" / "the shot") inside a VideoItem. The whole
// point of the app's name: the natural unit of search is a moment in a clip,
// not just the whole file. A moment carries its own in/out range, label,
// rating, notes, tags, and a thumbnail captured at its in-point — so search can
// return the actual shot with a "jump to" action rather than the parent file.
//
// Source video files are never touched; like everything else here a moment is
// pure catalog metadata. Its thumbnail JPG lives in the app's thumbnail cache,
// not next to the video.
public class VideoMoment
{
    public int Id { get; set; }

    public int VideoItemId { get; set; }

    public VideoItem VideoItem { get; set; } = null!;

    // In-point, in seconds from the start of the clip. Always set.
    public double StartSeconds { get; set; }

    // Out-point, in seconds. Null means a single-point marker (an instant the
    // reviewer flagged with just an in-point); the UI treats it as a zero-length
    // moment that still has a thumbnail and metadata.
    public double? EndSeconds { get; set; }

    // Short human label for the shot, e.g. "hero drone reveal" or "gull dives".
    public string? Label { get; set; }

    public string? Notes { get; set; }

    public int Rating { get; set; }

    // Full path to the cached JPG captured at StartSeconds. Generated on demand
    // (mirrors VideoItem.ThumbnailPath); null until the frame has been grabbed.
    public string? ThumbnailPath { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<MomentTag> MomentTags { get; set; } = new List<MomentTag>();
}
