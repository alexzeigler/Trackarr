using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Trackarr.Core.Health;
using Trackarr.Core.Routine;
using Trackarr.Core.SystemInfo;
using Trackarr.Data;
using Trackarr.Services.SystemInfo;

namespace Trackarr.Routines;

public sealed class HealthCheckRoutine(
    IDbContextFactory<AppDbContext> dbFactory,
    SystemStatusService statusService,
    IHttpClientFactory httpClientFactory,
    ILogger<HealthCheckRoutine> logger) : Routine
{
    public override string Key { get; set; } = "health_check";
    public override string Name { get; set; } = "Health Check";

    public override string Description { get; set; } =
        "Verifies system health: connectivity, database, servers, and downloads.";

    public override bool Enabled { get; set; } = true;
    public override int IntervalSeconds { get; set; } = 600;
    public override DateTimeOffset NextRun { get; set; } = DateTimeOffset.UtcNow.AddSeconds(30);

    public override async Task<string> ExecuteAsync(CancellationToken cancellationToken)
    {
        var messages = new List<SystemStatusMessage>();

        await CheckInternetAsync(messages);
        await CheckDatabaseAsync(messages);
        await CheckDiskSpaceAsync(messages);
        await CheckServersAsync(messages);
        await CheckDownloadHealthAsync(messages);
        await CheckRoutinesAsync(messages);

        statusService.SetHealthCheckMessages(messages);

        var issues = messages.Count(m => m.Level >= StatusLevel.Warning);
        logger.LogInformation("Health check complete: {issues} issue(s)", issues);
        return issues > 0 ? $"{issues} issue(s) found" : "All systems healthy";
    }

    private async Task CheckInternetAsync(List<SystemStatusMessage> messages)
    {
        try
        {
            var client = httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(5);
            using var response = await client.GetAsync("http://clients3.google.com/generate_204",
                HttpCompletionOption.ResponseHeadersRead);
        }
        catch
        {
            messages.Add(new SystemStatusMessage
            {
                Level = StatusLevel.Warning,
                Source = "Network",
                Message = "Unable to reach the internet. Update checks and remote features may not work."
            });
        }
    }

    private async Task CheckDatabaseAsync(List<SystemStatusMessage> messages)
    {
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync();
            var conn = db.Database.GetDbConnection();
            var opened = false;
            if (conn.State != ConnectionState.Open)
            {
                await conn.OpenAsync();
                opened = true;
            }

            try
            {
                using var quickCheck = conn.CreateCommand();
                quickCheck.CommandText = "PRAGMA quick_check;";
                var result = (await quickCheck.ExecuteScalarAsync())?.ToString();
                if (result is not null && !result.Equals("ok", StringComparison.OrdinalIgnoreCase))
                {
                    messages.Add(new SystemStatusMessage
                    {
                        Level = StatusLevel.Error,
                        Source = "Database",
                        Message = $"Database integrity check failed: {result}"
                    });
                }

                using var fkCheck = conn.CreateCommand();
                fkCheck.CommandText = "PRAGMA foreign_key_check;";
                using var reader = await fkCheck.ExecuteReaderAsync();
                if (reader.HasRows)
                {
                    var violations = 0;
                    while (await reader.ReadAsync()) violations++;
                    messages.Add(new SystemStatusMessage
                    {
                        Level = StatusLevel.Warning,
                        Source = "Database",
                        Message =
                            $"Database has {violations} foreign key violation(s). This may indicate orphaned records."
                    });
                }
            }
            finally
            {
                if (opened) await conn.CloseAsync();
            }
        }
        catch (Exception ex)
        {
            messages.Add(new SystemStatusMessage
            {
                Level = StatusLevel.Error, Source = "Database", Message = $"Database check failed: {ex.Message}"
            });
        }
    }

    private async Task CheckDiskSpaceAsync(List<SystemStatusMessage> messages)
    {
        try
        {
            var appInfo = await statusService.GetAppInfoAsync();
            var root = Path.GetPathRoot(appInfo.DataDirectory);
            if (root is null) return;

            var drive = new DriveInfo(root);
            if (!drive.IsReady) return;

            var freeMb = drive.AvailableFreeSpace / (1024 * 1024);

            if (freeMb < 100)
            {
                messages.Add(new SystemStatusMessage
                {
                    Level = StatusLevel.Error,
                    Source = "Disk",
                    Message = $"Data directory has only {freeMb} MB free. Trackarr may fail to write data."
                });
            }
            else if (freeMb < 500)
            {
                messages.Add(new SystemStatusMessage
                {
                    Level = StatusLevel.Warning,
                    Source = "Disk",
                    Message = $"Data directory is running low on space ({freeMb} MB free)."
                });
            }
        }
        catch
        {
            // Non-critical — skip if we can't check
        }
    }

    private async Task CheckServersAsync(List<SystemStatusMessage> messages)
    {
        try
        {
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
                    continue;
                }

                if (!server.IsAuthenticated)
                {
                    messages.Add(new SystemStatusMessage
                    {
                        Level = StatusLevel.Error,
                        Source = server.Name,
                        Message =
                            $"Server \"{server.Name}\" ({server.ServiceType}) failed authentication. Check the API key."
                    });
                    continue;
                }

                if (server.LastPingedAt is { } pinged && DateTime.UtcNow - pinged > TimeSpan.FromMinutes(15))
                {
                    messages.Add(new SystemStatusMessage
                    {
                        Level = StatusLevel.Warning,
                        Source = server.Name,
                        Message =
                            $"Server \"{server.Name}\" ({server.ServiceType}) has not been reached in over 15 minutes."
                    });
                }

                if (server.LastQueueFetchAt != default &&
                    DateTime.UtcNow - server.LastQueueFetchAt > TimeSpan.FromMinutes(5))
                {
                    messages.Add(new SystemStatusMessage
                    {
                        Level = StatusLevel.Warning,
                        Source = server.Name,
                        Message =
                            $"Queue data for \"{server.Name}\" is stale (last fetched {FormatTimeAgo(server.LastQueueFetchAt)})."
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
        }
        catch (Exception ex)
        {
            messages.Add(new SystemStatusMessage
            {
                Level = StatusLevel.Error,
                Source = "Servers",
                Message = $"Failed to check server health: {ex.Message}"
            });
        }
    }

    private async Task CheckDownloadHealthAsync(List<SystemStatusMessage> messages)
    {
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync();

            var criticalCount = await db.HealthSnapshots.AsNoTracking()
                .Where(h => h.DownloadAttempt != null && h.DownloadAttempt.Status == AttemptStatus.Active)
                .CountAsync(h => h.State == HealthState.Critical || h.State == HealthState.Remove);

            if (criticalCount > 0)
            {
                messages.Add(new SystemStatusMessage
                {
                    Level = StatusLevel.Warning,
                    Source = "Downloads",
                    Message = $"{criticalCount} download(s) in critical or removal-eligible state."
                });
            }

            var orphanedAttempts = await db.DownloadAttempts.AsNoTracking().Where(a => a.Status == AttemptStatus.Active)
                .Where(a => !db.DownloadQueue.Any(q => q.DownloadId == a.DownloadId && q.ServerId == a.ServerId))
                .CountAsync();

            if (orphanedAttempts > 0)
            {
                messages.Add(new SystemStatusMessage
                {
                    Level = StatusLevel.Notice,
                    Source = "Downloads",
                    Message = $"{orphanedAttempts} active download attempt(s) no longer appear in any server queue."
                });
            }
        }
        catch (Exception ex)
        {
            messages.Add(new SystemStatusMessage
            {
                Level = StatusLevel.Error,
                Source = "Downloads",
                Message = $"Failed to check download health: {ex.Message}"
            });
        }
    }

    private async Task CheckRoutinesAsync(List<SystemStatusMessage> messages)
    {
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync();
            var routines = await db.Routines.AsNoTracking().Where(r => r.Enabled).ToListAsync();

            foreach (var routine in routines)
            {
                if (routine.Key == Key) continue;

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
        }
        catch (Exception ex)
        {
            messages.Add(new SystemStatusMessage
            {
                Level = StatusLevel.Error,
                Source = "Routines",
                Message = $"Failed to check routine health: {ex.Message}"
            });
        }
    }

    private static string FormatTimeAgo(DateTime utc)
    {
        var ago = DateTime.UtcNow - utc;
        if (ago.TotalHours >= 1) return $"{(int)ago.TotalHours}h {ago.Minutes}m ago";
        return $"{(int)ago.TotalMinutes}m ago";
    }
}
