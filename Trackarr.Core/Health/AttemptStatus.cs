namespace Trackarr.Core.Health;

public enum AttemptStatus
{
    Active,
    Imported,
    RemovedByTrackarr,
    RemovedExternally,
    Replaced,
    Failed,
    Disappeared,
    Unknown
}
