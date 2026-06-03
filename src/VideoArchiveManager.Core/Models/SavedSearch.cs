namespace VideoArchiveManager.Core.Models;

// A named, reusable filter — the app's equivalent of Lightroom's Smart
// Collections. The user's current sidebar filter state (text, status,
// rating, camera, tags, dates, folder scope, availability, unreviewed)
// is captured into SavedSearchCriteria, serialised to CriteriaJson, and
// re-applied on demand. Membership is *dynamic*: clicking a saved search
// re-runs the query against the live catalog, so newly-scanned clips that
// match show up automatically without any manual bucketing.
public class SavedSearch
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    // System.Text.Json serialisation of SavedSearchCriteria. Stored as a
    // blob rather than a wide column set so the criteria shape can evolve
    // without a schema migration each time SearchQuery grows a field.
    public string CriteriaJson { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    // Ascending display order in the sidebar. Defaults to creation order;
    // reserved for future drag-to-reorder without another migration.
    public int SortOrder { get; set; }
}
