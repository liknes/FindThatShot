namespace VideoArchiveManager.Core.Models;

public class VideoTag
{
    public int VideoItemId { get; set; }
    public VideoItem VideoItem { get; set; } = null!;

    public int TagId { get; set; }
    public Tag Tag { get; set; } = null!;

    // Prominence of this tag on this specific clip. false = primary (the
    // default — the tag describes a main subject of the shot); true = the
    // subject is only incidental/background (e.g. distant islands behind a
    // beach). Search can filter to / rank primary tags first, but the tag is
    // still attached so the clip stays findable.
    public bool IsBackground { get; set; }
}
