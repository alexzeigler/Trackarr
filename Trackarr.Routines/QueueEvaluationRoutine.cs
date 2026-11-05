using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Trackarr.Core.Exception;
using Trackarr.Core.Health;
using Trackarr.Core.Queue;
using Trackarr.Core.Routine;
using Trackarr.Core.Server;
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

    public override async Task<RoutineResult> ExecuteAsync(CancellationToken cancellationToken)
    {
        // Monitor Queue off means hands off the queue entirely: no fetching, scoring or removing on that server.
        var servers = (await mediaServerService.GetAllServersAsync())
            .Where(s => s.IsEnabled && settings.ShouldMonitorQueue(s)).OrderBy(s => s.Priority)
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
        var skippedServers = 0;
        var failedServers = 0;
        var failedRemovals = 0;

        var blacklist = await blacklistService.GetAllAsync();
        var passiveMode = settings.IsPassiveMode();

        foreach (var server in servers)
        {
            // One unreachable server must not stop evaluation (and removals) on the others.
            try
            {
                var queue = await mediaServerService.GetQueueAsync(server);
                if (HealthEvaluator.IsDownloadClientUnavailable(queue))
                {
                    // While the client is down every item looks stalled; scoring now would put the whole queue at risk.
                    logger.LogWarning("Download client unavailable on {name}; skipping queue evaluation this run",
                        server.Name);
                    skippedServers++;
                    continue;
                }

                var downloads = queue.GroupBy(HealthService.GetDownloadId).Select(e => e.First()).ToList();

                var autoRemoveEnabled = settings.ShouldAutoRemove(server);

                foreach (var download in downloads)
                {
                    if (IsBlacklisted(download, blacklist))
                    {
                        var blDecision = await healthService.EvaluateAsync(download, cancellationToken);
                        // A blacklist match is still a removal, so Auto Remove off keeps it in the queue too.
                        if (passiveMode || !autoRemoveEnabled) continue;

                        if (await TryRemoveAsync(server, download))
                        {
                            await healthService.MarkRemovedByTrackarrAsync(blDecision.DownloadAttemptId,
                                $"Removed by blacklist match: {download.Title}", cancellationToken);
                            blacklisted++;
                        }
                        else failedRemovals++;

                        continue;
                    }

                    var decision = await healthService.EvaluateAsync(download, cancellationToken);
                    evaluated++;

                    if (!decision.ShouldRemove || !autoRemoveEnabled || passiveMode) continue;

                    if (await TryRemoveAsync(server, download))
                    {
                        await healthService.MarkRemovedByTrackarrAsync(decision.DownloadAttemptId, decision.Message,
                            cancellationToken);
                        removed++;
                    }
                    else failedRemovals++;
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
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                failedServers++;
                logger.LogError(ex, "Queue evaluation failed for {name}", server.Name);
            }
        }

        var notes = new List<string>();
        if (skippedServers > 0) notes.Add($"Skipped {skippedServers} server(s) with the download client unavailable.");
        if (failedRemovals > 0) notes.Add($"{failedRemovals} removal(s) failed.");
        if (failedServers > 0) notes.Add($"{failedServers} server(s) failed.");
        var suffix = notes.Count > 0 ? " " + string.Join(" ", notes) : "";
        return new RoutineResult(
            $"Evaluated {evaluated}, removed {removed}, blacklisted {blacklisted}, imported {imported}, disappeared {disappeared}.{suffix}",
            failedServers == 0 && failedRemovals == 0);
    }

    /// <summary>
    ///     Removes one item from the server's queue. A failure (say a 404 because the item already left) is logged
    ///     and the attempt stays active, so it's re-evaluated next run instead of aborting the rest of the queue.
    /// </summary>
    private async Task<bool> TryRemoveAsync(MediaServer server, DownloadItem download)
    {
        try
        {
            await mediaServerService.RemoveFromQueueAsync(server, download.RemoteId);
            return true;
        }
        catch (MediaServerException ex)
        {
            logger.LogWarning(ex, "Could not remove {title} from {name}'s queue", download.Title, server.Name);
            return false;
        }
    }

    private bool IsBlacklisted(DownloadItem download, List<BlacklistEntry> blacklist)
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
                catch (ArgumentException ex)
                {
                    // An invalid blacklist pattern is your config, not a bug: say so, then skip the entry.
                    logger.LogWarning(ex, "Blacklist pattern {Pattern} is not a valid regex; skipping it", entry.Pattern);
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
