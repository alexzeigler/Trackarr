using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Trackarr.Core.Media;
using Trackarr.Core.Media.Movies;
using Trackarr.Core.Media.Shows;
using Trackarr.Core.Queue;
using Trackarr.Core.Server;

namespace Trackarr.Components.Modals;

public partial class MediaDetailsModal : ComponentBase {
    private string _activeTab = "overview";
    private Episode? _episode;
    private string? _issueBody;
    private string? _issueTitle;

    private MediaItem? _media;
    private Movie? _movie;
    private string? _nextActionBody;
    private string? _nextActionTitle;
    private Series? _series;
    private MediaServer? _server;

    [Parameter] public bool IsOpen { get; set; }
    [Parameter] public DownloadItem? Item { get; set; }
    [Parameter] public Movie? Movie { get; set; }
    [Parameter] public Series? Series { get; set; }
    [Parameter] public Episode? Episode { get; set; }
    [Parameter] public EventCallback OnClose { get; set; }
    [Parameter] public EventCallback<DownloadItem> OnForceRecheck { get; set; }
    [Parameter] public EventCallback<DownloadItem> OnRemoveFromQueue { get; set; }

    private bool IsSeries => Series != null || Episode != null || Item?.SeriesId > 0;
    private bool HasIssue => !string.IsNullOrWhiteSpace(_issueTitle);
    private bool HasNextAction => !string.IsNullOrWhiteSpace(_nextActionTitle);

    private string StatusText => TextDisplay(Item?.Status);
    private string ServerDisplay => TextDisplay(_server?.Name);
    private string ProfileDisplay => TextDisplay(Item?.Quality?.Quality?.Name);
    private string LastActivityDisplay => TextDisplay(Item?.StatusMessages?.LastOrDefault()?.Title);
    private string TypeLabel => IsSeries ? "Episode" : "Movie";

    private string TitleDisplay {
        get {
            if (IsSeries) {
                string title = TextDisplay(Series?.Title ?? _media?.Title);
                return string.IsNullOrWhiteSpace(EpisodeLabel) ? title : $"{title} {EpisodeLabel}";
            }
            return TextDisplay(Movie?.Title ?? _media?.Title);
        }
    }

    private string HeaderSubtitle {
        get {
            string? title = IsSeries ? Item?.Title : null;
            string? quality = Item?.Quality?.Quality?.Name;

            if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(quality)) {
                return "—";
            }

            if (string.IsNullOrWhiteSpace(title)) {
                return quality!;
            }

            if (string.IsNullOrWhiteSpace(quality)) {
                return title;
            }

            return $"{title} ({quality})";
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

    private double ProgressPercent {
        get {
            if (Item is not { Size: > 0 }) {
                return 0;
            }

            double downloaded = Math.Max(0, Item.Size - Item.SizeLeft);
            double percent = downloaded / Item.Size * 100;

            return Math.Round(Math.Clamp(percent, 0, 100), 1);
        }
    }

    private string SizeDisplay => FormatBytes(Item?.Size ?? 0);
    private string DownloadedDisplay => FormatBytes(Math.Max(0, (Item?.Size ?? 0) - (Item?.SizeLeft ?? 0)));
    private string RemainingDisplay => FormatBytes(Item?.SizeLeft ?? 0);

    public async Task Open(DownloadItem item, int serverId) {
        ResetModalState();

        Item = item;
        _server = await MediaServerService.GetServerByIdAsync(serverId);
        if (item.EpisodeId is not 0) {
            if (_server != null)
                _episode = await MediaServerService.GetEpisodeByIdAsync(_server, item.EpisodeId);
        }
        else {
            _episode = null;
        }
        await ResolveMediaAsync(item);
        ResolveIssueAndAction();

        await InvokeAsync(StateHasChanged);
        await Js.InvokeVoidAsync("modalHelper.show", "mediaDetailsModal");
    }

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

    private void ResolveIssueAndAction() {
        ClearIssueAndAction();

        Core.Queue.StatusMessage? message = Item?.StatusMessages.LastOrDefault();

        if (message == null) {
            return;
        }

        _issueTitle = message.Title;
        _issueBody = string.Join(" ", message.Messages ?? []);

        (_nextActionTitle, _nextActionBody) = Item?.Status?.ToLowerInvariant() switch {
            "warning" => ("Waiting for progress", "Trackarr will continue to monitor this download."),
            "failed" => ("Re-grab available", "A replacement can be searched automatically."),
            _ => ("Monitoring", "No action required at this time.")
        };
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
        _media = null;
        _server = null;
        _movie = null;
        _series = null;
        _episode = null;

        ClearIssueAndAction();
    }

    private void ClearIssueAndAction() {
        _issueTitle = null;
        _issueBody = null;
        _nextActionTitle = null;
        _nextActionBody = null;
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

    private static string ServerBadgeClass(object? serviceType, string? name) {
        string value = serviceType?.ToString() ?? name ?? string.Empty;

        return value.ToLowerInvariant() switch {
            "sonarr" => "badge bg-green-lt text-green",
            "radarr" => "badge bg-blue-lt text-blue",
            _ => "badge bg-secondary-lt text-secondary"
        };
    }

    private static string YesNo(bool value) => value ? "Yes" : "No";

    private static string TextDisplay(string? value) => string.IsNullOrWhiteSpace(value) ? "—" : value;

    private static string NullableIntDisplay(int? value) => value?.ToString() ?? "—";

    private static string DateDisplay(DateTime? value) => value?.ToString("MMM d, yyyy") ?? "—";

    private static string DateTimeDisplay(DateTime? value) => value?.ToString("MMM d, yyyy h:mm tt") ?? "—";

    private static string TimeAgo(DateTime? value) {
        if (value == null) {
            return "—";
        }

        TimeSpan diff = DateTime.UtcNow - value.Value.ToUniversalTime();

        return diff switch {
            { TotalMinutes: < 1 } => "just now",
            { TotalHours: < 1 } => $"{(int)diff.TotalMinutes}m ago",
            { TotalDays: < 1 } => $"{(int)diff.TotalHours}h ago",
            { TotalDays: < 7 } => $"{(int)diff.TotalDays}d ago",
            { TotalDays: < 30 } => $"{(int)(diff.TotalDays / 7)}w ago",
            _ => $"{(int)(diff.TotalDays / 30)}mo ago"
        };
    }

    private static string FormatBytes(long bytes) {
        return bytes switch {
            >= 1_073_741_824 => $"{bytes / 1_073_741_824.0:F1} GB",
            >= 1_048_576 => $"{bytes / 1_048_576.0:F1} MB",
            >= 1_024 => $"{bytes / 1_024.0:F1} KB",
            _ => $"{bytes} B"
        };
    }

    private static string? YouTubeTrailerUrl(string? trailerId) {
        if (string.IsNullOrWhiteSpace(trailerId)) {
            return null;
        }

        return Uri.TryCreate(trailerId, UriKind.Absolute, out Uri? uri) ? uri.ToString() : $"https://www.youtube.com/watch?v={Uri.EscapeDataString(trailerId)}";

    }

    private static string? ExternalUrl(string? url) {
        if (string.IsNullOrWhiteSpace(url)) {
            return null;
        }

        if (Uri.TryCreate(url, UriKind.Absolute, out Uri? uri)) {
            return uri.ToString();
        }

        return $"https://{url}";
    }
    private static string IntDisplay(int? value) => value?.ToString() ?? "—";

    private static string PositiveIntDisplay(int value) => value > 0 ? value.ToString() : "—";

    private static string PercentDisplay(int? value) => value.HasValue ? $"{value.Value}%" : "—";

    private static string ProgressDisplay(double value) => $"{value:0.#}%";

    private static string DecimalDisplay(decimal? value) => value?.ToString("0.##") ?? "—";

    private static string ResolutionDisplay(int? value) => value is > 0 ? $"{value}p" : "—";

    private static string EnumDisplay<T>(T? value) where T : struct, Enum => value?.ToString() ?? "—";
}