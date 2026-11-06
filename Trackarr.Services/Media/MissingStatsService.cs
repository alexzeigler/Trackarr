using Microsoft.EntityFrameworkCore;
using Trackarr.Core.Media.Missing;
using Trackarr.Core.Queue;
using Trackarr.Data;

namespace Trackarr.Services.Media;

public sealed class MissingStatsService(IDbContextFactory<AppDbContext> dbFactory)
{
    public MissingPageData BuildStats(List<MissingMediaRecord> items)
    {
        var data = new MissingPageData();

        data.Stats = new MissingStats
        {
            TotalMissing = items.Count,
            SearchReady = items.Count(IsSearchReady),
            FailedSearches = items.Count(x => x.FailedSearchCount > 0),
            Abandoned = items.Count(x => x.SearchState == MissingSearchState.Abandoned)
        };

        data.StateBreakdown = BuildStateBreakdown(items);
        return data;
    }

    public async Task<List<MissingActivityPoint>> GetActivityAsync(int hours = 24,
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        var now = DateTime.UtcNow;
        var from = now.AddHours(-hours);

        var events = await db.SearchEvents.AsNoTracking().Where(e => e.OccurredAt >= from)
            .ToListAsync(cancellationToken);

        var points = new List<MissingActivityPoint>();

        for (var h = 0; h < hours; h++)
        {
            var bucketStart = from.AddHours(h);
            var bucketEnd = bucketStart.AddHours(1);

            var bucket = events.Where(e => e.OccurredAt >= bucketStart && e.OccurredAt < bucketEnd).ToList();

            points.Add(new MissingActivityPoint
            {
                Label = bucketStart.ToLocalTime().ToString("h tt").ToLower(),
                Pushed = bucket.Count(e => e.EventType == MissingEventType.SearchPushed),
                Cascaded = bucket.Count(e => e.EventType == MissingEventType.CascadedToEpisode),
                Resolved = bucket.Count(e => e.EventType == MissingEventType.Resolved),
                Failed = bucket.Count(e => e.EventType == MissingEventType.SearchFailed),
                Abandoned = bucket.Count(e => e.EventType == MissingEventType.Abandoned)
            });
        }

        return points;
    }

    public void ComputeTrends(MissingStats stats, List<MissingActivityPoint> activity)
    {
        if (activity.Count < 2)
        {
            stats.SearchTrend = Trend.Flat;
            stats.ResolvedTrend = Trend.Flat;
            return;
        }

        var half = activity.Count / 2;
        var earlier = activity.Take(half).ToList();
        var recent = activity.Skip(half).ToList();

        stats.SearchesPushed24H = activity.Sum(p => p.Pushed);
        stats.Resolved24H = activity.Sum(p => p.Resolved);

        var earlierPushed = earlier.Sum(p => p.Pushed);
        var recentPushed = recent.Sum(p => p.Pushed);
        stats.SearchTrend = ComputeTrend(recentPushed, earlierPushed);

        var earlierResolved = earlier.Sum(p => p.Resolved);
        var recentResolved = recent.Sum(p => p.Resolved);
        stats.ResolvedTrend = ComputeTrend(recentResolved, earlierResolved);
    }

    private static Trend ComputeTrend(int recent, int earlier)
    {
        if (recent == earlier) return Trend.Flat;
        var threshold = Math.Max(1, earlier * 0.15);
        if (recent > earlier + threshold) return Trend.Up;
        if (recent < earlier - threshold) return Trend.Down;
        return Trend.Flat;
    }

    private static bool IsSearchReady(MissingMediaRecord item)
    {
        return item.LastSearchPushedAt == null ||
               item.SearchState is MissingSearchState.New or MissingSearchState.InQueue;
    }

    private static List<MissingStateBreakdown> BuildStateBreakdown(List<MissingMediaRecord> items)
    {
        if (items.Count == 0) return [];

        return items.GroupBy(i => DisplayState(i.SearchState))
            .Select(g => new MissingStateBreakdown
            {
                Label = g.Key, Count = g.Count(), Percent = Math.Round(g.Count() / (decimal)items.Count * 100, 1)
            }).OrderByDescending(b => b.Count).ToList();
    }

    private static string DisplayState(MissingSearchState state)
    {
        return state switch
        {
            MissingSearchState.New => "New",
            MissingSearchState.InQueue => "In Queue",
            MissingSearchState.SearchPushed => "Search Pushed",
            MissingSearchState.SkippedCooldown => "Cooldown",
            MissingSearchState.SkippedSearching => "Skipped",
            MissingSearchState.NotAvailable => "Not Available",
            MissingSearchState.Abandoned => "Abandoned",
            MissingSearchState.Waiting => "Waiting",
            _ => state.ToString()
        };
    }
}
