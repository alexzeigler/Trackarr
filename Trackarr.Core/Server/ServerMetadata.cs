namespace Trackarr.Core.Server;

public class ServerMetadata
{
    public int ServerId { get; set; }
    public string AppName { get; init; } = string.Empty;
    public string InstanceName { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;
    public DateTime BuildTime { get; init; }
    public bool IsDebug { get; init; }
    public bool IsProduction { get; init; }
    public bool IsAdmin { get; init; }
    public bool IsUserInteractive { get; init; }
    public string StartupPath { get; init; } = string.Empty;
    public string AppData { get; init; } = string.Empty;
    public string OsName { get; init; } = string.Empty;
    public string OsVersion { get; init; } = string.Empty;
    public bool IsNetCore { get; init; }
    public bool IsLinux { get; init; }
    public bool IsOsx { get; init; }
    public bool IsWindows { get; init; }
    public bool IsDocker { get; init; }
    public string Mode { get; init; } = string.Empty;
    public string Branch { get; init; } = string.Empty;
    public string DatabaseType { get; init; } = string.Empty;
    public string DatabaseVersion { get; init; } = string.Empty;
    public string Authentication { get; init; } = string.Empty;
    public int MigrationVersion { get; init; }
    public string UrlBase { get; init; } = string.Empty;
    public string RuntimeVersion { get; init; } = string.Empty;
    public string RuntimeName { get; init; } = string.Empty;
    public DateTime StartTime { get; init; }
    public string PackageVersion { get; init; } = string.Empty;
    public string PackageAuthor { get; init; } = string.Empty;
    public string PackageUpdateMechanism { get; init; } = string.Empty;
    public string PackageUpdateMechanismMessage { get; init; } = string.Empty;
}
