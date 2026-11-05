using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Trackarr.Components.Modals;
using Trackarr.Core.Exception;
using Trackarr.Core.Health;
using Trackarr.Core.Media.Movies;
using Trackarr.Core.Media.Shows;
using Trackarr.Core.Queue;
using Trackarr.Core.Server;
using Trackarr.Services.Media;
using Trackarr.Services.Routines;
using Trackarr.Services.Settings;
using Trackarr.Services.UI;
using static Trackarr.Utils;

namespace Trackarr.Components.Pages;

public partial class Queue : ComponentBase
{
    #region Parameters

    [SupplyParameterFromQuery(Name = "search")]
    public string? InitialSearch { get; set; }

    #endregion

    private sealed class QueueGroup
    {
        public string Key { get; init; } = "";
        public string Title { get; init; } = "";
        public string? PosterUrl { get; init; }
        public string MediaType { get; init; } = "";
        public string? Quality { get; init; }
        public string? ServerName { get; init; }
        public ServiceType? ServerType { get; init; }
        public List<DownloadItem> Items { get; init; } = [];
        public int AverageProgress { get; init; }
    }

    private record SeriesLegend(string Label, string Color, int Total);

    #region Injected Services

    [Inject] private AppStateService AppState { get; set; } = null!;
    [Inject] private RoutineRunner RoutineRunner { get; set; } = null!;
    [Inject] private QueueStatsService QueueStatsService { get; set; } = null!;
    [Inject] private SettingsService Settings { get; set; } = null!;
    [Inject] private IJSRuntime Js { get; set; } = null!;

    #endregion

    #region State

    private readonly HashSet<string> _expandedGroups = [];
    private readonly HashSet<string> _collapsedGroups = [];
    private bool _expandGroupsByDefault;

    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private bool _hasServers = true;
    private bool _chartsInitialized;
    private bool _chartsPending;
    private bool _disposed;
    private string? _loadError;

    private MediaDetailsModal _detailsModal = null!;
    private List<DownloadItem> _downloads = [];

    private Dictionary<int, Episode> _episodes = [];
    private Dictionary<string, DownloadHealthEvent> _healthEvents = [];
    private Dictionary<int, Movie> _movies = [];

    private QueuePageData _pageData = new();
    private Dictionary<string, QualityProfile> _profiles = [];

    private string _searchTerm = string.Empty;
    private Dictionary<int, Series> _seriesMap = [];
    private string? _serverFilter;
    private Dictionary<int, MediaServer>? _serverMap;
    private List<MediaServer> _servers = [];
    private SetScoreModal _setScoreModal = null!;
    private Dictionary<string, HealthSnapshot> _snapshots = [];
    private string _sortColumn = "name";
    private bool _sortDescending;
    private string? _statusFilter;
    private Dictionary<string, MediaHealthSummary> _summaries = [];
    private string? _typeFilter;
    private HashSet<int> _disabledServerIds = [];

    private int _page = 1;

    private int PageSize => Settings.GetInt("Display", "QueuePageSize", 10);
    private List<QueueGroup> PagedGroups => Groups.Skip((_page - 1) * PageSize).Take(PageSize).ToList();
    private int TotalPages => Math.Max(1, (int)Math.Ceiling(Groups.Count / (double)PageSize));

    private string Density => Settings.GetString("Display", "TableDensity", "normal");

    private string TableDensityClass =>
        Density switch
        {
            "compact" => "table-density-compact",
            "comfortable" => "table-density-comfortable",
            _ => ""
        };

    private bool IsCompact => Density == "compact";
    private bool IsComfortable => Density == "comfortable";
    private bool ShowPosters => !IsCompact && Settings.GetBool("Display", "ShowPosterImages", true);
    private bool ShowServerIcons => !IsCompact;
    private bool ShowMediaTypeBadges => !IsCompact;
    private bool ShowHealthBadges => !IsCompact;

    public bool IsRefreshing { get; set; }

    private decimal DownloadingPercent =>
        _pageData.Stats.TotalInQueue > 0
            ? Math.Round(_pageData.Stats.Downloading / (decimal)_pageData.Stats.TotalInQueue * 100, 1)
            : 0;

    private decimal AtRiskPercent =>
        _pageData.Stats.TotalInQueue > 0
            ? Math.Round(_pageData.Stats.AtRisk / (decimal)_pageData.Stats.TotalInQueue * 100, 1)
            : 0;

    private string AvgScoreClass =>
        _pageData.Stats.AvgHealthScore switch
        {
            <= 39 => "text-red",
            <= 59 => "text-orange",
            <= 79 => "text-warning",
            _ => "text-green"
        };

    private (string Class, string Icon) ActiveTrendDisplay =>
        _pageData.Stats.ActiveTrend switch
        {
            Trend.Up => ("text-green", "ti ti-trending-up"),
            Trend.Down => ("text-red", "ti ti-trending-down"),
            _ => ("text-warning", "ti ti-minus")
        };

    private (string Class, string Icon) AtRiskTrendDisplay =>
        _pageData.Stats.AtRiskTrend switch
        {
            Trend.Up => ("text-red", "ti ti-trending-up"),
            Trend.Down => ("text-green", "ti ti-trending-down"),
            _ => ("text-warning", "ti ti-minus")
        };

    private (string Class, string Icon) ScoreTrendDisplay =>
        _pageData.Stats.ScoreTrend switch
        {
            Trend.Up => ("text-green", "ti ti-trending-up"),
            Trend.Down => ("text-red", "ti ti-trending-down"),
            _ => ("text-warning", "ti ti-minus")
        };

    private List<SeriesLegend> ActivityLegend =>
    [
        new("Added", "#4299e1", _pageData.Activity.Sum(p => p.Added)),
        new("Completed", "#2fb344", _pageData.Activity.Sum(p => p.Completed)),
        new("Failed", "#d63939", _pageData.Activity.Sum(p => p.Failed)),
        new("Removed", "#f76707", _pageData.Activity.Sum(p => p.Removed))
    ];

    private bool HasActiveFilters =>
        !string.IsNullOrWhiteSpace(_searchTerm) || _statusFilter != null || _serverFilter != null ||
        _typeFilter != null;

    private List<string> AvailableStatuses =>
        _downloads.Select(d => GetStatusBucket(GetStatusText(d))).Where(s => !string.IsNullOrEmpty(s))
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(s => s).ToList();

    private List<string> AvailableTypes =>
        _downloads.Select(GetMediaType).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(t => t).ToList();

    private List<QueueGroup> Groups => BuildGroups();

    #endregion

    #region Lifecycle

    protected override async Task OnInitializedAsync()
    {
        if (!string.IsNullOrWhiteSpace(InitialSearch))
            _searchTerm = InitialSearch;

        _expandGroupsByDefault = Settings.GetBool("Display", "ExpandQueueGroupsByDefault", true);

        AppState.OnRefreshRequested += OnAutoRefreshAsync;
        await RefreshAsync();
        await RefreshChartsAsync();
    }

    protected override void OnParametersSet()
    {
        if (InitialSearch is not null && InitialSearch != _searchTerm)
            _searchTerm = InitialSearch;
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!_chartsPending) return;
        _chartsPending = false;

        try
        {
            await Js.InvokeVoidAsync("trackarrQueueCharts.renderActivity", "queue-activity-chart",
                ActivityChartPayload());

            await Js.InvokeVoidAsync("trackarrQueueCharts.renderStatusBreakdown", "queue-status-chart",
                StatusBreakdownPayload());

            await Js.InvokeVoidAsync("trackarrQueueCharts.renderHealthDistribution", "queue-health-chart",
                HealthDistributionPayload());

            _chartsInitialized = true;
        }
        catch (JSException)
        {
        }
    }

    public async ValueTask DisposeAsync()
    {
        _disposed = true;
        AppState.OnRefreshRequested -= OnAutoRefreshAsync;
        _refreshLock.Dispose();

        try
        {
            await Js.InvokeVoidAsync("trackarrQueueCharts.destroyAll");
        }
        catch (JSDisconnectedException)
        {
        }
    }

    #endregion

    #region Event Handlers

    private void OnSearchChanged(ChangeEventArgs args)
    {
        _searchTerm = args.Value?.ToString() ?? "";
    }

    private void OnStatusFilterChanged(ChangeEventArgs args)
    {
        _statusFilter = string.IsNullOrWhiteSpace(args.Value?.ToString()) ? null : args.Value.ToString();
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
        _statusFilter = null;
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
        {
            _sortDescending = !_sortDescending;
        }
        else
        {
            _sortColumn = column;
            _sortDescending = false;
        }
    }

    private void ToggleGroup(string groupKey)
    {
        if (_expandGroupsByDefault)
        {
            if (!_collapsedGroups.Add(groupKey))
                _collapsedGroups.Remove(groupKey);
        }
        else
        {
            if (!_expandedGroups.Add(groupKey))
                _expandedGroups.Remove(groupKey);
        }
    }

    private async Task OpenGroupDetails(QueueGroup group, DownloadItem firstItem, Movie? movie, Series? series)
    {
        await _detailsModal.Open(firstItem, firstItem.ServerId, movie, series);
    }

    private async Task EvaluateGroupAsync(QueueGroup group)
    {
        IsRefreshing = true;
        await InvokeAsync(StateHasChanged);

        try
        {
            foreach (var item in group.Items)
                await HealthService.EvaluateAsync(item);

            await RefreshAsync();
        }
        finally
        {
            IsRefreshing = false;
            await InvokeAsync(StateHasChanged);
        }
    }

    private async Task EvaluateItemAsync(DownloadItem item)
    {
        IsRefreshing = true;
        await InvokeAsync(StateHasChanged);

        try
        {
            await HealthService.EvaluateAsync(item);
            await RefreshAsync();
        }
        finally
        {
            IsRefreshing = false;
            await InvokeAsync(StateHasChanged);
        }
    }

    private async Task OpenSetScore(DownloadItem item, HealthSnapshot? snapshot)
    {
        await _setScoreModal.Open(item, GetChildLabel(item), snapshot?.VisibleScore, snapshot?.Confidence);
    }

    private async Task RemoveItemAsync(DownloadItem item)
    {
        var server = _serverMap?.GetValueOrDefault(item.ServerId);
        if (server == null) return;

        IsRefreshing = true;
        await InvokeAsync(StateHasChanged);

        try
        {
            await MediaServerService.RemoveFromQueueAsync(server, item.RemoteId);
            await HealthService.MarkRemovedByUserAsync(item);
            RoutineRunner.RunAsync("queue_refresh");
            await RefreshAsync();
        }
        finally
        {
            IsRefreshing = false;
            await InvokeAsync(StateHasChanged);
        }
    }

    private async Task RemoveGroupAsync(QueueGroup group)
    {
        IsRefreshing = true;
        await InvokeAsync(StateHasChanged);

        try
        {
            foreach (var item in group.Items)
            {
                var server = _serverMap?.GetValueOrDefault(item.ServerId);
                if (server == null) continue;

                await MediaServerService.RemoveFromQueueAsync(server, item.RemoteId);
                await HealthService.MarkRemovedByUserAsync(item);
            }

            RoutineRunner.RunAsync("queue_refresh");
            await RefreshAsync();
        }
        finally
        {
            IsRefreshing = false;
            await InvokeAsync(StateHasChanged);
        }
    }

    #endregion

    #region Data Loading

    private async Task OnAutoRefreshAsync()
    {
        await RefreshAsync();
        await RefreshChartsAsync();
    }

    private async Task RefreshChartsAsync()
    {
        var activity = await QueueStatsService.GetActivityAsync();
        _pageData.Activity = activity;

        var stats = _pageData.Stats;
        await QueueStatsService.ComputeTrendsAsync(stats, activity);

        _chartsPending = true;
        await InvokeAsync(StateHasChanged);
    }

    private async Task RefreshAsync()
    {
        if (_disposed) return;
        if (!await _refreshLock.WaitAsync(0)) return;

        try
        {
            IsRefreshing = true;
            await InvokeAsync(StateHasChanged);

            _loadError = null;
            var allServers = await MediaServerService.GetAllServersAsync();
            _hasServers = allServers.Count > 0;
            if (!_hasServers) return;

            var disabledBehavior = Settings.GetString("General", "DisabledServerBehavior", "hidden");
            var servers = disabledBehavior == "hidden" ? allServers.Where(s => s.IsEnabled).ToList() : allServers;
            var disabledIds = new HashSet<int>(allServers.Where(s => !s.IsEnabled).Select(s => s.Id));
            var serverMap = servers.ToDictionary(server => server.Id);
            var downloads = new List<DownloadItem>();
            var episodes = new Dictionary<int, Episode>();
            var movies = new Dictionary<int, Movie>();
            var seriesMap = new Dictionary<int, Series>();

            foreach (var server in servers)
            {
                var items = await MediaServerService.GetQueueAsync(server);

                foreach (var item in items) item.ServerId = server.Id;

                downloads.AddRange(items);

                foreach (var kvp in await MediaServerService.GetParentMovieAsync(server.Id, items))
                    movies[kvp.Key] = kvp.Value;

                foreach (var kvp in await MediaServerService.GetParentSeriesAsync(server.Id, items))
                    seriesMap[kvp.Key] = kvp.Value;

                foreach (var kvp in await MediaServerService.GetQueueEpisodesAsync(server.Id, items))
                    episodes[kvp.Key] = kvp.Value;
            }

            // Same key the evaluation routine uses. Items with no download ID (pending releases, for example)
            // fall back to queue ID + title instead of all collapsing into one "{server}_" row.
            downloads = downloads.DistinctBy(download => $"{download.ServerId}_{HealthService.GetDownloadId(download)}")
                .ToList();

            var profiles = new Dictionary<string, QualityProfile>();

            foreach (var server in servers)
            foreach (var profile in await MediaServerService.GetProfilesAsync(server))
                profiles[$"{profile.ServerId}_{profile.RemoteId}"] = profile;

            var snapshots = new Dictionary<string, HealthSnapshot>();
            var summaries = new Dictionary<string, MediaHealthSummary>();
            var healthEvents = new Dictionary<string, DownloadHealthEvent>();

            foreach (var item in downloads)
            {
                var key = GetItemKey(item);

                if (await HealthService.GetMostRecentSnapshotAsync(item) is { } snapshot) snapshots[key] = snapshot;

                if (await HealthService.GetMediaSummary(item) is { } summary) summaries[key] = summary;

                if (await HealthService.GetMostRecentHealthEventAsync(item) is { } healthEvent)
                    healthEvents[key] = healthEvent;
            }

            _servers = servers;
            _serverMap = serverMap;
            _disabledServerIds = disabledIds;
            _episodes = episodes;
            _movies = movies;
            _seriesMap = seriesMap;
            _profiles = profiles;
            _snapshots = snapshots;
            _summaries = summaries;
            _healthEvents = healthEvents;
            _downloads = downloads;

            var pageData = QueueStatsService.BuildStats(downloads, snapshots, summaries, GetItemKey);
            pageData.Activity = _pageData.Activity;
            await QueueStatsService.ComputeTrendsAsync(pageData.Stats, pageData.Activity);
            _pageData = pageData;

            if (!_chartsInitialized)
                _chartsPending = true;
        }
        catch (MediaServerException ex)
        {
            _loadError = ex.Message;
        }
        finally
        {
            IsRefreshing = false;

            if (!_disposed)
            {
                _refreshLock.Release();
                await InvokeAsync(StateHasChanged);
            }
        }
    }

    #endregion

    #region Display Helpers

    private List<QueueGroup> BuildGroups()
    {
        var filtered = _downloads.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(_searchTerm))
            filtered = filtered.Where(item => MatchesSearch(item, _searchTerm.Trim()));

        if (_statusFilter != null)
        {
            filtered = filtered.Where(item => string.Equals(GetStatusBucket(GetStatusText(item)), _statusFilter,
                StringComparison.OrdinalIgnoreCase));
        }

        if (_serverFilter != null)
        {
            filtered = filtered.Where(item =>
                string.Equals(GetServerName(item), _serverFilter, StringComparison.OrdinalIgnoreCase));
        }

        if (_typeFilter != null)
        {
            filtered = filtered.Where(item =>
                string.Equals(GetMediaType(item), _typeFilter, StringComparison.OrdinalIgnoreCase));
        }

        var grouping = Settings.GetString("Display", "DefaultQueueGrouping", "mediaTitle");
        var groups = filtered.GroupBy(GetGroupKey).Select(group =>
        {
            var items = group.ToList();
            var first = items[0];
            var server = _serverMap?.GetValueOrDefault(first.ServerId);
            var title = grouping switch
            {
                "server" => server?.Name ?? $"Server {first.ServerId}",
                "status" => GetStatusBucket(GetStatusText(first)),
                "mediaType" => GetMediaType(first),
                "mediaTitle" => GetDisplayTitle(first),
                _ => first.Title ?? "—"
            };
            var isMediaGrouped = grouping is "mediaTitle";
            return new QueueGroup
            {
                Key = group.Key,
                Title = title,
                PosterUrl = isMediaGrouped ? GetGroupPosterUrl(first) : null,
                MediaType = GetMediaType(first),
                Quality = isMediaGrouped ? GetQualityName(first) : null,
                ServerName = server?.Name,
                ServerType = server?.ServiceType,
                Items = items,
                AverageProgress = items.Count > 0 ? (int)items.Average(i => i.DownloadPercent) : 0
            };
        });

        groups = _sortColumn switch
        {
            "status" => _sortDescending
                ? groups.OrderByDescending(g => GetGroupStatusSummary(g))
                : groups.OrderBy(g => GetGroupStatusSummary(g)),
            "progress" => _sortDescending
                ? groups.OrderByDescending(g => g.AverageProgress)
                : groups.OrderBy(g => g.AverageProgress),
            "seen" => _sortDescending
                ? groups.OrderByDescending(g => GetSortTicks(GetGroupOldestSeen(g)))
                : groups.OrderBy(g => GetSortTicks(GetGroupOldestSeen(g))),
            "evaluated" => _sortDescending
                ? groups.OrderByDescending(g => GetSortTicks(GetGroupOldestEvaluated(g)))
                : groups.OrderBy(g => GetSortTicks(GetGroupOldestEvaluated(g))),
            _ => _sortDescending ? groups.OrderByDescending(g => g.Title) : groups.OrderBy(g => g.Title)
        };

        return groups.ToList();
    }

    private string GetGroupKey(DownloadItem item)
    {
        var grouping = Settings.GetString("Display", "DefaultQueueGrouping", "mediaTitle");
        return grouping switch
        {
            "server" => $"server_{item.ServerId}",
            "status" => $"status_{GetStatusBucket(GetStatusText(item))}",
            "mediaType" => $"type_{GetMediaType(item)}",
            "mediaTitle" => GetMediaGroupKey(item),
            _ => $"{item.ServerId}_{item.RemoteId}"
        };
    }

    private static string GetMediaGroupKey(DownloadItem item)
    {
        if (item.SeriesId > 0) return $"series_{item.ServerId}_{item.SeriesId}";
        if (item.MovieId > 0) return $"movie_{item.ServerId}_{item.MovieId}";
        return $"{item.ServerId}_{item.RemoteId}";
    }

    private string? GetGroupPosterUrl(DownloadItem item)
    {
        var movie = GetMovie(item);
        var series = GetSeries(item);
        return movie != null ? GetPosterUrl(movie.ImagesJson) : series != null ? GetPosterUrl(series.ImagesJson) : null;
    }

    private bool IsExpanded(string groupKey)
    {
        if (_expandGroupsByDefault)
            return !_collapsedGroups.Contains(groupKey);
        return _expandedGroups.Contains(groupKey);
    }

    private string GetChildLabel(DownloadItem item)
    {
        var episode = item.EpisodeId > 0 ? _episodes.GetValueOrDefault(item.EpisodeId) : null;
        var episodeCode = GetEpisodeCode(item);

        if (episode?.Title != null && episodeCode != null)
            return $"{episode.Title} ({episodeCode})";

        if (episode?.Title != null)
            return episode.Title;

        if (episodeCode != null)
            return episodeCode;

        return item.Title ?? "—";
    }

    private static string? GetEpisodeCode(DownloadItem item)
    {
        if (item.SeasonNumber > 0 || item.EpisodeNumber > 0)
            return $"S{item.SeasonNumber:D2}E{item.EpisodeNumber:D2}";

        return ParseEpisodeFromTitle(item.Title);
    }

    private static string? ParseEpisodeFromTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return null;

        var match = Regex.Match(title, @"S(\d{2,})E(\d{2,})", RegexOptions.IgnoreCase);

        return match.Success ? $"S{match.Groups[1].Value}E{match.Groups[2].Value}" : null;
    }

    private static (int Season, int Episode)? ParseEpisodeTuple(DownloadItem item)
    {
        if (item.SeasonNumber > 0 || item.EpisodeNumber > 0)
            return (item.SeasonNumber, item.EpisodeNumber);

        if (string.IsNullOrWhiteSpace(item.Title)) return null;

        var match = Regex.Match(item.Title, @"S(\d{2,})E(\d{2,})", RegexOptions.IgnoreCase);

        return match.Success ? (int.Parse(match.Groups[1].Value), int.Parse(match.Groups[2].Value)) : null;
    }

    private static string GetEpisodeSummary(List<DownloadItem> items)
    {
        var episodes = items.Select(ParseEpisodeTuple).Where(e => e != null).Select(e => e!.Value).Distinct()
            .OrderBy(e => e.Season).ThenBy(e => e.Episode).ToList();

        if (episodes.Count == 0)
            return $"{items.Count} downloads";

        var bySeason = episodes.GroupBy(e => e.Season).OrderBy(g => g.Key);
        var parts = new List<string>();

        foreach (var season in bySeason)
        {
            var sorted = season.Select(e => e.Episode).OrderBy(e => e).ToList();
            var ranges = BuildRanges(sorted);
            var seasonNum = season.Key;

            if (ranges.Count == 1 && ranges[0].Start == ranges[0].End)
            {
                parts.Add($"S{seasonNum:D2}E{ranges[0].Start:D2}");
            }
            else
            {
                var rangeStrs = ranges.Select(r =>
                    r.Start == r.End ? $"S{seasonNum:D2}E{r.Start:D2}" : $"S{seasonNum:D2}E{r.Start:D2}-E{r.End:D2}");
                parts.AddRange(rangeStrs);
            }
        }

        return string.Join(", ", parts);
    }

    private static List<(int Start, int End)> BuildRanges(List<int> sorted)
    {
        var ranges = new List<(int Start, int End)>();
        if (sorted.Count == 0) return ranges;

        var start = sorted[0];
        var end = sorted[0];

        for (var i = 1; i < sorted.Count; i++)
        {
            if (sorted[i] == end + 1)
            {
                end = sorted[i];
            }
            else
            {
                ranges.Add((start, end));
                start = sorted[i];
                end = sorted[i];
            }
        }

        ranges.Add((start, end));
        return ranges;
    }

    private string GetGroupStatusSummary(QueueGroup group)
    {
        var completed = group.Items.Count(i => i.DownloadPercent >= 100);
        if (completed == group.Items.Count) return "Completed";
        var downloading = group.Items.Count(i => i.DownloadPercent is > 0 and < 100);
        if (downloading > 0) return "Downloading";
        return GetStatusText(group.Items[0]);
    }

    private string GetGroupStatusClass(QueueGroup group)
    {
        return StatusBadgeClass(GetGroupStatusSummary(group));
    }

    private string SortClass(string column)
    {
        return _sortColumn != column ? "table-sort" : _sortDescending ? "table-sort desc" : "table-sort asc";
    }

    private bool MatchesSearch(DownloadItem item, string search)
    {
        return Contains(GetDisplayTitle(item), search) || Contains(GetQualityName(item), search) ||
               Contains(GetStatusText(item), search) || Contains(GetServerName(item), search) ||
               Contains(GetMediaType(item), search) || Contains(GetStatusBucket(GetStatusText(item)), search);
    }

    private static bool Contains(string? value, string search)
    {
        return value?.Contains(search, StringComparison.OrdinalIgnoreCase) == true;
    }

    private static string GetItemKey(DownloadItem item)
    {
        return $"{item.ServerId}_{item.RemoteId}";
    }

    private HealthSnapshot? GetSnapshot(DownloadItem item)
    {
        return _snapshots.GetValueOrDefault(GetItemKey(item));
    }

    private MediaHealthSummary? GetSummary(DownloadItem item)
    {
        return _summaries.GetValueOrDefault(GetItemKey(item));
    }

    private DownloadHealthEvent? GetHealthEvent(DownloadItem item)
    {
        return _healthEvents.GetValueOrDefault(GetItemKey(item));
    }

    private bool IsServerDisabled(int serverId) => _disabledServerIds.Contains(serverId);

    private string? GetServerName(DownloadItem item)
    {
        return _serverMap?.GetValueOrDefault(item.ServerId)?.Name;
    }

    private Movie? GetMovie(DownloadItem item)
    {
        return item.MovieId > 0 && _movies.TryGetValue(item.MovieId, out var movie) ? movie : null;
    }

    private Series? GetSeries(DownloadItem item)
    {
        return item.SeriesId > 0 && _seriesMap.TryGetValue(item.SeriesId, out var series) ? series : null;
    }

    private string GetDisplayTitle(DownloadItem item)
    {
        var movie = GetMovie(item);
        var series = GetSeries(item);

        return movie != null ? $"{movie.Title} ({movie.Year})" :
            series != null ? $"{series.Title} ({series.Year})" : item.Title ?? "—";
    }

    private string? GetQualityName(DownloadItem item)
    {
        var movie = GetMovie(item);
        var series = GetSeries(item);

        return movie != null ? _profiles.GetValueOrDefault($"{movie.ServerId}_{movie.QualityProfileId}")?.Name :
            series != null ? _profiles.GetValueOrDefault($"{series.ServerId}_{series.QualityProfileId}")?.Name : null;
    }

    private static string? GetPosterUrl(string? imagesJson)
    {
        if (string.IsNullOrWhiteSpace(imagesJson)) return null;

        var images = JsonSerializer.Deserialize<List<JsonElement>>(imagesJson);
        var poster = images?.FirstOrDefault(image =>
            image.TryGetProperty("coverType", out var type) && type.GetString() == "poster");

        return poster?.TryGetProperty("remoteUrl", out var url) == true ? url.GetString() : null;
    }

    private DateTime? GetGroupOldestSeen(QueueGroup group)
    {
        return group.Items.Select(i => GetSummary(i)?.CreatedAt).Where(d => d.HasValue).OrderBy(d => d)
            .FirstOrDefault();
    }

    private DateTime? GetGroupOldestEvaluated(QueueGroup group)
    {
        return group.Items.Select(i => GetHealthEvent(i)?.OccurredAt).Where(d => d.HasValue).OrderBy(d => d)
            .FirstOrDefault();
    }

    private static string GetGroupDownloadClient(QueueGroup group)
    {
        var clients = group.Items.Select(i => i.DownloadClient).Where(c => !string.IsNullOrWhiteSpace(c))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return clients.Count switch
        {
            0 => "",
            1 => clients[0]!,
            _ => $"{clients[0]} +{clients.Count - 1}"
        };
    }

    private static string GetGroupIndexer(QueueGroup group)
    {
        var indexers = group.Items.Select(i => i.Indexer).Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return indexers.Count switch
        {
            0 => "",
            1 => indexers[0]!,
            _ => $"{indexers[0]} +{indexers.Count - 1}"
        };
    }

    private static long GetGroupTotalSize(QueueGroup group)
    {
        return group.Items.Sum(i => i.Size);
    }

    private static string FormatNumber(int value)
    {
        return value.ToString("N0", CultureInfo.InvariantCulture);
    }

    private static string FormatPercent(decimal value)
    {
        return value <= 0 ? "0%" : $"{value:0.0}%";
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes <= 0) return "—";

        string[] units = ["B", "KB", "MB", "GB", "TB", "PB"];
        var size = (double)bytes;
        var unit = 0;

        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }

        return $"{size:0.##} {units[unit]}";
    }

    private static string FormatDuration(TimeSpan? value)
    {
        if (value == null || value.Value.TotalSeconds <= 0) return "—";

        if (value.Value.TotalDays >= 1) return $"{(int)value.Value.TotalDays}d {value.Value.Hours}h";

        if (value.Value.TotalHours >= 1) return $"{(int)value.Value.TotalHours}h {value.Value.Minutes}m";

        return $"{Math.Max(1, value.Value.Minutes)}m";
    }

    private static string BreakdownDotClass(string label)
    {
        return label.ToLowerInvariant() switch
        {
            "downloading" => "bg-success",
            "importing" => "bg-blue",
            "queued" => "bg-purple",
            "stalled" => "bg-warning",
            "failed" => "bg-danger",
            "warning" => "bg-orange",
            _ => "bg-secondary"
        };
    }

    private static string ScoreBucketDotClass(string label)
    {
        return label.ToLowerInvariant() switch
        {
            _ when label.Contains("Critical") => "bg-danger",
            _ when label.Contains("Poor") => "bg-orange",
            _ when label.Contains("Fair") => "bg-warning",
            _ when label.Contains("Good") => "bg-blue",
            _ when label.Contains("Healthy") => "bg-success",
            _ => "bg-secondary"
        };
    }

    private object ActivityChartPayload()
    {
        return new
        {
            labels = _pageData.Activity.Select(p => p.Label).ToArray(),
            added = _pageData.Activity.Select(p => p.Added).ToArray(),
            completed = _pageData.Activity.Select(p => p.Completed).ToArray(),
            failed = _pageData.Activity.Select(p => p.Failed).ToArray(),
            removed = _pageData.Activity.Select(p => p.Removed).ToArray()
        };
    }

    private object StatusBreakdownPayload()
    {
        return new
        {
            labels = _pageData.StatusBreakdown.Select(b => b.Label).ToArray(),
            series = _pageData.StatusBreakdown.Select(b => b.Count).ToArray()
        };
    }

    private object HealthDistributionPayload()
    {
        return new
        {
            labels = _pageData.ScoreDistribution.Select(b => b.Label.Split(' ')[0]).ToArray(),
            series = _pageData.ScoreDistribution.Select(b => b.Count).ToArray(),
            colors = new[] { "#d63939", "#f76707", "#f4b83f", "#4299e1", "#2fb344" }
        };
    }

    #endregion
}
