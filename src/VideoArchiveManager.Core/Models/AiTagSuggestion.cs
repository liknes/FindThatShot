namespace VideoArchiveManager.Core.Models;

public class AiTagSuggestion
{
    public int Id { get; set; }

    public int VideoItemId { get; set; }
    public VideoItem VideoItem { get; set; } = null!;

    public string TagName { get; set; } = string.Empty;

    public double Confidence { get; set; }

    public string? Source { get; set; }

    public bool Approved { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
