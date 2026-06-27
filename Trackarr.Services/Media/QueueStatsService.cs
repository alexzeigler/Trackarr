using Microsoft.EntityFrameworkCore;
using Trackarr.Core.Health;
using Trackarr.Core.Queue;
using Trackarr.Data;

namespace Trackarr.Services.Media;

public sealed class QueueStatsService(IDbContextFactory<AppDbContext> dbFactory)
{
    public QueuePageData BuildStats(List<DownloadItem> downloads, Dictionary<string, HealthSnapshot> snapshots,
        Dictionary<string, MediaHealthSummary> summaries, Func<DownloadItem, string> keyFunc)
    {
        var data = new QueuePageData();

        var scores = downloads.Select(d => snapshots.GetValueOrDefault(keyFunc(d))).Where(s => s != null)
            .Select(s => s!.VisibleScore).ToList();

        var downloading = downloads.Count(d =>
            d.DownloadPercent is > 0 and < 100 ||
            d.Status?.Contains("download", StringComparison.OrdinalIgnoreCase) == true);

        var atRisk = downloads.Count(d =>
        {
            var snap = snapshots.GetValueOrDefault(keyFunc(d));
            if (snap is { VisibleScore: <= 39 }) return true;
            if (snap is { IsEligibleForRemoval: true }) return true;

            var status = d.Status?.ToLowerInvariant() ?? "";
            return status.Contains("stuck") || status.Contains("stalled") || status.Contains("failed") ||
                   status.Contains("error") || status.Contains("warning");
        });

        var oldestAdded = downloads.Where(d => d.Added.HasValue).Select(d => d.Added!.Value).OrderBy(d => d)
            .FirstOrDefault();

        data.Stats = new QueueStats
        {
            TotalInQueue = downloads.Count,
            Downloading = downloading,
            AtRisk = atRisk,
            AvgHealthScore = scores.Count > 0 ? (int)scores.Average() : 0,
            DataRemaining = downloads.Sum(d => d.SizeLeft),
            OldestInQueue = oldestAdded != default ? DateTime.UtcNow - oldestAdded.ToUniversalTime() : null
        };

        data.StatusBreakdown = BuildStatusBreakdown(downloads);
        data.ScoreDistribution = BuildScoreDistribution(scores);

        return data;
    }

    public async Task<List<QueueActivityPoint>> GetActivityAsync(int hours = 24,
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        var now = DateTime.UtcNow;
        var from = now.AddHours(-hours);

        var attempts = await db.DownloadAttempts.AsNoTracking().Where(a =>
                a.FirstSeenAt >= from || (a.CompletedAt.HasValue && a.CompletedAt.Value >= from) ||
                (a.FailedAt.HasValue && a.FailedAt.Value >= from) ||
                (a.RemovedAt.HasValue && a.RemovedAt.Value >= from))
            .ToListAsync(cancellationToken);

        var points = new List<QueueActivityPoint>();

        for (var h = 0; h < hours; h++)
        {
            var bucketStart = from.AddHours(h);
            var bucketEnd = bucketStart.AddHours(1);

            points.Add(new QueueActivityPoint
            {
                Label = bucketStart.ToLocalTime().ToString("h tt").ToLower(),
                Added = attempts.Count(a => a.FirstSeenAt >= bucketStart && a.FirstSeenAt < bucketEnd),
                Completed =
                    attempts.Count(a =>
                        a.CompletedAt.HasValue && a.CompletedAt.Value >= bucketStart &&
                        a.CompletedAt.Value < bucketEnd),
                Failed = attempts.Count(a =>
                    a.FailedAt.HasValue && a.FailedAt.Value >= bucketStart && a.FailedAt.Value < bucketEnd),
                Removed = attempts.Count(a =>
                    a.RemovedAt.HasValue && a.RemovedAt.Value >= bucketStart && a.RemovedAt.Value < bucketEnd)
            });
        }

        return points;
    }

    public async Task ComputeTrendsAsync(QueueStats stats, List<QueueActivityPoint> activity,
        CancellationToken cancellationToken = default)
    {
        if (activity.Count < 2)
        {
            stats.ActiveTrend = Trend.Flat;
            stats.AtRiskTrend = Trend.Flat;
            stats.ScoreTrend = Trend.Flat;
            return;
        }

        var half = activity.Count / 2;
        var earlier = activity.Take(half).ToList();
        var recent = activity.Skip(half).ToList();

        var earlierAdded = earlier.Sum(p => p.Added + p.Completed);
        var recentAdded = recent.Sum(p => p.Added + p.Completed);
        stats.ActiveTrend = ComputeTrend(recentAdded, earlierAdded);

        var earlierBad = earlier.Sum(p => p.Failed + p.Removed);
        var recentBad = recent.Sum(p => p.Failed + p.Removed);
        stats.AtRiskTrend = ComputeTrend(recentBad, earlierBad);

        stats.ScoreTrend = await ComputeScoreTrendAsync(cancellationToken);
    }

    private async Task<Trend> ComputeScoreTrendAsync(CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        var now = DateTime.UtcNow;
        var midpoint = now.AddHours(-12);
        var from = now.AddHours(-24);

        var recentAvg = await db.HealthEvents.AsNoTracking().Where(e => e.OccurredAt >= midpoint && e.OccurredAt < now)
            .Where(e => e.ScoreDelta != 0).Select(e => (decimal?)e.ScoreDelta).AverageAsync(cancellationToken);

        var earlierAvg = await db.HealthEvents.AsNoTracking()
            .Where(e => e.OccurredAt >= from && e.OccurredAt < midpoint).Where(e => e.ScoreDelta != 0)
            .Select(e => (decimal?)e.ScoreDelta).AverageAsync(cancellationToken);

        var recentVal = recentAvg ?? 0m;
        var earlierVal = earlierAvg ?? 0m;

        if (recentVal > earlierVal + 0.5m) return Trend.Up;
        if (recentVal < earlierVal - 0.5m) return Trend.Down;
        return Trend.Flat;
    }

    private static Trend ComputeTrend(int recent, int earlier)
    {
        if (recent == earlier) return Trend.Flat;
        var threshold = Math.Max(1, earlier * 0.15);
        if (recent > earlier + threshold) return Trend.Up;
        if (recent < earlier - threshold) return Trend.Down;
        return Trend.Flat;
    }

    private static List<QueueStatusBreakdown> BuildStatusBreakdown(List<DownloadItem> downloads)
    {
        if (downloads.Count == 0) return [];

        var buckets = downloads.GroupBy(d => GetStatusBucket(d.Status)).Where(g => !string.IsNullOrEmpty(g.Key))
            .Select(g => new QueueStatusBreakdown
            {
                Label = g.Key,
                Count = g.Count(),
                Percent = Math.Round(g.Count() / (decimal)downloads.Count * 100, 1)
            }).OrderByDescending(b => b.Count).ToList();

        return buckets;
    }

    private static string GetStatusBucket(string? status)
    {
        if (string.IsNullOrWhiteSpace(status)) return "Unknown";

        var lower = status.ToLowerInvariant();

        if (lower.Contains("download") && !lower.Contains("metadata")) return "Downloading";
        if (lower.Contains("import")) return "Importing";
        if (lower.Contains("stall") || lower.Contains("stuck")) return "Stalled";
        if (lower.Contains("fail") || lower.Contains("error")) return "Failed";
        if (lower.Contains("warning")) return "Warning";
        if (lower.Contains("wait") || lower.Contains("queue") || lower.Contains("pending") || lower.Contains("delay"))
            return "Queued";
        if (lower.Contains("pause")) return "Paused";
        if (lower.Contains("metadata")) return "Fetching Metadata";

        return "Other";
    }

    private static List<QueueScoreBucket> BuildScoreDistribution(List<int> scores)
    {
        return
        [
            new QueueScoreBucket { Label = "Critical (0-39)", Count = scores.Count(s => s <= 39) },
            new QueueScoreBucket { Label = "Poor (40-59)", Count = scores.Count(s => s is >= 40 and <= 59) },
            new QueueScoreBucket { Label = "Fair (60-79)", Count = scores.Count(s => s is >= 60 and <= 79) },
            new QueueScoreBucket { Label = "Good (80-89)", Count = scores.Count(s => s is >= 80 and <= 89) },
            new QueueScoreBucket { Label = "Healthy (90+)", Count = scores.Count(s => s >= 90) }
        ];
    }
}
