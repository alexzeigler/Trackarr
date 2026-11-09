using Trackarr.Core.Server;
using Trackarr.Integrations.Media.Arr.Dtos;

namespace Trackarr.Integrations.Media.Arr;

/// <summary>Sonarr's v3 API: the shared endpoints plus series and episodes.</summary>
public sealed class SonarrClient(HttpClient http, MediaServer server) : ArrClientBase(http, server)
{
    private static readonly HashSet<ArrSearch> SonarrSearches = [ArrSearch.Episodes, ArrSearch.Series];

    protected override string MediaResource => "series";
    protected override IReadOnlySet<ArrSearch> Searches => SonarrSearches;

    /// <summary>One page of monitored, aired episodes without a file, with their series included.</summary>
    public async Task<MissingShowResponse> GetMissingPageAsync(int page, int pageSize, CancellationToken ct = default)
    {
        return await GetAsync<MissingShowResponse>(
                   FormattableString.Invariant(
                       $"/api/v3/wanted/missing?page={page}&pageSize={pageSize}&includeSeries=true"), ct) ??
               new MissingShowResponse { Page = page, PageSize = pageSize };
    }

    /// <summary>One series. A series that no longer exists is an <see cref="Core.Exception.ApiException" /> (404).</summary>
    public Task<SeriesRecord?> GetSeriesAsync(int seriesId, CancellationToken ct = default)
    {
        return GetAsync<SeriesRecord>(FormattableString.Invariant($"/api/v3/series/{seriesId}"), ct);
    }

    /// <summary>Every episode of a series.</summary>
    public async Task<IReadOnlyList<EpisodeRecord>> GetEpisodesAsync(int seriesId, CancellationToken ct = default)
    {
        return await GetAsync<List<EpisodeRecord>>(FormattableString.Invariant($"/api/v3/episode?seriesId={seriesId}"),
            ct) ?? [];
    }
}
