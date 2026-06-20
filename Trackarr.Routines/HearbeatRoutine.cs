using Microsoft.Extensions.Logging;
using Trackarr.Core.Routine;
using Trackarr.Services.Media;

namespace Trackarr.Tasks;

public sealed class HeartbeatRoutine(MediaServerService mediaServerService, ILogger<HeartbeatRoutine> logger) : Routine
{
    public override string Key { get; set; } = "heartbeat";
    public override string Name { get; set; } = "Server Heartbeat";
    public override string Description { get; set; } = "Checks server status.";
    public override bool Enabled { get; set; } = true;
    public override int IntervalSeconds { get; set; } = 300;
    public override DateTimeOffset NextRun { get; set; } = DateTimeOffset.UtcNow;

    public override async Task<string> ExecuteAsync(CancellationToken cancellationToken)
    {
        var servers = await mediaServerService.GetAllServersAsync();

        foreach (var server in servers)
            try
            {
                var result = await mediaServerService.TestConnectionAsync(server);
                logger.LogInformation("Heartbeat [{name}]: {message} ({ping}ms)", server.Name, result.ReasonPhrase,
                    result.ElapsedMilliseconds);
            }
            catch (Exception ex)
            {
                logger.LogWarning("Heartbeat [{name}]: failed — {message}", server.Name, ex.Message);
            }

        return $"{servers.Count} servers checked";
    }
}