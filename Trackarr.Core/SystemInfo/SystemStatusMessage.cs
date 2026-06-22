namespace Trackarr.Core.SystemInfo;

public enum StatusLevel
{
    Notice,
    Warning,
    Error
}

public sealed class SystemStatusMessage
{
    public required StatusLevel Level { get; init; }
    public required string Source { get; init; }
    public required string Message { get; init; }
}
