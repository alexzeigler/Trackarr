using System.Data;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.EntityFrameworkCore;
using Trackarr.Core.SystemInfo;
using Trackarr.Data;

namespace Trackarr.Services.SystemInfo;

public sealed class SystemStatusService(IDbContextFactory<AppDbContext> dbFactory)
{
    private AppInfo? _cachedAppInfo;
    private List<SystemStatusMessage>? _healthCheckMessages;

    #region Helpers

    private async Task<(string Type, string Version, int MigrationCount)> GetDatabaseInfoAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();

        var version = "Unknown";
        try
        {
            var conn = db.Database.GetDbConnection();
            var opened = false;
            if (conn.State != ConnectionState.Open)
            {
                await conn.OpenAsync();
                opened = true;
            }

            try
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT sqlite_version()";
                var result = await cmd.ExecuteScalarAsync();
                if (result is string v) version = v;
            }
            finally
            {
                if (opened) await conn.CloseAsync();
            }
        }
        catch
        {
            // non-critical
        }

        var migrationCount = (await db.Database.GetAppliedMigrationsAsync()).Count();

        return ("SQLite", version, migrationCount);
    }

    #endregion

    #region Public API

    public async Task<AppInfo> GetAppInfoAsync()
    {
        if (_cachedAppInfo is not null)
            return _cachedAppInfo;

        var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
        var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ??
                      assembly.GetName().Version?.ToString() ?? "0.0.0";

        var buildTime = DateTime.MinValue;
        var plusIndex = version.IndexOf('+');
        if (plusIndex > 0)
        {
            if (DateTime.TryParseExact(version[(plusIndex + 1)..], "yyyyMMddHHmmss", CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed))
                buildTime = parsed;
            version = version[..plusIndex];
        }

        var isDocker = string.Equals(Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER"), "true",
            StringComparison.OrdinalIgnoreCase);

        var (dbType, dbVersion, migrationCount) = await GetDatabaseInfoAsync();

        var dataDir = isDocker ? "/config" : Path.GetDirectoryName(assembly.Location) ?? Environment.CurrentDirectory;

        _cachedAppInfo = new AppInfo
        {
            Version = version,
            BuildTime = buildTime,
            StartTime = Process.GetCurrentProcess().StartTime.ToUniversalTime(),
            OsName = RuntimeInformation.OSDescription,
            OsVersion = Environment.OSVersion.VersionString,
            OsArchitecture = RuntimeInformation.OSArchitecture.ToString(),
            IsDocker = isDocker,
            RuntimeName = RuntimeInformation.FrameworkDescription,
            RuntimeVersion = Environment.Version.ToString(),
            DatabaseType = dbType,
            DatabaseVersion = dbVersion,
            MigrationCount = migrationCount,
            DataDirectory = dataDir,
            StartupDirectory = Environment.CurrentDirectory
        };

        return _cachedAppInfo;
    }

    public void SetHealthCheckMessages(List<SystemStatusMessage> messages)
    {
        _healthCheckMessages = messages;
    }

    public async Task<List<SystemStatusMessage>> GetStatusMessagesAsync()
    {
        if (_healthCheckMessages is not null)
            return _healthCheckMessages;

        // Fallback before health check routine has run for the first time
        var messages = new List<SystemStatusMessage>();

        await using var db = await dbFactory.CreateDbContextAsync();

        var servers = await db.MediaServers.AsNoTracking().Where(s => s.IsEnabled).ToListAsync();
        foreach (var server in servers)
        {
            if (!server.IsOnline)
            {
                messages.Add(new SystemStatusMessage
                {
                    Level = StatusLevel.Error,
                    Source = server.Name,
                    Message = $"Server \"{server.Name}\" ({server.ServiceType}) is offline." +
                              (server.LastError is not null ? $" Last error: {server.LastError}" : "")
                });
            }
            else if (!server.IsAuthenticated)
            {
                messages.Add(new SystemStatusMessage
                {
                    Level = StatusLevel.Error,
                    Source = server.Name,
                    Message =
                        $"Server \"{server.Name}\" ({server.ServiceType}) failed authentication. Check the API key."
                });
            }
        }

        if (servers.Count == 0)
        {
            messages.Add(new SystemStatusMessage
            {
                Level = StatusLevel.Notice,
                Source = "Configuration",
                Message = "No servers configured. Add a Radarr or Sonarr server to get started."
            });
        }

        return messages;
    }

    public async Task<SystemStats> GetSystemStatsAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();

        return new SystemStats
        {
            Servers = await db.MediaServers.CountAsync(),
            Movies = await db.Movies.CountAsync(),
            Series = await db.Series.CountAsync(),
            Episodes = await db.Episodes.CountAsync(),
            Downloads = await db.DownloadAttempts.CountAsync(),
            HealthEvents = await db.HealthEvents.CountAsync()
        };
    }

    public async Task<List<DiskSpaceInfo>> GetDiskSpaceAsync()
    {
        var paths = new List<string>();

        var appInfo = await GetAppInfoAsync();
        paths.Add(appInfo.DataDirectory);
        paths.Add(appInfo.StartupDirectory);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<DiskSpaceInfo>();

        foreach (var path in paths)
        {
            if (string.IsNullOrWhiteSpace(path)) continue;

            try
            {
                var root = Path.GetPathRoot(path);
                if (root is null || !seen.Add(root)) continue;

                var drive = new DriveInfo(root);
                if (!drive.IsReady) continue;

                result.Add(new DiskSpaceInfo
                {
                    Location = path, FreeBytes = drive.AvailableFreeSpace, TotalBytes = drive.TotalSize
                });
            }
            catch
            {
                // skip inaccessible paths
            }
        }

        return result;
    }

    #endregion
}
