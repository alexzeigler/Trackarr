using Trackarr.Core.Media.Missing;
using Trackarr.Core.Media.Shows;

namespace Trackarr.Tests;

public class MissingEpisodeCountsTests
{
    private static readonly DateTime Now = new(2026, 6, 30, 12, 0, 0, DateTimeKind.Utc);

    private static Episode Ep(int serverId, int remoteSeriesId, int remoteId, bool monitored, bool hasFile,
        DateTime? airDateUtc)
    {
        return new Episode
        {
            ServerId = serverId,
            RemoteSeriesId = remoteSeriesId,
            RemoteId = remoteId,
            Monitored = monitored,
            HasFile = hasFile,
            AirDateUtc = airDateUtc
        };
    }

    #region GetTotalEpisodeCount

    [Fact]
    public void GetTotalEpisodeCount_CountsAllEpisodesForSeries_RegardlessOfFileOrMonitorState()
    {
        var episodes = new List<Episode>
        {
            Ep(1, 100, 1, true, false, Now.AddDays(-1)),
            Ep(1, 100, 2, false, true, Now.AddDays(-2)),
            Ep(1, 100, 3, true, true, null)
        };

        var total = MissingEpisodeCounts.GetTotalEpisodeCount(episodes, 1, 100);

        Assert.Equal(3, total);
    }

    [Fact]
    public void GetTotalEpisodeCount_IgnoresEpisodesFromOtherSeriesOrServers()
    {
        var episodes = new List<Episode>
        {
            Ep(1, 100, 1, true, false, Now),
            Ep(1, 200, 2, true, false, Now), // different series, same server
            Ep(2, 100, 3, true, false, Now) // same series id, different server
        };

        var total = MissingEpisodeCounts.GetTotalEpisodeCount(episodes, 1, 100);

        Assert.Equal(1, total);
    }

    [Fact]
    public void GetTotalEpisodeCount_NoEpisodes_ReturnsZero()
    {
        var total = MissingEpisodeCounts.GetTotalEpisodeCount([], 1, 100);

        Assert.Equal(0, total);
    }

    #endregion

    #region GetMissingEpisodeCount

    [Fact]
    public void GetMissingEpisodeCount_EpisodeGroup_ReturnsRecordCountDirectly()
    {
        // Already cascaded into one MissingMediaRecord per episode, so the record
        // count is the source of truth regardless of what the episode table says.
        var episodes = new List<Episode> { Ep(1, 100, 1, true, false, Now.AddDays(-1)) };

        var count = MissingEpisodeCounts.GetMissingEpisodeCount(episodes, 1, 100, true, 5, Now);

        Assert.Equal(5, count);
    }

    [Fact]
    public void GetMissingEpisodeCount_SeriesTier_CountsMonitoredUnfiledAiredEpisodes()
    {
        var episodes = new List<Episode>
        {
            Ep(1, 100, 1, true, false, Now.AddDays(-1)), // missing
            Ep(1, 100, 2, true, false, Now.AddDays(-2)), // missing
            Ep(1, 100, 3, true, false, Now.AddDays(-3)) // missing
        };

        var count = MissingEpisodeCounts.GetMissingEpisodeCount(episodes, 1, 100, false, 1, Now);

        Assert.Equal(3, count);
    }

    [Fact]
    public void GetMissingEpisodeCount_SeriesTier_ExcludesEpisodesThatHaveFile()
    {
        var episodes = new List<Episode>
        {
            Ep(1, 100, 1, true, false, Now.AddDays(-1)), // missing
            Ep(1, 100, 2, true, true, Now.AddDays(-2)) // already downloaded
        };

        var count = MissingEpisodeCounts.GetMissingEpisodeCount(episodes, 1, 100, false, 1, Now);

        Assert.Equal(1, count);
    }

    [Fact]
    public void GetMissingEpisodeCount_SeriesTier_ExcludesUnmonitoredEpisodes()
    {
        var episodes = new List<Episode>
        {
            Ep(1, 100, 1, true, false, Now.AddDays(-1)), // missing
            Ep(1, 100, 2, false, false, Now.AddDays(-2)) // unmonitored, not counted
        };

        var count = MissingEpisodeCounts.GetMissingEpisodeCount(episodes, 1, 100, false, 1, Now);

        Assert.Equal(1, count);
    }

    [Fact]
    public void GetMissingEpisodeCount_SeriesTier_ExcludesEpisodesNotYetAired()
    {
        var episodes = new List<Episode>
        {
            Ep(1, 100, 1, true, false, Now.AddDays(-1)), // aired, missing
            Ep(1, 100, 2, true, false, Now.AddDays(1)) // airs in the future
        };

        var count = MissingEpisodeCounts.GetMissingEpisodeCount(episodes, 1, 100, false, 1, Now);

        Assert.Equal(1, count);
    }

    [Fact]
    public void GetMissingEpisodeCount_SeriesTier_ExcludesEpisodesWithNullAirDate()
    {
        var episodes = new List<Episode>
        {
            Ep(1, 100, 1, true, false, Now.AddDays(-1)), // aired, missing
            Ep(1, 100, 2, true, false, null) // unknown air date, not counted
        };

        var count = MissingEpisodeCounts.GetMissingEpisodeCount(episodes, 1, 100, false, 1, Now);

        Assert.Equal(1, count);
    }

    [Fact]
    public void GetMissingEpisodeCount_SeriesTier_IgnoresEpisodesFromOtherSeriesOrServers()
    {
        var episodes = new List<Episode>
        {
            Ep(1, 100, 1, true, false, Now.AddDays(-1)),
            Ep(1, 200, 2, true, false, Now.AddDays(-1)), // different series
            Ep(2, 100, 3, true, false, Now.AddDays(-1)) // different server
        };

        var count = MissingEpisodeCounts.GetMissingEpisodeCount(episodes, 1, 100, false, 1, Now);

        Assert.Equal(1, count);
    }

    [Fact]
    public void GetMissingEpisodeCount_SeriesTier_NoMatchingEpisodes_FallsBackToRecordCount()
    {
        // Episode state hasn't been synced yet (or nothing matches) - fall back to the
        // tracking-record count rather than reporting zero missing.
        var count = MissingEpisodeCounts.GetMissingEpisodeCount([], 1, 100, false, 1, Now);

        Assert.Equal(1, count);
    }

    #endregion
}
