using Microsoft.Extensions.Logging;
using Trackarr.Core.Routine;
using Trackarr.Core.Server;
using Trackarr.Services.Media;
using Trackarr.Services.Settings;

namespace Trackarr.Routines;

public sealed class QueueRefreshRoutine(
    MediaServerService mediaServerService,
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
        var servers = (await mediaServerService.GetAllServersAsync())
            .Where(s => s.IsEnabled && settings.ShouldMonitorQueue(s)).OrderBy(s => s.Priority).ToList();

        if (servers.Count == 0)
        {
            logger.LogDebug("No servers configured — skipping queue refresh");
            return "No servers configured.";
        }

        var count = 0;
        var evaluated = 0;
        var failed = 0;

        foreach (var server in servers)
        {
            // One unreachable server must not stop the others from refreshing.
            try
            {
                var existingIds = (await healthService.GetActiveDownloadIdsAsync(server.Id, cancellationToken))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                var items = server.ServiceType switch
                {
                    ServiceType.Radarr or ServiceType.Sonarr => await mediaServerService.GetQueueAsync(server, true),
                    _ => []
                };

                count += items.Count;

                foreach (var item in items.Where(i => !existingIds.Contains(HealthService.GetDownloadId(i))))
                {
                    await healthService.EvaluateAsync(item, cancellationToken);
                    evaluated++;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                failed++;
                logger.LogError(ex, "Queue refresh failed for {name}", server.Name);
            }
        }

        var summary = evaluated > 0
            ? $"{count} queue items found, {evaluated} new item(s) evaluated"
            : $"{count} queue items found";
        return new RoutineResult(summary + (failed > 0 ? $", {failed} server(s) failed." : "."), failed == 0);
    }
}
