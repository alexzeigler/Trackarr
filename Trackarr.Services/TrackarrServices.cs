using Microsoft.Extensions.DependencyInjection;
using Trackarr.Services.Actions;
using Trackarr.Services.Diagnostics;
using Trackarr.Services.Media;
using Trackarr.Services.Routines;
using Trackarr.Services.Servers;
using Trackarr.Services.Settings;
using Trackarr.Services.SystemInfo;
using Trackarr.Services.UI;

namespace Trackarr.Services;

public static class TrackarrServices
{
    /// <summary>
    ///     Trackarr's own services. Program.cs adds what they run on (the database, the HTTP clients, logging, data
    ///     protection), and a test builds them the same way to prove every one can be created.
    /// </summary>
    public static IServiceCollection AddTrackarrServices(this IServiceCollection services)
    {
        services.AddSingleton<EventService>();
        services.AddSingleton<RoutineRunner>();
        services.AddSingleton<RoutineService>();

        // Radarr/Sonarr clients with each server's timeout (Trackarr.Integrations), and the servers themselves.
        services.AddSingleton<MediaServerClients>();
        services.AddSingleton<ServerRepository>();
        services.AddSingleton<QualityProfileService>();
        services.AddSingleton<ServerConnectionService>();
        services.AddSingleton<MediaServerService>();

        // Every change on Radarr/Sonarr goes through here, and Passive Mode is enforced here only (ROADMAP 4.8).
        services.AddSingleton<ActionExecutor>();
        services.AddSingleton<MissingService>();
        services.AddSingleton<ServerSyncService>();
        services.AddSingleton<HealthService>();
        services.AddSingleton<HistoryService>();
        services.AddSingleton<QueueStatsService>();
        services.AddSingleton<MissingStatsService>();
        services.AddSingleton<SystemStatusService>();
        services.AddSingleton<GitHubReleaseService>();
        services.AddSingleton<SettingsService>();
        services.AddSingleton<BlacklistService>();
        services.AddSingleton<DataChangeBus>();

        services.AddScoped<AppStateService>();

        return services;
    }
}
