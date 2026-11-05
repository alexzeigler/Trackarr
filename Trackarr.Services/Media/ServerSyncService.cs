using Microsoft.Extensions.Logging;
using Trackarr.Core.Server;
using Trackarr.Services.Settings;

namespace Trackarr.Services.Media;

/// <summary>
///     Brings one server fully up to date outside the routines: metadata, the queue (scoring new downloads) and
///     the missing list. Used when a server is added and by the Sync Now buttons, so a new server's data shows up
///     straight away instead of after the next routine runs.
/// </summary>
public sealed class ServerSyncService(
    MediaServerService mediaServerService,
    MissingService missingService,
    SettingsService settings,
    ILogger<ServerSyncService> logger)
{
    public async Task SyncAsync(MediaServer server, CancellationToken cancellationToken = default)
    {
        await mediaServerService.SyncServerAsync(server);

        // The same pass the Missing Media routine makes, so the Missing page fills in for this server now. It
        // honours Passive Mode and the search limits, exactly as the routine would a few minutes later.
        if (server.IsEnabled && settings.ShouldSearchMissing(server))
            await missingService.EvaluateServerAsync(server, cancellationToken);
    }

    /// <summary>Runs <see cref="SyncAsync" /> in the background and logs a failure instead of losing it.</summary>
    public void SyncInBackground(MediaServer server)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                logger.LogInformation("Syncing {name} after it was added", server.Name);
                await SyncAsync(server);
                logger.LogInformation("Initial sync of {name} finished", server.Name);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Initial sync of {name} failed; the routines will pick it up", server.Name);
            }
        });
    }
}
