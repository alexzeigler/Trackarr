using Trackarr.Core.Exception;
using Trackarr.Core.Queue;
using Trackarr.Core.Routine;
using Trackarr.Core.Server;
using Trackarr.Services;

namespace Trackarr.Tasks;

public sealed class QueueRefreshRoutine(MediaServerService mediaServerService) : Routine {
    public override string Key { get; set; } = "queue_refresh";
    public override string Name { get; set; } = "Queue Refresh";
    public override string Description { get; set; } = "Updates the queue from the configured servers";
    public override bool Enabled { get; set; } = true;
    public override int IntervalSeconds { get; set; } = 60;
    public override DateTimeOffset NextRun { get; set; } = DateTimeOffset.UtcNow;

    public override async Task<string> ExecuteAsync(CancellationToken cancellationToken) {
        (bool Success, List<MediaServer> Items) serverResult = await mediaServerService.GetAllServersAsync();
        int count = 0;

        foreach (Task<(bool Success, List<DownloadItem> Items, string Message)> result in serverResult.Items.Select(server => mediaServerService.GetQueueAsync(server, true))) {
            if (!result.Result.Success) {
                throw new RoutineFailedException(result.Result.Message, this);
            }

            count = result.Result.Items.Count;
            LastRunSuccess = true;
        }

        return $"{count} queue items found.";
    }
}