using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Trackarr.Components.Modals;
using Trackarr.Core.Media.Movies;
using Trackarr.Core.Media.Shows;
using Trackarr.Core.Queue;
using Trackarr.Core.Server;

namespace Trackarr.Components.Pages;

public partial class Queue : ComponentBase {
    private readonly Dictionary<int, Movie> _movies = [];

    private readonly CancellationTokenSource _refreshCancellation = new();
    private readonly Dictionary<int, Series> _seriesMap = [];
    private MediaDetailsModal _detailsModal = null!;
    private List<DownloadItem> _downloads = [];

    private int _pageSize = 10;
    private string? _priorityFilter;

    private int? _serverFilter;
    private List<MediaServer> _servers = [];
    private string? _statusFilter;
    private string? _typeFilter;

    private IEnumerable<DownloadItem> FilteredDownloads => _downloads.Where(item => (_serverFilter == null || item.ServerId == _serverFilter) && (_typeFilter == null || GetMediaType(item) == _typeFilter) && (_statusFilter == null || GetStatusText(item) == _statusFilter));

    private int StuckCount => _downloads.Count(download => IsStatus(download.Status, "stuck", "failed", "warning"));

    private int WaitingCount => _downloads.Count(download => IsStatus(download.Status,
    "waiting",
    "queued",
    "pending",
    "delay"));

    private int TotalQueueCount => _downloads.Count;

    private int MissingCount { get; }

    public async ValueTask DisposeAsync() {
        await _refreshCancellation.CancelAsync();
        _refreshCancellation.Dispose();
    }

    private void SetServerFilter(int? id) => _serverFilter = id;
    private void SetTypeFilter(string? type) => _typeFilter = type;
    private void SetStatusFilter(string? status) => _statusFilter = status;
    private void SetPriorityFilter(string? priority) => _priorityFilter = priority;

    private static bool IsStatus(string? status, params string[] statuses) {
        if (string.IsNullOrWhiteSpace(status)) {
            return false;
        }

        return statuses.Any(value => status.Contains(value, StringComparison.OrdinalIgnoreCase));
    }

    protected override async void OnInitialized() {
        base.OnInitialized();
        _servers = await MediaServerService.GetAllServersAsync();
        // LoadRandomData();
        await RefreshQueue();
        _ = RefreshLoopAsync();
    }

    protected async Task RefreshQueue() {
        _servers = await MediaServerService.GetAllServersAsync();
        _downloads.Clear();
        foreach (MediaServer server in _servers) {
            List<DownloadItem> items = await MediaServerService.GetQueueAsync(server);
            _downloads.AddRange(items);

            Dictionary<int, Movie> movieLookup = await MediaServerService.GetParentMovieAsync(server.Id, items);
            Dictionary<int, Series> seriesLookup = await MediaServerService.GetParentSeriesAsync(server.Id, items);

            foreach (KeyValuePair<int, Movie> kvp in movieLookup) _movies[kvp.Key] = kvp.Value;
            foreach (KeyValuePair<int, Series> kvp in seriesLookup) _seriesMap[kvp.Key] = kvp.Value;
        }
        _downloads = _downloads.DistinctBy(d => $"{d.ServerId}_{d.RemoteId}").ToList();
    }

    protected override async Task OnAfterRenderAsync(bool firstRender) {
        if (!firstRender || _downloads.Count == 0) {
            return;
        }

        await Js.InvokeVoidAsync("trackarrTable.init",
        "advanced-table",
        "advanced-table-search",
        "page-count",
        _pageSize,
        new[] {
            "sort-name",
            "sort-server",
            "sort-type",
            "sort-status",
            "sort-score",
            "sort-progress",
            "sort-activity",
            "sort-added"
        });
    }
    private static string? GetPosterUrl(string? imagesJson) {
        if (string.IsNullOrWhiteSpace(imagesJson)) return null;

        List<JsonElement>? images = JsonSerializer.Deserialize<List<JsonElement>>(imagesJson);
        JsonElement? poster = images?.FirstOrDefault(i => i.TryGetProperty("coverType", out JsonElement t) && t.GetString() == "poster");

        return poster?.TryGetProperty("remoteUrl", out JsonElement url) == true ? url.GetString() : null;
    }

    private async Task RefreshLoopAsync() {
        using PeriodicTimer timer = new(TimeSpan.FromSeconds(1));

        while (await timer.WaitForNextTickAsync(_refreshCancellation.Token)) {
            // LoadRandomData();
            await RefreshQueue();
            await InvokeAsync(StateHasChanged);
        }
    }

    private void LoadRandomData() {
        DateTime now = DateTime.UtcNow;

        string[] titles = [
            "The Batman (2022)",
            "Breaking Bad",
            "Dune: Part Two (2024)",
            "The Last of Us",
            "Interstellar (2014)",
            "Severance",
            "Silo",
            "Alien: Romulus (2024)",
            "Better Call Saul",
            "The Penguin",
            "Fallout",
            "The Matrix (1999)",
            "House of the Dragon",
            "Oppenheimer (2023)",
            "True Detective"
        ];

        string[] statuses = [
            "Downloading",
            "Queued",
            "Waiting",
            "Importing",
            "Completed",
            "Warning",
            "Error",
            "Stuck",
            "Ignored",
            "Unknown"
        ];

        _downloads = Enumerable
            .Range(1, 45)
            .Select(index => {
                bool isMovie = index % 3 != 0;
                long size = Random.Shared.NextInt64(2_500_000_000, 80_000_000_000);
                int percent = Random.Shared.Next(1, 100);
                long sizeLeft = size - (long)(size * (percent / 100d));
                DateTime added = now.AddDays(-Random.Shared.Next(0, 30)).AddMinutes(-Random.Shared.Next(0, 1440));
                DateTime lastSeen = now.AddSeconds(-Random.Shared.Next(5, 7200));
                string status = statuses[Random.Shared.Next(statuses.Length)];

                return new DownloadItem {
                    RemoteId = index,
                    Title = titles[Random.Shared.Next(titles.Length)],
                    Size = size,
                    SizeLeft = sizeLeft,
                    Status = status,
                    TrackedDownloadStatus = status,
                    TrackedDownloadState = status,
                    DownloadId = $"test-download-{index}",
                    DownloadClient = Random.Shared.Next(0, 2) == 0 ? "qBittorrent" : "SABnzbd",
                    Indexer = Random.Shared.Next(0, 2) == 0 ? "Prowlarr" : "NZBGeek",
                    Added = added,
                    LastSeenAtUtc = lastSeen,
                    LastProgressAtUtc = lastSeen.AddMinutes(-Random.Shared.Next(0, 60)),
                    ServerId = isMovie ? 1 : 2,
                    MovieId = isMovie ? Random.Shared.Next(1, 5000) : 0,
                    SeriesId = isMovie ? 0 : Random.Shared.Next(1, 5000),
                    EpisodeId = isMovie ? 0 : Random.Shared.Next(1, 5000),
                    CustomFormatScore = Random.Shared.Next(0, 101),
                    QueueStatus = QueueStatus.Pending
                };
            })
            .ToList();
    }

    private ValueTask SetPageSizeAsync(int pageSize) {
        _pageSize = pageSize;

        return Js.InvokeVoidAsync("trackarrTable.setPageSize", "advanced-table", "page-count", pageSize);
    }

    private static int GetScore(DownloadItem item) => item.CustomFormatScore;

    private static string GetServerName(DownloadItem item) => item.ServerId == 1 ? "Radarr" : "Sonarr";

    private static string GetMediaType(DownloadItem item) => item.MovieId > 0 ? "Movie" : "Series";

    private static string GetStatusText(DownloadItem item) => string.IsNullOrWhiteSpace(item.Status) ? "Unknown" : item.Status;

    private static string GetServerBadgeClass(string server) => server.Equals("Radarr", StringComparison.OrdinalIgnoreCase) ? "triage-badge-radarr" : "triage-badge-sonarr";

    private static string GetStatusBadgeClass(string? status) {
        return NormalizeStatus(status) switch {
            "stuck" => "status-badge status-stalled",
            "stalled" => "status-badge status-stalled",
            "warning" => "status-badge status-stalled",
            "waiting" => "status-badge status-paused",
            "delay" => "status-badge status-paused",
            "paused" => "status-badge status-paused",
            "queued" => "status-badge status-queued",
            "importing" => "status-badge status-importing",
            "importpending" => "status-badge status-importing",
            "importblocked" => "status-badge status-stalled",
            "downloading" => "status-badge status-downloading",
            "completed" => "status-badge status-paused",
            "failed" => "status-badge status-failed",
            "error" => "status-badge status-failed",
            "failedpending" => "status-badge status-failed",
            "downloadclientunavailable" => "status-badge status-failed",
            _ => "status-badge status-paused"
        };
    }


    private static string NormalizeStatus(string? status) => string.IsNullOrWhiteSpace(status) ? string.Empty : status.Trim().Replace("-", "").Replace("_", "").Replace(" ", "").ToLowerInvariant();

    private static int ClampScore(int score) => Math.Clamp(score, 0, 100);

    private static string GetScoreBadgeClass(int score) {
        if (score < 0) {
            return "triage-score-badge triage-score-removed";
        }

        score = Math.Clamp(score, 0, 100);

        return score switch {
            <= 9 => "triage-score-badge triage-score-red",
            <= 19 => "triage-score-badge triage-score-red",
            <= 29 => "triage-score-badge triage-score-red",
            <= 39 => "triage-score-badge triage-score-red",
            <= 49 => "triage-score-badge triage-score-orange",
            <= 59 => "triage-score-badge triage-score-orange",
            <= 69 => "triage-score-badge triage-score-orange",
            <= 79 => "triage-score-badge triage-score-yellow",
            <= 89 => "triage-score-badge triage-score-yellow",
            _ => "triage-score-badge triage-score-green"
        };
    }

    private static string GetRelativeTime(DateTime? value) {
        if (value is null) {
            return "—";
        }

        TimeSpan diff = DateTime.UtcNow - value.Value.ToUniversalTime();

        if (diff.TotalSeconds < 60) {
            return $"{Math.Max(1, (int)diff.TotalSeconds)}s ago";
        }

        if (diff.TotalMinutes < 60) {
            return $"{(int)diff.TotalMinutes}m ago";
        }

        return diff.TotalHours < 24 ? $"{(int)diff.TotalHours}h ago" : $"{(int)diff.TotalDays}d ago";

    }

    private static long GetSortTicks(DateTime? value) => value?.ToUniversalTime().Ticks ?? 0;
}