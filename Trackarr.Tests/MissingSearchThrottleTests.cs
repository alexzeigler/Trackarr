using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Trackarr.Core.Actions;
using Trackarr.Core.Media.Missing;
using Trackarr.Core.Server;
using Trackarr.Services.Actions;
using Trackarr.Services.Media;
using Trackarr.Services.Settings;
using Trackarr.Services.UI;

namespace Trackarr.Tests;

/// <summary>
///     Regression guards for C4: missing search must be capped per run, batched into few commands, back off
///     between attempts, honor Passive Mode, and retry abandoned items eventually.
/// </summary>
public class MissingSearchThrottleTests
{
    #region Pure pieces

    [Fact]
    public void Cooldown_DoublesPerFailure_AndCaps()
    {
        var policy = new MissingPolicy { SearchCooldown = TimeSpan.FromHours(6), MaxCooldown = TimeSpan.FromDays(7) };

        Assert.Equal(TimeSpan.FromHours(6), policy.GetCooldown(0));
        Assert.Equal(TimeSpan.FromHours(12), policy.GetCooldown(1));
        Assert.Equal(TimeSpan.FromHours(24), policy.GetCooldown(2));
        Assert.Equal(TimeSpan.FromDays(7), policy.GetCooldown(10));
        Assert.Equal(TimeSpan.FromDays(7), policy.GetCooldown(1000));
    }

    [Fact]
    public void Batch_StopsAtCapacity_AndSortsByCommand()
    {
        var batch = new MissingSearchBatch(3);

        Assert.True(batch.TryAdd(false, new MissingMediaRecord { RemoteId = 1, Tier = MissingSearchTier.Series }));
        Assert.True(batch.TryAdd(false,
            new MissingMediaRecord { RemoteId = 1, EpisodeId = 10, Tier = MissingSearchTier.Episode }));
        Assert.True(batch.TryAdd(false,
            new MissingMediaRecord { RemoteId = 1, EpisodeId = 11, Tier = MissingSearchTier.Episode }));
        Assert.False(batch.TryAdd(false,
            new MissingMediaRecord { RemoteId = 1, EpisodeId = 12, Tier = MissingSearchTier.Episode }));

        Assert.Equal([1], batch.SeriesIds);
        Assert.Equal([10, 11], batch.EpisodeIds);
        Assert.Empty(batch.MovieIds);
    }

    [Fact]
    public void Batch_ZeroCapacity_AcceptsNothing()
    {
        Assert.False(new MissingSearchBatch(0).TryAdd(true, new MissingMediaRecord { RemoteId = 1 }));
    }

    [Fact]
    public void Batch_DescribesEachCommand_WithUpToFiveNames()
    {
        var batch = new MissingSearchBatch(10);
        for (var i = 1; i <= 7; i++)
            batch.TryAdd(true, new MissingMediaRecord { RemoteId = i }, $"Movie {i}");

        var command = Assert.Single(batch.Commands());
        Assert.Equal(SearchKind.Movies, command.Kind);
        Assert.Equal(7, command.Records.Count);
        Assert.Equal("Movie 1, Movie 2, Movie 3, Movie 4, Movie 5 and 2 more", command.Description);
    }

    #endregion

    #region End to end against a fake Radarr

    [Fact]
    public async Task Run_CapsSearches_AndSendsOneMoviesSearchCommand()
    {
        var (service, fake, factory, server) = await CreateAsync(missingMovies: 25);
        using var _ = factory;

        await service.EvaluateServerAsync(server);

        // One command for the whole run, holding exactly the default cap of 10 movies.
        var command = Assert.Single(fake.Commands);
        Assert.Equal("MoviesSearch", command.Name);
        Assert.Equal(10, command.Ids.Count);

        await using var db = factory.CreateDbContext();
        var states = await db.MissingMedia.Select(r => r.SearchState).ToListAsync();
        Assert.Equal(10, states.Count(s => s == MissingSearchState.SearchPushed));
        Assert.Equal(15, states.Count(s => s == MissingSearchState.Waiting));
        Assert.Equal(0, await db.MissingMedia.SumAsync(r => r.FailedSearchCount));
    }

    [Fact]
    public async Task SecondRun_SearchesTheNextItems_NotTheSameOnesAgain()
    {
        var (service, fake, factory, server) = await CreateAsync(missingMovies: 25);
        using var _ = factory;

        await service.EvaluateServerAsync(server);
        await service.EvaluateServerAsync(server);

        Assert.Equal(2, fake.Commands.Count);
        var first = fake.Commands.First().Ids;
        var second = fake.Commands.Last().Ids;
        Assert.Empty(first.Intersect(second)); // the first ten are inside their 6 hour cooldown
    }

    [Fact]
    public async Task PassiveMode_PushesNoSearches()
    {
        var (service, fake, factory, server) = await CreateAsync(missingMovies: 5, passive: true);
        using var _ = factory;

        await service.EvaluateServerAsync(server);

        Assert.Empty(fake.Commands);
        await using var db = factory.CreateDbContext();
        Assert.All(await db.MissingMedia.ToListAsync(), r => Assert.Equal(MissingSearchState.Waiting, r.SearchState));
    }

    [Fact]
    public async Task PassiveMode_LogsTheSearchItWouldHaveSent()
    {
        var (service, _, factory, server) = await CreateAsync(missingMovies: 3, passive: true);
        using var _ = factory;

        await service.EvaluateServerAsync(server);

        await using var db = factory.CreateDbContext();
        var entry = Assert.Single(await db.ActionLog.ToListAsync());
        Assert.Equal(ActionKind.SearchMovies, entry.Kind);
        Assert.Equal(ActionOutcome.Simulated, entry.Outcome);
        Assert.Equal("routine:missing_search", entry.Source);
    }

    [Fact]
    public async Task ForceSearch_SearchesTheMovie_AndStartsItsCooldown()
    {
        // Passive Mode, so the routine sends nothing: the forced search (yours, so not simulated) is the only one.
        var (service, fake, factory, server) = await CreateAsync(missingMovies: 1, passive: true);
        using var _ = factory;
        await service.EvaluateServerAsync(server);

        var (sent, message) = await service.ForceSearchAsync(server.Id, 1);

        Assert.True(sent, message);
        var command = Assert.Single(fake.Commands);
        Assert.Equal("MoviesSearch", command.Name);
        Assert.Equal([1], command.Ids);

        await using var db = factory.CreateDbContext();
        var record = await db.MissingMedia.SingleAsync();
        Assert.Equal(MissingSearchState.SearchPushed, record.SearchState);
        Assert.NotNull(record.LastSearchPushedAt);
        Assert.Equal(ActionSource.User,
            (await db.ActionLog.SingleAsync(e => e.Outcome == ActionOutcome.Executed)).Source);
    }

    [Fact]
    public async Task AbandonedItem_IsRetriedOnceTheRetryWindowPasses()
    {
        var (service, fake, factory, server) = await CreateAsync(missingMovies: 1);
        using var _ = factory;

        await using (var db = factory.CreateDbContext())
        {
            db.MissingMedia.Add(new MissingMediaRecord
            {
                ServerId = server.Id, RemoteId = 1, Tier = MissingSearchTier.Series,
                SearchState = MissingSearchState.Abandoned, FailedSearchCount = 5,
                FirstSeenMissingAt = DateTime.UtcNow.AddDays(-60), LastEvaluatedAt = DateTime.UtcNow
            });
            db.SearchEvents.Add(new MissingSearchEvent
            {
                ServerId = server.Id, RemoteId = 1, EventType = MissingEventType.Abandoned,
                Tier = MissingSearchTier.Series, OccurredAt = DateTime.UtcNow.AddDays(-31)
            });
            await db.SaveChangesAsync();
        }

        await service.EvaluateServerAsync(server);

        var command = Assert.Single(fake.Commands);
        Assert.Equal([1], command.Ids);
        await using var check = factory.CreateDbContext();
        var rec = await check.MissingMedia.SingleAsync();
        Assert.Equal(MissingSearchState.SearchPushed, rec.SearchState);
        Assert.Equal(0, rec.FailedSearchCount);
    }

    [Fact]
    public async Task RecentlyAbandonedItem_StaysAbandoned()
    {
        var (service, fake, factory, server) = await CreateAsync(missingMovies: 1);
        using var _ = factory;

        await using (var db = factory.CreateDbContext())
        {
            db.MissingMedia.Add(new MissingMediaRecord
            {
                ServerId = server.Id, RemoteId = 1, Tier = MissingSearchTier.Series,
                SearchState = MissingSearchState.Abandoned, FailedSearchCount = 5,
                FirstSeenMissingAt = DateTime.UtcNow.AddDays(-10), LastEvaluatedAt = DateTime.UtcNow
            });
            db.SearchEvents.Add(new MissingSearchEvent
            {
                ServerId = server.Id, RemoteId = 1, EventType = MissingEventType.Abandoned,
                Tier = MissingSearchTier.Series, OccurredAt = DateTime.UtcNow.AddDays(-2)
            });
            await db.SaveChangesAsync();
        }

        await service.EvaluateServerAsync(server);

        Assert.Empty(fake.Commands);
    }

    #endregion

    #region Quality profile restore

    private const int OriginalProfile = 4;
    private const int FallbackProfile = 9;

    [Fact]
    public async Task Lowering_RemembersTheOriginalProfile()
    {
        var (service, fake, factory, server) = await CreateAsync(missingMovies: 1, withFallback: true);
        using var _ = factory;
        await SeedRecordAsync(factory, server, r => r.FailedSearchCount = 3);

        await service.EvaluateServerAsync(server);

        Assert.Equal(FallbackProfile, fake.Movies[1].ProfileId);
        await using var db = factory.CreateDbContext();
        var rec = await db.MissingMedia.SingleAsync();
        Assert.True(rec.LoweredToFallback);
        Assert.Equal(OriginalProfile, rec.OriginalQualityProfileId);
    }

    [Fact]
    public async Task Import_PutsTheOriginalProfileBack()
    {
        var (service, fake, factory, server) = await CreateAsync(missingMovies: 1, withFallback: true);
        using var _ = factory;
        await SeedRecordAsync(factory, server, r =>
        {
            r.LoweredToFallback = true;
            r.OriginalQualityProfileId = OriginalProfile;
        });
        fake.Movies[1].ProfileId = FallbackProfile;
        fake.Movies[1].HasFile = true; // imported since the last run

        await service.EvaluateServerAsync(server);

        Assert.Equal(OriginalProfile, fake.Movies[1].ProfileId);
        await using var db = factory.CreateDbContext();
        Assert.Empty(await db.MissingMedia.ToListAsync());
        var events = await db.SearchEvents.Select(e => e.EventType).ToListAsync();
        Assert.Contains(MissingEventType.ProfileRestored, events);
        Assert.Contains(MissingEventType.Resolved, events);
    }

    [Fact]
    public async Task Import_LeavesAProfileChangedByHandAlone()
    {
        var (service, fake, factory, server) = await CreateAsync(missingMovies: 1, withFallback: true);
        using var _ = factory;
        await SeedRecordAsync(factory, server, r =>
        {
            r.LoweredToFallback = true;
            r.OriginalQualityProfileId = OriginalProfile;
        });
        fake.Movies[1].ProfileId = 7; // someone picked a different profile in Radarr
        fake.Movies[1].HasFile = true;

        await service.EvaluateServerAsync(server);

        Assert.Equal(7, fake.Movies[1].ProfileId);
        Assert.Empty(fake.ProfileChanges);
        await using var db = factory.CreateDbContext();
        Assert.Empty(await db.MissingMedia.ToListAsync());
        Assert.DoesNotContain(MissingEventType.ProfileRestored, await db.SearchEvents.Select(e => e.EventType).ToListAsync());
    }

    private static async Task SeedRecordAsync(SqliteInMemoryFactory factory, MediaServer server,
        Action<MissingMediaRecord> configure)
    {
        var rec = new MissingMediaRecord
        {
            ServerId = server.Id, RemoteId = 1, Tier = MissingSearchTier.Series,
            SearchState = MissingSearchState.SkippedCooldown, FirstSeenMissingAt = DateTime.UtcNow.AddDays(-5),
            LastEvaluatedAt = DateTime.UtcNow
        };
        configure(rec);

        await using var db = factory.CreateDbContext();
        db.MissingMedia.Add(rec);
        await db.SaveChangesAsync();
    }

    #endregion

    #region Setup

    private static async Task<(MissingService, FakeRadarr, SqliteInMemoryFactory, MediaServer)> CreateAsync(
        int missingMovies, bool passive = false, bool withFallback = false)
    {
        var factory = new SqliteInMemoryFactory();
        var bus = new DataChangeBus();
        var settings = new SettingsService(factory, NullLogger<SettingsService>.Instance, bus);
        await settings.InitializeAsync();
        if (passive)
            await settings.SaveCategoryAsync("General", new Dictionary<string, string> { ["PassiveMode"] = "true" });

        var health = new HealthService(factory, settings, bus);
        var fake = new FakeRadarr(missingMovies);
        var services = new TestServices(factory, fake, bus: bus, health: health);
        var media = services.Media;

        var server = new MediaServer
        {
            Name = "Fake Radarr", ServiceType = ServiceType.Radarr, Host = "radarr.test", Port = 7878,
            ApiKey = "test-key", IsEnabled = true
        };
        await using (var db = factory.CreateDbContext())
        {
            db.MediaServers.Add(server);
            await db.SaveChangesAsync();

            if (withFallback)
            {
                var profile = new QualityProfile { RemoteId = FallbackProfile, Name = "Any", ServerId = server.Id };
                db.QualityProfiles.Add(profile);
                await db.SaveChangesAsync();
                server.FallBackProfileId = profile.Id;
                await db.SaveChangesAsync();
            }
        }

        var actions = new ActionExecutor(media, settings, factory, bus, NullLogger<ActionExecutor>.Instance);
        return (new MissingService(factory, media, services.Servers, settings, actions, bus), fake, factory, server);
    }

    private sealed record SentCommand(string Name, List<int> Ids);

    private sealed class FakeMovie
    {
        public bool HasFile { get; set; }
        public bool Monitored { get; set; } = true;
        public int ProfileId { get; set; } = OriginalProfile;
    }

    /// <summary>
    ///     Serves just enough of the Radarr v3 API for a missing-media run: movies 1..n start missing on the
    ///     original profile. Records search commands and profile changes.
    /// </summary>
    private sealed class FakeRadarr(int missingMovies) : HttpMessageHandler, IHttpClientFactory
    {
        public ConcurrentQueue<SentCommand> Commands { get; } = new();
        public ConcurrentQueue<(int MovieId, int ProfileId)> ProfileChanges { get; } = new();

        public Dictionary<int, FakeMovie> Movies { get; } =
            Enumerable.Range(1, missingMovies).ToDictionary(i => i, _ => new FakeMovie());

        public HttpClient CreateClient(string name) => new(this, false);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;

            if (path.EndsWith("/api/v3/command") && request.Method == HttpMethod.Post)
            {
                using var doc = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
                var name = doc.RootElement.GetProperty("name").GetString()!;
                var ids = doc.RootElement.TryGetProperty("movieIds", out var arr)
                    ? arr.EnumerateArray().Select(e => e.GetInt32()).ToList()
                    : [];
                Commands.Enqueue(new SentCommand(name, ids));
                return Json("{}", HttpStatusCode.Created);
            }

            if (path.EndsWith("/api/v3/command")) return Json("[]");
            if (path.Contains("/api/v3/queue")) return Json("""{"page":1,"pageSize":100,"totalRecords":0,"records":[]}""");

            if (path.EndsWith("/api/v3/wanted/missing"))
            {
                var records = Movies.Where(m => !m.Value.HasFile && m.Value.Monitored)
                    .Select(m => MovieJson(m.Key, m.Value)).ToList();
                return Json(JsonSerializer.Serialize(new
                {
                    page = 1, pageSize = 100, totalRecords = records.Count, records
                }));
            }

            if (path.Contains("/api/v3/movie/") &&
                int.TryParse(path[(path.LastIndexOf('/') + 1)..], out var movieId))
            {
                if (!Movies.TryGetValue(movieId, out var movie)) return new HttpResponseMessage(HttpStatusCode.NotFound);

                if (request.Method == HttpMethod.Put)
                {
                    using var doc = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
                    movie.ProfileId = doc.RootElement.GetProperty("qualityProfileId").GetInt32();
                    ProfileChanges.Enqueue((movieId, movie.ProfileId));
                }

                return Json(JsonSerializer.Serialize(MovieJson(movieId, movie)));
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private static object MovieJson(int id, FakeMovie m)
        {
            return new
            {
                id, title = $"Movie {id}", monitored = m.Monitored, hasFile = m.HasFile, isAvailable = true,
                qualityProfileId = m.ProfileId
            };
        }

        private static HttpResponseMessage Json(string body, HttpStatusCode code = HttpStatusCode.OK)
        {
            return new HttpResponseMessage(code) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }

    #endregion
}
