using System.ComponentModel.DataAnnotations;
using VideoArchiveManager.Core.Models.Enums;

namespace VideoArchiveManager.Core.Models;

public class Tag
{
    public int Id { get; set; }

    [Required]
    public string Name { get; set; } = string.Empty;

    public TagType Type { get; set; } = TagType.Subject;

    public ICollection<VideoTag> VideoTags { get; set; } = new List<VideoTag>();
}
