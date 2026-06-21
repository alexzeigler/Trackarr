using System.Text.Json;
using Trackarr.Core.Media.Movies;
using Trackarr.Core.Media.Shows;
using Trackarr.Services.Media;

namespace Trackarr.Tests;

public class MediaRecordMappersTests
{
    [Fact]
    public void MovieRecord_ToEntity_MapsScalarsAndServerId()
    {
        var record = new MovieRecord
        {
            Id = 42,
            Title = "Inception",
            Year = 2010,
            HasFile = true,
            Monitored = true,
            QualityProfileId = 3,
            IsAvailable = true,
            Genres = JsonSerializer.SerializeToElement(new[] { "Action", "Sci-Fi" })
        };

        var movie = record.ToEntity(serverId: 7);

        Assert.Equal(42, movie.RemoteId);
        Assert.Equal("Inception", movie.Title);
        Assert.Equal(2010, movie.Year);
        Assert.True(movie.HasFile);
        Assert.Equal(3, movie.QualityProfileId);
        Assert.True(movie.IsAvailable);
        Assert.Equal(7, movie.ServerId);
        Assert.Contains("Action", movie.GenresJson);
    }

    [Fact]
    public void MissingEpisodeRecord_ToEntity_MapsSceneFieldsAndServerId()
    {
        var record = new MissingEpisodeRecord
        {
            Id = 100,
            SeriesId = 9,
            SeasonNumber = 2,
            EpisodeNumber = 5,
            SceneEpisodeNumber = 6,
            HasFile = false,
            Monitored = true
        };

        var episode = record.ToEntity(serverId: 4);

        Assert.Equal(100, episode.RemoteId);
        Assert.Equal(9, episode.RemoteSeriesId);
        Assert.Equal(2, episode.SeasonNumber);
        Assert.Equal(5, episode.EpisodeNumber);
        Assert.Equal(6, episode.SceneEpisodeNumber);
        Assert.Equal(4, episode.ServerId);
    }

    [Fact]
    public void EpisodeRecord_ToEntity_MapsCoreFieldsAndServerId()
    {
        var record = new EpisodeRecord
        {
            Id = 200,
            SeriesId = 9,
            SeasonNumber = 1,
            EpisodeNumber = 1,
            AbsoluteEpisodeNumber = 1,
            HasFile = true
        };

        var episode = record.ToEntity(serverId: 4);

        Assert.Equal(200, episode.RemoteId);
        Assert.Equal(9, episode.RemoteSeriesId);
        Assert.Equal(1, episode.AbsoluteEpisodeNumber);
        Assert.True(episode.HasFile);
        Assert.Equal(4, episode.ServerId);
    }

    [Fact]
    public void SeriesRecord_ToEntity_MapsScalarsAndServerId()
    {
        var record = new SeriesRecord
        {
            Id = 9,
            Title = "Breaking Bad",
            TvdbId = 81189,
            Year = 2008,
            Ended = true
        };

        var series = record.ToEntity(serverId: 4);

        Assert.Equal(9, series.RemoteId);
        Assert.Equal("Breaking Bad", series.Title);
        Assert.Equal(81189, series.TvdbId);
        Assert.Equal(2008, series.Year);
        Assert.True(series.Ended);
        Assert.Equal(4, series.ServerId);
    }
}
