using VideoArchiveManager.Core.Models.Enums;

namespace VideoArchiveManager.Core.Configuration;

// A single tag bound to a review-mode number-key hotkey. Persisted in
// settings.json rather than the catalog DB, so the binding survives a
// catalog restore / rebuild — it's identified by Name + Type (matching the
// catalog's (Name, Type) uniqueness contract) and resolved against the live
// tag catalog at runtime instead of by a (potentially stale) DB id.
public class PinnedTag
{
    // Number-key slot this tag is bound to. Slot 0 is the "1" key, slot 1
    // the "2" key … slot 8 the "9" key, and slot 9 the "0" key (so the
    // tenth pin sits on the 0 key at the end of the number row). Range 0-9.
    public int Slot { get; set; }

    public string Name { get; set; } = string.Empty;

    public TagType Type { get; set; } = TagType.Subject;
}
