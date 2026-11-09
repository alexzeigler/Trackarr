using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Trackarr.Core.Exception;
using Trackarr.Core.Server;
using Trackarr.Integrations.Media;
using Trackarr.Integrations.Media.Arr;

namespace Trackarr.Tests;

/// <summary>
///     The Radarr and Sonarr clients in Trackarr.Integrations (ROADMAP Phase 2). Reads replay the hand-written v3
///     responses in Fixtures/{radarr,sonarr}. Writes go to a handler that records each request.
/// </summary>
public class ArrClientTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    #region Radarr reads

    [Fact]
    public async Task Radarr_SystemStatus_ReadsTheMetadata()
    {
        var (radarr, fixtures) = Radarr();

        var status = await radarr.GetSystemStatusAsync();

        Assert.Equal("Radarr", status.AppName);
        Assert.Equal("5.14.0.9383", status.Version);
        Assert.True(status.IsDocker);
        Assert.Equal(["api_v3_system_status.json"], fixtures.Requested);
    }

    [Fact]
    public async Task Radarr_CheckConnection_ReturnsTheReasonPhrase()
    {
        var (radarr, _) = Radarr();

        var check = await radarr.CheckConnectionAsync();

        Assert.Equal("OK", check.ReasonPhrase);
        Assert.True(check.Elapsed >= TimeSpan.Zero);
    }

    [Fact]
    public async Task Radarr_HealthAndProfiles_AreRead()
    {
        var (radarr, _) = Radarr();

        var health = Assert.Single(await radarr.GetHealthAsync());
        Assert.Equal("IndexerStatusCheck", health.Source);
        Assert.Equal("warning", health.Type);

        var profiles = await radarr.GetQualityProfilesAsync();
        Assert.Equal([(1, "Any"), (4, "HD-1080p")], profiles.Select(p => (p.Id, p.Name)));
    }

    [Fact]
    public async Task Radarr_QueuePage_ReadsEveryRecord()
    {
        var (radarr, _) = Radarr();

        var queue = await radarr.GetQueuePageAsync(1, 100);

        Assert.Equal(2, queue.TotalRecords);
        var first = queue.Records[0];
        Assert.Equal(1001, first.Id);
        Assert.Equal(101, first.MovieId);
        Assert.Equal("0123456789ABCDEF0123456789ABCDEF01234567", first.DownloadId);
        Assert.Equal(8589934592, first.Size);
        Assert.Equal(4294967296, first.SizeLeft); // Radarr spells it "sizeleft"
        Assert.Equal("qBittorrent", first.DownloadClient);
        Assert.NotNull(first.Movie);
        Assert.Equal("The download is stalled with no connections", queue.Records[1].ErrorMessage);
    }

    [Fact]
    public async Task Radarr_MissingAndMovie_AreRead()
    {
        var (radarr, fixtures) = Radarr();

        var missing = await radarr.GetMissingPageAsync(1, 100);
        Assert.Equal([201, 202], missing.Records.Select(m => m.Id));
        Assert.False(missing.Records[1].IsAvailable);

        var movie = await radarr.GetMovieAsync(201);
        Assert.Equal("Missing Movie", movie!.Title);
        Assert.Equal(4, movie.QualityProfileId);
        Assert.Contains("api_v3_movie_id.json", fixtures.Requested);
    }

    [Fact]
    public async Task Radarr_CommandsAndHistory_AreRead()
    {
        var (radarr, _) = Radarr();

        var commands = await radarr.GetCommandsAsync();
        Assert.Equal(3, commands.Count);
        Assert.Equal([201, 202], commands[0].Body!.MovieIds!);

        var command = await radarr.GetCommandAsync(9);
        Assert.Equal("completed", command!.Status);
        Assert.Equal("Completed. 0 reports downloaded.", command.Message);

        Assert.True(await radarr.HasImportedAsync("0123456789ABCDEF0123456789ABCDEF01234567"));
    }

    #endregion

    #region Sonarr reads

    [Fact]
    public async Task Sonarr_QueuePage_ReadsSeriesAndEpisode()
    {
        var (sonarr, _) = Sonarr();

        var queue = await sonarr.GetQueuePageAsync(1, 100);

        Assert.Equal([(301, 3011), (301, 3012)], queue.Records.Select(r => (r.SeriesId, r.EpisodeId)));
        Assert.Equal(queue.Records[0].DownloadId, queue.Records[1].DownloadId); // one season pack, two rows
        Assert.NotNull(queue.Records[0].Series);
        Assert.NotNull(queue.Records[0].Episode);
    }

    [Fact]
    public async Task Sonarr_MissingSeriesAndEpisodes_AreRead()
    {
        var (sonarr, fixtures) = Sonarr();

        var missing = Assert.Single((await sonarr.GetMissingPageAsync(1, 100)).Records);
        Assert.Equal((3023, 302, 2, 3), (missing.Id, missing.SeriesId, missing.SeasonNumber, missing.EpisodeNumber));
        Assert.Equal("Missing Show", missing.Series?.GetProperty("title").GetString()); // includeSeries=true

        var series = await sonarr.GetSeriesAsync(302);
        Assert.Equal("Missing Show", series!.Title);
        Assert.Equal(6, series.QualityProfileId);

        var episodes = await sonarr.GetEpisodesAsync(302);
        Assert.Equal([true, true, false], episodes.Select(e => e.HasFile));
        Assert.Equal(["api_v3_wanted_missing.json", "api_v3_series_id.json", "api_v3_episode.json"],
            fixtures.Requested);
    }

    #endregion

    #region Errors

    [Fact]
    public async Task ErrorStatus_IsAnApiExceptionWithThePath()
    {
        var radarr = new RecordingServer { Status = HttpStatusCode.Unauthorized }.Client(Server(ServiceType.Radarr));

        var ex = await Assert.ThrowsAsync<ApiException>(() => radarr.GetSystemStatusAsync());

        Assert.Equal(401, ex.StatusCode);
        Assert.True(ex.IsAuthFailure);
        Assert.Contains("/api/v3/system/status", ex.Message);
    }

    [Fact]
    public async Task UnreadableJson_IsAMediaServerException()
    {
        var radarr = new RecordingServer { Body = "<html>not the API</html>" }.Client(Server(ServiceType.Radarr));

        var ex = await Assert.ThrowsAsync<MediaServerException>(() => radarr.GetQueuePageAsync(1, 10));

        Assert.Contains("Unexpected response from /api/v3/queue", ex.Message);
    }

    [Fact]
    public async Task MissingItem_IsFalse_NotAnError()
    {
        var recorder = new RecordingServer { Status = HttpStatusCode.NotFound };
        var radarr = recorder.Client(Server(ServiceType.Radarr));

        Assert.False(await radarr.ExistsAsync(ArrSearch.Movies, 404));
        Assert.Equal(["GET /api/v3/movie/404"], recorder.Requests.Select(r => r.Line));
    }

    [Fact]
    public async Task UnreachableServer_IsAConnectionException()
    {
        var client = new ArrClientFactory(new ThrowingFactory()).Radarr(Server(ServiceType.Radarr), Timeout);

        await Assert.ThrowsAsync<ConnectionException>(() => client.GetQueuePageAsync(1, 10));
    }

    [Fact]
    public async Task NoApiKey_FailsBeforeAnyRequest()
    {
        var recorder = new RecordingServer();
        var server = Server(ServiceType.Sonarr);
        server.ApiKey = "";

        var ex = await Assert.ThrowsAsync<MediaServerException>(() => recorder.Client(server).GetHealthAsync());

        Assert.Contains("API key is required", ex.Message);
        Assert.Empty(recorder.Requests);
    }

    [Fact]
    public async Task UrlBase_AndApiKey_AreOnEveryRequest()
    {
        var recorder = new RecordingServer();
        var server = Server(ServiceType.Radarr);
        server.UrlBase = "/radarr";

        await recorder.Client(server).GetCommandsAsync();

        var request = Assert.Single(recorder.Requests);
        Assert.Equal("GET /radarr/api/v3/command", request.Line);
        Assert.Equal("test-key", request.ApiKey);
    }

    #endregion

    #region Writes

    [Fact]
    public async Task RemoveFromQueue_SendsTheOptions()
    {
        var recorder = new RecordingServer();

        await recorder.Client(Server(ServiceType.Radarr)).RemoveFromQueueAsync(7, removeFromClient: true,
            blocklist: false);

        Assert.Equal(["DELETE /api/v3/queue/7?removeFromClient=true&blocklist=false&skipRedownload=false"],
            recorder.Requests.Select(r => r.Line));
    }

    [Fact]
    public async Task SetQualityProfile_ChangesOnlyTheProfile()
    {
        var recorder = new RecordingServer { Body = """{"id":5,"title":"Example","qualityProfileId":4,"tags":[1]}""" };

        var change = await recorder.Client(Server(ServiceType.Radarr)).SetQualityProfileAsync(5, 1);

        Assert.Equal(new ProfileChange(4, ProfileChangeOutcome.Changed), change);
        Assert.Equal(["GET /api/v3/movie/5", "PUT /api/v3/movie/5"], recorder.Requests.Select(r => r.Line));
        var sent = JsonNode.Parse(recorder.Requests.Last().Body!)!;
        Assert.Equal(1, sent["qualityProfileId"]!.GetValue<int>());
        Assert.Equal("Example", sent["title"]!.GetValue<string>()); // the rest of the movie goes back unchanged
        Assert.Equal(1, sent["tags"]![0]!.GetValue<int>());
    }

    [Fact]
    public async Task SetQualityProfile_LeavesAHandChangedProfileAlone()
    {
        var recorder = new RecordingServer { Body = """{"id":5,"qualityProfileId":9}""" };
        var sonarr = recorder.Client(Server(ServiceType.Sonarr));

        Assert.Equal(new ProfileChange(9, ProfileChangeOutcome.LeftAlone),
            await sonarr.SetQualityProfileAsync(5, 6, onlyIfCurrent: 1));
        Assert.Equal(new ProfileChange(9, ProfileChangeOutcome.AlreadySet), await sonarr.SetQualityProfileAsync(5, 9));
        Assert.All(recorder.Requests, r => Assert.Equal("GET /api/v3/series/5", r.Line)); // never a PUT
    }

    [Fact]
    public async Task Search_SendsOneCommandPerSearch_AndOnePerSeries()
    {
        var recorder = new RecordingServer { Body = """{"id":42,"name":"x","status":"queued"}""" };

        var movies = await recorder.Client(Server(ServiceType.Radarr)).SearchAsync(ArrSearch.Movies, [1, 2]);
        var series = await recorder.Client(Server(ServiceType.Sonarr)).SearchAsync(ArrSearch.Series, [7, 8]);

        Assert.Equal([42], movies);
        Assert.Equal([42, 42], series);
        Assert.Equal(
        [
            """{"name":"MoviesSearch","movieIds":[1,2]}""", """{"name":"SeriesSearch","seriesId":7}""",
            """{"name":"SeriesSearch","seriesId":8}"""
        ], recorder.Requests.Select(r => r.Body));
    }

    [Fact]
    public async Task Search_TheAppDoesNotHave_IsRefused()
    {
        var recorder = new RecordingServer();

        await Assert.ThrowsAsync<MediaServerException>(() =>
            recorder.Client(Server(ServiceType.Radarr)).SearchAsync(ArrSearch.Episodes, [1]));
        Assert.Empty(recorder.Requests);
    }

    #endregion

    #region Factory

    [Fact]
    public void Factory_PicksTheClient_AndTheInsecureHttpClientWhenAsked()
    {
        var names = new ConcurrentQueue<string>();
        var factory = new ArrClientFactory(new NamingFactory(names));
        var sonarr = Server(ServiceType.Sonarr);
        sonarr.SkipCertificateValidation = true;

        Assert.IsType<RadarrClient>(factory.Create(Server(ServiceType.Radarr), Timeout));
        Assert.IsType<SonarrClient>(factory.Create(sonarr, Timeout));
        Assert.Equal([ArrClientFactory.HttpClientName, ArrClientFactory.InsecureHttpClientName], names);
        Assert.Throws<MediaServerException>(() => factory.Create(Server((ServiceType)99), Timeout));
    }

    #endregion

    #region Helpers

    private static MediaServer Server(ServiceType type)
    {
        return new MediaServer
        {
            Name = $"Test {type}", ServiceType = type, Host = $"{type}.test".ToLowerInvariant(), Port = 7878,
            ApiKey = "test-key"
        };
    }

    private static (RadarrClient, FixtureHandler) Radarr()
    {
        var fixtures = new FixtureHandler(FixtureHandler.Radarr);
        return (new ArrClientFactory(fixtures.AsFactory()).Radarr(Server(ServiceType.Radarr), Timeout), fixtures);
    }

    private static (SonarrClient, FixtureHandler) Sonarr()
    {
        var fixtures = new FixtureHandler(FixtureHandler.Sonarr);
        return (new ArrClientFactory(fixtures.AsFactory()).Sonarr(Server(ServiceType.Sonarr), Timeout), fixtures);
    }

    private sealed record Sent(string Line, string? ApiKey, string? Body);

    /// <summary>Answers every request with <see cref="Status" /> and <see cref="Body" />, and records it.</summary>
    private sealed class RecordingServer : HttpMessageHandler, IHttpClientFactory
    {
        public ConcurrentQueue<Sent> Requests { get; } = new();
        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;
        public string Body { get; set; } = "[]";

        public HttpClient CreateClient(string name)
        {
            return new HttpClient(this, false);
        }

        public ArrClientBase Client(MediaServer server)
        {
            return new ArrClientFactory(this).Create(server, Timeout);
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Enqueue(new Sent($"{request.Method} {request.RequestUri!.PathAndQuery}",
                request.Headers.TryGetValues("X-Api-Key", out var key) ? key.Single() : null,
                body is null ? null : JsonSerializer.Serialize(JsonNode.Parse(body))));
            return new HttpResponseMessage(Status) { Content = new StringContent(Body, Encoding.UTF8, "application/json") };
        }
    }

    private sealed class ThrowingFactory : HttpMessageHandler, IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            return new HttpClient(this, false);
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            throw new HttpRequestException("Connection refused");
        }
    }

    private sealed class NamingFactory(ConcurrentQueue<string> names) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            names.Enqueue(name);
            return new HttpClient();
        }
    }

    #endregion
}
