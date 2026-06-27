namespace Trackarr.Core.Queue;

public enum QueueState
{
    Downloading,
    Importing,
    PendingImport,
    PendingFailure,
    Failed,
    Imported,
    ImportBlocked,
    Stalled,
    DownloadingMetadata,
    Warning,
    Unknown
}
