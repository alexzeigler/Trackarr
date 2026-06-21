namespace Trackarr.Core.Media.Missing;

public enum MissingSearchState
{
    New,
    InQueue,
    SearchPushed,
    SkippedCooldown,
    SkippedSearching,
    NotAvailable,
    Stuck,
    Abandoned
}