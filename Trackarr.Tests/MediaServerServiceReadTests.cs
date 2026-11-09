using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Trackarr.Core.Exception;
using Trackarr.Core.Server;
using Trackarr.Services.Media;
using Trackarr.Services.Settings;
using Trackarr.Services.UI;

namespace Trackarr.Tests;

/// <summary>
///     Pins what MediaServerService's reads return, against the API fixtures. Written before its HTTP code moved onto
///     the Trackarr.Integrations clients (ROADMAP Phase 2 step 4), and kept passing through the move.
/// </summary>
public sealed class MediaServerServiceReadTests : IDisposable
{
    private readonly SqliteInMemoryFactory _db = new();

    public void Dispose()
    {
        _db.Dispose();
    }

    [Fact]
    public async Task TestConnection_Reachable_MarksTheServerOnlineAndAuthenticated()
    {
        var (service, server, _) = await CreateAsync(FixtureHandler.Radarr);

        var (success, elapsed, reason) = await service.TestConnectionAsync(server);

        Assert.True(success);
        Assert.True(elapsed >= 0);
        Assert.Equal("OK", reason);
        var saved = await service.GetServerByIdAsync(server.Id);
        Assert.True(saved!.IsOnline);
        Assert.True(saved.IsAuthenticated);
        Assert.NotNull(saved.LastPingMs);
    }

    [Fact]
    public async Task GetProfiles_StoresTheServersProfiles_AndCachesThemBriefly()
    {
        var (service, server, fixtures) = await CreateAsync(FixtureHandler.Radarr);

        var profiles = await service.GetProfilesAsync(server, forceRefresh: true);
        var again = await service.GetProfilesAsync(server);

        Assert.Equal([(1, "Any"), (4, "HD-1080p")], profiles.OrderBy(p => p.RemoteId).Select(p => (p.RemoteId, p.Name)));
        Assert.All(profiles, p => Assert.Equal(server.Id, p.ServerId));
        Assert.Equal(profiles.Select(p => p.Id).Order(), again.Select(p => p.Id).Order());
        Assert.Single(fixtures.Requested, "api_v3_qualityprofile.json"); // the second call came from the database

        await using var db = _db.CreateDbContext();
        Assert.Equal(2, await db.QualityProfiles.CountAsync(p => p.ServerId == server.Id));
    }

    [Fact]
    public async Task GetProfiles_RefreshUpdatesRowsInPlace()
    {
        var (service, server, _) = await CreateAsync(FixtureHandler.Radarr);

        var first = await service.GetProfilesAsync(server, forceRefresh: true);
        var second = await service.GetProfilesAsync(server, forceRefresh: true);

        Assert.Equal(first.Select(p => p.Id).Order(), second.Select(p => p.Id).Order()); // no duplicates
    }

    [Fact]
    public async Task RemoteHealth_ReadsTheMessages_AndIsEmptyWhenItCantBeRead()
    {
        var (radarr, radarrServer, _) = await CreateAsync(FixtureHandler.Radarr);
        var (sonarr, sonarrServer, _) = await CreateAsync(FixtureHandler.Sonarr);

        var message = Assert.Single(await radarr.GetRemoteHealthAsync(radarrServer));
        Assert.Equal("IndexerStatusCheck", message.Source);
        Assert.Empty(await sonarr.GetRemoteHealthAsync(sonarrServer));

        radarrServer.ApiKey = "";
        Assert.Empty(await radarr.GetRemoteHealthAsync(radarrServer));

        var (broken, brokenServer, _) = await CreateAsync(FixtureHandler.Radarr, root: "no-such-folder");
        Assert.Empty(await broken.GetRemoteHealthAsync(brokenServer)); // 404: swallowed
    }

    [Fact]
    public async Task ActiveSearches_AreTheUnfinishedSearchCommands()
    {
        var (service, server, _) = await CreateAsync(FixtureHandler.Radarr);

        var active = await service.GetActiveSearchesAsync(server);

        // Command 9 finished and 11 isn't a search; 10 is a MoviesSearch still running.
        Assert.Equal([203], active.MovieIds);
        Assert.Empty(active.SeriesIds);
        Assert.Empty(active.EpisodeIds);
    }

    [Fact]
    public async Task ImportedDownloadIds_AreTheOnesWithAnImportInTheHistory()
    {
        var (service, server, fixtures) = await CreateAsync(FixtureHandler.Radarr);

        Assert.Empty(await service.GetImportedDownloadIdsAsync(server, ["", "  "]));
        Assert.Empty(fixtures.Requested);

        // The history fixture has one import whatever the query, so both count as imported.
        var imported = await service.GetImportedDownloadIdsAsync(server, ["abc", " ABC ", "def"]);

        Assert.Equal(["abc", "def"], imported.Order(StringComparer.Ordinal));
        Assert.Equal(2, fixtures.Requested.Count(r => r == "api_v3_history.json"));
    }

    [Fact]
    public async Task MissingMovies_AreFetchedAndStored_ThenServedFromTheDatabase()
    {
        var (service, server, fixtures) = await CreateAsync(FixtureHandler.Radarr);

        var fetched = await service.GetMissingMoviesAsync(server, forceRefresh: true);
        var cached = await service.GetMissingMoviesAsync(server);

        Assert.Equal([(201, "Missing Movie"), (202, "Not Out Yet")],
            fetched.OrderBy(m => m.RemoteId).Select(m => (m.RemoteId, m.Title)));
        Assert.All(fetched, m => Assert.Equal(server.Id, m.ServerId));
        Assert.Equal(fetched.Select(m => m.Id).Order(), cached.Select(m => m.Id).Order());
        Assert.Single(fixtures.Requested, "api_v3_wanted_missing.json");
    }

    [Fact]
    public async Task MissingMovies_RefreshUpdatesRowsInPlace()
    {
        var (service, server, _) = await CreateAsync(FixtureHandler.Radarr);

        var first = await service.GetMissingMoviesAsync(server, forceRefresh: true);
        var second = await service.GetMissingMoviesAsync(server, forceRefresh: true);

        Assert.Equal(first.Select(m => m.Id).Order(), second.Select(m => m.Id).Order());
        await using var db = _db.CreateDbContext();
        Assert.Equal(2, await db.Movies.CountAsync(m => m.ServerId == server.Id));
    }

    [Fact]
    public async Task MissingMovies_AreRadarrOnly_AndComeFromTheDatabaseWhileTheServerIsOff()
    {
        var (sonarr, sonarrServer, _) = await CreateAsync(FixtureHandler.Sonarr);
        await Assert.ThrowsAsync<MediaServerException>(() => sonarr.GetMissingMoviesAsync(sonarrServer));

        var (service, server, fixtures) = await CreateAsync(FixtureHandler.Radarr);
        await service.GetMissingMoviesAsync(server, forceRefresh: true);
        server.IsEnabled = false;

        Assert.Equal(2, (await service.GetMissingMoviesAsync(server, forceRefresh: true)).Count);
        Assert.Single(fixtures.Requested);
    }

    [Fact]
    public async Task MovieById_StoresTheMovie_AndUpdatesItInPlace()
    {
        var (service, server, _) = await CreateAsync(FixtureHandler.Radarr);

        var first = await service.GetMovieByIdAsync(server, 201);
        var second = await service.GetMovieByIdAsync(server, 201);

        Assert.Equal((201, "Missing Movie"), (first!.RemoteId, first.Title));
        Assert.Equal(first.Id, second!.Id);
        await using var db = _db.CreateDbContext();
        Assert.Equal(1, await db.Movies.CountAsync(m => m.ServerId == server.Id));
    }

    [Fact]
    public async Task MovieById_AMovieThatsGone_IsAnApiError()
    {
        var (service, server, _) = await CreateAsync(FixtureHandler.Radarr, root: "no-such-folder");

        var error = await Assert.ThrowsAsync<ApiException>(() => service.GetMovieByIdAsync(server, 999));

        Assert.Equal(404, error.StatusCode);
    }

    [Fact]
    public async Task MissingEpisodes_StoreTheEpisodes_AndTheirSeriesFromTheSameReply()
    {
        var (radarr, radarrServer, _) = await CreateAsync(FixtureHandler.Radarr);
        await Assert.ThrowsAsync<MediaServerException>(() => radarr.GetMissingEpisodesAsync(radarrServer));

        var (service, server, fixtures) = await CreateAsync(FixtureHandler.Sonarr);

        var episode = Assert.Single(await service.GetMissingEpisodesAsync(server, forceRefresh: true));

        Assert.Equal((3023, 302, 2, 3),
            (episode.RemoteId, episode.RemoteSeriesId, episode.SeasonNumber, episode.EpisodeNumber));
        Assert.Equal(["api_v3_wanted_missing.json"], fixtures.Requested); // no follow-up fetch per series

        await using var db = _db.CreateDbContext();
        var series = Assert.Single(await db.Series.Where(s => s.ServerId == server.Id).ToListAsync());
        Assert.Equal((302, "Missing Show"), (series.RemoteId, series.Title));
    }

    [Fact]
    public async Task SeriesById_StoresTheSeries_AndIsSonarrOnly()
    {
        var (radarr, radarrServer, _) = await CreateAsync(FixtureHandler.Radarr);
        await Assert.ThrowsAsync<MediaServerException>(() => radarr.GetSeriesByIdAsync(radarrServer, 302));

        var (service, server, _) = await CreateAsync(FixtureHandler.Sonarr);

        var first = await service.GetSeriesByIdAsync(server, 302);
        var second = await service.GetSeriesByIdAsync(server, 302);

        Assert.Equal((302, "Missing Show"), (first!.RemoteId, first.Title));
        Assert.Equal(first.Id, second!.Id);
    }

    [Fact]
    public async Task SyncSeriesEpisodes_StoresEveryEpisode_LinkedToItsSeries()
    {
        var (service, server, _) = await CreateAsync(FixtureHandler.Sonarr);
        var series = await service.GetSeriesByIdAsync(server, 302);

        await service.SyncSeriesEpisodesAsync(server, 302);
        await service.SyncSeriesEpisodesAsync(server, 302); // again: updates, no duplicates

        var episodes = await service.GetEpisodesBySeriesIdAsync(server.Id, 302);
        Assert.Equal([3021, 3022, 3023], episodes.Select(e => e.RemoteId));
        Assert.All(episodes, e => Assert.Equal(series!.Id, e.SeriesDbId));
    }

    [Fact]
    public async Task SyncSeriesEpisodes_WithoutAnApiKey_DoesNothing()
    {
        var (service, server, fixtures) = await CreateAsync(FixtureHandler.Sonarr);
        server.ApiKey = "";

        await service.SyncSeriesEpisodesAsync(server, 302);

        Assert.Empty(fixtures.Requested);
    }

    [Fact]
    public async Task SeriesEpisodeStates_ComeFromTheEpisodeList()
    {
        var (service, server, _) = await CreateAsync(FixtureHandler.Sonarr);

        var states = await service.GetSeriesEpisodeStatesAsync(server, 302);

        Assert.Equal([(3021, true, true), (3022, true, true), (3023, false, true)], states);

        server.ApiKey = "";
        await Assert.ThrowsAsync<MediaServerException>(() => service.GetSeriesEpisodeStatesAsync(server, 302));
    }

    private async Task<(MediaServerService, MediaServer, FixtureHandler)> CreateAsync(string app, string? root = null)
    {
        var fixtures = new FixtureHandler(app, root is null ? null : Path.Combine(FixtureHandler.DefaultRoot, root));
        var bus = new DataChangeBus();
        var settings = new SettingsService(_db, NullLogger<SettingsService>.Instance, bus);
        await settings.InitializeAsync();
        var service = new MediaServerService(_db, fixtures.AsFactory(), NullLogger<MediaServerService>.Instance, bus,
            new HealthService(_db, settings, bus), settings);

        var server = new MediaServer
        {
            Name = $"Test {app}", ServiceType = app == FixtureHandler.Radarr ? ServiceType.Radarr : ServiceType.Sonarr,
            Host = $"{app}.test", Port = 7878, ApiKey = "test-key", IsEnabled = true, CreatedAt = DateTime.UtcNow
        };
        await using (var db = _db.CreateDbContext())
        {
            db.MediaServers.Add(server);
            await db.SaveChangesAsync();
        }

        return (service, server, fixtures);
    }
}

public class SearchFollowUpTests
{
    [Theory]
    [InlineData("Completed. 1 reports downloaded.", 1)]
    [InlineData("Movie search completed. 3 report downloaded", 3)]
    [InlineData("Completed. 0 reports downloaded.", 0)]
    [InlineData("Completed", null)]
    [InlineData(null, null)]
    public void ReportsDownloaded_ReadsTheCountFromTheCommandMessage(string? message, int? expected)
    {
        Assert.Equal(expected, MediaServerService.ReportsDownloaded(message));
    }
}
