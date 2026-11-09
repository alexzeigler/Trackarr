using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Trackarr.Core.Exception;
using Trackarr.Core.Server;
using Trackarr.Services.Settings;
using Trackarr.Services.UI;

namespace Trackarr.Tests;

/// <summary>
///     Pins how servers are stored, checked and described (metadata, quality profiles), against the API fixtures.
///     Written against MediaServerService, then kept passing when this code moved into <c>ServerRepository</c>,
///     <c>ServerConnectionService</c> and <c>QualityProfileService</c> (ROADMAP Phase 2 step 5).
/// </summary>
public sealed class ServerServicesTests : IDisposable
{
    private readonly SqliteInMemoryFactory _db = new();

    public void Dispose()
    {
        _db.Dispose();
    }

    #region Connection

    [Fact]
    public async Task TestConnection_Reachable_MarksTheServerOnlineAndAuthenticated()
    {
        var (services, server, _) = await CreateAsync(FixtureHandler.Radarr);

        var (success, elapsed, reason) = await services.Connection.TestConnectionAsync(server);

        Assert.True(success);
        Assert.True(elapsed >= 0);
        Assert.Equal("OK", reason);
        var saved = await services.Servers.GetByIdAsync(server.Id);
        Assert.True(saved!.IsOnline);
        Assert.True(saved.IsAuthenticated);
        Assert.NotNull(saved.LastPingMs);
    }

    [Fact]
    public async Task TestConnection_Refused_MarksTheServerOffline_AndThrows()
    {
        var connection = new TestServices(_db, new Refusing()).Connection;
        var server = await AddServerAsync(ServiceType.Radarr);

        await Assert.ThrowsAsync<ConnectionException>(() => connection.TestConnectionAsync(server));

        await using var db = _db.CreateDbContext();
        var saved = await db.MediaServers.SingleAsync();
        Assert.False(saved.IsOnline);
        Assert.NotNull(saved.LastPingedAt);
        Assert.Contains("Could not reach", saved.LastError);
    }

    [Fact]
    public async Task RemoteHealth_ReadsTheMessages_AndIsEmptyWhenItCantBeRead()
    {
        var (radarr, radarrServer, _) = await CreateAsync(FixtureHandler.Radarr);
        var (sonarr, sonarrServer, _) = await CreateAsync(FixtureHandler.Sonarr);

        var message = Assert.Single(await radarr.Connection.GetRemoteHealthAsync(radarrServer));
        Assert.Equal("IndexerStatusCheck", message.Source);
        Assert.Empty(await sonarr.Connection.GetRemoteHealthAsync(sonarrServer));

        radarrServer.ApiKey = "";
        Assert.Empty(await radarr.Connection.GetRemoteHealthAsync(radarrServer));

        var (broken, brokenServer, _) = await CreateAsync(FixtureHandler.Radarr, root: "no-such-folder");
        Assert.Empty(await broken.Connection.GetRemoteHealthAsync(brokenServer)); // 404: swallowed
    }

    #endregion

    #region Saving and metadata

    [Fact]
    public async Task SavingAServer_ThenRefreshing_StoresTheMetadataAndTheProfiles()
    {
        var (services, server, fixtures) = await CreateAsync(FixtureHandler.Radarr);

        server.Name = "Renamed";
        await services.Servers.UpdateAsync(server);
        Assert.True(await services.Connection.TryRefreshMetadataAsync(server));

        Assert.Equal("5.14.0.9383", server.Metadata?.Version);
        await using var db = _db.CreateDbContext();
        Assert.Equal("Renamed", (await db.MediaServers.SingleAsync()).Name);
        var metadata = await db.ServerMetadata.SingleAsync();
        Assert.Equal((server.Id, "Radarr", "5.14.0.9383"), (metadata.ServerId, metadata.AppName, metadata.Version));
        Assert.Equal(2, await db.QualityProfiles.CountAsync(p => p.ServerId == server.Id));
        Assert.Contains("api_v3_system_status.json", fixtures.Requested);
    }

    [Fact]
    public async Task RefreshMetadata_TwiceUpdatesItInPlace()
    {
        var (services, server, _) = await CreateAsync(FixtureHandler.Sonarr);

        await services.Connection.RefreshMetadataAsync(server);
        await services.Connection.RefreshMetadataAsync(server);

        await using var db = _db.CreateDbContext();
        Assert.Equal("Sonarr", (await db.ServerMetadata.SingleAsync()).AppName);
    }

    [Fact]
    public async Task AddingAServer_StoresIt_AndTheRefreshFetchesItsMetadata()
    {
        var (services, _, _) = await CreateAsync(FixtureHandler.Radarr);
        var added = new MediaServer
        {
            Name = "Second Radarr", ServiceType = ServiceType.Radarr, Host = "https://radarr2.test:9898/movies",
            ApiKey = "test-key", IsEnabled = true
        };

        await services.Servers.AddAsync(added);
        await services.Connection.TryRefreshMetadataAsync(added);

        Assert.NotEqual(0, added.Id);
        Assert.Equal(("radarr2.test", 9898, true), (added.Host, added.Port, added.UseHttps)); // address normalized
        Assert.Equal("Radarr", added.Metadata?.AppName);
        await using var db = _db.CreateDbContext();
        Assert.Equal(2, await db.QualityProfiles.CountAsync(p => p.ServerId == added.Id));
    }

    [Fact]
    public async Task AddingAServer_ThatCantBeReached_StillStoresIt()
    {
        var (services, _, _) = await CreateAsync(FixtureHandler.Radarr, root: "no-such-folder");
        var added = new MediaServer
        {
            Name = "Broken Radarr", ServiceType = ServiceType.Radarr, Host = "broken.test", Port = 7878,
            ApiKey = "test-key", IsEnabled = true
        };

        await services.Servers.AddAsync(added);
        Assert.False(await services.Connection.TryRefreshMetadataAsync(added)); // logged, not thrown

        Assert.Null(added.Metadata);
        await using var db = _db.CreateDbContext();
        Assert.True(await db.MediaServers.AnyAsync(s => s.Id == added.Id));
        Assert.Empty(await db.ServerMetadata.ToListAsync());
    }

    [Fact]
    public async Task SavingAServer_DoesntContactIt()
    {
        var (services, server, fixtures) = await CreateAsync(FixtureHandler.Radarr);

        server.IsEnabled = false;
        await services.Servers.UpdateAsync(server);

        Assert.Empty(fixtures.Requested);
        Assert.False((await services.Servers.GetByIdAsync(server.Id))!.IsEnabled);
    }

    #endregion

    #region Quality profiles

    [Fact]
    public async Task GetProfiles_StoresTheServersProfiles_AndCachesThemBriefly()
    {
        var (services, server, fixtures) = await CreateAsync(FixtureHandler.Radarr);

        var profiles = await services.Profiles.GetProfilesAsync(server, forceRefresh: true);
        var again = await services.Profiles.GetProfilesAsync(server);

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
        var (services, server, _) = await CreateAsync(FixtureHandler.Radarr);

        var first = await services.Profiles.GetProfilesAsync(server, forceRefresh: true);
        var second = await services.Profiles.GetProfilesAsync(server, forceRefresh: true);

        Assert.Equal(first.Select(p => p.Id).Order(), second.Select(p => p.Id).Order()); // no duplicates
    }

    [Fact]
    public async Task GetProfiles_CacheBelongsToOneService()
    {
        var (services, server, fixtures) = await CreateAsync(FixtureHandler.Radarr);
        var other = new TestServices(_db, fixtures.AsFactory()).Profiles;

        await services.Profiles.GetProfilesAsync(server, forceRefresh: true);
        await other.GetProfilesAsync(server);

        // A static cache (AUDIT A5) would have answered the second service from the database.
        Assert.Equal(2, fixtures.Requested.Count(r => r == "api_v3_qualityprofile.json"));
    }

    [Fact]
    public async Task GetProfiles_ForAServerNotSavedYet_ReturnsThemWithoutStoringThem()
    {
        var (services, _, _) = await CreateAsync(FixtureHandler.Radarr);
        var unsaved = new MediaServer
        {
            Name = "New Radarr", ServiceType = ServiceType.Radarr, Host = "radarr.test", Port = 7878,
            ApiKey = "test-key"
        };

        var profiles = await services.Profiles.GetProfilesAsync(unsaved);

        Assert.Equal(["Any", "HD-1080p"], profiles.Select(p => p.Name).Order(StringComparer.Ordinal));
        await using var db = _db.CreateDbContext();
        Assert.Empty(await db.QualityProfiles.ToListAsync());
    }

    [Fact]
    public async Task GetProfiles_WithoutAnApiKey_IsAnError()
    {
        var (services, server, fixtures) = await CreateAsync(FixtureHandler.Radarr);
        server.ApiKey = "";

        await Assert.ThrowsAsync<MediaServerException>(() => services.Profiles.GetProfilesAsync(server));
        Assert.Empty(fixtures.Requested);
    }

    #endregion

    #region Heartbeat history

    [Fact]
    public async Task StatusSamples_SinceATime_AreOldestFirst()
    {
        var (services, server, _) = await CreateAsync(FixtureHandler.Radarr);
        var now = DateTime.UtcNow;
        await using (var db = _db.CreateDbContext())
        {
            db.ServerStatusSamples.AddRange(
                new ServerStatusSample { ServerId = server.Id, SampledAt = now.AddHours(-3), Success = true },
                new ServerStatusSample { ServerId = server.Id, SampledAt = now.AddMinutes(-5), Success = false },
                new ServerStatusSample { ServerId = server.Id, SampledAt = now.AddMinutes(-30), Success = true });
            await db.SaveChangesAsync();
        }

        var samples = await services.Servers.GetStatusSamplesAsync(server.Id, now.AddHours(-1));

        Assert.Equal([now.AddMinutes(-30), now.AddMinutes(-5)], samples.Select(s => s.SampledAt));
        Assert.Equal(1, await services.Servers.CountRecentFailuresAsync(server.Id, 5));
    }

    [Fact]
    public async Task HealthEvents_AreNewestFirst_UpToTheLimit()
    {
        var (services, server, _) = await CreateAsync(FixtureHandler.Radarr);

        await services.Servers.LogHealthEventAsync(server.Id, ServerHealthEventType.Down, "first", "Test");
        await services.Servers.LogHealthEventAsync(server.Id, ServerHealthEventType.Resolved, "second");
        await services.Servers.LogHealthEventAsync(server.Id, ServerHealthEventType.Down, "third");

        var events = await services.Servers.GetRecentHealthEventsAsync(server.Id, 2);

        Assert.Equal(["third", "second"], events.Select(e => e.Message));
    }

    #endregion

    private async Task<(TestServices, MediaServer, FixtureHandler)> CreateAsync(string app, string? root = null)
    {
        var fixtures = new FixtureHandler(app, root is null ? null : Path.Combine(FixtureHandler.DefaultRoot, root));
        var bus = new DataChangeBus();
        var settings = new SettingsService(_db, NullLogger<SettingsService>.Instance, bus);
        await settings.InitializeAsync();
        var services = new TestServices(_db, fixtures.AsFactory(), settings, bus);

        var server = await AddServerAsync(app == FixtureHandler.Radarr ? ServiceType.Radarr : ServiceType.Sonarr);
        return (services, server, fixtures);
    }

    private async Task<MediaServer> AddServerAsync(ServiceType type)
    {
        var server = new MediaServer
        {
            Name = $"Test {type}", ServiceType = type, Host = $"{type}.test".ToLowerInvariant(), Port = 7878,
            ApiKey = "test-key", IsEnabled = true, CreatedAt = DateTime.UtcNow
        };
        await using var db = _db.CreateDbContext();
        db.MediaServers.Add(server);
        await db.SaveChangesAsync();
        return server;
    }

    /// <summary>Every request fails the way a refused connection does.</summary>
    private sealed class Refusing : HttpMessageHandler, IHttpClientFactory
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
}
