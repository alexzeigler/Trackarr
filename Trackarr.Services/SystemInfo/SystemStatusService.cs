using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.EntityFrameworkCore;
using Trackarr.Core.SystemInfo;
using Trackarr.Data;

namespace Trackarr.Services.SystemInfo;

public sealed class SystemStatusService(IDbContextFactory<AppDbContext> dbFactory)
{
    private AppInfo? _cachedAppInfo;

    #region Helpers

    private async Task<(string Type, string Version, int MigrationCount)> GetDatabaseInfoAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();

        var version = "Unknown";
        try
        {
            var conn = db.Database.GetDbConnection();
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT sqlite_version()";
            var result = await cmd.ExecuteScalarAsync();
            if (result is string v) version = v;
            await conn.CloseAsync();
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

        var plusIndex = version.IndexOf('+');
        if (plusIndex > 0) version = version[..plusIndex];

        var buildTime = File.GetLastWriteTimeUtc(assembly.Location);

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

    public async Task<List<SystemStatusMessage>> GetStatusMessagesAsync()
    {
        var messages = new List<SystemStatusMessage>();

        await using var db = await dbFactory.CreateDbContextAsync();

        var servers = await db.MediaServers.AsNoTracking().ToListAsync();
        foreach (var server in servers)
        {
            if (!server.IsEnabled) continue;

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
            else if (server.LastPingedAt is { } pinged && DateTime.UtcNow - pinged > TimeSpan.FromMinutes(15))
            {
                messages.Add(new SystemStatusMessage
                {
                    Level = StatusLevel.Warning,
                    Source = server.Name,
                    Message =
                        $"Server \"{server.Name}\" ({server.ServiceType}) has not been reached in over 15 minutes."
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

        var routines = await db.Routines.AsNoTracking().ToListAsync();
        foreach (var routine in routines)
        {
            if (!routine.Enabled) continue;

            if (routine.LastRun is not null && !routine.LastRunSuccess)
            {
                messages.Add(new SystemStatusMessage
                {
                    Level = StatusLevel.Warning,
                    Source = routine.Name,
                    Message = $"Routine \"{routine.Name}\" failed on its last run."
                });
            }
        }

        return messages;
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
