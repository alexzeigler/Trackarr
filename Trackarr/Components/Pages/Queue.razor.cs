using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Trackarr.Components.Modals;
using Trackarr.Core.Health;
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

    private int? _serverFilter;
    private List<MediaServer> _servers = [];

    private Dictionary<string, HealthSnapshot> _snapshots = new();
    private string? _statusFilter;
    private Dictionary<string, MediaHealthSummary> _summaries = new();
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
        await LoadHealthAsync();
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
        _downloads = _downloads.DistinctBy(d => $"{d.ServerId}_{d.DownloadId}").ToList();
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

    private static string GetScoreBadgeClass(int? score) {
        if (score < 0) {
            return "triage-score-badge triage-score-removed";
        }

        if (score != null) {
            score = Math.Clamp((int)score, 0, 100);

            return score switch {
                <= 9 => "score-badge-red",
                <= 19 => "score-badge-red",
                <= 29 => "score-badge-red",
                <= 39 => "score-badge-red",
                <= 49 => "score-badge-orange",
                <= 59 => "score-badge-orange",
                <= 69 => "score-badge-orange",
                <= 79 => "score-badge-yellow ",
                <= 89 => "score-badge-yellow",
                _ => "score-badge-green"
            };
        }
        return "score-badge-muted";
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

    private async Task LoadHealthAsync() {
        Dictionary<string, HealthSnapshot> snapshots = new();
        Dictionary<string, MediaHealthSummary> summaries = new();

        foreach (DownloadItem item in FilteredDownloads) {
            string key = $"{item.ServerId}_{item.RemoteId}";

            HealthSnapshot? snapshot = await HealthService.GetMostRecentSnapshotAsync(item);
            if (snapshot != null) {
                snapshots[key] = snapshot;
            }

            MediaHealthSummary? summary = await HealthService.GetMediaSummary(item);
            if (summary != null) {
                summaries[key] = summary;
            }
        }

        _snapshots = snapshots;
        _summaries = summaries;
    }

    private static string HealthStateLabel(HealthState state) => state switch {
        HealthState.Healthy => "Healthy",
        HealthState.Stable => "Stable",
        HealthState.Watching => "Watching",
        HealthState.Degrading => "Degrading",
        HealthState.Critical => "Critical",
        HealthState.Remove => "Remove",
        HealthState.Resolved => "Resolved",
        HealthState.Failed => "Failed",
        HealthState.New => "New",
        _ => "Unknown"
    };

    private static string HealthStateClass(HealthState state) => state switch {
        HealthState.Healthy or HealthState.Stable or HealthState.Resolved => "status-badge-success",
        HealthState.Watching or HealthState.Degrading or HealthState.New => "status-badge-warning",
        HealthState.Critical or HealthState.Remove or HealthState.Failed => "status-badge-failed",
        _ => "status-badge-muted"
    };
}