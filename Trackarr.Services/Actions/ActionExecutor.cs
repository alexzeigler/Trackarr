using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Trackarr.Core.Actions;
using Trackarr.Core.Exception;
using Trackarr.Core.Health;
using Trackarr.Data;
using Trackarr.Services.Media;
using Trackarr.Services.Settings;
using Trackarr.Services.UI;

namespace Trackarr.Services.Actions;

/// <summary>
///     The one gate for every change Trackarr makes on Radarr or Sonarr (ROADMAP 4.8). The writers on
///     <see cref="MediaServerService" /> are internal to this project, so nothing else can call them.
///     <list type="number">
///         <item>
///             Passive Mode, checked here and nowhere else: an automatic action is <see cref="ActionOutcome.Simulated" />
///             and nothing is sent. What you click still happens.
///         </item>
///         <item>Protection, checked once more: a routine never removes a protected download.</item>
///         <item>The call itself. A failure comes back as <see cref="ActionOutcome.Failed" />, not an exception.</item>
///         <item>An <see cref="ActionLogEntry" /> for every outcome.</item>
///     </list>
/// </summary>
public sealed class ActionExecutor(
    MediaServerService media,
    SettingsService settings,
    IDbContextFactory<AppDbContext> dbFactory,
    DataChangeBus changeBus,
    ILogger<ActionExecutor> logger)
{
    /// <summary>A simulated action repeats every run while Passive Mode is on. Within a day it updates one row.</summary>
    private static readonly TimeSpan RepeatWindow = TimeSpan.FromHours(24);

    private const int MaxTargetLength = 1000;

    public async Task<ActionResult> ExecuteAsync(ServerAction action, CancellationToken ct = default)
    {
        var result = await DecideAndRunAsync(action, ct);
        await LogAsync(action, result, ct);
        return result;
    }

    private async Task<ActionResult> DecideAndRunAsync(ServerAction action, CancellationToken ct)
    {
        if (action.IsAutomatic && settings.IsPassiveMode())
            return new ActionResult(ActionOutcome.Simulated);

        if (action is RemoveFromQueue remove && action.IsAutomatic && await IsProtectedAsync(remove, ct))
            return new ActionResult(ActionOutcome.Skipped, "The download is protected.");

        try
        {
            switch (action)
            {
                case RemoveFromQueue r:
                    await media.RemoveFromQueueAsync(r.Server, r.QueueId, r.Blocklist, r.RemoveFromClient);
                    return new ActionResult(ActionOutcome.Executed);

                case TriggerSearch s:
                    await SearchAsync(s);
                    return new ActionResult(ActionOutcome.Executed);

                case SetQualityProfile p:
                    var previous = await media.SetQualityProfileAsync(p.Server, p.RemoteItemId, p.ProfileId,
                        p.OnlyIfCurrent);
                    return new ActionResult(ActionOutcome.Executed, PreviousProfileId: previous);

                default:
                    throw new ArgumentOutOfRangeException(nameof(action), action.GetType().Name, "Unknown action.");
            }
        }
        catch (MediaServerException ex)
        {
            logger.LogWarning(ex, "{Kind} on {Server} failed for {Target}", action.Kind, action.Server.Name,
                action.Target);
            return new ActionResult(ActionOutcome.Failed, ex.Message);
        }
    }

    private async Task SearchAsync(TriggerSearch search)
    {
        switch (search.What)
        {
            case SearchKind.Movies:
                await media.TriggerMoviesSearchAsync(search.Server, search.Ids);
                break;
            case SearchKind.Episodes:
                await media.TriggerEpisodesSearchAsync(search.Server, search.Ids);
                break;
            case SearchKind.Series:
                // SeriesSearch takes one series at a time.
                foreach (var id in search.Ids)
                    await media.TriggerSeriesSearchAsync(search.Server, id);
                break;
        }
    }

    private async Task<bool> IsProtectedAsync(RemoveFromQueue remove, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.DownloadAttempts.AnyAsync(a =>
            a.ServerId == remove.Server.Id && a.DownloadId == remove.DownloadId && a.Status == AttemptStatus.Active &&
            a.IsProtected, ct);
    }

    private async Task LogAsync(ServerAction action, ActionResult result, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var target = action.Target.Length > MaxTargetLength ? action.Target[..MaxTargetLength] : action.Target;

        // The action already happened (or didn't): failing to write the log mustn't turn that into an error.
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);

            var since = now - RepeatWindow;
            var repeat = result.Outcome == ActionOutcome.Simulated
                ? await db.ActionLog
                    .Where(e => e.Outcome == ActionOutcome.Simulated && e.ServerId == action.Server.Id &&
                                e.Kind == action.Kind && e.Target == target && e.LastOccurredAt >= since)
                    .OrderByDescending(e => e.LastOccurredAt).FirstOrDefaultAsync(ct)
                : null;

            if (repeat is not null)
            {
                repeat.LastOccurredAt = now;
                repeat.Count++;
                repeat.Reason = action.Reason;
            }
            else
            {
                db.ActionLog.Add(new ActionLogEntry
                {
                    OccurredAt = now, LastOccurredAt = now, ServerId = action.Server.Id,
                    ServerName = action.Server.Name, Kind = action.Kind, Target = target, Reason = action.Reason,
                    Source = action.Source, Outcome = result.Outcome, Error = result.Error
                });
            }

            await db.SaveChangesAsync(ct);
            changeBus.NotifyChanged();
        }
        catch (DbUpdateException ex)
        {
            logger.LogError(ex, "Couldn't write the action log for {Kind} on {Server}", action.Kind,
                action.Server.Name);
        }
    }
}
