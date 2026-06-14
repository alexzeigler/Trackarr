using Trackarr.Core.Health;
using Trackarr.Core.Queue;
using Trackarr.Core.Routine;
using Trackarr.Core.Server;
using Trackarr.Services.Media;

namespace Trackarr.Tasks;

public class QueueEvaluationRoutine(MediaServerService mediaServerService, HealthService healthService) : Routine {
    public override string Key { get; set; } = "queue-evaluation";
    public override string Name { get; set; } = "Queue Evaluation";
    public override string Description { get; set; } = "Evaluates the queue and removes downloads that meet the removal threshold.";
    public override bool Enabled { get; set; } = true;
    public override int IntervalSeconds { get; set; } = 300;
    public override DateTimeOffset NextRun { get; set; } = DateTimeOffset.UtcNow;

    public override async Task<string> ExecuteAsync(CancellationToken cancellationToken) {
        int evaluated = 0;
        int removed = 0;

        List<MediaServer> servers = await mediaServerService.GetAllServersAsync();

        foreach (MediaServer mediaServer in servers) {
            List<DownloadItem> downloadItems = await mediaServerService.GetQueueAsync(mediaServer);

            IEnumerable<DownloadItem> uniqueDownloads = downloadItems.GroupBy(e => string.IsNullOrWhiteSpace(e.DownloadId) ? e.AttemptKey : e.DownloadId).Select(e => e.First());

            bool removedAny = false;

            foreach (DownloadItem downloadItem in uniqueDownloads) {
                HealthDecision decision = await healthService.EvaluateAsync(downloadItem, cancellationToken);
                evaluated++;

                if (!decision.ShouldRemove)
                    continue;

                await mediaServerService.RemoveFromQueueAsync(mediaServer, downloadItem.RemoteId);
                await healthService.MarkRemovedByTrackarrAsync(decision.DownloadAttemptId, decision.Message, cancellationToken);

                removed++;
                removedAny = true;
            }

            if (removedAny)
                await mediaServerService.GetQueueAsync(mediaServer, true);
        }

        LastRunSuccess = true;
        return $"Evaluated {evaluated} items, removed {removed}.";
    }
}