namespace Trackarr.Core.SystemInfo;

public sealed class AppInfo
{
    public string Version { get; init; } = string.Empty;
    public DateTime BuildTime { get; init; }
    public DateTime StartTime { get; init; }

    public string OsName { get; init; } = string.Empty;
    public string OsVersion { get; init; } = string.Empty;
    public string OsArchitecture { get; init; } = string.Empty;
    public bool IsDocker { get; init; }

    public string RuntimeName { get; init; } = string.Empty;
    public string RuntimeVersion { get; init; } = string.Empty;

    public string DatabaseType { get; init; } = string.Empty;
    public string DatabaseVersion { get; init; } = string.Empty;
    public int MigrationCount { get; init; }

    public string DataDirectory { get; init; } = string.Empty;
    public string StartupDirectory { get; init; } = string.Empty;

    public TimeSpan Uptime => DateTime.UtcNow - StartTime;
}
