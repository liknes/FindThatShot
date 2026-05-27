namespace VideoArchiveManager.Core.Models;

public class VideoTag
{
    public int VideoItemId { get; set; }
    public VideoItem VideoItem { get; set; } = null!;

    public int TagId { get; set; }
    public Tag Tag { get; set; } = null!;
}
