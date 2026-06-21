using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using Trackarr.Components.Modals;
using Trackarr.Core.Media.Missing;
using Trackarr.Core.Media.Movies;
using Trackarr.Core.Media.Shows;
using Trackarr.Core.Server;
using Trackarr.Data;
using static Trackarr.Utils;

namespace Trackarr.Components.Pages;

public partial class Missing : ComponentBase
{
    private MediaDetailsModal _detailsModal = null!;
    private Dictionary<string, Episode> _episodes = [];


    private List<MissingMediaRecord> _missingItems = [];
    private List<MissingMediaRow> _missingRows = [];
    private Dictionary<string, Movie> _movies = [];
    private Dictionary<string, Series> _series = [];
    private Dictionary<int, MediaServer> _servers = [];
    [Inject] private IDbContextFactory<AppDbContext> DbFactory { get; set; } = null!;

    private int PageSize { get; } = 10;
    private bool IsRefreshing { get; set; }

    private int TotalMissingCount => _missingItems.Count;
    private int SearchReadyCount => _missingItems.Count(IsSearchReady);
    private int FallbackLoweredCount => _missingItems.Count(x => x.LoweredToFallback);
    private int FailedSearchCount => _missingItems.Count(x => x.FailedSearchCount > 0);

    private Dictionary<string, Func<MissingMediaRow, object>> MissingSorts =>
        new()
        {
            ["name"] = row => GetTitle(row),
            ["server"] = row => GetServer(row)?.Name ?? string.Empty,
            ["count"] = row => row.MissingCount,
            ["tier"] = row => GetTierDisplay(row),
            ["state"] = row => GetStateDisplay(row),
            ["failed"] = row => row.FailedSearchCount,
            ["fallback"] = row => row.LoweredToFallback ? 1 : 0,
            ["firstSeen"] = row => GetSortTicks(row.FirstSeenMissingAt),
            ["lastSearch"] = row => GetSortTicks(row.LastSearchPushedAt),
            ["lastEval"] = row => GetSortTicks(row.LastEvaluatedAt)
        };

    protected override async Task OnInitializedAsync()
    {
        await RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        if (IsRefreshing) return;

        IsRefreshing = true;

        try
        {
            await using var db = await DbFactory.CreateDbContextAsync();

            _missingItems = await db.MissingMedia.AsNoTracking().OrderByDescending(x => x.FirstSeenMissingAt)
                .ToListAsync();

            var serverIds = _missingItems.Select(x => x.ServerId).Distinct().ToList();

            var remoteIds = _missingItems.Select(x => x.RemoteId).Distinct().ToList();

            var missingSeriesRemoteIds = _missingItems.Where(x => x.EpisodeId != null).Select(x => x.RemoteId)
                .Distinct().ToList();

            var servers = await db.MediaServers.AsNoTracking().Where(x => serverIds.Contains(x.Id)).ToListAsync();

            var movies = await db.Movies.AsNoTracking()
                .Where(x => serverIds.Contains(x.ServerId) && remoteIds.Contains(x.RemoteId)).ToListAsync();

            var series = await db.Series.AsNoTracking()
                .Where(x => serverIds.Contains(x.ServerId) && remoteIds.Contains(x.RemoteId)).ToListAsync();

            var episodes = missingSeriesRemoteIds.Count == 0
                ? []
                : await db.Episodes.AsNoTracking().Where(x =>
                    serverIds.Contains(x.ServerId) && missingSeriesRemoteIds.Contains(x.RemoteSeriesId)).ToListAsync();

            _missingRows = _missingItems.GroupBy(GetGroupKey).Select(x => new MissingMediaRow
            {
                Key = x.Key,
                ServerId = x.First().ServerId,
                RemoteId = x.First().RemoteId,
                Items = x.OrderBy(y => y.EpisodeId ?? 0).ToList()
            }).OrderByDescending(x => x.FirstSeenMissingAt).ToList();

            _servers = servers.ToDictionary(x => x.Id);
            _movies = BuildLookup(movies, x => MediaKey(x.ServerId, x.RemoteId));
            _series = BuildLookup(series, x => MediaKey(x.ServerId, x.RemoteId));
            _episodes = BuildLookup(episodes, x => EpisodeKey(x.ServerId, x.RemoteSeriesId, x.RemoteId));
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    private async Task OpenDetailsAsync(MissingMediaRow row)
    {
        var movie = GetMovie(row);
        var series = GetSeries(row);

        if (movie != null)
        {
            await _detailsModal.Open(movie);
            return;
        }

        if (series != null) await _detailsModal.Open(series);
    }

    private bool MatchesSearch(MissingMediaRow row, string search)
    {
        if (string.IsNullOrWhiteSpace(search)) return true;

        var value = search.Trim();

        return GetTitle(row).Contains(value, StringComparison.OrdinalIgnoreCase) ||
               GetSubtitle(row).Contains(value, StringComparison.OrdinalIgnoreCase) ||
               GetMissingType(row).Contains(value, StringComparison.OrdinalIgnoreCase) ||
               (GetServer(row)?.Name.Contains(value, StringComparison.OrdinalIgnoreCase) ?? false) ||
               row.RemoteId.ToString().Contains(value, StringComparison.OrdinalIgnoreCase) ||
               GetTierDisplay(row).Contains(value, StringComparison.OrdinalIgnoreCase) ||
               GetStateDisplay(row).Contains(value, StringComparison.OrdinalIgnoreCase) || row.Items.Any(x =>
                   x.EpisodeId?.ToString().Contains(value, StringComparison.OrdinalIgnoreCase) ?? false);
    }

    private MediaServer? GetServer(MissingMediaRow row)
    {
        return _servers.GetValueOrDefault(row.ServerId);
    }

    private Movie? GetMovie(MissingMediaRow row)
    {
        return row.IsEpisodeGroup ? null : _movies.GetValueOrDefault(MediaKey(row.ServerId, row.RemoteId));
    }

    private Series? GetSeries(MissingMediaRow row)
    {
        return _series.GetValueOrDefault(MediaKey(row.ServerId, row.RemoteId));
    }

    private string GetTitle(MissingMediaRow row)
    {
        var movie = GetMovie(row);

        if (movie != null) return TextDisplay(movie.Title);

        var series = GetSeries(row);

        if (series != null) return TextDisplay(series.Title);

        return $"Remote {IntDisplay(row.RemoteId)}";
    }

    private string GetSubtitle(MissingMediaRow row)
    {
        if (!row.IsEpisodeGroup) return string.Empty;

        var total = GetTotalEpisodeCount(row);

        if (total > 0) return $"{row.MissingCount} of {total} episodes missing";

        return row.MissingCount == 1 ? "1 missing episode" : $"{row.MissingCount} missing episodes";
    }

    private string GetMissingType(MissingMediaRow row)
    {
        if (row.IsEpisodeGroup) return "Series";

        var server = GetServer(row);

        return server?.ServiceType switch
        {
            ServiceType.Radarr => "Movie",
            ServiceType.Sonarr => "Series",
            _ => "Media"
        };
    }

    private string? GetPosterUrl(MissingMediaRow row)
    {
        var movie = GetMovie(row);

        if (movie != null) return GetPosterUrl(movie.ImagesJson);

        var series = GetSeries(row);

        return series != null ? GetPosterUrl(series.ImagesJson) : null;
    }

    private int GetTotalEpisodeCount(MissingMediaRow row)
    {
        return _episodes.Values.Count(x => x.ServerId == row.ServerId && x.RemoteSeriesId == row.RemoteId);
    }

    private static string GetTierDisplay(MissingMediaRow row)
    {
        var tiers = row.Items.Select(x => x.Tier).Distinct().ToList();

        return tiers.Count == 1 ? EnumDisplay<MissingSearchTier>(tiers[0]) : "Mixed";
    }

    private static string GetStateDisplay(MissingMediaRow row)
    {
        var states = row.Items.Select(x => x.SearchState).Distinct().ToList();

        return states.Count == 1 ? EnumDisplay<MissingSearchState>(states[0]) : "Mixed";
    }

    private static string GetTierBadgeClass(MissingMediaRow row)
    {
        var value = GetTierDisplay(row);

        if (value == "Mixed") return "app-badge app-badge-muted";

        if (IsStatus(value, "fallback", "lower")) return "app-badge app-badge-sonarr";

        if (IsStatus(value, "primary", "normal", "original")) return "app-badge app-badge-radarr";

        return "app-badge app-badge-muted";
    }

    private static string GetStateBadgeClass(MissingMediaRow row)
    {
        var value = GetStateDisplay(row);

        if (value == "Mixed") return StatusBadgeClass("waiting");

        if (IsStatus(value, "failed", "error", "blocked")) return StatusBadgeClass("failed");

        if (IsStatus(value, "search", "pushed", "running")) return StatusBadgeClass("downloading");

        if (IsStatus(value, "ready", "pending", "queued", "waiting", "delay")) return StatusBadgeClass("waiting");

        if (IsStatus(value, "complete", "found")) return StatusBadgeClass("completed");

        return StatusBadgeClass(value);
    }

    private static string? GetPosterUrl(string? imagesJson)
    {
        if (string.IsNullOrWhiteSpace(imagesJson)) return null;

        try
        {
            var images = JsonSerializer.Deserialize<List<MediaImage>>(imagesJson,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            var poster = images?.FirstOrDefault(x =>
                string.Equals(x.CoverType, "poster", StringComparison.OrdinalIgnoreCase));

            return poster?.RemoteUrl ?? poster?.Url;
        }
        catch
        {
            return null;
        }
    }

    private static bool IsSearchReady(MissingMediaRecord item)
    {
        var state = item.SearchState.ToString();

        return item.LastSearchPushedAt == null || IsStatus(state, "ready", "pending", "queued", "waiting", "missing");
    }

    private static string GetGroupKey(MissingMediaRecord item)
    {
        return item.EpisodeId == null
            ? $"{item.ServerId}_{item.RemoteId}_{item.Id}"
            : $"{item.ServerId}_{item.RemoteId}";
    }

    private static string MediaKey(int serverId, int remoteId)
    {
        return $"{serverId}_{remoteId}";
    }

    private static string EpisodeKey(int serverId, int remoteSeriesId, int remoteEpisodeId)
    {
        return $"{serverId}_{remoteSeriesId}_{remoteEpisodeId}";
    }

    private static Dictionary<string, T> BuildLookup<T>(IEnumerable<T> items, Func<T, string> keySelector)
    {
        return items.GroupBy(keySelector).ToDictionary(x => x.Key, x => x.First());
    }

    private sealed class MissingMediaRow
    {
        public string Key { get; set; } = string.Empty;
        public int ServerId { get; set; }
        public int RemoteId { get; set; }
        public List<MissingMediaRecord> Items { get; set; } = [];

        public MissingMediaRecord PrimaryRecord => Items.OrderByDescending(x => x.LastEvaluatedAt).First();

        public bool IsEpisodeGroup => Items.Any(x => x.EpisodeId != null);
        public int MissingCount => Items.Count;
        public int FailedSearchCount => Items.Sum(x => x.FailedSearchCount);
        public bool LoweredToFallback => Items.Any(x => x.LoweredToFallback);
        public DateTime FirstSeenMissingAt => Items.Min(x => x.FirstSeenMissingAt);
        public DateTime? LastSearchPushedAt => Items.Max(x => x.LastSearchPushedAt);
        public DateTime LastEvaluatedAt => Items.Max(x => x.LastEvaluatedAt);
    }

    private sealed class MediaImage
    {
        public string? CoverType { get; set; }
        public string? Url { get; set; }
        public string? RemoteUrl { get; set; }
    }
}