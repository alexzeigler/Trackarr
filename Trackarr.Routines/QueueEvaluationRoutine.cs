using Trackarr.Core.Health;
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
        var imported = 0;
        var disappeared = 0;

        foreach (var server in await mediaServerService.GetAllServersAsync())
        {
            var queue = await mediaServerService.GetQueueAsync(server);
            var downloads = queue.GroupBy(HealthService.GetDownloadId).Select(e => e.First()).ToList();

            foreach (var download in downloads)
            {
                var decision = await healthService.EvaluateAsync(download, cancellationToken);
                evaluated++;

                if (!decision.ShouldRemove) continue;

                await mediaServerService.RemoveFromQueueAsync(server, download.RemoteId);
                await healthService.MarkRemovedByTrackarrAsync(decision.DownloadAttemptId, decision.Message,
                    cancellationToken);
                removed++;
            }

            var activeIds = downloads.Select(HealthService.GetDownloadId).ToList();
            var missingIds = await healthService.GetMissingDownloadIdsAsync(server.Id, activeIds, cancellationToken);
            var importedIds = missingIds.Count == 0
                ? []
                : await mediaServerService.GetImportedDownloadIdsAsync(server, missingIds);

            var resolved = await healthService.ResolveMissingAttemptsAsync(server.Id, activeIds, importedIds,
                cancellationToken);

            imported += resolved.Count(e => e.Status == AttemptStatus.Imported);
            disappeared += resolved.Count(e => e.Status == AttemptStatus.Disappeared);
        }

        LastRunSuccess = true;
        return $"Evaluated {evaluated}, removed {removed}, imported {imported}, disappeared {disappeared}.";
    }
}