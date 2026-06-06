using Microsoft.Extensions.Logging;
using Trackarr.Core.Exception;
using Trackarr.Core.Routine;
using Trackarr.Core.Server;
using Trackarr.Services;

namespace Trackarr.Tasks;

public sealed class HeartbeatRoutine(MediaServerService mediaServerService, ILogger<HeartbeatRoutine> logger) : Routine {
    public override string Key { get; set; } = "heartbeat";
    public override string Name { get; set; } = "Server Heartbeat";
    public override string Description { get; set; } = "Checks server status.";
    public override bool Enabled { get; set; } = true;
    public override int IntervalSeconds { get; set; } = 300;
    public override DateTimeOffset NextRun { get; set; } = DateTimeOffset.UtcNow;

    public override async Task<string> ExecuteAsync(CancellationToken cancellationToken) {
        (bool Success, List<MediaServer> Items) serverResult = await mediaServerService.GetAllServersAsync();

        foreach (MediaServer server in serverResult.Items) {
            (bool Success, string Message, long? PingTime) connectionResult = await mediaServerService.TestConnectionAsync(server);
            if (!serverResult.Success) {
                throw new RoutineFailedException(connectionResult.Message, this);
            }

            logger.LogInformation("Heartbeat [{name}]: {message} ({ping}ms)", server.Name, connectionResult.Message, connectionResult.PingTime);
        }

        return $"{serverResult.Items.Count} servers connected";
    }
}