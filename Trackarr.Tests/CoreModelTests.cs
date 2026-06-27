using Trackarr.Core.Media;
using Trackarr.Core.Media.Movies;
using Trackarr.Core.Media.Shows;
using Trackarr.Core.Queue;

namespace Trackarr.Tests;

public class SeriesAvailabilityTests
{
    private static readonly DateTime Now = new(2026, 6, 21, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void IsAvailable_AllDatesNull_ReturnsFalse()
    {
        var series = new Series();
        Assert.False(series.IsAvailable(Now));
    }

    [Fact]
    public void IsAvailable_FirstAiredInPast_ReturnsTrue()
    {
        var series = new Series { FirstAired = Now.AddYears(-1) };
        Assert.True(series.IsAvailable(Now));
    }

    [Fact]
    public void IsAvailable_OnlyNextAiringInPast_ReturnsFalse()
    {
        var series = new Series { NextAiring = Now.AddDays(-1) };
        Assert.False(series.IsAvailable(Now));
    }

    [Fact]
    public void IsAvailable_AllDatesInFuture_ReturnsFalse()
    {
        var series = new Series
        {
            FirstAired = Now.AddDays(1), NextAiring = Now.AddDays(2), PreviousAiring = Now.AddDays(3)
        };
        Assert.False(series.IsAvailable(Now));
    }
}

public class MediaItemJsonTests
{
    [Fact]
    public void Images_DeserializesPosterUrl()
    {
        var movie = new Movie
        {
            ImagesJson = """[{"coverType":"poster","url":"u","remoteUrl":"http://img/poster.jpg"}]"""
        };

        Assert.Single(movie.Images);
        Assert.Equal("http://img/poster.jpg", movie.PosterUrl);
    }

    [Fact]
    public void Genres_DeserializesList()
    {
        var movie = new Movie { GenresJson = """["Action","Drama"]""" };

        Assert.Equal(["Action", "Drama"], movie.Genres);
    }

    [Fact]
    public void OriginalLanguage_DeserializesRecord()
    {
        var movie = new Movie { OriginalLanguageJson = """{"id":1,"name":"English"}""" };

        Assert.Equal("English", movie.OriginalLanguage?.Name);
    }

    [Fact]
    public void MalformedJson_FallsBackToEmptyInsteadOfThrowing()
    {
        var movie = new Movie { ImagesJson = "{ not valid json", GenresJson = "<broken>" };

        Assert.Empty(movie.Images);
        Assert.Empty(movie.Genres);
        Assert.Null(movie.PosterUrl);
    }
}

public class EpisodeJsonTests
{
    [Fact]
    public void ImageResources_RoundTripsThroughJson()
    {
        var episode = new Episode { ImageResources = [new MediaImage("poster", "u", "http://img/ep.jpg")] };

        Assert.NotNull(episode.ImagesJson);
        Assert.Equal("http://img/ep.jpg", episode.PosterUrl);
    }
}

public class DownloadItemTests
{
    [Theory]
    [InlineData(1000, 250, 75)]
    [InlineData(1000, 0, 100)]
    [InlineData(1000, 1000, 0)]
    [InlineData(0, 0, 0)] // guards divide-by-zero
    public void DownloadPercent_ComputesFromSizeAndSizeLeft(long size, long sizeLeft, int expected)
    {
        var item = new DownloadItem { Size = size, SizeLeft = sizeLeft };
        Assert.Equal(expected, item.DownloadPercent);
    }

    [Fact]
    public void Quality_DeserializesNestedModel()
    {
        var item = new DownloadItem
        {
            QualityJson = """{"quality":{"id":1,"name":"WEBDL-1080p","source":"web","resolution":1080}}"""
        };

        Assert.Equal("WEBDL-1080p", item.Quality?.Quality.Name);
    }

    [Fact]
    public void StatusMessages_DeserializeList()
    {
        var item = new DownloadItem { StatusMessagesJson = """[{"title":"Import","messages":["m1","m2"]}]""" };

        Assert.Single(item.StatusMessages);
        Assert.Equal("Import", item.StatusMessages[0].Title);
        Assert.Equal(2, item.StatusMessages[0].Messages.Count);
    }

    [Fact]
    public void StatusMessages_MalformedJson_ReturnsEmpty()
    {
        var item = new DownloadItem { StatusMessagesJson = "not json" };
        Assert.Empty(item.StatusMessages);
    }
}
