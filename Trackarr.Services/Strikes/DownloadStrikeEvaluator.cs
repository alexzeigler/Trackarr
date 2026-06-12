using Microsoft.EntityFrameworkCore;
using Trackarr.Core.Queue;
using Trackarr.Data;

namespace Trackarr.Services.Strikes;

public sealed class DownloadStrikeEvaluator(AppDbContext db, DownloadStrikeService strikeService, DownloadStrikePolicy policy) {
    public async Task EvaluateAsync(int downloadItemId, CancellationToken cancellationToken = default) {
        DownloadItem? downloadItem = await db.DownloadQueue.FirstOrDefaultAsync(x => x.Id == downloadItemId, cancellationToken);

        if (downloadItem is null) {
            return;
        }

        await EvaluateAsync(downloadItem, cancellationToken);
    }

    public async Task EvaluateAsync(DownloadItem downloadItem, CancellationToken cancellationToken = default) {
        DateTime now = DateTime.UtcNow;

        downloadItem.FirstSeenAtUtc ??= now;
        downloadItem.LastSeenAtUtc = now;

        decimal? currentProgress = GetProgress(downloadItem);
        long? currentSizeLeft = GetSizeLeft(downloadItem);

        bool progressed = HasProgressed(downloadItem, currentProgress, currentSizeLeft);

        if (downloadItem.LastProgressAtUtc is null) {
            downloadItem.LastProgressAtUtc = now;
        }

        if (progressed) {
            downloadItem.LastProgressAtUtc = now;

            await strikeService.ResolveActiveStrikesAsync(downloadItem.Id, "Download started progressing again.", DownloadStrikeReason.NoProgress, cancellationToken);
        }

        downloadItem.LastObservedProgress = currentProgress;
        downloadItem.LastObservedSizeLeft = currentSizeLeft;

        if (IsCompleted(downloadItem)) {
            downloadItem.TrackingState = DownloadItemTrackingState.Completed;
            downloadItem.CompletedAtUtc ??= now;

            await strikeService.ResolveActiveStrikesAsync(downloadItem.Id, "Download completed.", null, cancellationToken);

            await db.SaveChangesAsync(cancellationToken);
            return;
        }

        if (HasError(downloadItem)) {
            await AddDownloadClientErrorStrikeAsync(downloadItem, cancellationToken);
        }

        if (ShouldEvaluateNoProgress(downloadItem)) {
            await AddNoProgressStrikesAsync(downloadItem, now, cancellationToken);
        }

        await strikeService.RefreshStrikeCountsAsync(downloadItem.Id, cancellationToken);

        if (downloadItem.ActiveStrikeCount >= policy.MaxActiveStrikesBeforeFailed) {
            downloadItem.TrackingState = DownloadItemTrackingState.Failed;
            downloadItem.FailedAtUtc ??= now;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task AddDownloadClientErrorStrikeAsync(DownloadItem downloadItem, CancellationToken cancellationToken) {
        string fingerprint = $"download-client-error:{Normalize(downloadItem.ErrorMessage ?? downloadItem.Status)}";

        await strikeService.AddStrikeAsync(downloadItem.Id,
        DownloadStrikeReason.DownloadClientError,
        DownloadStrikeSeverity.Strike,
        downloadItem.ErrorMessage ?? "Download client reported an error.",
        fingerprint,
        true,
        CreateObservation(downloadItem),
        cancellationToken);
    }

    private async Task AddNoProgressStrikesAsync(DownloadItem downloadItem, DateTime now, CancellationToken cancellationToken) {
        DateTime lastProgressAt = downloadItem.LastProgressAtUtc ?? downloadItem.FirstSeenAtUtc ?? now;

        TimeSpan elapsed = now - lastProgressAt;

        if (elapsed < policy.NoProgressGracePeriod) {
            return;
        }

        int strikeCount = (int)Math.Floor(elapsed.TotalMinutes / policy.NoProgressStrikeInterval.TotalMinutes);
        strikeCount = Math.Clamp(strikeCount, 1, policy.MaxNoProgressStrikes);

        for (int strikeNumber = 1; strikeNumber <= strikeCount; strikeNumber++) {
            string fingerprint = $"no-progress:{strikeNumber}";

            await strikeService.AddStrikeAsync(downloadItem.Id,
            DownloadStrikeReason.NoProgress,
            DownloadStrikeSeverity.Strike,
            $"No progress detected for {elapsed.TotalMinutes:0} minutes.",
            fingerprint,
            true,
            CreateObservation(downloadItem),
            cancellationToken);
        }
    }

    private static DownloadStrikeObservation CreateObservation(DownloadItem downloadItem) {
        return new DownloadStrikeObservation {
            Status = downloadItem.Status,
            ErrorMessage = downloadItem.ErrorMessage,
            Progress = GetProgress(downloadItem),
            Size = downloadItem.Size,
            SizeLeft = GetSizeLeft(downloadItem),
            DownloadId = downloadItem.DownloadId,
            DownloadClient = downloadItem.DownloadClient,
            Indexer = downloadItem.Indexer
        };
    }

    private static bool HasProgressed(DownloadItem downloadItem, decimal? currentProgress, long? currentSizeLeft) {
        if (currentProgress is not null && downloadItem.LastObservedProgress is not null && currentProgress > downloadItem.LastObservedProgress) {
            return true;
        }

        if (currentSizeLeft is not null && downloadItem.LastObservedSizeLeft is not null && currentSizeLeft < downloadItem.LastObservedSizeLeft) {
            return true;
        }

        return false;
    }

    private static decimal? GetProgress(DownloadItem downloadItem) {
        long? sizeLeft = GetSizeLeft(downloadItem);

        if (downloadItem.Size <= 0 || sizeLeft is null) {
            return null;
        }

        decimal downloaded = downloadItem.Size - sizeLeft.Value;
        decimal progress = downloaded / downloadItem.Size * 100;

        return Math.Clamp(progress, 0, 100);
    }

    private static long? GetSizeLeft(DownloadItem downloadItem) {
        return downloadItem.SizeLeft;
    }

    private static bool ShouldEvaluateNoProgress(DownloadItem downloadItem) {
        if (downloadItem.TrackingState is DownloadItemTrackingState.Completed or DownloadItemTrackingState.Removed) {
            return false;
        }

        if (IsCompleted(downloadItem)) {
            return false;
        }

        return downloadItem.Size > 0;
    }

    private static bool HasError(DownloadItem downloadItem) {
        if (!string.IsNullOrWhiteSpace(downloadItem.ErrorMessage)) {
            return true;
        }

        string status = downloadItem.Status ?? string.Empty;

        return status.Contains("error", StringComparison.OrdinalIgnoreCase) || status.Contains("failed", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsCompleted(DownloadItem downloadItem) {
        string status = downloadItem.Status ?? string.Empty;

        return status.Contains("completed", StringComparison.OrdinalIgnoreCase) || status.Contains("imported", StringComparison.OrdinalIgnoreCase);
    }

    private static string Normalize(string? value) {
        if (string.IsNullOrWhiteSpace(value)) {
            return "unknown";
        }

        return value.Trim().ToLowerInvariant();
    }
}