namespace Trackarr.Core.Health;

public enum HealthState
{
    New,
    Monitoring,
    Healthy,
    Stable,
    Watching,
    Degrading,
    Critical,
    Remove,
    Replaced,
    Resolved,
    Failed,
    Unknown
}