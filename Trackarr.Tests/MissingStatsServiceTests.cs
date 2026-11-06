using Trackarr.Core.Media.Missing;
using Trackarr.Core.Queue;
using Trackarr.Services.Media;

namespace Trackarr.Tests;

public class MissingStatsServiceTests : IDisposable
{
    private readonly SqliteInMemoryFactory _factory = new();
    private readonly MissingStatsService _sut;

    public MissingStatsServiceTests()
    {
        _sut = new MissingStatsService(_factory);
    }

    public void Dispose() => _factory.Dispose();

    #region BuildStats — basic counts

    [Fact]
    public void BuildStats_EmptyList_ReturnsZeroCounts()
    {
        var result = _sut.BuildStats([]);

        Assert.Equal(0, result.Stats.TotalMissing);
        Assert.Equal(0, result.Stats.SearchReady);
        Assert.Equal(0, result.Stats.FailedSearches);
        Assert.Equal(0, result.Stats.Abandoned);
        Assert.Empty(result.StateBreakdown);
    }

    [Fact]
    public void BuildStats_TotalMissing_EqualsItemCount()
    {
        var items = new List<MissingMediaRecord>
        {
            MakeRecord(MissingSearchState.New),
            MakeRecord(MissingSearchState.SearchPushed),
            MakeRecord(MissingSearchState.Abandoned)
        };

        var result = _sut.BuildStats(items);

        Assert.Equal(3, result.Stats.TotalMissing);
    }

    [Fact]
    public void BuildStats_SearchReady_CountsNullLastSearchPushedAt()
    {
        var items = new List<MissingMediaRecord>
        {
            MakeRecord(MissingSearchState.SearchPushed, lastPushed: null),
            MakeRecord(MissingSearchState.SearchPushed, lastPushed: DateTime.UtcNow)
        };

        // First item has LastSearchPushedAt == null so is search-ready.
        // Second has a non-null LastSearchPushedAt and state SearchPushed, so NOT ready.
        var result = _sut.BuildStats(items);

        Assert.Equal(1, result.Stats.SearchReady);
    }

    [Fact]
    public void BuildStats_SearchReady_CountsNewState()
    {
        var items = new List<MissingMediaRecord>
        {
            MakeRecord(MissingSearchState.New, lastPushed: DateTime.UtcNow),
            MakeRecord(MissingSearchState.Abandoned, lastPushed: DateTime.UtcNow)
        };

        // New state is search-ready even with a non-null LastSearchPushedAt.
        var result = _sut.BuildStats(items);

        Assert.Equal(1, result.Stats.SearchReady);
    }

    [Fact]
    public void BuildStats_SearchReady_CountsInQueueState()
    {
        var items = new List<MissingMediaRecord>
        {
            MakeRecord(MissingSearchState.InQueue, lastPushed: DateTime.UtcNow),
            MakeRecord(MissingSearchState.Abandoned, lastPushed: DateTime.UtcNow)
        };

        var result = _sut.BuildStats(items);

        Assert.Equal(1, result.Stats.SearchReady);
    }

    [Fact]
    public void BuildStats_FailedSearches_CountsPositiveFailedCount()
    {
        var items = new List<MissingMediaRecord>
        {
            MakeRecord(MissingSearchState.SearchPushed, failedCount: 3),
            MakeRecord(MissingSearchState.SearchPushed, failedCount: 0),
            MakeRecord(MissingSearchState.SkippedCooldown, failedCount: 1)
        };

        var result = _sut.BuildStats(items);

        Assert.Equal(2, result.Stats.FailedSearches);
    }

    [Fact]
    public void BuildStats_Abandoned_CountsAbandonedState()
    {
        var items = new List<MissingMediaRecord>
        {
            MakeRecord(MissingSearchState.Abandoned),
            MakeRecord(MissingSearchState.Abandoned),
            MakeRecord(MissingSearchState.Waiting)
        };

        var result = _sut.BuildStats(items);

        Assert.Equal(2, result.Stats.Abandoned);
    }

    #endregion

    #region BuildStats — state breakdown

    [Fact]
    public void BuildStats_StateBreakdown_GroupsByDisplayState()
    {
        var items = new List<MissingMediaRecord>
        {
            MakeRecord(MissingSearchState.New),
            MakeRecord(MissingSearchState.New),
            MakeRecord(MissingSearchState.Abandoned)
        };

        var result = _sut.BuildStats(items);

        Assert.Equal(2, result.StateBreakdown.Count);
        var newBreakdown = result.StateBreakdown.Single(b => b.Label == "New");
        Assert.Equal(2, newBreakdown.Count);

        var abandonedBreakdown = result.StateBreakdown.Single(b => b.Label == "Abandoned");
        Assert.Equal(1, abandonedBreakdown.Count);
    }

    [Fact]
    public void BuildStats_StateBreakdown_CalculatesPercent()
    {
        var items = new List<MissingMediaRecord>
        {
            MakeRecord(MissingSearchState.New),
            MakeRecord(MissingSearchState.New),
            MakeRecord(MissingSearchState.New),
            MakeRecord(MissingSearchState.Abandoned)
        };

        var result = _sut.BuildStats(items);

        var newBreakdown = result.StateBreakdown.Single(b => b.Label == "New");
        Assert.Equal(75.0m, newBreakdown.Percent);

        var abandonedBreakdown = result.StateBreakdown.Single(b => b.Label == "Abandoned");
        Assert.Equal(25.0m, abandonedBreakdown.Percent);
    }

    [Fact]
    public void BuildStats_StateBreakdown_OrderedByCountDescending()
    {
        var items = new List<MissingMediaRecord>
        {
            MakeRecord(MissingSearchState.Abandoned),
            MakeRecord(MissingSearchState.New),
            MakeRecord(MissingSearchState.New),
            MakeRecord(MissingSearchState.New)
        };

        var result = _sut.BuildStats(items);

        Assert.Equal("New", result.StateBreakdown[0].Label);
        Assert.Equal("Abandoned", result.StateBreakdown[1].Label);
    }

    [Fact]
    public void BuildStats_StateBreakdown_MapsDisplayLabelsCorrectly()
    {
        var items = new List<MissingMediaRecord>
        {
            MakeRecord(MissingSearchState.InQueue),
            MakeRecord(MissingSearchState.SearchPushed),
            MakeRecord(MissingSearchState.SkippedCooldown),
            MakeRecord(MissingSearchState.SkippedSearching),
            MakeRecord(MissingSearchState.NotAvailable),
            MakeRecord(MissingSearchState.Abandoned)
        };

        var result = _sut.BuildStats(items);

        var labels = result.StateBreakdown.Select(b => b.Label).OrderBy(l => l).ToList();
        Assert.Contains("In Queue", labels);
        Assert.Contains("Search Pushed", labels);
        Assert.Contains("Cooldown", labels);
        Assert.Contains("Skipped", labels);
        Assert.Contains("Not Available", labels);
        Assert.Contains("Abandoned", labels);
    }

    #endregion

    #region ComputeTrends

    [Fact]
    public void ComputeTrends_FewerThanTwoPoints_SetsFlat()
    {
        var stats = new MissingStats();
        var activity = new List<MissingActivityPoint> { new() { Pushed = 10, Resolved = 5 } };

        _sut.ComputeTrends(stats, activity);

        Assert.Equal(Trend.Flat, stats.SearchTrend);
        Assert.Equal(Trend.Flat, stats.ResolvedTrend);
    }

    [Fact]
    public void ComputeTrends_EmptyActivity_SetsFlat()
    {
        var stats = new MissingStats();

        _sut.ComputeTrends(stats, []);

        Assert.Equal(Trend.Flat, stats.SearchTrend);
        Assert.Equal(Trend.Flat, stats.ResolvedTrend);
    }

    [Fact]
    public void ComputeTrends_SumsTotalPushedAndResolved()
    {
        var stats = new MissingStats();
        var activity = new List<MissingActivityPoint>
        {
            new() { Pushed = 5, Resolved = 2 },
            new() { Pushed = 3, Resolved = 4 },
            new() { Pushed = 7, Resolved = 1 },
            new() { Pushed = 2, Resolved = 3 }
        };

        _sut.ComputeTrends(stats, activity);

        Assert.Equal(17, stats.SearchesPushed24H); // 5+3+7+2
        Assert.Equal(10, stats.Resolved24H); // 2+4+1+3
    }

    [Fact]
    public void ComputeTrends_RecentMuchHigher_TrendIsUp()
    {
        var stats = new MissingStats();
        // Earlier half: pushed=1, recent half: pushed=10 -> clearly up
        var activity = new List<MissingActivityPoint>
        {
            new() { Pushed = 1, Resolved = 0 },
            new() { Pushed = 0, Resolved = 0 },
            new() { Pushed = 5, Resolved = 0 },
            new() { Pushed = 5, Resolved = 0 }
        };

        _sut.ComputeTrends(stats, activity);

        Assert.Equal(Trend.Up, stats.SearchTrend);
    }

    [Fact]
    public void ComputeTrends_RecentMuchLower_TrendIsDown()
    {
        var stats = new MissingStats();
        // Earlier half: pushed=10, recent half: pushed=1 -> clearly down
        var activity = new List<MissingActivityPoint>
        {
            new() { Pushed = 5, Resolved = 0 },
            new() { Pushed = 5, Resolved = 0 },
            new() { Pushed = 0, Resolved = 0 },
            new() { Pushed = 1, Resolved = 0 }
        };

        _sut.ComputeTrends(stats, activity);

        Assert.Equal(Trend.Down, stats.SearchTrend);
    }

    [Fact]
    public void ComputeTrends_SimilarValues_TrendIsFlat()
    {
        var stats = new MissingStats();
        // Earlier=10, Recent=11 — within 15% threshold -> flat
        var activity = new List<MissingActivityPoint>
        {
            new() { Pushed = 5, Resolved = 5 },
            new() { Pushed = 5, Resolved = 5 },
            new() { Pushed = 5, Resolved = 5 },
            new() { Pushed = 6, Resolved = 6 }
        };

        _sut.ComputeTrends(stats, activity);

        Assert.Equal(Trend.Flat, stats.SearchTrend);
        Assert.Equal(Trend.Flat, stats.ResolvedTrend);
    }

    [Fact]
    public void ComputeTrends_ResolvedTrend_TrackedIndependently()
    {
        var stats = new MissingStats();
        // Pushed: earlier=1, recent=10 -> Up
        // Resolved: earlier=10, recent=1 -> Down
        var activity = new List<MissingActivityPoint>
        {
            new() { Pushed = 1, Resolved = 5 },
            new() { Pushed = 0, Resolved = 5 },
            new() { Pushed = 5, Resolved = 0 },
            new() { Pushed = 5, Resolved = 1 }
        };

        _sut.ComputeTrends(stats, activity);

        Assert.Equal(Trend.Up, stats.SearchTrend);
        Assert.Equal(Trend.Down, stats.ResolvedTrend);
    }

    [Fact]
    public void ComputeTrends_ExactlyTwoPoints_SplitsCorrectly()
    {
        var stats = new MissingStats();
        // half = 2/2 = 1, earlier = first 1, recent = last 1
        var activity = new List<MissingActivityPoint>
        {
            new() { Pushed = 0, Resolved = 0 }, new() { Pushed = 10, Resolved = 10 }
        };

        _sut.ComputeTrends(stats, activity);

        Assert.Equal(Trend.Up, stats.SearchTrend);
        Assert.Equal(Trend.Up, stats.ResolvedTrend);
    }

    [Fact]
    public void ComputeTrends_EqualEarlierAndRecent_TrendIsFlat()
    {
        var stats = new MissingStats();
        var activity = new List<MissingActivityPoint>
        {
            new() { Pushed = 5, Resolved = 3 }, new() { Pushed = 5, Resolved = 3 }
        };

        _sut.ComputeTrends(stats, activity);

        Assert.Equal(Trend.Flat, stats.SearchTrend);
        Assert.Equal(Trend.Flat, stats.ResolvedTrend);
    }

    #endregion

    #region Helpers

    private static int _nextId;

    private static MissingMediaRecord MakeRecord(MissingSearchState state, int failedCount = 0,
        DateTime? lastPushed = default)
    {
        return new MissingMediaRecord
        {
            ServerId = 1,
            RemoteId = ++_nextId,
            SearchState = state,
            FailedSearchCount = failedCount,
            LastSearchPushedAt = lastPushed
        };
    }

    #endregion
}
