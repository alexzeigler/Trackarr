namespace Trackarr.Core.Queue;

public enum DownloadItemTrackingState {
    Active = 0,
    Watching = 1,
    Struck = 2,
    Failed = 3,
    Completed = 4,
    Removed = 5,
    ManualInterventionRequired = 6
}