using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Trackarr.Data;
using Trackarr.Services.Diagnostics;
using Trackarr.Services.Media;
using Trackarr.Services.Servers;
using Trackarr.Services.Settings;
using Trackarr.Services.UI;

namespace Trackarr.Tests;

/// <summary>
///     The Radarr/Sonarr services, wired the way <c>AddTrackarrServices</c> wires them, over one test database and one
///     fake HTTP handler. Built by hand so a test can leave the settings out (the clients then use their fallback
///     timeouts) or share its own bus and health service.
/// </summary>
internal sealed class TestServices
{
    public TestServices(IDbContextFactory<AppDbContext> db, IHttpClientFactory http, SettingsService? settings = null,
        DataChangeBus? bus = null, HealthService? health = null, EventService? events = null)
    {
        Bus = bus ?? new DataChangeBus();
        Health = health ??
                 new HealthService(db, settings ?? new SettingsService(db, NullLogger<SettingsService>.Instance, Bus),
                     Bus);
        Clients = new MediaServerClients(http, settings);
        Servers = new ServerRepository(db, Bus, NullLogger<ServerRepository>.Instance);
        Profiles = new QualityProfileService(db, Clients, NullLogger<QualityProfileService>.Instance);
        Connection = new ServerConnectionService(db, Clients, Servers, Profiles, Bus,
            NullLogger<ServerConnectionService>.Instance);
        Media = new MediaServerService(db, Clients, Servers, Connection, NullLogger<MediaServerService>.Instance, Bus,
            Health, settings, events);
    }

    public DataChangeBus Bus { get; }
    public HealthService Health { get; }
    public MediaServerClients Clients { get; }
    public ServerRepository Servers { get; }
    public QualityProfileService Profiles { get; }
    public ServerConnectionService Connection { get; }
    public MediaServerService Media { get; }
}
