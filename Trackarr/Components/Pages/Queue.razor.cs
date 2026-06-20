using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Trackarr.Components.Modals;
using Trackarr.Core.Health;
using Trackarr.Core.Media.Movies;
using Trackarr.Core.Media.Shows;
using Trackarr.Core.Queue;
using Trackarr.Core.Server;
using static Trackarr.Utils;

namespace Trackarr.Components.Pages;

public partial class Queue : ComponentBase, IAsyncDisposable
{
    private const int PageSize = 10;

    private readonly CancellationTokenSource _refreshCancellation = new();
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    private MediaDetailsModal _detailsModal = null!;
    private List<DownloadItem> _downloads = [];
    private Dictionary<string, DownloadHealthEvent> _healthEvents = [];

    private Dictionary<int, Movie> _movies = [];
    private Dictionary<string, QualityProfile> _profiles = [];
    private Dictionary<int, Series> _seriesMap = [];
    private Dictionary<int, MediaServer>? _serverMap;
    private List<MediaServer> _servers = [];
    private Dictionary<string, HealthSnapshot> _snapshots = [];
    private Dictionary<string, MediaHealthSummary> _summaries = [];

    public bool IsRefreshing { get; set; }

    private int StuckCount => _downloads.Count(download => IsStatus(download.Status, "stuck", "failed", "warning"));

    private int WaitingCount =>
        _downloads.Count(download => IsStatus(download.Status, "waiting", "queued", "pending", "delay"));

    private int TotalQueueCount => _downloads.Count;

    private IReadOnlyDictionary<string, Func<DownloadItem, object?>> QueueSorts =>
        new Dictionary<string, Func<DownloadItem, object?>>
        {
            ["name"] = GetDisplayTitle,
            ["profile"] = GetQualityName,
            ["status"] = item => GetStatusText(item),
            ["score"] = item => GetSnapshot(item)?.VisibleScore ?? 0,
            ["confidence"] = item => GetSnapshot(item)?.Confidence ?? 0,
            ["progress"] = item => item.DownloadPercent,
            ["firstSeen"] = item => GetSortTicks(GetSummary(item)?.CreatedAt),
            ["lastProgress"] = item => GetSortTicks(GetSnapshot(item)?.LastGoodObservationAt),
            ["lastEval"] = item => GetSortTicks(GetHealthEvent(item)?.OccurredAt),
            ["server"] = GetServerName,
            ["mediaType"] = item => GetMediaType(item),
            ["statusBucket"] = item => GetStatusBucket(GetStatusText(item))
        };

    public async ValueTask DisposeAsync()
    {
        await _refreshCancellation.CancelAsync();
        _refreshCancellation.Dispose();
        _refreshLock.Dispose();
    }

    protected override async Task OnInitializedAsync()
    {
        await RefreshAsync();
        _ = RefreshLoopAsync();
    }

    private async Task RefreshLoopAsync()
    {
        try
        {
            using PeriodicTimer timer = new(TimeSpan.FromSeconds(30));

            while (await timer.WaitForNextTickAsync(_refreshCancellation.Token)) await RefreshAsync();
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task RefreshAsync()
    {
        if (!await _refreshLock.WaitAsync(0)) return;

        try
        {
            IsRefreshing = true;
            await InvokeAsync(StateHasChanged);

            var servers = await MediaServerService.GetAllServersAsync();
            var serverMap = servers.ToDictionary(server => server.Id);
            var downloads = new List<DownloadItem>();
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
            }

            downloads = downloads.DistinctBy(download => $"{download.ServerId}_{download.DownloadId}").ToList();

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
            _movies = movies;
            _seriesMap = seriesMap;
            _profiles = profiles;
            _snapshots = snapshots;
            _summaries = summaries;
            _healthEvents = healthEvents;
            _downloads = downloads;
        }
        finally
        {
            IsRefreshing = false;
            _refreshLock.Release();
            await InvokeAsync(StateHasChanged);
        }
    }

    private bool MatchesSearch(DownloadItem item, string search)
    {
        return Contains(GetDisplayTitle(item), search)
               || Contains(GetQualityName(item), search)
               || Contains(GetStatusText(item), search)
               || Contains(GetServerName(item), search)
               || Contains(GetMediaType(item), search)
               || Contains(GetStatusBucket(GetStatusText(item)), search);
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

        return movie != null
            ? $"{movie.Title} ({movie.Year})"
            : series != null
                ? $"{series.Title} ({series.Year})"
                : item.Title ?? "—";
    }

    private string? GetQualityName(DownloadItem item)
    {
        var movie = GetMovie(item);
        var series = GetSeries(item);

        return movie != null
            ? _profiles.GetValueOrDefault($"{movie.ServerId}_{movie.QualityProfileId}")?.Name
            : series != null
                ? _profiles.GetValueOrDefault($"{series.ServerId}_{series.QualityProfileId}")?.Name
                : null;
    }

    private static string? GetPosterUrl(string? imagesJson)
    {
        if (string.IsNullOrWhiteSpace(imagesJson)) return null;

        var images = JsonSerializer.Deserialize<List<JsonElement>>(imagesJson);
        var poster = images?.FirstOrDefault(image =>
            image.TryGetProperty("coverType", out var type) && type.GetString() == "poster");

        return poster?.TryGetProperty("remoteUrl", out var url) == true ? url.GetString() : null;
    }
}