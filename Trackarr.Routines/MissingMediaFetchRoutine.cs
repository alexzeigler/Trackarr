using Trackarr.Core.Media.Movies;
using Trackarr.Core.Media.Shows;
using Trackarr.Core.Routine;
using Trackarr.Core.Server;
using Trackarr.Services.Media;

namespace Trackarr.Tasks;

public class MissingMediaFetchRoutine(MediaServerService mediaServerService) : Routine {
    public override string Key { get; set; } = "missing_search";
    public override string Name { get; set; } = "Missing Media Refresh";
    public override string Description { get; set; } = "Fetches missing movies and episodes from configured servers.";
    public override bool Enabled { get; set; } = true;
    public override int IntervalSeconds { get; set; } = 900;
    public override DateTimeOffset NextRun { get; set; } = DateTimeOffset.UtcNow;

    public override async Task<string> ExecuteAsync(CancellationToken cancellationToken) {
        List<MediaServer> servers = await mediaServerService.GetAllServersAsync();

        int movieCount = 0;
        int episodeCount = 0;

        foreach (MediaServer server in servers) {
            switch (server.ServiceType) {
                case ServiceType.Radarr:
                    List<Movie> movies = await mediaServerService.GetMissingMovies(server, true);
                    movieCount += movies.Count;
                    await mediaServerService.UpdateServerStatsAsync(server.Id,
                    s => {
                        s.LastMissingFetchAt = DateTime.UtcNow;
                        s.LastMissingCount = movies.Count;
                    });
                    break;

                case ServiceType.Sonarr:
                    List<Episode> episodes = await mediaServerService.GetMissingEpisodes(server, true);
                    episodeCount += episodes.Count;
                    await mediaServerService.UpdateServerStatsAsync(server.Id,
                    s => {
                        s.LastMissingFetchAt = DateTime.UtcNow;
                        s.LastMissingCount = episodes.Count;
                    });
                    break;

                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        LastRunSuccess = true;
        return $"{movieCount} missing movies and {episodeCount} missing episodes found.";
    }
}