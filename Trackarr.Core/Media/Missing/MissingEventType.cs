namespace Trackarr.Core.Media.Missing;

public enum MissingEventType
{
    SearchPushed,
    SearchFailed,
    DowngradedToFallback,
    Resolved,
    CascadedToEpisode,
    Abandoned,
    Retried,
    ProfileRestored
}
