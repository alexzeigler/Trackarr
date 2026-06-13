using Microsoft.EntityFrameworkCore;
using Trackarr.Core.Queue;
using Trackarr.Data;

namespace Trackarr.Services.Strikes;

public sealed class DownloadStrikeService(AppDbContext db) {
    public async Task<DownloadStrike> AddStrikeAsync(
        int downloadItemId,
        DownloadStrikeReason reason,
        DownloadStrikeSeverity severity,
        string? message = null,
        string? fingerprint = null,
        bool countsAgainstDownload = true,
        DownloadStrikeObservation? observation = null,
        CancellationToken cancellationToken = default) {
        DownloadItem downloadItem = await db.DownloadQueue.FirstOrDefaultAsync(x => x.Id == downloadItemId, cancellationToken) ?? throw new InvalidOperationException($"Download item {downloadItemId} was not found.");

        string fingerprintValue = NormalizeFingerprint(reason, fingerprint);

        DownloadStrike? existingStrike = await db.DownloadStrikes.FirstOrDefaultAsync(x => x.DownloadItemId == downloadItemId && x.Reason == reason && x.Fingerprint == fingerprintValue && x.ResolvedAtUtc == null, cancellationToken);

        if (existingStrike is not null) {
            await RefreshStrikeCountsAsync(downloadItemId, cancellationToken);
            return existingStrike;
        }

        DownloadStrike strike = new() {
            DownloadItemId = downloadItem.Id,
            Reason = reason,
            Severity = severity,
            Message = message,
            Fingerprint = fingerprintValue,
            CountsAgainstDownload = countsAgainstDownload,
            ObservedStatus = observation?.Status,
            ObservedErrorMessage = observation?.ErrorMessage,
            ObservedProgress = observation?.Progress,
            ObservedSize = observation?.Size,
            ObservedSizeLeft = observation?.SizeLeft,
            ObservedDownloadId = observation?.DownloadId,
            ObservedDownloadClient = observation?.DownloadClient,
            ObservedIndexer = observation?.Indexer,
            CreatedAtUtc = DateTime.UtcNow
        };

        db.DownloadStrikes.Add(strike);

        await db.SaveChangesAsync(cancellationToken);
        await RefreshStrikeCountsAsync(downloadItemId, cancellationToken);

        return strike;
    }

    public async Task<bool> ResolveStrikeAsync(int strikeId, string? resolution = null, CancellationToken cancellationToken = default) {
        DownloadStrike? strike = await db.DownloadStrikes.FirstOrDefaultAsync(x => x.Id == strikeId, cancellationToken);

        if (strike is null || strike.ResolvedAtUtc is not null) {
            return false;
        }

        strike.ResolvedAtUtc = DateTime.UtcNow;
        strike.Resolution = resolution;

        await db.SaveChangesAsync(cancellationToken);
        await RefreshStrikeCountsAsync(strike.DownloadItemId, cancellationToken);

        return true;
    }

    public async Task<int> ResolveActiveStrikesAsync(int downloadItemId, string? resolution = null, DownloadStrikeReason? reason = null, CancellationToken cancellationToken = default) {
        IQueryable<DownloadStrike> query = db.DownloadStrikes.Where(x => x.DownloadItemId == downloadItemId).Where(x => x.ResolvedAtUtc == null);

        if (reason is not null) {
            query = query.Where(x => x.Reason == reason.Value);
        }

        List<DownloadStrike> strikes = await query.ToListAsync(cancellationToken);

        if (strikes.Count == 0) {
            return 0;
        }

        DateTime now = DateTime.UtcNow;

        foreach (DownloadStrike strike in strikes) {
            strike.ResolvedAtUtc = now;
            strike.Resolution = resolution;
        }

        await db.SaveChangesAsync(cancellationToken);
        await RefreshStrikeCountsAsync(downloadItemId, cancellationToken);

        return strikes.Count;
    }

    public async Task<bool> HasActiveStrikeAsync(int downloadItemId, DownloadStrikeReason reason, string? fingerprint = null, CancellationToken cancellationToken = default) {
        string fingerprintValue = NormalizeFingerprint(reason, fingerprint);

        return await db.DownloadStrikes.AnyAsync(x => x.DownloadItemId == downloadItemId && x.Reason == reason && x.Fingerprint == fingerprintValue && x.ResolvedAtUtc == null, cancellationToken);
    }

    public async Task<bool> RefreshStrikeCountsAsync(int downloadItemId, CancellationToken cancellationToken = default) {
        DownloadItem? downloadItem = await db.DownloadQueue.FirstOrDefaultAsync(x => x.Id == downloadItemId, cancellationToken);

        if (downloadItem is null) {
            return false;
        }

        int activeStrikeCount = await db.DownloadStrikes.CountAsync(x => x.DownloadItemId == downloadItemId && x.CountsAgainstDownload && x.ResolvedAtUtc == null, cancellationToken);

        int totalStrikeCount = await db.DownloadStrikes.CountAsync(x => x.DownloadItemId == downloadItemId && x.CountsAgainstDownload, cancellationToken);

        downloadItem.ActiveStrikeCount = activeStrikeCount;
        downloadItem.TotalStrikeCount = totalStrikeCount;

        if (CanUpdateTrackingState(downloadItem.TrackingState)) {
            downloadItem.TrackingState = activeStrikeCount > 0 ? DownloadItemTrackingState.Struck : DownloadItemTrackingState.Active;
        }

        await db.SaveChangesAsync(cancellationToken);

        return true;
    }

    private static string NormalizeFingerprint(DownloadStrikeReason reason, string? fingerprint) {
        if (!string.IsNullOrWhiteSpace(fingerprint)) {
            return fingerprint.Trim().ToLowerInvariant();
        }

        return reason.ToString().ToLowerInvariant();
    }

    private static bool CanUpdateTrackingState(DownloadItemTrackingState state) => state is DownloadItemTrackingState.Active or DownloadItemTrackingState.Watching or DownloadItemTrackingState.Struck;
}