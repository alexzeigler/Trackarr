using Trackarr.Core.Routine;
using Trackarr.Core.Server;
using Trackarr.Services.Media;
using Trackarr.Services.Settings;

namespace Trackarr.Routines;

public sealed class QueueRefreshRoutine(MediaServerService mediaServerService, SettingsService settings) : Routine
{
    public override string Key { get; set; } = "queue_refresh";
    public override string Name { get; set; } = "Queue Refresh";
    public override string Description { get; set; } = "Updates the queue from the configured servers";
    public override bool Enabled { get; set; } = true;
    public override int IntervalSeconds { get; set; } = 60;
    public override DateTimeOffset NextRun { get; set; } = DateTimeOffset.UtcNow;

    public override async Task<string> ExecuteAsync(CancellationToken cancellationToken)
    {
        var servers = (await mediaServerService.GetAllServersAsync())
            .Where(s => s.IsEnabled && settings.ShouldMonitorQueue(s)).OrderBy(s => s.Priority).ToList();
        var count = 0;

        foreach (var server in servers)
        {
            var items = server.ServiceType switch
            {
                ServiceType.Radarr or ServiceType.Sonarr => await mediaServerService.GetQueueAsync(server, true),
                _ => []
            };

            count += items.Count;
        }

        LastRunSuccess = true;
        return $"{count} queue items found.";
    }
}
