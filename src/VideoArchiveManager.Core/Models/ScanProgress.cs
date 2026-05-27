namespace VideoArchiveManager.Core.Models;

public class ScanProgress
{
    public int TotalFound { get; init; }
    public int Processed { get; init; }
    public int Added { get; init; }
    public int Updated { get; init; }
    public int Skipped { get; init; }
    public int Failed { get; init; }
    public string? CurrentFile { get; init; }
    public string? Message { get; init; }
    public bool IsComplete { get; init; }
}
