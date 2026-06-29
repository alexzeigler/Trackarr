using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Trackarr.Core.Health;
using Trackarr.Core.Queue;
using Trackarr.Core.Routine;
using Trackarr.Core.Settings;
using Trackarr.Services.Media;
using Trackarr.Services.Settings;

namespace Trackarr.Routines;

public sealed class QueueEvaluationRoutine(
    MediaServerService mediaServerService,
    HealthService healthService,
    BlacklistService blacklistService,
    SettingsService settings,
    ILogger<QueueEvaluationRoutine> logger) : Routine
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
        var servers = (await mediaServerService.GetAllServersAsync()).Where(s => s.IsEnabled).OrderBy(s => s.Priority)
            .ToList();

        if (servers.Count == 0)
        {
            logger.LogDebug("No servers configured — skipping queue evaluation");
            return "No servers configured.";
        }

        var evaluated = 0;
        var removed = 0;
        var blacklisted = 0;
        var imported = 0;
        var disappeared = 0;

        var blacklist = await blacklistService.GetAllAsync();
        var passiveMode = settings.IsPassiveMode();

        foreach (var server in servers)
        {
            var queue = await mediaServerService.GetQueueAsync(server);
            var downloads = queue.GroupBy(HealthService.GetDownloadId).Select(e => e.First()).ToList();

            var autoRemoveEnabled = settings.ShouldAutoRemove(server);

            foreach (var download in downloads)
            {
                if (IsBlacklisted(download, blacklist))
                {
                    var blDecision = await healthService.EvaluateAsync(download, cancellationToken);
                    if (!passiveMode)
                    {
                        await mediaServerService.RemoveFromQueueAsync(server, download.RemoteId);
                        await healthService.MarkRemovedByTrackarrAsync(blDecision.DownloadAttemptId,
                            $"Removed by blacklist match: {download.Title}", cancellationToken);
                        blacklisted++;
                    }

                    continue;
                }

                var decision = await healthService.EvaluateAsync(download, cancellationToken);
                evaluated++;

                if (!decision.ShouldRemove || !autoRemoveEnabled || passiveMode) continue;

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
        return
            $"Evaluated {evaluated}, removed {removed}, blacklisted {blacklisted}, imported {imported}, disappeared {disappeared}.";
    }

    private static bool IsBlacklisted(DownloadItem download, List<BlacklistEntry> blacklist)
    {
        if (blacklist.Count == 0) return false;

        foreach (var entry in blacklist)
        {
            var target = entry.Type switch
            {
                BlacklistType.Indexer => download.Indexer,
                BlacklistType.ReleaseGroup => download.Title,
                BlacklistType.Keyword => download.Title,
                _ => null
            };

            if (string.IsNullOrWhiteSpace(target)) continue;

            if (entry.IsRegex)
            {
                try
                {
                    if (Regex.IsMatch(target, entry.Pattern, RegexOptions.IgnoreCase)) return true;
                }
                catch
                {
                    /* invalid regex, skip */
                }
            }
            else
            {
                if (target.Contains(entry.Pattern, StringComparison.OrdinalIgnoreCase)) return true;
            }
        }

        return false;
    }
}
