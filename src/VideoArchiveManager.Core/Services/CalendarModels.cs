namespace VideoArchiveManager.Core.Services;

/// <summary>
/// Aggregate count of clips whose effective shoot date
/// (<c>FolderDate ?? ModifiedAtFile</c>) falls in a given calendar month.
/// Drives the year-month heatmap of the calendar browse view. Read-only over
/// the catalog — no source file is touched to build it.
/// </summary>
public sealed record MonthShootCount(int Year, int Month, int Count);

/// <summary>
/// Lightweight projection of a clip for the calendar browse view's month clip
/// list + side preview panel. Carries only what the list and preview need
/// (identity, thumbnail, online state, rating, effective date) so listing a
/// busy month stays cheap — mirrors <see cref="MapClipPoint"/> for the map
/// browse view. The catalog is read only.
/// </summary>
public sealed class CalendarClip
{
    public int Id { get; init; }

    public string FileName { get; init; } = string.Empty;

    public string FolderPath { get; init; } = string.Empty;

    public string? ThumbnailPath { get; init; }

    public bool FileExists { get; init; }

    public int Rating { get; init; }

    /// <summary>The date the clip was bucketed under: FolderDate ?? ModifiedAtFile.</summary>
    public DateTime EffectiveDate { get; init; }
}
