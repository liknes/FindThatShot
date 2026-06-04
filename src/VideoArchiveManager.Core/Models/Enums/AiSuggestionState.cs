namespace VideoArchiveManager.Core.Models.Enums;

// Lifecycle of an AI-generated tag suggestion in the review queue. Rejections
// are remembered (not deleted) so a later re-run of the tagging pass doesn't
// keep re-proposing a label the user has already dismissed for that clip.
public enum AiSuggestionState
{
    Pending = 0,
    Accepted = 1,
    Rejected = 2
}
