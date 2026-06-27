using System.Text.Json;
using Trackarr.Core;
using Trackarr.Core.Health;
using Trackarr.Core.Media;
using Trackarr.Core.Media.Missing;
using Trackarr.Core.Media.Movies;
using Trackarr.Core.Media.Shows;
using Trackarr.Core.Queue;
using Trackarr.Core.Server;

namespace Trackarr.Tests;

#region JsonHelper

public class JsonHelperTests
{
    [Fact]
    public void Deserialize_Null_ReturnsDefault()
    {
        Assert.Null(JsonHelper.Deserialize<string>(null));
    }

    [Fact]
    public void Deserialize_Empty_ReturnsDefault()
    {
        Assert.Null(JsonHelper.Deserialize<List<string>>(""));
    }

    [Fact]
    public void Deserialize_ValidJson_ReturnsParsed()
    {
        var result = JsonHelper.Deserialize<List<string>>("""["a","b"]""");
        Assert.NotNull(result);
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void Deserialize_InvalidJson_ReturnsDefault()
    {
        Assert.Null(JsonHelper.Deserialize<List<string>>("not json at all"));
    }

    [Fact]
    public void Deserialize_WrongType_ReturnsDefault()
    {
        Assert.Equal(0, JsonHelper.Deserialize<int>("""{"key":"value"}"""));
    }

    [Fact]
    public void Deserialize_CaseInsensitive()
    {
        var result = JsonHelper.Deserialize<MediaImage>("""{"coverType":"poster","url":"u","remoteUrl":"r"}""");
        Assert.NotNull(result);
        Assert.Equal("poster", result.CoverType);
    }

    [Fact]
    public void Serialize_Null_ReturnsNull()
    {
        Assert.Null(JsonHelper.Serialize<string>(null));
    }

    [Fact]
    public void Serialize_Value_ReturnsJson()
    {
        var result = JsonHelper.Serialize(new[] { "a", "b" });
        Assert.NotNull(result);
        Assert.Contains("a", result);
    }

    [Fact]
    public void Serialize_JsonElement_ReturnsRawText()
    {
        var element = JsonSerializer.Deserialize<JsonElement>("""{"key":"val"}""");
        var result = JsonHelper.Serialize(element);
        Assert.Contains("key", result);
        Assert.Contains("val", result);
    }
}

#endregion

#region MediaServer

public class MediaServerTests
{
    [Fact]
    public void BaseUrl_HttpWithPort()
    {
        var server = new MediaServer { Host = "localhost", Port = 7878, UseHttps = false };
        Assert.Equal("http://localhost:7878", server.BaseUrl);
    }

    [Fact]
    public void BaseUrl_HttpsWithPort()
    {
        var server = new MediaServer { Host = "myserver.com", Port = 443, UseHttps = true };
        Assert.Equal("https://myserver.com:443", server.BaseUrl);
    }

    [Fact]
    public void Clone_CreatesShallowCopy()
    {
        var server = new MediaServer
        {
            Id = 1,
            Name = "Test",
            Host = "localhost",
            Port = 7878,
            IsEnabled = true,
            ServiceType = ServiceType.Radarr
        };

        var clone = server.Clone();

        Assert.Equal(server.Id, clone.Id);
        Assert.Equal(server.Name, clone.Name);
        Assert.Equal(server.Host, clone.Host);
        Assert.NotSame(server, clone);
    }
}

#endregion

#region MissingMediaRecord

public class MissingMediaRecordTests
{
    [Fact]
    public void Key_WithoutEpisode()
    {
        var record = new MissingMediaRecord { ServerId = 1, RemoteId = 42 };
        Assert.Equal("1_42", record.Key);
    }

    [Fact]
    public void Key_WithEpisode()
    {
        var record = new MissingMediaRecord { ServerId = 1, RemoteId = 42, EpisodeId = 7 };
        Assert.Equal("1_42_7", record.Key);
    }
}

#endregion

#region Series.IsAvailable Extended

public class SeriesAvailabilityExtendedTests
{
    private static readonly DateTime Now = new(2026, 6, 21, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void PreviousAiringInPast_ReturnsTrue()
    {
        var series = new Series { PreviousAiring = Now.AddDays(-1) };
        Assert.True(series.IsAvailable(Now));
    }

    [Fact]
    public void PreviousAiringExactlyAtNow_ReturnsFalse()
    {
        var series = new Series { PreviousAiring = Now };
        Assert.False(series.IsAvailable(Now));
    }

    [Fact]
    public void FirstAiredExactlyAtNow_ReturnsFalse()
    {
        var series = new Series { FirstAired = Now };
        Assert.False(series.IsAvailable(Now));
    }

    [Fact]
    public void FirstAiredOneTickBefore_ReturnsTrue()
    {
        var series = new Series { FirstAired = Now.AddTicks(-1) };
        Assert.True(series.IsAvailable(Now));
    }

    [Fact]
    public void BothDatesInFuture_ReturnsFalse()
    {
        var series = new Series { FirstAired = Now.AddDays(1), PreviousAiring = Now.AddDays(1) };
        Assert.False(series.IsAvailable(Now));
    }

    [Fact]
    public void BothDatesNull_ReturnsFalse()
    {
        var series = new Series { FirstAired = null, PreviousAiring = null };
        Assert.False(series.IsAvailable(Now));
    }

    [Fact]
    public void OnlyPreviousAiringInPast_BothNullable_ReturnsTrue()
    {
        var series = new Series { FirstAired = null, PreviousAiring = Now.AddDays(-1) };
        Assert.True(series.IsAvailable(Now));
    }
}

#endregion

#region MediaItem Extended

public class MediaItemExtendedTests
{
    [Fact]
    public void FanartUrl_ReturnsCorrectImage()
    {
        var movie = new Movie
        {
            ImagesJson = """[{"coverType":"fanart","url":"u","remoteUrl":"http://img/fanart.jpg"}]"""
        };
        Assert.Equal("http://img/fanart.jpg", movie.FanartUrl);
    }

    [Fact]
    public void BannerUrl_ReturnsCorrectImage()
    {
        var movie = new Movie
        {
            ImagesJson = """[{"coverType":"banner","url":"u","remoteUrl":"http://img/banner.jpg"}]"""
        };
        Assert.Equal("http://img/banner.jpg", movie.BannerUrl);
    }

    [Fact]
    public void PosterUrl_WhenNoPoster_ReturnsNull()
    {
        var movie = new Movie
        {
            ImagesJson = """[{"coverType":"fanart","url":"u","remoteUrl":"http://img/fanart.jpg"}]"""
        };
        Assert.Null(movie.PosterUrl);
    }

    [Fact]
    public void Tags_DeserializesIntList()
    {
        var movie = new Movie { TagsJson = "[1,2,3]" };
        Assert.Equal(new[] { 1, 2, 3 }, movie.Tags);
    }

    [Fact]
    public void Tags_NullJson_ReturnsEmpty()
    {
        var movie = new Movie { TagsJson = null };
        Assert.Empty(movie.Tags);
    }

    [Fact]
    public void Genres_NullJson_ReturnsEmpty()
    {
        var movie = new Movie { GenresJson = null };
        Assert.Empty(movie.Genres);
    }

    [Fact]
    public void Images_NullJson_ReturnsEmpty()
    {
        var movie = new Movie { ImagesJson = null };
        Assert.Empty(movie.Images);
    }

    [Fact]
    public void OriginalLanguage_NullJson_ReturnsNull()
    {
        var movie = new Movie { OriginalLanguageJson = null };
        Assert.Null(movie.OriginalLanguage);
    }

    [Fact]
    public void OriginalLanguage_MalformedJson_ReturnsNull()
    {
        var movie = new Movie { OriginalLanguageJson = "broken" };
        Assert.Null(movie.OriginalLanguage);
    }

    [Fact]
    public void MultipleImages_PicksCorrectCoverType()
    {
        var movie = new Movie
        {
            ImagesJson =
                """[{"coverType":"fanart","url":"u","remoteUrl":"fan"},{"coverType":"poster","url":"u","remoteUrl":"post"},{"coverType":"banner","url":"u","remoteUrl":"ban"}]"""
        };

        Assert.Equal("post", movie.PosterUrl);
        Assert.Equal("fan", movie.FanartUrl);
        Assert.Equal("ban", movie.BannerUrl);
    }
}

#endregion

#region Episode Extended

public class EpisodeExtendedTests
{
    [Fact]
    public void ImageResources_SetAndGet_RoundTrips()
    {
        var episode = new Episode { ImageResources = [new MediaImage("poster", "url", "http://remote/poster.jpg")] };

        Assert.NotNull(episode.ImagesJson);
        Assert.Single(episode.Images);
        Assert.Equal("http://remote/poster.jpg", episode.PosterUrl);
    }

    [Fact]
    public void ImageResources_SetNull_ClearsJson()
    {
        var episode = new Episode { ImageResources = [new MediaImage("poster", "url", "http://remote/poster.jpg")] };

        episode.ImageResources = null;

        Assert.Null(episode.ImagesJson);
        Assert.Empty(episode.Images);
    }

    [Fact]
    public void FanartUrl_WhenNoFanart_ReturnsNull()
    {
        var episode = new Episode { ImageResources = [new MediaImage("poster", "url", "http://remote/poster.jpg")] };

        Assert.Null(episode.FanartUrl);
    }

    [Fact]
    public void BannerUrl_WhenNoBanner_ReturnsNull()
    {
        var episode = new Episode();
        Assert.Null(episode.BannerUrl);
    }

    [Fact]
    public void NullImagesJson_EmptyImages()
    {
        var episode = new Episode { ImagesJson = null };
        Assert.Empty(episode.Images);
    }
}

#endregion

#region DownloadItem Extended

public class DownloadItemExtendedTests
{
    [Fact]
    public void DownloadPercent_NegativeSize_ReturnsZero()
    {
        var item = new DownloadItem { Size = -100, SizeLeft = 50 };
        Assert.Equal(0, item.DownloadPercent);
    }

    [Fact]
    public void DownloadPercent_SizeLeftLargerThanSize_ClampsToZeroOrNegative()
    {
        var item = new DownloadItem { Size = 100, SizeLeft = 200 };
        Assert.True(item.DownloadPercent <= 0);
    }

    [Fact]
    public void DownloadPercent_ExactlyDownloaded_Returns100()
    {
        var item = new DownloadItem { Size = 1000, SizeLeft = 0 };
        Assert.Equal(100, item.DownloadPercent);
    }

    [Fact]
    public void DownloadPercent_SmallRemainder()
    {
        var item = new DownloadItem { Size = 1000, SizeLeft = 1 };
        Assert.Equal(99, item.DownloadPercent);
    }

    [Fact]
    public void DownloadPercent_HalfDone()
    {
        var item = new DownloadItem { Size = 1000, SizeLeft = 500 };
        Assert.Equal(50, item.DownloadPercent);
    }

    [Fact]
    public void Quality_ValidJson_Parses()
    {
        var item = new DownloadItem
        {
            QualityJson = """{"quality":{"id":1,"name":"Bluray-1080p","source":"bluray","resolution":1080}}"""
        };

        Assert.NotNull(item.Quality);
        Assert.Equal("Bluray-1080p", item.Quality.Quality.Name);
        Assert.Equal(1080, item.Quality.Quality.Resolution);
    }

    [Fact]
    public void Quality_NullJson_ReturnsNull()
    {
        var item = new DownloadItem { QualityJson = null };
        Assert.Null(item.Quality);
    }

    [Fact]
    public void Quality_MalformedJson_ReturnsNull()
    {
        var item = new DownloadItem { QualityJson = "not json" };
        Assert.Null(item.Quality);
    }

    [Fact]
    public void StatusMessages_EmptyArray_ReturnsEmpty()
    {
        var item = new DownloadItem { StatusMessagesJson = "[]" };
        Assert.Empty(item.StatusMessages);
    }

    [Fact]
    public void StatusMessages_NullJson_ReturnsEmpty()
    {
        var item = new DownloadItem { StatusMessagesJson = null };
        Assert.Empty(item.StatusMessages);
    }

    [Fact]
    public void StatusMessages_MultipleGroups()
    {
        var item = new DownloadItem
        {
            StatusMessagesJson = """[{"title":"A","messages":["1"]},{"title":"B","messages":["2","3"]}]"""
        };

        Assert.Equal(2, item.StatusMessages.Count);
        Assert.Equal("A", item.StatusMessages[0].Title);
        Assert.Equal(2, item.StatusMessages[1].Messages.Count);
    }

    [Fact]
    public void LargeSize_DoesNotOverflow()
    {
        var item = new DownloadItem { Size = long.MaxValue / 2, SizeLeft = long.MaxValue / 4 };
        var percent = item.DownloadPercent;
        Assert.True(percent >= 0 && percent <= 100);
    }
}

#endregion

#region HealthSnapshot Defaults

public class HealthSnapshotDefaultTests
{
    [Fact]
    public void NewSnapshot_HasDefaultScore()
    {
        var snapshot = new HealthSnapshot();
        Assert.Equal(70, snapshot.RawScore);
        Assert.Equal(70, snapshot.VisibleScore);
    }

    [Fact]
    public void NewSnapshot_StateIsNew()
    {
        var snapshot = new HealthSnapshot();
        Assert.Equal(HealthState.New, snapshot.State);
    }

    [Fact]
    public void NewSnapshot_ZeroCounters()
    {
        var snapshot = new HealthSnapshot();
        Assert.Equal(0, snapshot.GoodScanCount);
        Assert.Equal(0, snapshot.BadScanCount);
        Assert.Equal(0, snapshot.Confidence);
        Assert.Equal(0, snapshot.RecoveryCredit);
    }
}

#endregion

#region HealthPolicy Defaults

public class HealthPolicyDefaultTests
{
    [Fact]
    public void DefaultPolicy_HasExpectedValues()
    {
        var policy = new HealthPolicy();

        Assert.Equal(70, policy.StartingScore);
        Assert.Equal(100, policy.MaximumScore);
        Assert.Equal(0, policy.MinimumVisibleScore);
        Assert.Equal(70, policy.RemovalConfidenceThreshold);
        Assert.Equal(10, policy.StartupGracePeriodMinutes);
        Assert.Equal(8, policy.MinimumBadScansBeforeRemoval);
        Assert.Equal(10, policy.RollingWindowSize);
        Assert.Equal(85, policy.HealthyThreshold);
        Assert.Equal(65, policy.StableThreshold);
        Assert.Equal(40, policy.WatchingThreshold);
        Assert.Equal(15, policy.DegradingThreshold);
    }

    [Fact]
    public void DefaultPolicy_PenaltiesAreNegative()
    {
        var policy = new HealthPolicy();

        Assert.True(policy.NoProgressPenalty < 0);
        Assert.True(policy.WarningPenalty < 0);
        Assert.True(policy.ErrorPenalty < 0);
        Assert.True(policy.StalledPenalty < 0);
        Assert.True(policy.StalledNoConnectionsPenalty < 0);
        Assert.True(policy.RepeatedErrorPenalty < 0);
        Assert.True(policy.FetchingMetadataPenalty < 0);
    }

    [Fact]
    public void DefaultPolicy_RewardsArePositive()
    {
        var policy = new HealthPolicy();

        Assert.True(policy.ProgressIncreaseReward > 0);
        Assert.True(policy.SizeLeftDecreaseReward > 0);
        Assert.True(policy.ErrorClearedReward > 0);
        Assert.True(policy.RecoveryReward > 0);
        Assert.True(policy.SustainedProgressReward > 0);
    }

    [Fact]
    public void DefaultPolicy_ThresholdsInOrder()
    {
        var policy = new HealthPolicy();

        Assert.True(policy.DegradingThreshold < policy.WatchingThreshold);
        Assert.True(policy.WatchingThreshold < policy.StableThreshold);
        Assert.True(policy.StableThreshold < policy.HealthyThreshold);
        Assert.True(policy.HealthyThreshold <= policy.MaximumScore);
    }
}

#endregion

#region StatusMessageDecode Extended

public class StatusMessageDecodeExtendedTests
{
    [Fact]
    public void MultipleGroups_AllRendered()
    {
        var result = StatusMessageDecode.FormatStatusMessages(
            """[{"title":"Group A","messages":["msg1"]},{"title":"Group B","messages":["msg2"]}]""");

        Assert.Contains("Group A", result.Value);
        Assert.Contains("Group B", result.Value);
        Assert.Contains("msg1", result.Value);
        Assert.Contains("msg2", result.Value);
    }

    [Fact]
    public void GroupWithNoMessages_RendersTitle()
    {
        var result = StatusMessageDecode.FormatStatusMessages("""[{"title":"Only Title","messages":[]}]""");

        Assert.Contains("Only Title", result.Value);
        Assert.DoesNotContain("<ul class=\"mt-1", result.Value);
    }

    [Fact]
    public void EmptyTitle_StillRendersStructure()
    {
        var result = StatusMessageDecode.FormatStatusMessages("""[{"title":"","messages":["msg"]}]""");

        Assert.Contains("msg", result.Value);
        Assert.Contains("<li>", result.Value);
    }

    [Fact]
    public void NestedHtmlInMessages_Encoded()
    {
        var result = StatusMessageDecode.FormatStatusMessages(
            """[{"title":"Test","messages":["<img src=x onerror=alert(1)>"]}]""");

        Assert.DoesNotContain("<img", result.Value);
        Assert.Contains("&lt;img", result.Value);
    }

    [Fact]
    public void SpecialCharacters_Preserved()
    {
        var result = StatusMessageDecode.FormatStatusMessages(
            """[{"title":"Error & Warning","messages":["file \"test\" <missing>"]}]""");

        Assert.Contains("&amp;", result.Value);
        Assert.Contains("&quot;", result.Value);
        Assert.Contains("&lt;missing&gt;", result.Value);
    }

    [Fact]
    public void ValidJsonButWrongStructure_FallsBackToEncoded()
    {
        var result = StatusMessageDecode.FormatStatusMessages("""{"not":"an array"}""");

        Assert.DoesNotContain("<script>", result.Value);
        Assert.Contains("not", result.Value);
    }
}

#endregion

#region DownloadAttempt Defaults

public class DownloadAttemptDefaultTests
{
    [Fact]
    public void NewAttempt_StatusIsActive()
    {
        var attempt = new DownloadAttempt();
        Assert.Equal(AttemptStatus.Active, attempt.Status);
    }

    [Fact]
    public void NewAttempt_BoolsAreFalse()
    {
        var attempt = new DownloadAttempt();
        Assert.False(attempt.WasRemovedByTrackarr);
        Assert.False(attempt.WasImported);
        Assert.False(attempt.WasSuccessful);
        Assert.False(attempt.IsProtected);
    }

    [Fact]
    public void NewAttempt_CollectionsInitialized()
    {
        var attempt = new DownloadAttempt();
        Assert.NotNull(attempt.HealthEvents);
        Assert.Empty(attempt.HealthEvents);
    }
}

#endregion

#region HealthDecision Defaults

public class HealthDecisionDefaultTests
{
    [Fact]
    public void NewDecision_RestrictionsEmpty()
    {
        var decision = new HealthDecision();
        Assert.NotNull(decision.Restrictions);
        Assert.Empty(decision.Restrictions);
    }

    [Fact]
    public void NewDecision_DefaultsToNotRemove()
    {
        var decision = new HealthDecision();
        Assert.False(decision.ShouldRemove);
        Assert.False(decision.ShouldReplace);
        Assert.False(decision.ShouldProtect);
    }
}

#endregion
