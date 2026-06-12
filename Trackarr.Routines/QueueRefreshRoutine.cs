using Trackarr.Core.Queue;
using Trackarr.Core.Routine;
using Trackarr.Core.Server;
using Trackarr.Services.Media;

namespace Trackarr.Tasks;

public sealed class QueueRefreshRoutine(MediaServerService mediaServerService) : Routine {
    public override string Key { get; set; } = "queue_refresh";
    public override string Name { get; set; } = "Queue Refresh";
    public override string Description { get; set; } = "Updates the queue from the configured servers";
    public override bool Enabled { get; set; } = true;
    public override int IntervalSeconds { get; set; } = 60;
    public override DateTimeOffset NextRun { get; set; } = DateTimeOffset.UtcNow;

    public override async Task<string> ExecuteAsync(CancellationToken cancellationToken) {
        List<MediaServer> servers = await mediaServerService.GetAllServersAsync();
        int count = 0;

        foreach (MediaServer server in servers) {
            List<DownloadItem> items = server.ServiceType switch {
                ServiceType.Radarr => await mediaServerService.GetQueueAsync(server, true),
                ServiceType.Sonarr => await mediaServerService.GetQueueAsync(server, true),
                _ => []
            };

            count += items.Count;
        }

        LastRunSuccess = true;
        return $"{count} queue items found.";
    }
}