namespace Trackarr.Core.Health;

public enum HealthState
{
    New,
    Healthy,
    Stable,
    Watching,
    Degrading,
    Critical,
    Remove,
    Resolved,
    Failed,
    Unknown
}
