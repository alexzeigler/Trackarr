namespace Trackarr.Core.SystemInfo;

public sealed class AppInfo
{
    public string Version { get; init; } = "";
    public DateTime BuildTime { get; init; }
    public DateTime StartTime { get; init; }

    public string OsName { get; init; } = "";
    public string OsVersion { get; init; } = "";
    public string OsArchitecture { get; init; } = "";
    public bool IsDocker { get; init; }

    public string RuntimeName { get; init; } = "";
    public string RuntimeVersion { get; init; } = "";

    public string DatabaseType { get; init; } = "";
    public string DatabaseVersion { get; init; } = "";
    public int MigrationCount { get; init; }

    public string DataDirectory { get; init; } = "";
    public string StartupDirectory { get; init; } = "";

    public TimeSpan Uptime => DateTime.UtcNow - StartTime;
}
