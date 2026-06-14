using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Trackarr.Core.Health;
using Trackarr.Core.Media;
using Trackarr.Core.Media.Movies;
using Trackarr.Core.Media.Shows;
using Trackarr.Core.Queue;
using Trackarr.Core.Server;

namespace Trackarr.Components.Modals;

public partial class MediaDetailsModal : ComponentBase {
    private string _activeTab = "overview";
    private List<DownloadAttempt> _downloadAttempts = [];
    private Episode? _episode;
    private List<DownloadHealthEvent> _healthEvents = [];
    private MediaItem? _media;
    private MediaHealthSummary? _mediaHealthSummary;
    private HealthSnapshot? _mostRecentSnapshot;
    private Movie? _movie;
    private Series? _series;
    private MediaServer? _server;

    private bool IsSeries => _series != null || _episode != null || Item?.SeriesId > 0;
    private string StatusText => TextDisplay(Item?.Status);
    private string LastProgress => TimeAgo(_mostRecentSnapshot?.LastGoodObservationAt);
    private string TypeLabel => IsSeries ? "Episode" : "Movie";
    private DownloadHealthEvent? LatestHealthEvent => _healthEvents.OrderByDescending(e => e.OccurredAt).FirstOrDefault();
    private DownloadAttempt? CurrentAttempt => _downloadAttempts.OrderByDescending(e => e.LastSeenAt).ThenByDescending(e => e.FirstSeenAt).FirstOrDefault();
    private string HealthStateDisplay => TextDisplay(_mostRecentSnapshot?.State.ToString());


    private int ConfidencePercent {
        get {
            object? value = _mostRecentSnapshot?.Confidence;
            return value == null ? 0 : Math.Clamp(Convert.ToInt32(value), 0, 100);
        }
    }

    private string EpisodeLabel {
        get {
            if (Item is not { SeasonNumber: > 0, EpisodeNumber: > 0 }) {
                return string.Empty;
            }

            return $"S{Item.SeasonNumber:D2}E{Item.EpisodeNumber:D2}";
        }
    }

    private string TitleDisplay {
        get {
            if (IsSeries) {
                string title = TextDisplay(_series?.Title ?? _media?.Title);
                return string.IsNullOrWhiteSpace(EpisodeLabel) ? title : $"{title} {EpisodeLabel}";
            }
            return TextDisplay(_movie?.Title ?? _media?.Title);
        }
    }

    private string HealthStateBadgeClass => _mostRecentSnapshot?.State.ToString().ToLowerInvariant() switch {
        "healthy" => "app-badge-success",
        "ok" => "app-badge-success",
        "watching" => "app-badge-info",
        "warning" => "app-badge-warning",
        "severe" => "app-badge-danger",
        "failed" => "app-badge-danger",
        "protected" => "app-badge-info",
        _ => "app-badge-muted"
    };

    [Parameter] public DownloadItem? Item { get; set; }
    [Parameter] public EventCallback OnClose { get; set; }
    [Parameter] public EventCallback<DownloadItem> OnForceRecheck { get; set; }
    [Parameter] public EventCallback<DownloadItem> OnRemoveFromQueue { get; set; }

    private static string AttemptStatusBadgeClass(AttemptStatus status) {
        return status.ToString() switch {
            "Active" => "app-badge-primary",
            "Completed" => "app-badge-success",
            "Imported" => "app-badge-success",
            "Failed" => "app-badge-danger",
            "Removed" => "app-badge-warning",
            "Replaced" => "app-badge-info",
            _ => "app-badge-muted"
        };

    }

    public async Task Open(
        DownloadItem item,
        int serverId,
        Movie? movie = null,
        Series? series = null,
        Episode? episode = null) {
        ResetModalState();

        Item = item;

        _movie = movie;
        _series = series;
        _episode = episode;
        _media = (MediaItem?)movie ?? series;

        _server = await MediaServerService.GetServerByIdAsync(serverId);

        if (_episode == null && item.EpisodeId is not 0 && _server != null) {
            _episode = await MediaServerService.GetEpisodeByIdAsync(_server, item.EpisodeId);

            if (_episode?.Series != null) {
                _series = _episode.Series;
                _media = _series;
            }
        }

        await ResolveMediaAsync(item);
        await LoadHealthDataAsync(item);

        await InvokeAsync(StateHasChanged);
        await Js.InvokeVoidAsync("modalHelper.show", "mediaDetailsModal");
    }

    private async Task EvaluateItem(DownloadItem? item) {
        if (item != null)
            await HealthService.EvaluateAsync(item);
        if (item != null)
            await LoadHealthDataAsync(item);
    }

    private async Task LoadHealthDataAsync(DownloadItem item) {
        _healthEvents = await HealthService.GetEventsForItemAsync(item);
        _downloadAttempts = (await HealthService.GetDownloadAttemptsAsync(item)).OrderByDescending(x => x.FirstSeenAt).ToList();
        _mostRecentSnapshot = await HealthService.GetMostRecentSnapshotAsync(item);
        _mediaHealthSummary = await HealthService.GetMediaSummary(item);
    }
    private static string SeverityBadgeClass(Severity severity) => severity switch {
        Severity.Critical => "status-badge-failed",
        Severity.Warning => "status-badge-warning",
        _ => "app-badge-muted"
    };

    private static string DeltaClass(decimal delta) => delta switch {
        > 0 => "text-success",
        < 0 => "text-danger",
        _ => "text-muted"
    };

    private static string DeltaDisplay(decimal delta) => delta switch {
        > 0 => $"+{delta:0.#}",
        < 0 => delta.ToString("0.#"),
        _ => string.Empty
    };


    private static string AttemptNumberDisplay(int attemptNumber) => attemptNumber <= 0 ? "Original" : $"Replacement {attemptNumber}";

    private async Task ResolveMediaAsync(DownloadItem item) {
        if (_movie != null) {
            _media = _movie;
            return;
        }

        if (_series != null) {
            _media = _series;
            return;
        }

        if (_episode?.Series != null) {
            _series = _episode.Series;
            _media = _series;
            return;
        }

        if (_server == null) {
            return;
        }

        if (item.MovieId > 0) {
            _movie = await MediaServerService.GetMovieByIdAsync(_server, item.MovieId);
            _media = _movie;
            return;
        }

        if (item.SeriesId > 0) {
            _series = await MediaServerService.GetSeriesByIdAsync(_server, item.SeriesId);
            _media = _series;
        }
    }

    private async Task ForceRecheck() {
        if (Item != null) {
            await OnForceRecheck.InvokeAsync(Item);
        }
    }

    private async Task RemoveFromQueue() {
        if (Item != null) {
            await OnRemoveFromQueue.InvokeAsync(Item);
        }
    }

    private void ResetModalState() {
        _activeTab = "overview";
        Item = null;
        _media = null;
        _server = null;
        _movie = null;
        _series = null;
        _episode = null;
        _mostRecentSnapshot = null;
        _healthEvents = [];
        _downloadAttempts = [];
        _mediaHealthSummary = null;
    }


    private static string StatusBadgeClass(string? status) {
        return status?.ToLowerInvariant() switch {
            "stuck" => "badge bg-red-lt text-red",
            "warning" => "badge bg-yellow-lt text-yellow",
            "waiting" => "badge bg-yellow-lt text-yellow",
            "queued" => "badge bg-blue-lt text-blue",
            "importing" => "badge bg-purple-lt text-purple",
            "downloading" => "badge bg-green-lt text-green",
            "completed" => "badge bg-green-lt text-green",
            _ => "badge bg-secondary-lt text-secondary"
        };
    }

    private static string YesNo(bool value) => value ? "Yes" : "No";

    private static string TextDisplay(string? value) => string.IsNullOrWhiteSpace(value) ? "—" : value;

    private static string DateTimeDisplay(DateTime? value) => value?.ToString("MMM d, yyyy h:mm tt") ?? "—";

    private static string TimeAgo(DateTime? value) {
        if (value == null) {
            return "—";
        }

        DateTime utcValue = value.Value.Kind switch {
            DateTimeKind.Utc => value.Value,
            DateTimeKind.Local => value.Value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value.Value, DateTimeKind.Utc)
        };

        TimeSpan diff = DateTime.UtcNow - utcValue;

        if (diff.TotalSeconds < 0) {
            diff = TimeSpan.Zero;
        }

        return diff switch {
            { TotalSeconds: < 60 } => "just now",
            { TotalMinutes: < 60 } => $"{(int)diff.TotalMinutes}m ago",
            { TotalHours: < 24 } => $"{(int)diff.TotalHours}h ago",
            { TotalDays: < 7 } => $"{(int)diff.TotalDays}d ago",
            { TotalDays: < 30 } => $"{(int)(diff.TotalDays / 7)}w ago",
            _ => $"{(int)(diff.TotalDays / 30)}mo ago"
        };
    }

    private static string IntDisplay(int? value) => value?.ToString() ?? "—";

    private static string EnumDisplay<T>(T? value) where T : struct, Enum => value?.ToString() ?? "—";
}