namespace Trackarr.Core.Health;

public enum HealthEventType
{
    FirstSeen,
    QueueSeen,
    QueueMissing,
    ProgressIncreased,
    SizeLeftDecreased,
    ErrorCleared,
    RecoveryStarted,
    RecoveryCreditApplied,
    SustainedProgress,
    NoProgress,
    WarningReported,
    ErrorReported,
    ErrorRepeated,
    Stalled,
    StalledNoConnections,
    StatusWorsened,
    RemovedByTrackarr,
    ImportedSuccessfully,
    FailedExternally,
    FetchingMetadata,
    ManualOverride
}
