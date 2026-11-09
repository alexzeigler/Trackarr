using Microsoft.Extensions.Logging;
using Trackarr.Core.Routine;
using Trackarr.Core.Server;
using Trackarr.Services.Media;
using Trackarr.Services.Servers;
using Trackarr.Services.Settings;

namespace Trackarr.Routines;

public sealed class QueueRefreshRoutine(
    MediaServerService mediaServerService,
    ServerRepository serverRepository,
    HealthService healthService,
    SettingsService settings,
    ILogger<QueueRefreshRoutine> logger) : Routine
{
    public override string Key { get; set; } = "queue_refresh";
    public override string Name { get; set; } = "Queue Refresh";
    public override string Description { get; set; } = "Updates the queue from the configured servers";
    public override bool Enabled { get; set; } = true;
    public override int IntervalSeconds { get; set; } = 60;
    public override DateTimeOffset NextRun { get; set; } = DateTimeOffset.UtcNow;

    public override async Task<RoutineResult> ExecuteAsync(CancellationToken cancellationToken)
    {
        var servers = (await serverRepository.GetAllAsync())
            .Where(s => s.IsEnabled && settings.ShouldMonitorQueue(s)).OrderBy(s => s.Priority).ToList();

        if (servers.Count == 0)
        {
            logger.LogDebug("No servers configured — skipping queue refresh");
            return "No servers configured.";
        }

        var count = 0;
        var added = 0;
        var failed = 0;

        foreach (var server in servers)
        {
            // One unreachable server must not stop the others from refreshing.
            try
            {
                var existingIds = (await healthService.GetActiveDownloadIdsAsync(server.Id, cancellationToken))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                // The fresh fetch scores every download it hasn't seen before (MediaServerService.GetQueueAsync).
                var items = server.ServiceType switch
                {
                    ServiceType.Radarr or ServiceType.Sonarr => await mediaServerService.GetQueueAsync(server, true),
                    _ => []
                };

                count += items.Count;
                added += items.Select(HealthService.GetDownloadId).Distinct(StringComparer.OrdinalIgnoreCase)
                    .Count(id => !existingIds.Contains(id));
            }
            catch (Exception ex) when (ex is not OperationCanceledException ||
                                       !cancellationToken.IsCancellationRequested)
            {
                failed++;
                logger.LogError(ex, "Queue refresh failed for {name}", server.Name);
            }
        }

        var summary = added > 0
            ? $"{count} queue items found, {added} new item(s) evaluated"
            : $"{count} queue items found";
        return new RoutineResult(summary + (failed > 0 ? $", {failed} server(s) failed." : "."), failed == 0);
    }
}
