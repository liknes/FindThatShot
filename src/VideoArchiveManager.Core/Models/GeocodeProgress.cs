namespace VideoArchiveManager.Core.Models;

public class GeocodeProgress
{
    public int TotalCandidates { get; init; }
    public int Processed { get; init; }
    public int Filled { get; init; }
    public int Skipped { get; init; }
    public int Failed { get; init; }
    public string? CurrentFile { get; init; }
    public string? Message { get; init; }
    public bool IsComplete { get; init; }
}
