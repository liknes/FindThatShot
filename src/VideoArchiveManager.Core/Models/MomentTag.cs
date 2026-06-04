namespace VideoArchiveManager.Core.Models;

// Join row linking a VideoMoment to a global Tag. Mirrors VideoTag so moments
// share the same tag vocabulary as whole clips — a "birds" tag means the same
// thing whether it's on a file or on a single shot inside it.
public class MomentTag
{
    public int VideoMomentId { get; set; }
    public VideoMoment VideoMoment { get; set; } = null!;

    public int TagId { get; set; }
    public Tag Tag { get; set; } = null!;
}
