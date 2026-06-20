using Microsoft.Extensions.Logging;
using Trackarr.Core.Routine;
using Trackarr.Core.Server;
using Trackarr.Services.Media;

namespace Trackarr.Tasks;

public class MissingMediaFetchRoutine(
    MediaServerService mediaServerService,
    MissingService missingService,
    ILogger<MissingMediaFetchRoutine> logger) : Routine
{
    public override string Key { get; set; } = "missing_search";
    public override string Name { get; set; } = "Missing Media Refresh";

    public override string Description { get; set; } =
        "Searches and tracks missing movies and episodes from configured servers.";

    public override bool Enabled { get; set; } = true;
    public override int IntervalSeconds { get; set; } = 900;
    public override DateTimeOffset NextRun { get; set; } = DateTimeOffset.UtcNow;

    public override async Task<string> ExecuteAsync(CancellationToken cancellationToken)
    {
        var servers = await mediaServerService.GetAllServersAsync();

        var movieCount = 0;
        var episodeCount = 0;
        var failed = 0;

        foreach (var server in servers)
            try
            {
                var count = await missingService.EvaluateServerAsync(server, cancellationToken);

                if (server.ServiceType == ServiceType.Radarr) movieCount += count;
                else episodeCount += count;
            }
            catch (Exception ex)
            {
                failed++;
                logger.LogError(ex, "Missing evaluation failed for {name}", server.Name);
            }

        LastRunSuccess = failed == 0;
        return $"{movieCount} missing movies, {episodeCount} missing episodes" +
               (failed > 0 ? $", {failed} server(s) failed." : ".");
    }
}