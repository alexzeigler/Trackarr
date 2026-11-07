using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using Microsoft.JSInterop;
using Trackarr.Components.Modals;
using Trackarr.Core.Media.Missing;
using Trackarr.Core.Media.Movies;
using Trackarr.Core.Media.Shows;
using Trackarr.Core.Queue;
using Trackarr.Core.Server;
using Trackarr.Data;
using Trackarr.Services.Media;
using Trackarr.Services.Settings;
using Trackarr.Services.UI;
using static Trackarr.Utils;

namespace Trackarr.Components.Pages;

public partial class Missing : ComponentBase, IAsyncDisposable
{
    private sealed class MissingMediaRow
    {
        public string Key { get; init; } = string.Empty;
        public int ServerId { get; init; }
        public int RemoteId { get; init; }
        public List<MissingMediaRecord> Items { get; init; } = [];

        public bool IsEpisodeGroup => Items.Any(x => x.EpisodeId != null);
        public int MissingCount => Items.Count;
        public int FailedSearchCount => Items.Sum(x => x.FailedSearchCount);
        public bool LoweredToFallback => Items.Any(x => x.LoweredToFallback);
        public DateTime FirstSeenMissingAt => Items.Min(x => x.FirstSeenMissingAt);
        public DateTime? LastSearchPushedAt => Items.Max(x => x.LastSearchPushedAt);
    }

    private sealed class MediaImage
    {
        public string? CoverType { get; set; }
        public string? Url { get; set; }
        public string? RemoteUrl { get; set; }
    }

    private record SeriesLegend(string Label, string Color, int Total);

    #region Injected Services

    [Inject] private AppStateService AppState { get; set; } = null!;
    [Inject] private IDbContextFactory<AppDbContext> DbFactory { get; set; } = null!;
    [Inject] private MissingStatsService MissingStatsService { get; set; } = null!;
    [Inject] private SettingsService Settings { get; set; } = null!;
    [Inject] private IJSRuntime Js { get; set; } = null!;

    #endregion

    #region State

    private bool _hasServers = true;
    private bool _chartsInitialized;
    private bool _chartsPending;

    private MediaDetailsModal _detailsModal = null!;
    private Dictionary<string, Episode> _episodes = [];
    private List<MissingMediaRecord> _missingItems = [];
    private List<MissingMediaRow> _missingRows = [];
    private Dictionary<string, Movie> _movies = [];

    private MissingPageData _pageData = new();

    private string _searchTerm = string.Empty;
    private Dictionary<string, Series> _series = [];
    private string? _serverFilter;
    private Dictionary<int, MediaServer> _servers = [];
    private HashSet<int> _disabledServerIds = [];
    private string _sortColumn = "firstSeen";
    private bool _sortDescending = true;
    private string? _stateFilter;
    private string? _typeFilter;

    private int _page = 1;

    private int PageSize => Settings.GetInt("Display", "MissingPageSize", 10);
    private List<MissingMediaRow> PagedRows => FilteredRows.Skip((_page - 1) * PageSize).Take(PageSize).ToList();
    private int TotalPages => Math.Max(1, (int)Math.Ceiling(FilteredRows.Count / (double)PageSize));

    private string Density => Settings.GetString("Display", "TableDensity", "normal");

    private string TableDensityClass =>
        Density switch
        {
            "compact" => "table-density-compact",
            "comfortable" => "table-density-comfortable",
            _ => ""
        };

    private bool ShowMediaTypeBadges => Density != "compact";

    private bool IsRefreshing { get; set; }

    private bool HasActiveFilters =>
        !string.IsNullOrWhiteSpace(_searchTerm) || _stateFilter != null || _serverFilter != null || _typeFilter != null;

    private List<string> AvailableStates =>
        _missingRows.Select(GetStateDisplay).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(s => s).ToList();

    private List<string> AvailableTypes =>
        _missingRows.Select(GetMissingType).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(t => t).ToList();

    private (string Class, string Icon) SearchTrendDisplay =>
        _pageData.Stats.SearchTrend switch
        {
            Trend.Up => ("text-green", "ti ti-trending-up"),
            Trend.Down => ("text-red", "ti ti-trending-down"),
            _ => ("text-warning", "ti ti-minus")
        };

    private (string Class, string Icon) ResolvedTrendDisplay =>
        _pageData.Stats.ResolvedTrend switch
        {
            Trend.Up => ("text-green", "ti ti-trending-up"),
            Trend.Down => ("text-red", "ti ti-trending-down"),
            _ => ("text-warning", "ti ti-minus")
        };

    private List<SeriesLegend> ActivityLegend =>
    [
        new("Pushed", "#7738e9", _pageData.Activity.Sum(p => p.Pushed)),
        new("Cascaded", "#4299e1", _pageData.Activity.Sum(p => p.Cascaded)),
        new("Resolved", "#2fb344", _pageData.Activity.Sum(p => p.Resolved)),
        new("Failed", "#d63939", _pageData.Activity.Sum(p => p.Failed))
    ];

    private List<MissingMediaRow> FilteredRows => BuildFilteredRows();

    #endregion

    #region Lifecycle

    protected override async Task OnInitializedAsync()
    {
        _hasServers = (await MediaServerService.GetAllServersAsync()).Count > 0;
        if (!_hasServers) return;

        AppState.OnRefreshRequested += OnAutoRefreshAsync;
        await RefreshAsync();
        await RefreshChartsAsync();
    }

    public async ValueTask DisposeAsync()
    {
        AppState.OnRefreshRequested -= OnAutoRefreshAsync;

        try
        {
            await Js.InvokeVoidAsync("trackarrMissingCharts.destroyAll");
        }
        catch (JSDisconnectedException)
        {
        }
    }

    // Hidden charts are torn down; charts shown again get new elements, so they're drawn on the next render.
    private async Task OnChartsToggled(bool visible)
    {
        if (visible)
        {
            _chartsPending = true;
            return;
        }

        try
        {
            await Js.InvokeVoidAsync("trackarrMissingCharts.destroyAll");
        }
        catch (JSException)
        {
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!_chartsPending) return;
        _chartsPending = false;

        try
        {
            await Js.InvokeVoidAsync("trackarrMissingCharts.renderActivity", "missing-activity-chart",
                ActivityChartPayload());

            await Js.InvokeVoidAsync("trackarrMissingCharts.renderStateBreakdown", "missing-state-chart",
                StateBreakdownPayload());

            _chartsInitialized = true;
        }
        catch (JSException)
        {
        }
    }

    #endregion

    #region Event Handlers

    private void OnSearchChanged(ChangeEventArgs args)
    {
        _searchTerm = args.Value?.ToString() ?? "";
    }

    private void OnStateFilterChanged(ChangeEventArgs args)
    {
        _stateFilter = string.IsNullOrWhiteSpace(args.Value?.ToString()) ? null : args.Value.ToString();
    }

    private void OnServerFilterChanged(ChangeEventArgs args)
    {
        _serverFilter = string.IsNullOrWhiteSpace(args.Value?.ToString()) ? null : args.Value.ToString();
    }

    private void OnTypeFilterChanged(ChangeEventArgs args)
    {
        _typeFilter = string.IsNullOrWhiteSpace(args.Value?.ToString()) ? null : args.Value.ToString();
    }

    private void ClearFilters()
    {
        _searchTerm = string.Empty;
        _stateFilter = null;
        _serverFilter = null;
        _typeFilter = null;
        _page = 1;
    }

    private void PrevPage()
    {
        if (_page > 1) _page--;
    }

    private void NextPage()
    {
        if (_page < TotalPages) _page++;
    }

    private void SortBy(string column)
    {
        if (_sortColumn == column)
            _sortDescending = !_sortDescending;
        else
        {
            _sortColumn = column;
            _sortDescending = column is "firstSeen" or "lastSearch" or "count";
        }
    }

    private async Task OpenDetailsAsync(MissingMediaRow row)
    {
        var movie = GetMovie(row);
        if (movie != null)
        {
            await _detailsModal.Open(movie);
            return;
        }

        var series = GetSeries(row);
        if (series != null) await _detailsModal.Open(series);
    }

    private async Task SearchMedia(MissingMediaRow row)
    {
        // await, not .Result: blocking on an async call can freeze or deadlock a Blazor Server page.
        var server = await MediaServerService.GetServerByIdAsync(row.ServerId);
        if (server is null) return;

        var movie = GetMovie(row);
        if (movie != null)
        {
            await MediaServerService.TriggerMovieSearchAsync(server, movie.RemoteId);
            return;
        }

        var series = GetSeries(row);
        if (series != null)
            await MediaServerService.TriggerSeriesSearchAsync(server, series.RemoteId);
    }

    #endregion

    #region Data Loading

    // Raised from a background thread: run on the component's dispatcher so state isn't changed off-thread.
    private Task OnAutoRefreshAsync()
    {
        return InvokeAsync(async () =>
        {
            await RefreshAsync();
            await RefreshChartsAsync();
        });
    }

    private async Task RefreshChartsAsync()
    {
        var activity = await MissingStatsService.GetActivityAsync();
        _pageData.Activity = activity;
        MissingStatsService.ComputeTrends(_pageData.Stats, activity);
        _chartsPending = true;
        await InvokeAsync(StateHasChanged);
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

            var disabledBehavior = Settings.GetString("General", "DisabledServerBehavior", "hidden");
            var allServers = await db.MediaServers.AsNoTracking().Where(x => serverIds.Contains(x.Id)).ToListAsync();
            var disabledIds = new HashSet<int>(allServers.Where(s => !s.IsEnabled).Select(s => s.Id));
            var servers = disabledBehavior == "hidden" ? allServers.Where(s => s.IsEnabled).ToList() : allServers;

            if (disabledBehavior == "hidden")
                _missingItems = _missingItems.Where(x => !disabledIds.Contains(x.ServerId)).ToList();

            var movies = await db.Movies.AsNoTracking().Where(x =>
                    x.ServerId.HasValue && serverIds.Contains(x.ServerId.Value) && remoteIds.Contains(x.RemoteId))
                .ToListAsync();

            var series = await db.Series.AsNoTracking().Where(x =>
                    x.ServerId.HasValue && serverIds.Contains(x.ServerId.Value) && remoteIds.Contains(x.RemoteId))
                .ToListAsync();

            var seriesRemoteIds = series.Select(s => s.RemoteId).Distinct().ToList();

            var episodes = seriesRemoteIds.Count == 0
                ? []
                : await db.Episodes.AsNoTracking().Where(x =>
                    x.ServerId.HasValue && serverIds.Contains(x.ServerId.Value) &&
                    seriesRemoteIds.Contains(x.RemoteSeriesId)).ToListAsync();

            _servers = servers.ToDictionary(x => x.Id);
            _disabledServerIds = disabledIds;
            _movies = BuildLookup(movies, x => MediaKey(x.ServerId, x.RemoteId));
            _series = BuildLookup(series, x => MediaKey(x.ServerId, x.RemoteId));
            _episodes = BuildLookup(episodes, x => EpisodeKey(x.ServerId, x.RemoteSeriesId, x.RemoteId));

            _missingRows = _missingItems.GroupBy(GetGroupKey).Select(x => new MissingMediaRow
            {
                Key = x.Key,
                ServerId = x.First().ServerId,
                RemoteId = x.First().RemoteId,
                Items = x.OrderBy(y => y.EpisodeId ?? 0).ToList()
            }).OrderByDescending(x => x.FirstSeenMissingAt).ToList();

            var pageData = MissingStatsService.BuildStats(_missingItems);
            pageData.Activity = _pageData.Activity;
            MissingStatsService.ComputeTrends(pageData.Stats, pageData.Activity);
            _pageData = pageData;

            if (!_chartsInitialized)
                _chartsPending = true;
        }
        finally
        {
            IsRefreshing = false;
            await InvokeAsync(StateHasChanged);
        }
    }

    #endregion

    #region Display Helpers

    private List<MissingMediaRow> BuildFilteredRows()
    {
        var rows = _missingRows.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(_searchTerm))
            rows = rows.Where(r => MatchesSearch(r, _searchTerm.Trim()));

        if (_stateFilter != null)
            rows = rows.Where(r => string.Equals(GetStateDisplay(r), _stateFilter, StringComparison.OrdinalIgnoreCase));

        if (_serverFilter != null)
        {
            rows = rows.Where(r =>
                string.Equals(GetServer(r)?.Name, _serverFilter, StringComparison.OrdinalIgnoreCase));
        }

        if (_typeFilter != null)
            rows = rows.Where(r => string.Equals(GetMissingType(r), _typeFilter, StringComparison.OrdinalIgnoreCase));

        rows = _sortColumn switch
        {
            "state" => _sortDescending
                ? rows.OrderByDescending(r => GetStateDisplay(r))
                : rows.OrderBy(r => GetStateDisplay(r)),
            "count" => _sortDescending
                ? rows.OrderByDescending(r => r.MissingCount)
                : rows.OrderBy(r => r.MissingCount),
            "lastSearch" => _sortDescending
                ? rows.OrderByDescending(r => GetSortTicks(r.LastSearchPushedAt))
                : rows.OrderBy(r => GetSortTicks(r.LastSearchPushedAt)),
            "firstSeen" => _sortDescending
                ? rows.OrderByDescending(r => r.FirstSeenMissingAt)
                : rows.OrderBy(r => r.FirstSeenMissingAt),
            _ => _sortDescending ? rows.OrderByDescending(r => GetTitle(r)) : rows.OrderBy(r => GetTitle(r))
        };

        return rows.ToList();
    }

    private string SortClass(string column)
    {
        return _sortColumn != column ? "table-sort" : _sortDescending ? "table-sort desc" : "table-sort asc";
    }

    private bool MatchesSearch(MissingMediaRow row, string search)
    {
        return GetTitle(row).Contains(search, StringComparison.OrdinalIgnoreCase) ||
               GetMissingType(row).Contains(search, StringComparison.OrdinalIgnoreCase) ||
               (GetServer(row)?.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false) ||
               GetStateDisplay(row).Contains(search, StringComparison.OrdinalIgnoreCase) ||
               GetEpisodeSummary(row).Contains(search, StringComparison.OrdinalIgnoreCase);
    }

    private bool IsServerDisabled(int serverId) => _disabledServerIds.Contains(serverId);

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
        if (movie != null) return $"{TextDisplay(movie.Title)} ({movie.Year})";
        var series = GetSeries(row);
        if (series != null) return $"{TextDisplay(series.Title)} ({series.Year})";
        return $"Remote {IntDisplay(row.RemoteId)}";
    }

    private string GetEpisodeSummary(MissingMediaRow row)
    {
        if (!row.IsEpisodeGroup) return string.Empty;

        var codes = row.Items.Where(i => i.EpisodeId != null).Select(i =>
        {
            var ep = _episodes.GetValueOrDefault(EpisodeKey(i.ServerId, i.RemoteId, i.EpisodeId!.Value));
            return ep != null ? (int?)ep.SeasonNumber : null;
        }).Where(s => s != null).Select(s => s!.Value).Distinct().OrderBy(s => s).ToList();

        if (codes.Count == 0)
            return $"{row.MissingCount} episodes";

        var seasonParts = codes.Select(s =>
        {
            var count = row.Items.Count(i =>
            {
                if (i.EpisodeId == null) return false;
                var ep = _episodes.GetValueOrDefault(EpisodeKey(i.ServerId, i.RemoteId, i.EpisodeId.Value));
                return ep?.SeasonNumber == s;
            });
            return $"S{s:D2} ({count})";
        });

        return string.Join(", ", seasonParts);
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
        if (movie != null) return GetPosterUrlFromJson(movie.ImagesJson);
        var series = GetSeries(row);
        return series != null ? GetPosterUrlFromJson(series.ImagesJson) : null;
    }

    private int GetTotalEpisodeCount(MissingMediaRow row)
    {
        return MissingEpisodeCounts.GetTotalEpisodeCount(_episodes.Values, row.ServerId, row.RemoteId);
    }

    private int GetMissingEpisodeCount(MissingMediaRow row)
    {
        return MissingEpisodeCounts.GetMissingEpisodeCount(_episodes.Values, row.ServerId, row.RemoteId,
            row.IsEpisodeGroup, row.MissingCount, DateTime.UtcNow);
    }

    private static string GetStateDisplay(MissingMediaRow row)
    {
        var states = row.Items.Select(x => x.SearchState).Distinct().ToList();
        if (states.Count != 1) return "Mixed";
        return states[0] switch
        {
            MissingSearchState.New => "New",
            MissingSearchState.InQueue => "In Queue",
            MissingSearchState.SearchPushed => "Searching",
            MissingSearchState.SkippedCooldown => "Cooldown",
            MissingSearchState.SkippedSearching => "Skipped",
            MissingSearchState.NotAvailable => "Not Available",
            MissingSearchState.Abandoned => "Abandoned",
            MissingSearchState.Waiting => "Waiting",
            _ => states[0].ToString()
        };
    }

    private static string GetStateBadgeClass(MissingMediaRow row)
    {
        var value = GetStateDisplay(row);
        return value switch
        {
            "New" => "badge bg-blue-lt text-blue",
            "In Queue" => "badge bg-purple-lt text-purple",
            "Searching" => "badge bg-indigo-lt text-indigo",
            "Cooldown" => "badge bg-warning-lt text-warning",
            "Skipped" => "badge bg-secondary-lt text-secondary",
            "Not Available" => "badge bg-orange-lt text-orange",
            _ => "badge bg-secondary-lt text-secondary"
        };
    }

    private static readonly JsonSerializerOptions CaseInsensitiveJson = new() { PropertyNameCaseInsensitive = true };

    private static string? GetPosterUrlFromJson(string? imagesJson)
    {
        if (string.IsNullOrWhiteSpace(imagesJson)) return null;
        try
        {
            var images = JsonSerializer.Deserialize<List<MediaImage>>(imagesJson, CaseInsensitiveJson);
            var poster = images?.FirstOrDefault(x =>
                string.Equals(x.CoverType, "poster", StringComparison.OrdinalIgnoreCase));
            return poster?.RemoteUrl ?? poster?.Url;
        }
        catch
        {
            return null;
        }
    }

    private static string GetGroupKey(MissingMediaRecord item)
    {
        return item.EpisodeId == null
            ? $"{item.ServerId}_{item.RemoteId}_{item.Id}"
            : $"{item.ServerId}_{item.RemoteId}";
    }

    private static string MediaKey(int? serverId, int remoteId)
    {
        return $"{serverId}_{remoteId}";
    }

    private static string EpisodeKey(int? serverId, int remoteSeriesId, int remoteEpisodeId)
    {
        return $"{serverId}_{remoteSeriesId}_{remoteEpisodeId}";
    }

    private static Dictionary<string, T> BuildLookup<T>(IEnumerable<T> items, Func<T, string> keySelector)
    {
        return items.GroupBy(keySelector).ToDictionary(x => x.Key, x => x.First());
    }

    private static string FormatNumber(int value)
    {
        return value.ToString("N0", CultureInfo.InvariantCulture);
    }

    private static string FormatPercent(decimal value)
    {
        return value <= 0 ? "0%" : $"{value:0.0}%";
    }

    private static string StateDotClass(string label)
    {
        return label switch
        {
            "New" => "bg-blue",
            "In Queue" => "bg-purple",
            "Search Pushed" => "bg-indigo",
            "Cooldown" => "bg-warning",
            "Skipped" => "bg-secondary",
            "Not Available" => "bg-orange",
            _ => "bg-secondary"
        };
    }

    private object ActivityChartPayload()
    {
        return new
        {
            labels = _pageData.Activity.Select(p => p.Label).ToArray(),
            pushed = _pageData.Activity.Select(p => p.Pushed).ToArray(),
            cascaded = _pageData.Activity.Select(p => p.Cascaded).ToArray(),
            resolved = _pageData.Activity.Select(p => p.Resolved).ToArray(),
            failed = _pageData.Activity.Select(p => p.Failed).ToArray()
        };
    }

    private object StateBreakdownPayload()
    {
        return new
        {
            labels = _pageData.StateBreakdown.Select(b => b.Label).ToArray(),
            series = _pageData.StateBreakdown.Select(b => b.Count).ToArray()
        };
    }

    #endregion
}
