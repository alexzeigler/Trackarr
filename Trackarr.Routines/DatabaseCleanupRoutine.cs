using Microsoft.EntityFrameworkCore;
using Trackarr.Core.Health;
using Trackarr.Core.Routine;
using Trackarr.Data;
using Trackarr.Services.Settings;

namespace Trackarr.Tasks;

public sealed class DatabaseCleanupRoutine(IDbContextFactory<AppDbContext> dbFactory, SettingsService settings)
    : Routine
{
    public override string Key { get; set; } = "database-cleanup";
    public override string Name { get; set; } = "Database Cleanup";

    public override string Description { get; set; } =
        "Removes duplicate health events, prunes old data, and compacts the database.";

    public override bool Enabled { get; set; } = true;

    public override int IntervalSeconds { get; set; } =
        settings.GetInt("Maintenance", "CleanupIntervalHours", 24) * 3600;

    public override DateTimeOffset NextRun { get; set; } = DateTimeOffset.UtcNow.AddMinutes(5);

    public override async Task<string> ExecuteAsync(CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        var eventRetention = settings.GetInt("Maintenance", "HealthEventRetentionDays", 90);
        var runRetention = settings.GetInt("Maintenance", "RoutineRunRetentionDays", 30);
        var logRetention = settings.GetInt("Maintenance", "LogEventRetentionDays", 14);
        var summaryRetention = settings.GetInt("Maintenance", "SourceSummaryRetentionDays", 90);
        var vacuumAfter = settings.GetBool("Maintenance", "VacuumAfterCleanup", true);

        var deduped = await DeduplicateHealthEventsAsync(db, cancellationToken);
        var oldEvents = await PruneOldHealthEventsAsync(db, eventRetention, cancellationToken);
        var oldRuns = await PruneOldRoutineRunsAsync(db, runRetention, cancellationToken);
        var oldLogs = await PruneOldLogEventsAsync(db, logRetention, cancellationToken);
        var orphanedSnapshots = await PruneOrphanedSnapshotsAsync(db, cancellationToken);
        var staleSummaries = await PruneStaleSummariesAsync(db, summaryRetention, cancellationToken);

        await db.SaveChangesAsync(cancellationToken);

        if (vacuumAfter)
            await VacuumAsync(db, cancellationToken);

        LastRunSuccess = true;
        return $"Deduped {deduped} events, pruned {oldEvents} old events, " +
               $"{oldRuns} old runs, {oldLogs} old logs, " +
               $"{orphanedSnapshots} orphaned snapshots, {staleSummaries} stale summaries.";
    }

    /// <summary>
    ///     Collapse consecutive health events that have identical event type, reason key,
    ///     and zero deltas into a single event (keeping the most recent timestamp).
    /// </summary>
    private static async Task<int> DeduplicateHealthEventsAsync(AppDbContext db, CancellationToken ct)
    {
        var attemptIds = await db.HealthEvents.Select(e => e.DownloadAttemptId).Distinct().ToListAsync(ct);

        var totalRemoved = 0;

        foreach (var attemptId in attemptIds)
        {
            var events = await db.HealthEvents.Where(e => e.DownloadAttemptId == attemptId).OrderBy(e => e.OccurredAt)
                .ToListAsync(ct);

            if (events.Count < 2) continue;

            var toRemove = new List<DownloadHealthEvent>();
            var prev = events[0];

            for (var i = 1; i < events.Count; i++)
            {
                var curr = events[i];

                if (curr.EventType == prev.EventType && curr.ReasonKey == prev.ReasonKey && curr.ScoreDelta == 0 &&
                    curr.ConfidenceDelta == 0 && curr.RecoveryCreditDelta == 0)
                {
                    prev.OccurredAt = curr.OccurredAt;
                    toRemove.Add(curr);
                }
                else
                {
                    prev = curr;
                }
            }

            if (toRemove.Count > 0)
            {
                db.HealthEvents.RemoveRange(toRemove);
                totalRemoved += toRemove.Count;
            }
        }

        return totalRemoved;
    }

    /// <summary>
    ///     Remove health events older than 90 days for non-active attempts.
    /// </summary>
    private static async Task<int> PruneOldHealthEventsAsync(AppDbContext db, int retentionDays, CancellationToken ct)
    {
        var cutoff = DateTime.UtcNow.AddDays(-retentionDays);

        return await db.HealthEvents.Where(e => e.OccurredAt < cutoff)
            .Where(e => e.DownloadAttempt != null && e.DownloadAttempt.Status != AttemptStatus.Active)
            .ExecuteDeleteAsync(ct);
    }

    /// <summary>
    ///     Remove routine run history older than 30 days.
    /// </summary>
    private static async Task<int> PruneOldRoutineRunsAsync(AppDbContext db, int retentionDays, CancellationToken ct)
    {
        var cutoff = DateTime.UtcNow.AddDays(-retentionDays);

        return await db.RoutineRuns.Where(r => r.EndTime < cutoff).ExecuteDeleteAsync(ct);
    }

    /// <summary>
    ///     Remove log events older than 14 days.
    /// </summary>
    private static async Task<int> PruneOldLogEventsAsync(AppDbContext db, int retentionDays, CancellationToken ct)
    {
        var cutoff = DateTime.UtcNow.AddDays(-retentionDays);

        return await db.LogEvents.Where(e => e.TimeStamp < cutoff).ExecuteDeleteAsync(ct);
    }

    /// <summary>
    ///     Remove snapshots whose download attempt no longer exists.
    /// </summary>
    private static async Task<int> PruneOrphanedSnapshotsAsync(AppDbContext db, CancellationToken ct)
    {
        return await db.HealthSnapshots.Where(s => !db.DownloadAttempts.Any(a => a.Id == s.DownloadAttemptId))
            .ExecuteDeleteAsync(ct);
    }

    /// <summary>
    ///     Remove source health summaries that haven't been seen in over 90 days.
    /// </summary>
    private static async Task<int> PruneStaleSummariesAsync(AppDbContext db, int retentionDays, CancellationToken ct)
    {
        var cutoff = DateTime.UtcNow.AddDays(-retentionDays);

        return await db.SourceHealthSummaries.Where(s => s.LastSeenAt < cutoff).ExecuteDeleteAsync(ct);
    }

    /// <summary>
    ///     Run SQLite VACUUM to reclaim disk space after deletions.
    /// </summary>
    private static async Task VacuumAsync(AppDbContext db, CancellationToken ct)
    {
        await db.Database.ExecuteSqlRawAsync("VACUUM", ct);
    }
}
