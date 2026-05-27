using System.ComponentModel.DataAnnotations;

namespace VideoArchiveManager.Core.Models;

public class RootFolder
{
    public int Id { get; set; }

    [Required]
    public string Path { get; set; } = string.Empty;

    public string? Name { get; set; }

    public DateTime? LastScannedAt { get; set; }
}
