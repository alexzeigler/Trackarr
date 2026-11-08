using Microsoft.EntityFrameworkCore;
using Trackarr.Core.Health;
using Trackarr.Core.Queue;
using Trackarr.Data;
using Trackarr.Services.Settings;
using Trackarr.Services.UI;

namespace Trackarr.Services.Media;

public sealed class HealthService(
    IDbContextFactory<AppDbContext> dbFactory,
    SettingsService settings,
    DataChangeBus changeBus)
{
    #region Evaluation

    /// <summary>Scores the item and records the scan: it counts toward removal.</summary>
    public Task<HealthDecision> EvaluateAsync(DownloadItem item, CancellationToken cancellationToken = default)
    {
        return EvaluateCoreAsync(item, true, cancellationToken);
    }

    /// <summary>
    ///     Shows what a scan would decide right now without recording it (M13): nothing is saved, so clicking it
    ///     repeatedly can't push a download toward removal. Uses exactly the same scoring as a real scan.
    /// </summary>
    public Task<HealthDecision> PreviewAsync(DownloadItem item, CancellationToken cancellationToken = default)
    {
        return EvaluateCoreAsync(item, false, cancellationToken);
    }

    private async Task<HealthDecision> EvaluateCoreAsync(DownloadItem item, bool persist,
        CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        var policy = Policy;
        var evaluator = CreateEvaluator();
        var now = DateTime.UtcNow;

        var server = await db.MediaServers.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == item.ServerId, cancellationToken);

        var attempt = await _repository.GetOrCreateAttemptAsync(db, item, server, now, cancellationToken);
        var snapshot = await _repository.GetOrCreateSnapshotAsync(db, attempt, now, cancellationToken);
        var exemption = HealthEvaluator.GetScoringExemption(item);
        if (exemption != null)
            return await RecordExemptAsync(db, item, attempt, snapshot, exemption, now, persist, cancellationToken);

        var mediaSummary = await _repository.GetMediaSummaryAsync(db, attempt, cancellationToken);

        var observation = HealthEvaluator.BuildObservation(item, attempt, snapshot, now);
        HealthEvaluator.RecordObservation(snapshot, observation, now);

        var recentWindow = attempt.Id == 0
            ? []
            : await db.HealthEvents.AsNoTracking().Where(e => e.DownloadAttemptId == attempt.Id)
                .Where(e => !TerminalEventTypes.Contains(e.EventType)).OrderByDescending(e => e.OccurredAt)
                .Take(Math.Max(0, policy.RollingWindowSize - 1)).ToListAsync(cancellationToken);

        // The window is the last N-1 scans, not events: one merged no-change event can stand for many scans.
        var (badInWindow, goodInWindow) = CountScansInWindow(recentWindow, Math.Max(0, policy.RollingWindowSize - 1));

        var evaluation = evaluator.Evaluate(attempt, snapshot, mediaSummary, observation, badInWindow, goodInWindow);

        HealthStateBefore before = new(snapshot.RawScore, snapshot.Confidence, snapshot.RecoveryCredit);

        evaluator.ApplyEvaluation(snapshot, evaluation, now);

        attempt.LastSeenAt = now;
        attempt.MissedScans = 0;
        attempt.Title = item.Title ?? attempt.Title;
        attempt.NormalizedTitle = HealthEvaluator.NormalizeTitle(item.Title ?? attempt.Title);
        attempt.DownloadClient = item.DownloadClient;
        attempt.Indexer = item.Indexer;
        attempt.Protocol = item.Protocol;
        attempt.RemoteId = item.RemoteId;

        var healthEvent = HealthEvaluator.CreateHealthEvent(attempt, snapshot, before, evaluation, observation, now);

        var lastEvent = recentWindow.Count > 0 ? recentWindow[0] : null;
        var isNoChange = lastEvent != null && healthEvent.ScoreDelta == 0 && healthEvent.ConfidenceDelta == 0 &&
                         healthEvent.RecoveryCreditDelta == 0 && healthEvent.EventType == lastEvent.EventType &&
                         healthEvent.ReasonKey == lastEvent.ReasonKey;

        if (isNoChange)
        {
            var tracked = await db.HealthEvents.FindAsync([lastEvent!.Id], cancellationToken);
            if (tracked != null)
            {
                tracked.OccurredAt = now;
                tracked.ScanCount++;
            }
        }
        else
        {
            db.HealthEvents.Add(healthEvent);
        }

        // Scans since the last meaningful recovery: this one (unless it is the recovery) plus the earlier scans
        // after it.
        int? scansSinceRecovery = snapshot.LastMeaningfulRecoveryAt is { } recoveredAt
            ? recentWindow.Where(e => e.OccurredAt > recoveredAt).Sum(e => e.ScanCount) + (recoveredAt < now ? 1 : 0)
            : null;

        var decision = evaluator.BuildDecision(attempt, snapshot, evaluation, now, scansSinceRecovery);

        // Settings that block removal belong in the decision too, so nothing shows as "Remove" that won't be. This
        // is for display: the action gate is what actually stops Passive Mode removals (ROADMAP 4.8).
        var autoRemoveOff = server is not null && !settings.ShouldAutoRemove(server);
        var settingsBlock = settings.IsPassiveMode() ? "Passive Mode is on." :
            autoRemoveOff ? "Auto Remove is off for this server." : null;
        if (settingsBlock is not null)
        {
            decision.Restrictions.Add(settingsBlock);
            if (decision.ShouldRemove)
            {
                decision.BlockedByPassiveMode = !autoRemoveOff;
                decision.ShouldRemove = false;
                decision.State = snapshot.State;
                decision.ReasonKey = evaluation.ReasonKey;
                decision.Message = evaluation.Message;
            }
        }

        var becameRemovable = decision.ShouldRemove && !snapshot.IsEligibleForRemoval;
        snapshot.IsEligibleForRemoval = decision.ShouldRemove;
        if (decision.ShouldRemove) snapshot.State = HealthState.Remove;
        snapshot.IsRemovalRestricted = decision.Restrictions.Count > 0;
        snapshot.RemovalRestrictionReason =
            decision.Restrictions.Count == 0 ? null : string.Join("; ", decision.Restrictions);

        await _repository.UpdateMediaSummaryAsync(db, attempt, decision, now, cancellationToken);

        if (!isNoChange)
            await _repository.UpdateSourceSummaryAsync(db, attempt, snapshot, healthEvent, becameRemovable, now,
                cancellationToken);

        if (persist)
        {
            await db.SaveChangesAsync(cancellationToken);
            changeBus.NotifyChanged();
        }

        decision.DownloadAttemptId = attempt.Id;

        return decision;
    }

    /// <summary>
    ///     Items waiting on something outside the download (queued, paused, delay profile, import) are not
    ///     scored: the score and streaks stay frozen and the item is never eligible for removal while waiting.
    /// </summary>
    private async Task<HealthDecision> RecordExemptAsync(AppDbContext db, DownloadItem item, DownloadAttempt attempt,
        HealthSnapshot snapshot, string reason, DateTime now, bool persist, CancellationToken cancellationToken)
    {
        attempt.LastSeenAt = now;
        attempt.MissedScans = 0;
        attempt.Title = item.Title ?? attempt.Title;
        attempt.RemoteId = item.RemoteId;

        snapshot.IsEligibleForRemoval = false;
        snapshot.IsRemovalRestricted = true;
        snapshot.RemovalRestrictionReason = reason;
        snapshot.UpdatedAt = now;

        if (persist) await db.SaveChangesAsync(cancellationToken);

        return new HealthDecision
        {
            DownloadAttemptId = attempt.Id,
            ShouldRemove = false,
            State = snapshot.State,
            RawScore = snapshot.RawScore,
            VisibleScore = snapshot.VisibleScore,
            Confidence = snapshot.Confidence,
            RecoveryCredit = snapshot.RecoveryCredit,
            ReasonKey = "waiting",
            Message = reason,
            Restrictions = [reason]
        };
    }

    #endregion

    #region Policy & Static Data

    private static readonly HealthEventType[] TerminalEventTypes =
    [
        HealthEventType.QueueMissing,
        HealthEventType.RemovedByTrackarr,
        HealthEventType.ImportedSuccessfully,
        HealthEventType.FailedExternally,
        HealthEventType.RemovedByUser
    ];

    private HealthPolicy Policy => settings.GetHealthPolicy();

    private HealthEvaluator CreateEvaluator()
    {
        return new HealthEvaluator(settings.GetHealthPolicy());
    }

    private readonly HealthRepository _repository = new();

    #endregion

    #region Lifecycle & Reconciliation

    // Test-only (nothing in the app calls it); internal so it isn't part of the service's API.
    internal async Task MarkImportedAsync(int attemptId, string? message = null,
        CancellationToken cancellationToken = default)
    {
        await MarkTerminalAsync(attemptId, AttemptStatus.Imported, HealthEventType.ImportedSuccessfully,
            Severity.Positive, "imported-successfully", message ?? "Download imported successfully.", true,
            cancellationToken);
    }

    public async Task MarkRemovedByTrackarrAsync(int attemptId, string? message = null,
        CancellationToken cancellationToken = default)
    {
        await MarkTerminalAsync(attemptId, AttemptStatus.RemovedByTrackarr, HealthEventType.RemovedByTrackarr,
            Severity.Critical, "removed-by-trackarr", message ?? "Download removed by Trackarr.", false,
            cancellationToken);
    }

    public async Task MarkRemovedByUserAsync(DownloadItem item, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        var downloadId = GetDownloadId(item);
        var attempt = await db.DownloadAttempts
            .Where(e => e.ServerId == item.ServerId && e.DownloadId == downloadId && e.Status == AttemptStatus.Active)
            .Select(e => e.Id).FirstOrDefaultAsync(cancellationToken);

        if (attempt == 0) return;

        // Your own removal isn't a Trackarr removal: it mustn't set WasRemovedByTrackarr or count toward the
        // media's stuck flag.
        await MarkTerminalAsync(attempt, AttemptStatus.RemovedExternally, HealthEventType.RemovedByUser, Severity.Info,
            "removed-by-user", "Manually removed from queue by user.", false, cancellationToken);
    }

    // Test-only (nothing in the app calls it); internal so it isn't part of the service's API.
    internal async Task MarkFailedAsync(int attemptId, string reasonKey, string message,
        CancellationToken cancellationToken = default)
    {
        await MarkTerminalAsync(attemptId, AttemptStatus.Failed, HealthEventType.FailedExternally, Severity.Severe,
            reasonKey, message, false, cancellationToken);
    }

    public async Task<List<DownloadAttempt>> ResolveMissingAttemptsAsync(int serverId,
        IEnumerable<string> activeDownloadIds, IEnumerable<string>? importedDownloadIds = null,
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        var now = DateTime.UtcNow;
        var active = activeDownloadIds.Where(e => !string.IsNullOrWhiteSpace(e)).Select(e => e.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var imported = (importedDownloadIds ?? []).Where(e => !string.IsNullOrWhiteSpace(e)).Select(e => e.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var missing = await db.DownloadAttempts.Include(e => e.HealthSnapshot).Where(e => e.ServerId == serverId)
            .Where(e => e.Status == AttemptStatus.Active).Where(e => !active.Contains(e.DownloadId))
            .ToListAsync(cancellationToken);

        foreach (var attempt in missing)
        {
            var snapshot = attempt.HealthSnapshot ??
                           await _repository.GetOrCreateSnapshotAsync(db, attempt, now, cancellationToken);

            if (imported.Contains(attempt.DownloadId))
            {
                attempt.Status = AttemptStatus.Imported;
                attempt.WasSuccessful = true;
                attempt.WasImported = true;
                attempt.CompletedAt = now;
                attempt.FinalReasonKey = "imported-successfully";
                attempt.FinalMessage = "Download imported successfully.";

                snapshot.RawScore = Policy.MaximumScore;
                snapshot.VisibleScore = Policy.MaximumScore;
                snapshot.State = HealthState.Resolved;
                snapshot.LastReasonKey = "imported-successfully";
                snapshot.LastReasonMessage = attempt.FinalMessage;
                snapshot.UpdatedAt = now;

                db.HealthEvents.Add(new DownloadHealthEvent
                {
                    DownloadAttempt = attempt,
                    OccurredAt = now,
                    EventType = HealthEventType.ImportedSuccessfully,
                    Severity = Severity.Positive,
                    ReasonKey = "imported-successfully",
                    Message = attempt.FinalMessage,
                    ScoreBefore = snapshot.RawScore,
                    ScoreDelta = 0,
                    ScoreAfter = snapshot.RawScore,
                    ConfidenceBefore = snapshot.Confidence,
                    ConfidenceDelta = 0,
                    ConfidenceAfter = snapshot.Confidence,
                    RecoveryCreditBefore = snapshot.RecoveryCredit,
                    RecoveryCreditDelta = 0,
                    RecoveryCreditAfter = snapshot.RecoveryCredit,
                    IsGoodObservation = true,
                    CountsTowardRemoval = false,
                    Source = nameof(HealthService)
                });
            }
            else
            {
                // One missed scan can be a glitch (a partial API response, a refresh caught mid-rewrite). Only
                // call it Disappeared once it's been missing twice in a row; that status counts as a failure.
                attempt.MissedScans++;
                if (attempt.MissedScans < MissedScansBeforeDisappeared) continue;

                attempt.Status = AttemptStatus.Disappeared;
                attempt.FailedAt = now;
                attempt.FinalReasonKey = "queue-missing";
                attempt.FinalMessage = "Download disappeared from the queue before Trackarr marked it as complete.";

                snapshot.State = HealthState.Unknown;
                snapshot.LastReasonKey = "queue-missing";
                snapshot.LastReasonMessage = attempt.FinalMessage;
                snapshot.UpdatedAt = now;

                db.HealthEvents.Add(new DownloadHealthEvent
                {
                    DownloadAttempt = attempt,
                    OccurredAt = now,
                    EventType = HealthEventType.QueueMissing,
                    Severity = Severity.Warning,
                    ReasonKey = "queue-missing",
                    Message = attempt.FinalMessage,
                    ScoreBefore = snapshot.RawScore,
                    ScoreDelta = 0,
                    ScoreAfter = snapshot.RawScore,
                    ConfidenceBefore = snapshot.Confidence,
                    ConfidenceDelta = 0,
                    ConfidenceAfter = snapshot.Confidence,
                    RecoveryCreditBefore = snapshot.RecoveryCredit,
                    RecoveryCreditDelta = 0,
                    RecoveryCreditAfter = snapshot.RecoveryCredit,
                    IsBadObservation = true,
                    CountsTowardRemoval = false,
                    Source = nameof(HealthService)
                });
            }

            await _repository.UpdateMediaSummaryAsync(db, attempt, null, now, cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);

        // Only the attempts actually resolved this scan; ones on their first miss are still Active.
        return missing.Where(e => e.Status != AttemptStatus.Active).ToList();
    }

    private const int MissedScansBeforeDisappeared = 2;

    /// <summary>
    ///     Bad and good scans among the most recent <paramref name="scans" /> scans, newest first. Each event
    ///     stands for <see cref="DownloadHealthEvent.ScanCount" /> scans.
    /// </summary>
    public static (int Bad, int Good) CountScansInWindow(IEnumerable<DownloadHealthEvent> newestFirst, int scans)
    {
        int bad = 0, good = 0, remaining = scans;

        foreach (var e in newestFirst)
        {
            if (remaining <= 0) break;
            var n = Math.Min(Math.Max(1, e.ScanCount), remaining);
            if (e.IsBadObservation) bad += n;
            if (e.IsGoodObservation) good += n;
            remaining -= n;
        }

        return (bad, good);
    }

    private async Task MarkTerminalAsync(int attemptId, AttemptStatus status, HealthEventType eventType,
        Severity severity, string reasonKey, string message, bool success, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        var now = DateTime.UtcNow;

        var attempt = await db.DownloadAttempts.Include(e => e.HealthSnapshot)
            .FirstOrDefaultAsync(e => e.Id == attemptId, cancellationToken);

        if (attempt is null) return;

        var snapshot = attempt.HealthSnapshot ??
                       await _repository.GetOrCreateSnapshotAsync(db, attempt, now, cancellationToken);

        attempt.Status = status;
        attempt.FinalReasonKey = reasonKey;
        attempt.FinalMessage = message;
        attempt.WasSuccessful = success;
        attempt.WasImported = status == AttemptStatus.Imported;
        attempt.WasRemovedByTrackarr = status == AttemptStatus.RemovedByTrackarr;

        if (status == AttemptStatus.Imported)
        {
            attempt.CompletedAt = now;
            snapshot.RawScore = Policy.MaximumScore;
            snapshot.VisibleScore = Policy.MaximumScore;
            snapshot.State = HealthState.Resolved;
        }

        if (status == AttemptStatus.RemovedByTrackarr)
        {
            attempt.RemovedAt = now;
            snapshot.State = HealthState.Remove;
        }

        if (status == AttemptStatus.Failed)
        {
            attempt.FailedAt = now;
            snapshot.State = HealthState.Failed;
        }

        if (status == AttemptStatus.RemovedExternally)
            attempt.RemovedAt = now;

        // A removal by the user says nothing about the download's health either way.
        var neutral = status == AttemptStatus.RemovedExternally;

        snapshot.LastReasonKey = reasonKey;
        snapshot.LastReasonMessage = message;
        snapshot.UpdatedAt = now;

        db.HealthEvents.Add(new DownloadHealthEvent
        {
            DownloadAttempt = attempt,
            OccurredAt = now,
            EventType = eventType,
            Severity = severity,
            ReasonKey = reasonKey,
            Message = message,
            ScoreBefore = snapshot.RawScore,
            ScoreDelta = 0,
            ScoreAfter = snapshot.RawScore,
            ConfidenceBefore = snapshot.Confidence,
            ConfidenceDelta = 0,
            ConfidenceAfter = snapshot.Confidence,
            RecoveryCreditBefore = snapshot.RecoveryCredit,
            RecoveryCreditDelta = 0,
            RecoveryCreditAfter = snapshot.RecoveryCredit,
            IsGoodObservation = success && !neutral,
            IsBadObservation = !success && !neutral,
            IsMeaningfulRecovery = false,
            CountsTowardRemoval = false,
            Source = nameof(HealthService)
        });

        await _repository.UpdateMediaSummaryAsync(db, attempt, null, now, cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    ///     Protects a download from automatic removal (or lifts that). A protected download is still scored and
    ///     shown, but the decision always lists "Download is protected." and never removes it. Returns false if
    ///     the item has no active attempt yet (it hasn't been scanned).
    /// </summary>
    public async Task<bool> SetProtectedAsync(DownloadItem item, bool isProtected,
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        var downloadId = GetDownloadId(item);
        var attempt = await db.DownloadAttempts.Include(e => e.HealthSnapshot)
            .Where(e => e.ServerId == item.ServerId && e.DownloadId == downloadId && e.Status == AttemptStatus.Active)
            .FirstOrDefaultAsync(cancellationToken);

        if (attempt is null) return false;
        if (attempt.IsProtected == isProtected) return true;

        var now = DateTime.UtcNow;
        attempt.IsProtected = isProtected;

        if (attempt.HealthSnapshot is { } snapshot)
        {
            if (isProtected)
            {
                snapshot.IsEligibleForRemoval = false;
                snapshot.IsRemovalRestricted = true;
                snapshot.RemovalRestrictionReason = "Download is protected.";
                if (snapshot.State == HealthState.Remove) snapshot.State = HealthState.Critical;
            }

            snapshot.UpdatedAt = now;
        }

        var message = isProtected ? "Protected from automatic removal by user." : "Protection removed by user.";
        db.HealthEvents.Add(new DownloadHealthEvent
        {
            DownloadAttempt = attempt,
            OccurredAt = now,
            EventType = HealthEventType.ManualOverride,
            Severity = Severity.Info,
            ReasonKey = isProtected ? "protected-by-user" : "unprotected-by-user",
            Message = message,
            ScoreBefore = attempt.HealthSnapshot?.RawScore ?? 0,
            ScoreAfter = attempt.HealthSnapshot?.RawScore ?? 0,
            ConfidenceBefore = attempt.HealthSnapshot?.Confidence ?? 0,
            ConfidenceAfter = attempt.HealthSnapshot?.Confidence ?? 0,
            RecoveryCreditBefore = attempt.HealthSnapshot?.RecoveryCredit ?? 0,
            RecoveryCreditAfter = attempt.HealthSnapshot?.RecoveryCredit ?? 0,
            Source = nameof(HealthService)
        });

        await db.SaveChangesAsync(cancellationToken);
        changeBus.NotifyChanged();
        return true;
    }

    public async Task SetScoreAsync(DownloadItem item, int score, int confidence,
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        var now = DateTime.UtcNow;
        var downloadId = GetDownloadId(item);

        var attempt = await db.DownloadAttempts.Include(e => e.HealthSnapshot).FirstOrDefaultAsync(
            e => e.ServerId == item.ServerId && e.DownloadId == downloadId && e.Status == AttemptStatus.Active,
            cancellationToken);

        if (attempt is null) return;

        var snapshot = attempt.HealthSnapshot ??
                       await _repository.GetOrCreateSnapshotAsync(db, attempt, now, cancellationToken);

        var clampedScore = Math.Clamp(score, 0, Policy.MaximumScore);
        var clampedConfidence = Math.Clamp(confidence, 0, 100);

        var scoreBefore = snapshot.RawScore;
        var confidenceBefore = snapshot.Confidence;

        snapshot.RawScore = clampedScore;
        snapshot.VisibleScore = clampedScore;
        snapshot.Confidence = clampedConfidence;
        snapshot.State = CreateEvaluator().DeriveStateFromScore(clampedScore);
        snapshot.LastReasonKey = "manual-override";
        snapshot.LastReasonMessage = $"Score manually set to {clampedScore} with confidence {clampedConfidence}.";
        snapshot.UpdatedAt = now;

        db.HealthEvents.Add(new DownloadHealthEvent
        {
            DownloadAttempt = attempt,
            OccurredAt = now,
            EventType = HealthEventType.ManualOverride,
            Severity = Severity.Info,
            ReasonKey = "manual-override",
            Message = snapshot.LastReasonMessage,
            ScoreBefore = scoreBefore,
            ScoreDelta = clampedScore - scoreBefore,
            ScoreAfter = clampedScore,
            ConfidenceBefore = confidenceBefore,
            ConfidenceDelta = clampedConfidence - confidenceBefore,
            ConfidenceAfter = clampedConfidence,
            RecoveryCreditBefore = snapshot.RecoveryCredit,
            RecoveryCreditDelta = 0,
            RecoveryCreditAfter = snapshot.RecoveryCredit,
            Source = nameof(HealthService)
        });

        await _repository.UpdateMediaSummaryAsync(db, attempt, null, now, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        changeBus.NotifyChanged();
    }

    #endregion

    #region Queries

    public async Task<DownloadAttempt?> GetAttemptAsync(int attemptId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        return await db.DownloadAttempts.AsNoTracking().Include(e => e.HealthSnapshot)
            .FirstOrDefaultAsync(e => e.Id == attemptId, cancellationToken);
    }

    public async Task<HealthSnapshot?> GetSnapshotAsync(int attemptId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        return await db.HealthSnapshots.AsNoTracking()
            .FirstOrDefaultAsync(e => e.DownloadAttemptId == attemptId, cancellationToken);
    }

    public async Task<List<DownloadHealthEvent>> GetEventsAsync(int attemptId, int take = 100,
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        return await db.HealthEvents.AsNoTracking().Where(e => e.DownloadAttemptId == attemptId)
            .OrderByDescending(e => e.OccurredAt).Take(take).ToListAsync(cancellationToken);
    }

    // Test-only (nothing in the app calls it); internal so it isn't part of the service's API.
    internal async Task<List<DownloadAttempt>> GetHistoryAsync(int take = 100,
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        return await db.DownloadAttempts.AsNoTracking().Include(e => e.HealthSnapshot)
            .OrderByDescending(e => e.LastSeenAt).Take(take).ToListAsync(cancellationToken);
    }

    // Test-only (nothing in the app calls it); internal so it isn't part of the service's API.
    internal async Task<List<DownloadAttempt>> GetActiveAttemptsAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        return await db.DownloadAttempts.AsNoTracking().Include(e => e.HealthSnapshot)
            .Where(e => e.Status == AttemptStatus.Active).OrderByDescending(e => e.LastSeenAt)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    ///     Scores every item that has no active attempt yet (first scan), once per download ID, and returns how
    ///     many it scored. Items already being tracked are left for the evaluation routine.
    /// </summary>
    public async Task<int> EvaluateNewItemsAsync(IReadOnlyCollection<DownloadItem> items,
        CancellationToken cancellationToken = default)
    {
        if (items.Count == 0) return 0;

        // Keyed by server too: attempts belong to one server, and two servers can share a download client.
        var tracked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var serverId in items.Select(i => i.ServerId).Distinct())
        foreach (var id in await GetActiveDownloadIdsAsync(serverId, cancellationToken))
            tracked.Add($"{serverId}|{id}");

        var evaluated = 0;
        foreach (var item in items)
        {
            // Add returns false for IDs already tracked or already scored in this batch (season packs share one).
            if (!tracked.Add($"{item.ServerId}|{GetDownloadId(item)}")) continue;
            await EvaluateAsync(item, cancellationToken);
            evaluated++;
        }

        return evaluated;
    }

    public async Task<List<string>> GetActiveDownloadIdsAsync(int serverId,
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        return await db.DownloadAttempts.AsNoTracking()
            .Where(e => e.ServerId == serverId && e.Status == AttemptStatus.Active).Select(e => e.DownloadId)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<DownloadHealthEvent>> GetEventsForItemAsync(DownloadItem item, int take = 100,
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        var downloadId = GetDownloadId(item);

        var attemptId = await db.DownloadAttempts.AsNoTracking()
            .Where(e => e.ServerId == item.ServerId && e.DownloadId == downloadId && e.Status == AttemptStatus.Active)
            .Select(e => (int?)e.Id).FirstOrDefaultAsync(cancellationToken);

        if (attemptId is null) return [];

        return await db.HealthEvents.AsNoTracking().Where(e => e.DownloadAttemptId == attemptId)
            .OrderByDescending(e => e.OccurredAt).Take(take).ToListAsync(cancellationToken);
    }

    /// <summary>
    ///     Health data for a whole page of queue items in one context and a handful of queries (P3), instead of
    ///     three methods per item with their own contexts. Keyed by <paramref name="keyOf" />. Semantics match
    ///     the per-item methods: the latest attempt's snapshot, the active attempt's latest event, and the movie's
    ///     or series' summary (the series roll-up for Sonarr items).
    /// </summary>
    public async
        Task<(Dictionary<string, HealthSnapshot> Snapshots, Dictionary<string, MediaHealthSummary> Summaries,
            Dictionary<string, DownloadHealthEvent> Events)> GetQueueHealthAsync(
            IReadOnlyCollection<DownloadItem> items,
            Func<DownloadItem, string> keyOf, CancellationToken cancellationToken = default)
    {
        var snapshots = new Dictionary<string, HealthSnapshot>();
        var summaries = new Dictionary<string, MediaHealthSummary>();
        var events = new Dictionary<string, DownloadHealthEvent>();
        if (items.Count == 0) return (snapshots, summaries, events);

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        var serverIds = items.Select(i => (int?)i.ServerId).Distinct().ToList();
        var downloadIds = items.Select(GetDownloadId).Distinct().ToList();

        var attempts = await db.DownloadAttempts.AsNoTracking().Include(e => e.HealthSnapshot)
            .Where(e => serverIds.Contains(e.ServerId) && downloadIds.Contains(e.DownloadId))
            .ToListAsync(cancellationToken);
        var attemptsByKey = attempts.ToLookup(e => (e.ServerId, e.DownloadId));

        var activeIds = attempts.Where(e => e.Status == AttemptStatus.Active).Select(e => e.Id).ToList();
        var latestEvents = await db.HealthEvents.AsNoTracking().Where(e => activeIds.Contains(e.DownloadAttemptId))
            .Where(e => e.Id == db.HealthEvents.Where(x => x.DownloadAttemptId == e.DownloadAttemptId)
                .OrderByDescending(x => x.OccurredAt).Select(x => x.Id).First())
            .ToDictionaryAsync(e => e.DownloadAttemptId, cancellationToken);

        var movieRemoteIds = items.Where(i => i.MovieId > 0).Select(i => i.MovieId).Distinct().ToList();
        var seriesRemoteIds = items.Where(i => i.MovieId <= 0 && i.SeriesId > 0).Select(i => i.SeriesId).Distinct()
            .ToList();

        var movieIds = await db.Movies.AsNoTracking()
            .Where(e => serverIds.Contains(e.ServerId) && movieRemoteIds.Contains(e.RemoteId))
            .Select(e => new { e.ServerId, e.RemoteId, e.Id }).ToListAsync(cancellationToken);
        var seriesIds = await db.Series.AsNoTracking()
            .Where(e => serverIds.Contains(e.ServerId) && seriesRemoteIds.Contains(e.RemoteId))
            .Select(e => new { e.ServerId, e.RemoteId, e.Id }).ToListAsync(cancellationToken);

        var localMovieIds = movieIds.Select(e => (int?)e.Id).ToList();
        var localSeriesIds = seriesIds.Select(e => (int?)e.Id).ToList();
        var movieSummaries = await db.MediaHealthSummaries.AsNoTracking()
            .Where(e => e.MediaType == TrackedMediaType.Movie && localMovieIds.Contains(e.MovieId))
            .ToListAsync(cancellationToken);
        var seriesSummaries = await db.MediaHealthSummaries.AsNoTracking()
            .Where(e => e.MediaType == TrackedMediaType.Series && localSeriesIds.Contains(e.SeriesId))
            .ToListAsync(cancellationToken);

        var movieIdByRemote =
            movieIds.GroupBy(e => (e.ServerId, e.RemoteId)).ToDictionary(g => g.Key, g => g.First().Id);
        var seriesIdByRemote =
            seriesIds.GroupBy(e => (e.ServerId, e.RemoteId)).ToDictionary(g => g.Key, g => g.First().Id);
        var movieSummaryById = movieSummaries.GroupBy(e => e.MovieId!.Value)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(e => e.CreatedAt).First());
        var seriesSummaryById = seriesSummaries.GroupBy(e => e.SeriesId!.Value)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(e => e.CreatedAt).First());

        foreach (var item in items)
        {
            var key = keyOf(item);
            var forItem = attemptsByKey[(item.ServerId, GetDownloadId(item))].ToList();

            var latestAttempt = forItem.OrderByDescending(e => e.LastSeenAt).FirstOrDefault();
            if (latestAttempt?.HealthSnapshot is { } snapshot)
            {
                snapshot.DownloadAttempt = latestAttempt; // so the page can show e.g. whether it's protected
                snapshots[key] = snapshot;
            }

            var active = forItem.FirstOrDefault(e => e.Status == AttemptStatus.Active);
            if (active is not null && latestEvents.TryGetValue(active.Id, out var latest))
                events[key] = latest;

            if (item.MovieId > 0)
            {
                if (movieIdByRemote.TryGetValue((item.ServerId, item.MovieId), out var movieId) &&
                    movieSummaryById.TryGetValue(movieId, out var summary))
                    summaries[key] = summary;
            }
            else if (item.SeriesId > 0 &&
                     seriesIdByRemote.TryGetValue((item.ServerId, item.SeriesId), out var seriesId) &&
                     seriesSummaryById.TryGetValue(seriesId, out var seriesSummary))
            {
                summaries[key] = seriesSummary;
            }
        }

        return (snapshots, summaries, events);
    }

    public async Task<HealthSnapshot?> GetMostRecentSnapshotAsync(DownloadItem item,
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        var downloadId = GetDownloadId(item);

        return await db.DownloadAttempts.AsNoTracking()
            .Where(e => e.ServerId == item.ServerId && e.DownloadId == downloadId).OrderByDescending(e => e.LastSeenAt)
            .Select(e => e.HealthSnapshot).FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<HealthSnapshot?> GetMostRecentSnapshotAsync(int serverId, int? movieRemoteId, int? seriesRemoteId,
        int? episodeRemoteId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        var local = await ResolveLocalIdsAsync(db, serverId, movieRemoteId, seriesRemoteId, episodeRemoteId,
            cancellationToken);
        var query = ApplyMediaAttemptFilter(db.DownloadAttempts.AsNoTracking().Where(e => e.ServerId == serverId),
            local.MovieId, local.SeriesId, local.EpisodeId);

        return await query.OrderByDescending(e => e.LastSeenAt).Select(e => e.HealthSnapshot)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public Task<List<DownloadHealthEvent>> GetEventsForMediaAsync(int serverId, int? movieRemoteId, int? seriesRemoteId,
        int? episodeRemoteId)
    {
        return GetEventsForMediaAsync(serverId, movieRemoteId, seriesRemoteId, episodeRemoteId, CancellationToken.None);
    }

    public async Task<List<DownloadHealthEvent>> GetEventsForMediaAsync(int serverId, int? movieRemoteId,
        int? seriesRemoteId, int? episodeRemoteId, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        var local = await ResolveLocalIdsAsync(db, serverId, movieRemoteId, seriesRemoteId, episodeRemoteId,
            cancellationToken);
        var query = ApplyMediaAttemptFilter(db.DownloadAttempts.AsNoTracking().Where(e => e.ServerId == serverId),
            local.MovieId, local.SeriesId, local.EpisodeId);

        return await query.SelectMany(e => e.HealthEvents).OrderByDescending(e => e.OccurredAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<DownloadAttempt>> GetDownloadAttemptsAsync(int serverId, int? movieRemoteId,
        int? seriesRemoteId, int? episodeRemoteId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        var local = await ResolveLocalIdsAsync(db, serverId, movieRemoteId, seriesRemoteId, episodeRemoteId,
            cancellationToken);
        var query = ApplyMediaAttemptFilter(db.DownloadAttempts.AsNoTracking().Where(e => e.ServerId == serverId),
            local.MovieId, local.SeriesId, local.EpisodeId);

        return await query.OrderByDescending(e => e.FirstSeenAt).ToListAsync(cancellationToken);
    }

    private async Task<(int? MovieId, int? SeriesId, int? EpisodeId)> ResolveLocalIdsAsync(AppDbContext db,
        int serverId, int? movieRemoteId, int? seriesRemoteId, int? episodeRemoteId, CancellationToken ct)
    {
        var movieId = movieRemoteId is > 0
            ? await db.Movies.AsNoTracking().Where(m => m.ServerId == serverId && m.RemoteId == movieRemoteId)
                .Select(m => (int?)m.Id).FirstOrDefaultAsync(ct)
            : null;
        var seriesId = seriesRemoteId is > 0
            ? await db.Series.AsNoTracking().Where(s => s.ServerId == serverId && s.RemoteId == seriesRemoteId)
                .Select(s => (int?)s.Id).FirstOrDefaultAsync(ct)
            : null;
        var episodeId = episodeRemoteId is > 0
            ? await db.Episodes.AsNoTracking().Where(e => e.ServerId == serverId && e.RemoteId == episodeRemoteId)
                .Select(e => (int?)e.Id).FirstOrDefaultAsync(ct)
            : null;
        return (movieId, seriesId, episodeId);
    }

    private static IQueryable<DownloadAttempt> ApplyMediaAttemptFilter(IQueryable<DownloadAttempt> query, int? movieId,
        int? seriesId, int? episodeId)
    {
        if (episodeId is > 0)
            return query.Where(e => e.EpisodeId == episodeId.Value);

        if (movieId is > 0)
            return query.Where(e => e.MovieId == movieId.Value);

        if (seriesId is > 0)
            return query.Where(e => e.SeriesId == seriesId.Value);

        return query.Where(e => false);
    }

    public async Task<MediaHealthSummary?> GetMediaSummary(int serverId, int? movieRemoteId, int? seriesRemoteId,
        int? episodeRemoteId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        if (episodeRemoteId is > 0)
        {
            var episode = await db.Episodes.AsNoTracking()
                .Where(e => e.ServerId == serverId && e.RemoteId == episodeRemoteId.Value)
                .Select(e => new { e.Id, e.RemoteSeriesId }).FirstOrDefaultAsync(cancellationToken);

            if (episode != null)
            {
                var summary = await db.MediaHealthSummaries.AsNoTracking().Where(e => e.EpisodeId == episode.Id)
                    .OrderByDescending(e => e.CreatedAt).FirstOrDefaultAsync(cancellationToken);

                if (summary != null)
                    return summary;

                seriesRemoteId ??= episode.RemoteSeriesId;
            }
        }

        if (movieRemoteId is > 0)
        {
            var movieId = await db.Movies.AsNoTracking()
                .Where(e => e.ServerId == serverId && e.RemoteId == movieRemoteId.Value).Select(e => e.Id)
                .FirstOrDefaultAsync(cancellationToken);

            if (movieId > 0)
            {
                return await db.MediaHealthSummaries.AsNoTracking().Where(e => e.MovieId == movieId)
                    .OrderByDescending(e => e.CreatedAt).FirstOrDefaultAsync(cancellationToken);
            }
        }

        if (seriesRemoteId is not > 0) return null;
        {
            var seriesId = await db.Series.AsNoTracking()
                .Where(e => e.ServerId == serverId && e.RemoteId == seriesRemoteId.Value).Select(e => e.Id)
                .FirstOrDefaultAsync(cancellationToken);

            if (seriesId > 0)
            {
                return await db.MediaHealthSummaries.AsNoTracking().Where(e =>
                        e.MediaType == TrackedMediaType.Series && e.SeriesId == seriesId)
                    .OrderByDescending(e => e.CreatedAt).FirstOrDefaultAsync(cancellationToken);
            }
        }

        return null;
    }

    public async Task<DownloadHealthEvent?> GetMostRecentHealthEventAsync(DownloadItem item,
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        var downloadId = GetDownloadId(item);

        var attemptId = await db.DownloadAttempts.AsNoTracking()
            .Where(e => e.ServerId == item.ServerId && e.DownloadId == downloadId && e.Status == AttemptStatus.Active)
            .Select(e => (int?)e.Id).FirstOrDefaultAsync(cancellationToken);

        if (attemptId is null) return null;

        return await db.HealthEvents.AsNoTracking().Where(e => e.DownloadAttemptId == attemptId)
            .OrderByDescending(e => e.OccurredAt).FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<MediaHealthSummary?> GetMediaSummary(DownloadItem item,
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        var remoteMovieId = item.MovieId;
        if (remoteMovieId > 0)
        {
            var movieId = await db.Movies.AsNoTracking()
                .Where(e => e.ServerId == item.ServerId && e.RemoteId == remoteMovieId).Select(e => e.Id)
                .FirstOrDefaultAsync(cancellationToken);

            if (movieId <= 0) return null;

            return await db.MediaHealthSummaries.AsNoTracking().Where(e => e.MovieId == movieId)
                .OrderByDescending(e => e.CreatedAt).FirstOrDefaultAsync(cancellationToken);
        }

        var remoteSeriesId = item.SeriesId;
        if (remoteSeriesId > 0)
        {
            var seriesId = await db.Series.AsNoTracking()
                .Where(e => e.ServerId == item.ServerId && e.RemoteId == remoteSeriesId).Select(e => e.Id)
                .FirstOrDefaultAsync(cancellationToken);
            if (seriesId <= 0) return null;

            return await db.MediaHealthSummaries.AsNoTracking()
                .Where(e => e.MediaType == TrackedMediaType.Series && e.SeriesId == seriesId)
                .OrderByDescending(e => e.CreatedAt).FirstOrDefaultAsync(cancellationToken);
        }

        return null;
    }

    public async Task<List<DownloadAttempt>> GetDownloadAttemptsAsync(DownloadItem item,
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        int? remoteMovieId = item.MovieId > 0 ? item.MovieId : null;
        int? remoteSeriesId = item.SeriesId > 0 ? item.SeriesId : null;
        int? remoteEpisodeId = item.EpisodeId > 0 ? item.EpisodeId : null;
        var downloadId = string.IsNullOrWhiteSpace(item.DownloadId) ? null : item.DownloadId;

        int? movieId = null;
        int? seriesId = null;
        int? episodeId = null;

        if (remoteMovieId > 0)
        {
            movieId = await db.Movies.AsNoTracking()
                .Where(x => x.ServerId == item.ServerId && x.RemoteId == remoteMovieId).Select(x => (int?)x.Id)
                .FirstOrDefaultAsync(cancellationToken);
        }

        if (remoteSeriesId > 0)
        {
            seriesId = await db.Series.AsNoTracking()
                .Where(x => x.ServerId == item.ServerId && x.RemoteId == remoteSeriesId).Select(x => (int?)x.Id)
                .FirstOrDefaultAsync(cancellationToken);
        }

        if (remoteEpisodeId > 0)
        {
            episodeId = await db.Episodes.AsNoTracking()
                .Where(x => x.ServerId == item.ServerId && x.RemoteId == remoteEpisodeId).Select(x => (int?)x.Id)
                .FirstOrDefaultAsync(cancellationToken);
        }

        return await db.DownloadAttempts.AsNoTracking().Where(x =>
                x.ServerId == item.ServerId && ((movieId != null && x.MovieId == movieId) ||
                                                (episodeId != null && x.EpisodeId == episodeId) ||
                                                (episodeId == null && seriesId != null && x.SeriesId == seriesId) ||
                                                (downloadId != null && x.DownloadId == downloadId)))
            .OrderByDescending(x => x.FirstSeenAt).ToListAsync(cancellationToken);
    }

    #endregion

    #region Helpers

    public static string GetDownloadId(DownloadItem item)
    {
        if (!string.IsNullOrWhiteSpace(item.DownloadId)) return item.DownloadId.Trim();
        return $"{item.ServerId}:{item.RemoteId}:{HealthEvaluator.NormalizeTitle(item.Title)}";
    }

    public async Task<List<string>> GetMissingDownloadIdsAsync(int serverId, IEnumerable<string> activeDownloadIds,
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        var active = activeDownloadIds.Where(e => !string.IsNullOrWhiteSpace(e)).Select(e => e.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var candidates = await db.DownloadAttempts.AsNoTracking()
            .Where(e => e.ServerId == serverId && e.Status == AttemptStatus.Active).Select(e => e.DownloadId)
            .ToListAsync(cancellationToken);

        return candidates.Where(e => !active.Contains(e)).ToList();
    }

    #endregion
}
