using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Trackarr.Core.Actions;
using Trackarr.Core.Health;
using Trackarr.Core.Queue;
using Trackarr.Core.Routine;
using Trackarr.Core.Server;
using Trackarr.Core.Settings;
using Trackarr.Services.Actions;
using Trackarr.Services.Media;
using Trackarr.Services.Servers;
using Trackarr.Services.Settings;

namespace Trackarr.Routines;

public sealed class QueueEvaluationRoutine(
    MediaServerService mediaServerService,
    ServerRepository serverRepository,
    HealthService healthService,
    BlacklistService blacklistService,
    SettingsService settings,
    ActionExecutor actions,
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
        var servers = (await serverRepository.GetAllAsync())
            .Where(s => s.IsEnabled && settings.ShouldMonitorQueue(s)).OrderBy(s => s.Priority).ToList();

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
        var simulated = 0;
        var skippedRemovals = 0;

        var blacklist = await blacklistService.GetAllAsync();

        foreach (var server in servers)
        {
            // One unreachable server must not stop evaluation (and removals) on the others.
            try
            {
                var trackedBefore = (await healthService.GetActiveDownloadIdsAsync(server.Id, cancellationToken))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                var queue = await mediaServerService.GetQueueAsync(server);
                // A fresh fetch scores downloads it hasn't seen before. Scoring those again right away would count
                // a second scan seconds after the first, which looks like no progress.
                var trackedAfter = (await healthService.GetActiveDownloadIdsAsync(server.Id, cancellationToken))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                bool JustScored(string id)
                {
                    return !trackedBefore.Contains(id) && trackedAfter.Contains(id);
                }

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
                    var justScored = JustScored(HealthService.GetDownloadId(download));

                    if (IsBlacklisted(download, blacklist))
                    {
                        var blDecision = justScored
                            ? await healthService.PreviewAsync(download, cancellationToken)
                            : await healthService.EvaluateAsync(download, cancellationToken);
                        // A blacklist match is still a removal, so Auto Remove off keeps it in the queue too.
                        if (!autoRemoveEnabled) continue;

                        var blOutcome = await RemoveAsync(server, download, "Matches the blacklist.",
                            cancellationToken);
                        if (blOutcome == ActionOutcome.Executed)
                        {
                            await healthService.MarkRemovedByTrackarrAsync(blDecision.DownloadAttemptId,
                                $"Removed by blacklist match: {download.Title}", cancellationToken);
                            blacklisted++;
                        }
                        else Tally(blOutcome);

                        continue;
                    }

                    // Scored by the fetch above, in this same run: count it, but don't scan it twice.
                    if (justScored)
                    {
                        evaluated++;
                        continue;
                    }

                    var decision = await healthService.EvaluateAsync(download, cancellationToken);
                    evaluated++;

                    // Blocked only by Passive Mode: still asked for, so the gate records the would-be removal.
                    if (!(decision.ShouldRemove || decision.BlockedByPassiveMode) || !autoRemoveEnabled) continue;

                    var outcome = await RemoveAsync(server, download, decision.Message, cancellationToken);
                    if (outcome == ActionOutcome.Executed)
                    {
                        await healthService.MarkRemovedByTrackarrAsync(decision.DownloadAttemptId, decision.Message,
                            cancellationToken);
                        removed++;
                    }
                    else Tally(outcome);
                }

                var activeIds = downloads.Select(HealthService.GetDownloadId).ToList();
                var missingIds =
                    await healthService.GetMissingDownloadIdsAsync(server.Id, activeIds, cancellationToken);
                var importedIds = missingIds.Count == 0
                    ? []
                    : await mediaServerService.GetImportedDownloadIdsAsync(server, missingIds);

                var resolved = await healthService.ResolveMissingAttemptsAsync(server.Id, activeIds, importedIds,
                    cancellationToken);

                imported += resolved.Count(e => e.Status == AttemptStatus.Imported);
                disappeared += resolved.Count(e => e.Status == AttemptStatus.Disappeared);
            }
            catch (Exception ex) when (ex is not OperationCanceledException ||
                                       !cancellationToken.IsCancellationRequested)
            {
                failedServers++;
                logger.LogError(ex, "Queue evaluation failed for {name}", server.Name);
            }
        }

        var notes = new List<string>();
        if (simulated > 0) notes.Add($"Passive Mode: would have removed {simulated} (see System > Actions).");
        if (skippedRemovals > 0) notes.Add($"Kept {skippedRemovals} protected download(s).");
        if (skippedServers > 0) notes.Add($"Skipped {skippedServers} server(s) with the download client unavailable.");
        if (failedRemovals > 0) notes.Add($"{failedRemovals} removal(s) failed.");
        if (failedServers > 0) notes.Add($"{failedServers} server(s) failed.");
        var suffix = notes.Count > 0 ? " " + string.Join(" ", notes) : "";
        return new RoutineResult(
            $"Evaluated {evaluated}, removed {removed}, blacklisted {blacklisted}, imported {imported}, disappeared {disappeared}.{suffix}",
            failedServers == 0 && failedRemovals == 0);

        void Tally(ActionOutcome outcome)
        {
            switch (outcome)
            {
                case ActionOutcome.Simulated: simulated++; break;
                case ActionOutcome.Skipped: skippedRemovals++; break;
                default: failedRemovals++; break;
            }
        }
    }

    /// <summary>
    ///     Asks the action gate to remove one download. Anything but Executed leaves the attempt active: simulated in
    ///     Passive Mode, skipped because it's protected, or failed (say a 404 because it already left), so it's looked
    ///     at again next run instead of aborting the rest of the queue.
    /// </summary>
    private async Task<ActionOutcome> RemoveAsync(MediaServer server, DownloadItem download, string reason,
        CancellationToken ct)
    {
        var result = await actions.ExecuteAsync(new RemoveFromQueue(server, download.RemoteId,
            HealthService.GetDownloadId(download), download.Title ?? $"Queue item {download.RemoteId}", reason,
            ActionSource.Routine(Key)), ct);
        return result.Outcome;
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
                    logger.LogWarning(ex, "Blacklist pattern {Pattern} is not a valid regex; skipping it",
                        entry.Pattern);
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
