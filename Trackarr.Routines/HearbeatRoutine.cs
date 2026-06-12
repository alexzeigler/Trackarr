using Microsoft.Extensions.Logging;
using Trackarr.Core.Routine;
using Trackarr.Core.Server;
using Trackarr.Services.Media;

namespace Trackarr.Tasks;

public sealed class HeartbeatRoutine(MediaServerService mediaServerService, ILogger<HeartbeatRoutine> logger) : Routine {
    public override string Key { get; set; } = "heartbeat";
    public override string Name { get; set; } = "Server Heartbeat";
    public override string Description { get; set; } = "Checks server status.";
    public override bool Enabled { get; set; } = true;
    public override int IntervalSeconds { get; set; } = 300;
    public override DateTimeOffset NextRun { get; set; } = DateTimeOffset.UtcNow;

    public override async Task<string> ExecuteAsync(CancellationToken cancellationToken) {
        List<MediaServer> servers = await mediaServerService.GetAllServersAsync();

        foreach (MediaServer server in servers) {
            (bool Success, long ElapsedMilliseconds, string? ReasonPhrase) connectionResult = await mediaServerService.TestConnectionAsync(server);
            logger.LogInformation("Heartbeat [{name}]: {message} ({ping}ms)", server.Name, connectionResult.ReasonPhrase, connectionResult.ElapsedMilliseconds);
        }

        return $"{servers.Count} servers connected";
    }
}