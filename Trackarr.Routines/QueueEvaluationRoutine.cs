using Trackarr.Core.Routine;
using Trackarr.Services.Media;

namespace Trackarr.Tasks;

public class QueueEvaluationRoutine(MediaServerService mediaServerService, HealthService healthService) : Routine
{
    public override string Key { get; set; } = "queue-evaluation";
    public override string Name { get; set; } = "Queue Evaluation";

    public override string Description { get; set; } =
        "Evaluates the queue and removes downloads that meet the removal threshold.";

    public override bool Enabled { get; set; } = true;
    public override int IntervalSeconds { get; set; } = 300;
    public override DateTimeOffset NextRun { get; set; } = DateTimeOffset.UtcNow;

    public override async Task<string> ExecuteAsync(CancellationToken cancellationToken)
    {
        var evaluated = 0;
        var removed = 0;

        var servers = await mediaServerService.GetAllServersAsync();

        foreach (var mediaServer in servers)
        {
            var downloadItems = await mediaServerService.GetQueueAsync(mediaServer);

            var uniqueDownloads = downloadItems
                .GroupBy(e => string.IsNullOrWhiteSpace(e.DownloadId) ? e.AttemptKey : e.DownloadId)
                .Select(e => e.First());

            var removedAny = false;

            foreach (var downloadItem in uniqueDownloads)
            {
                var decision = await healthService.EvaluateAsync(downloadItem, cancellationToken);
                evaluated++;

                if (!decision.ShouldRemove)
                    continue;

                await mediaServerService.RemoveFromQueueAsync(mediaServer, downloadItem.RemoteId);
                await healthService.MarkRemovedByTrackarrAsync(decision.DownloadAttemptId, decision.Message,
                    cancellationToken);

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