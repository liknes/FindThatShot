using System.ComponentModel.DataAnnotations;
using VideoArchiveManager.Core.Models.Enums;

namespace VideoArchiveManager.Core.Models;

public class VideoItem
{
    public int Id { get; set; }

    [Required]
    public string FilePath { get; set; } = string.Empty;

    [Required]
    public string FileName { get; set; } = string.Empty;

    public string Extension { get; set; } = string.Empty;

    public string FolderPath { get; set; } = string.Empty;

    public long FileSize { get; set; }

    public DateTime CreatedAtFile { get; set; }

    public DateTime ModifiedAtFile { get; set; }

    public double? DurationSeconds { get; set; }

    public int? Width { get; set; }

    public int? Height { get; set; }

    public double? FrameRate { get; set; }

    public string? Codec { get; set; }

    public string? Camera { get; set; }

    public double? GpsLatitude { get; set; }

    public double? GpsLongitude { get; set; }

    public DateTime? FolderDate { get; set; }

    public string? LocationText { get; set; }

    public string? ContextText { get; set; }

    public string? Notes { get; set; }

    public int Rating { get; set; }

    public VideoStatus Status { get; set; } = VideoStatus.Unreviewed;

    public string? ThumbnailPath { get; set; }

    public bool FileExists { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<VideoTag> VideoTags { get; set; } = new List<VideoTag>();
}
