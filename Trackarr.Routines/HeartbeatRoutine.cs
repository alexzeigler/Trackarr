using Microsoft.Extensions.Logging;
using Trackarr.Core.Routine;
using Trackarr.Core.Server;
using Trackarr.Services.Media;
using Trackarr.Services.Servers;

namespace Trackarr.Routines;

public sealed class HeartbeatRoutine(
    ServerRepository serverRepository,
    ServerConnectionService connection,
    MediaServerService mediaServerService,
    ILogger<HeartbeatRoutine> logger) : Routine
{
    /// <summary>
    ///     Failed checks in a row before a server is disabled (AUDIT B13). One failure is usually a DNS blip or
    ///     Radarr/Sonarr restarting, and disabling hides the server's queue and forces a full re-sync afterwards.
    ///     With the default 5-minute interval, this is about 15 minutes of downtime.
    /// </summary>
    public const int FailuresBeforeDisable = 3;

    public override string Key { get; set; } = "heartbeat";
    public override string Name { get; set; } = "Server Heartbeat";
    public override string Description { get; set; } = "Checks server status.";
    public override bool Enabled { get; set; } = true;
    public override int IntervalSeconds { get; set; } = 300;
    public override DateTimeOffset NextRun { get; set; } = DateTimeOffset.UtcNow;

    public override async Task<RoutineResult> ExecuteAsync(CancellationToken cancellationToken)
    {
        // Keep probing servers we disabled ourselves so we can detect recovery and re-enable them.
        // A server the user disabled manually (IsEnabled false, DisabledByHeartbeat false) is skipped.
        var servers = (await serverRepository.GetAllAsync())
            .Where(s => s.IsEnabled || s.DisabledByHeartbeat).ToList();

        var disabled = 0;
        var reenabled = 0;
        var failing = 0;

        foreach (var server in servers)
        {
            long? pingMs = null;
            var success = false;

            try
            {
                var result = await connection.TestConnectionAsync(server);
                success = true;
                pingMs = result.ElapsedMilliseconds;

                logger.LogInformation("Heartbeat [{name}]: {message} ({ping}ms)", server.Name, result.ReasonPhrase,
                    result.ElapsedMilliseconds);

                var wasDisabledByHeartbeat = server.DisabledByHeartbeat;

                if (wasDisabledByHeartbeat)
                {
                    // Keep the in-memory server in sync before SyncServerAsync uses it below — it reads
                    // IsEnabled to decide whether to hit the live APIs instead of returning cached data.
                    server.IsEnabled = true;
                    server.DisabledByHeartbeat = false;

                    await serverRepository.LogHealthEventAsync(server.Id, ServerHealthEventType.Resolved,
                        $"{server.Name} responded again after being automatically disabled; re-enabled.",
                        "Heartbeat");
                    reenabled++;
                }

                await serverRepository.UpdateStatusAsync(server.Id, s =>
                {
                    s.IsEnabled = true;
                    s.DisabledByHeartbeat = false;
                });

                if (wasDisabledByHeartbeat)
                {
                    // Recovering from a disable should pull a full sync (metadata, queue, health, missing) —
                    // not just resume the lightweight ping that runs on every other tick.
                    try
                    {
                        await mediaServerService.SyncServerAsync(server);
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning("Heartbeat [{name}]: recovery sync failed — {message}", server.Name,
                            ex.Message);
                    }
                }
            }
            catch (Exception ex)
            {
                // This failure plus the ones right before it (this run's sample is recorded below).
                var failures = server.IsEnabled
                    ? await serverRepository.CountRecentFailuresAsync(server.Id, FailuresBeforeDisable - 1) + 1
                    : 0;

                if (server.IsEnabled && failures < FailuresBeforeDisable)
                {
                    logger.LogWarning(
                        "Heartbeat [{name}]: failed ({count} of {limit} in a row before it's disabled) — {message}",
                        server.Name, failures, FailuresBeforeDisable, ex.Message);
                    failing++;
                }
                else if (server.IsEnabled)
                {
                    logger.LogWarning("Heartbeat [{name}]: failed {count} times in a row — {message}. Disabling server.",
                        server.Name, failures, ex.Message);

                    await serverRepository.LogHealthEventAsync(server.Id, ServerHealthEventType.Down, ex.Message,
                        "Heartbeat");

                    await serverRepository.UpdateStatusAsync(server.Id, s =>
                    {
                        s.IsEnabled = false;
                        s.DisabledByHeartbeat = true;
                    });

                    disabled++;
                }
                else
                {
                    logger.LogDebug("Heartbeat [{name}]: still unreachable — {message}", server.Name, ex.Message);
                }
            }

            await serverRepository.RecordStatusSampleAsync(server.Id, pingMs, success);
        }

        var parts = new List<string> { $"{servers.Count} servers checked" };
        if (failing > 0)
            parts.Add($"{failing} not responding (disabled after {FailuresBeforeDisable} failures in a row)");
        if (disabled > 0) parts.Add($"{disabled} disabled");
        if (reenabled > 0) parts.Add($"{reenabled} re-enabled");

        return string.Join(", ", parts);
    }
}
