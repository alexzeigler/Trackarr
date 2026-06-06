using Trackarr.Core.Exception;
using Trackarr.Core.Media;
using Trackarr.Core.Queue;
using Trackarr.Core.Routine;
using Trackarr.Core.Server;
using Trackarr.Services;

namespace Trackarr.Tasks;

public class MovieFetchRoutine(MediaServerService mediaServerService) : Routine {
    public override string Key { get; set; } = "missing_search";
    public override string Name { get; set; } = "Missing Movie Refresh";
    public override string Description { get; set; } = "Fetches the missing movies from each Radarr instance.";
    public override bool Enabled { get; set; } = true;
    public override int IntervalSeconds { get; set; } = 900;
    public override DateTimeOffset NextRun { get; set; } = DateTimeOffset.UtcNow;

    public override async Task<string> ExecuteAsync(CancellationToken cancellationToken) {
        (bool Success, List<MediaServer> Items) serverResult = await mediaServerService.GetAllServersAsync();
        int count = 0;

        foreach (MediaServer server in serverResult.Items.Where(server => server.ServiceType == ServiceType.Radarr)) {
            (bool Success, List<Movie> Items, string Message) result = await mediaServerService.GetMissingMovies(server);
            if (!result.Success)
                throw new RoutineFailedException(result.Message, this);
            count = result.Items.Count;
            LastRunSuccess = true;
        }
        return $"{count} missing items found.";
    }
}