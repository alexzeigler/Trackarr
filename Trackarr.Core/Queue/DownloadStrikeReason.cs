namespace Trackarr.Core.Queue;

public enum DownloadStrikeReason {
    Unknown = 0,
    NoProgress = 1,
    Stalled = 2,
    DownloadClientWarning = 3,
    DownloadClientError = 4,
    ImportBlocked = 5,
    ImportFailed = 6,
    MissingAfterGrab = 7,
    RemovedFromClient = 8,
    MaxRetriesReached = 9
}