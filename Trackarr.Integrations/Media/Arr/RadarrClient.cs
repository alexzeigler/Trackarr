using Trackarr.Core.Server;
using Trackarr.Integrations.Media.Arr.Dtos;

namespace Trackarr.Integrations.Media.Arr;

/// <summary>Radarr's v3 API: the shared endpoints plus movies.</summary>
public sealed class RadarrClient(HttpClient http, MediaServer server) : ArrClientBase(http, server)
{
    private static readonly HashSet<ArrSearch> RadarrSearches = [ArrSearch.Movies];

    protected override string MediaResource => "movie";
    protected override IReadOnlySet<ArrSearch> Searches => RadarrSearches;

    /// <summary>One page of monitored movies without a file.</summary>
    public async Task<MissingMovieResponse> GetMissingPageAsync(int page, int pageSize, CancellationToken ct = default)
    {
        return await GetAsync<MissingMovieResponse>(
                   FormattableString.Invariant($"/api/v3/wanted/missing?page={page}&pageSize={pageSize}"), ct) ??
               new MissingMovieResponse { Page = page, PageSize = pageSize };
    }

    /// <summary>One movie. A movie that no longer exists is an <see cref="Core.Exception.ApiException" /> (404).</summary>
    public Task<MovieRecord?> GetMovieAsync(int movieId, CancellationToken ct = default)
    {
        return GetAsync<MovieRecord>(FormattableString.Invariant($"/api/v3/movie/{movieId}"), ct);
    }
}
