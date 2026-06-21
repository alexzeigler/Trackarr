using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Trackarr.Core.Health;
using Trackarr.Core.Media;
using Trackarr.Core.Media.Movies;
using Trackarr.Core.Media.Shows;
using Trackarr.Core.Queue;
using Trackarr.Core.Server;
using static Trackarr.Utils;

namespace Trackarr.Components.Modals;

public partial class MediaDetailsModal : ComponentBase
{
    private readonly Dictionary<string, QualityProfile> _profiles = [];

    private string _activeTab = "overview";
    private List<DownloadAttempt> _downloadAttempts = [];
    private Episode? _episode;
    private List<Episode> _episodes = new();
    private List<DownloadHealthEvent> _healthEvents = [];
    private MediaItem? _media;
    private MediaHealthSummary? _mediaHealthSummary;
    private HealthSnapshot? _mostRecentSnapshot;
    private Movie? _movie;

    private HashSet<int> _openSeasons = [];
    private HashSet<int> _queuedEpisodeIds = new();
    private HashSet<int> _queuedMovieIds = new();
    private DownloadItem? _queueItem;
    private List<DownloadItem> _queueItems = [];
    private Series? _series;
    private MediaServer? _server;
    private bool IsDownloading => _queueItems.Count > 0 || _queueItem != null;

    private string StatusText => IsDownloading ? TextDisplay(_queueItem?.Status) : "Not downloading";

    private string QueueStateText => IsDownloading ? EnumDisplay(_queueItem?.State) : "Not in queue";

    private string TypeLabel => _episode != null ? "Episode" : _series != null ? "Series" : "Movie";

    private string QualityDisplay => _queueItem?.Quality?.Quality.Name ?? GetProfile()?.Name ?? "—";

    private string AddedDisplay => TimeAgo(_media?.Added);

    private string EpisodeLabel =>
        _episode == null ? string.Empty : $"S{_episode.SeasonNumber:D2}E{_episode.EpisodeNumber:D2}";

    private string TitleDisplay
    {
        get
        {
            if (_movie != null)
                return TextDisplay(_movie.Title);

            var title = TextDisplay(_series?.Title ?? _media?.Title);

            return string.IsNullOrWhiteSpace(EpisodeLabel) ? title : $"{title} {EpisodeLabel}";
        }
    }

    private string LastSearchDisplay =>
        _movie != null ? TimeAgo(_movie.LastSearchTime) : _episode != null ? TimeAgo(_episode.LastSearchTime) : "—";

    private string HasFileDisplay =>
        _movie != null ? TextDisplay(YesNo(_movie.HasFile)) :
        _episode != null ? TextDisplay(YesNo(_episode.HasFile)) : "—";


    private string LastProgress => TimeAgo(_mostRecentSnapshot?.LastGoodObservationAt);


    private DownloadHealthEvent? LatestHealthEvent =>
        _healthEvents.OrderByDescending(e => e.OccurredAt).FirstOrDefault();

    private DownloadAttempt? CurrentAttempt =>
        _downloadAttempts.OrderByDescending(e => e.LastSeenAt).ThenByDescending(e => e.FirstSeenAt).FirstOrDefault();

    private string HealthStateDisplay => TextDisplay(_mostRecentSnapshot?.State.ToString());


    private int ConfidencePercent
    {
        get
        {
            object? value = _mostRecentSnapshot?.Confidence;
            return value == null ? 0 : Math.Clamp(Convert.ToInt32(value), 0, 100);
        }
    }


    private string HealthStateBadgeClass =>
        _mostRecentSnapshot?.State.ToString().ToLowerInvariant() switch
        {
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

    private int ServerId => _server?.Id ?? _movie?.ServerId ?? _series?.ServerId ?? _episode?.ServerId ?? 0;
    private int? MovieRemoteId => _movie?.RemoteId;
    private int? SeriesRemoteId => _series?.RemoteId ?? _episode?.RemoteSeriesId;
    private int? EpisodeRemoteId => _episode?.RemoteId;

    private static string EpisodeTitle(Episode ep)
    {
        return $"{ep.Title} (S{ep.SeasonNumber:D2}E{ep.EpisodeNumber:D2})";
    }

    private void ToggleSeason(int season)
    {
        if (!_openSeasons.Remove(season))
            _openSeasons.Add(season);
    }

    private static string SeasonLabel(int season)
    {
        return season == 0 ? "Specials" : $"Season {season}";
    }

    private static string AttemptStatusBadgeClass(AttemptStatus status)
    {
        return status.ToString() switch
        {
            "Active" => "app-badge-primary",
            "Completed" => "app-badge-success",
            "Imported" => "app-badge-success",
            "Failed" => "app-badge-danger",
            "Removed" => "app-badge-warning",
            "Replaced" => "app-badge-info",
            _ => "app-badge-muted"
        };
    }

    public async Task Open(Movie movie)
    {
        await OpenMediaAsync(movie.ServerId, movie, null, null);
    }

    public async Task Open(Series series)
    {
        await OpenMediaAsync(series.ServerId, null, series, null);
    }

    public async Task Open(Episode episode, Series? series = null)
    {
        await OpenMediaAsync(episode.ServerId, null, series, episode);
    }

    public async Task Open(DownloadItem item, int serverId, Movie? movie = null, Series? series = null,
        Episode? episode = null)
    {
        await OpenMediaAsync(serverId, movie, series, episode, item);
    }

    public async Task Open(Movie movie, DownloadItem? queueItem)
    {
        await OpenMediaAsync(movie.ServerId, movie, null, null, queueItem);
    }

    public async Task Open(Series series, DownloadItem? queueItem)
    {
        await OpenMediaAsync(series.ServerId, null, series, null, queueItem);
    }

    public async Task Open(Episode episode, Series? series, DownloadItem? queueItem)
    {
        await OpenMediaAsync(episode.ServerId, null, series, episode, queueItem);
    }

    private async Task OpenMediaAsync(int serverId, Movie? movie, Series? series, Episode? episode,
        DownloadItem? queueItem = null)
    {
        ResetModalState();

        _movie = movie;
        _series = series;
        _episode = episode;
        _media = (MediaItem?)movie ?? series;
        _queueItem = queueItem;

        _server = await MediaServerService.GetServerByIdAsync(serverId);
        await LoadProfilesAsync();

        await ResolveMediaAsync(_queueItem);
        await ResolveQueueAsync();
        await LoadHealthDataAsync();
        await LoadFilesAsync();

        _openSeasons = _episodes.Select(e => e.SeasonNumber).ToHashSet();

        await InvokeAsync(StateHasChanged);
        await Js.InvokeVoidAsync("modalHelper.show", "mediaDetailsModal");
    }

    private async Task ResolveQueueAsync()
    {
        _queueItems = [];

        if (_server == null)
            return;

        var queue = await MediaServerService.GetQueueAsync(_server);

        _queueItems = queue.Where(QueueMatchesMedia).ToList();

        _queueItem ??= _queueItems.OrderByDescending(x => x.Added).FirstOrDefault();
    }

    private bool QueueMatchesMedia(DownloadItem item)
    {
        if (_episode != null)
            return item.EpisodeId == _episode.RemoteId;

        if (_movie != null)
            return item.MovieId == _movie.RemoteId;

        if (_series != null)
            return item.SeriesId == _series.RemoteId;

        return false;
    }

    private async Task EvaluateItem()
    {
        if (_queueItem == null)
            return;

        await HealthService.EvaluateAsync(_queueItem);
        await LoadHealthDataAsync();
    }

    private async Task LoadHealthDataAsync()
    {
        if (_queueItem != null)
        {
            _healthEvents = await HealthService.GetEventsForItemAsync(_queueItem);
            _downloadAttempts = (await HealthService.GetDownloadAttemptsAsync(_queueItem))
                .OrderByDescending(x => x.FirstSeenAt).ToList();
            _mostRecentSnapshot = await HealthService.GetMostRecentSnapshotAsync(_queueItem);
            _mediaHealthSummary = await HealthService.GetMediaSummary(_queueItem);
            return;
        }

        _healthEvents =
            await HealthService.GetEventsForMediaAsync(ServerId, MovieRemoteId, SeriesRemoteId, EpisodeRemoteId);
        _downloadAttempts =
            (await HealthService.GetDownloadAttemptsAsync(ServerId, MovieRemoteId, SeriesRemoteId, EpisodeRemoteId))
            .OrderByDescending(x => x.FirstSeenAt).ToList();

        _mostRecentSnapshot =
            await HealthService.GetMostRecentSnapshotAsync(ServerId, MovieRemoteId, SeriesRemoteId, EpisodeRemoteId);
        _mediaHealthSummary =
            await HealthService.GetMediaSummary(ServerId, MovieRemoteId, SeriesRemoteId, EpisodeRemoteId);
    }

    private async Task LoadProfilesAsync()
    {
        if (_server != null)
        {
            var profiles = await MediaServerService.GetProfilesAsync(_server);
            foreach (var p in profiles) _profiles[$"{p.ServerId}_{p.RemoteId}"] = p;
        }
    }


    private static string SeverityBadgeClass(Severity severity)
    {
        return severity switch
        {
            Severity.Critical => "status-badge-failed",
            Severity.Warning => "status-badge-warning",
            _ => "app-badge-muted"
        };
    }

    private static string DeltaClass(decimal delta)
    {
        return delta switch
        {
            > 0 => "text-success",
            < 0 => "text-danger",
            _ => "text-muted"
        };
    }

    private static string DeltaDisplay(decimal delta)
    {
        return delta switch
        {
            > 0 => $"+{delta:0.#}",
            < 0 => delta.ToString("0.#"),
            _ => string.Empty
        };
    }

    private async Task ResolveMediaAsync(DownloadItem? item)
    {
        if (_movie != null)
        {
            _media = _movie;
            return;
        }

        if (_series != null)
        {
            _media = _series;
            return;
        }

        if (_episode?.Series != null)
        {
            _series = _episode.Series;
            _media = _series;
            return;
        }

        if (_server == null) return;

        if (item is { MovieId: > 0 })
        {
            _movie = await MediaServerService.GetMovieByIdAsync(_server, item.MovieId);
            _media = _movie;
            return;
        }

        if (item is { SeriesId: > 0 })
        {
            _series = await MediaServerService.GetSeriesByIdAsync(_server, item.SeriesId);
            _media = _series;
        }
    }

    private async Task ForceRecheck()
    {
        if (Item != null) await OnForceRecheck.InvokeAsync(Item);
    }

    private async Task RemoveFromQueue()
    {
        if (Item != null) await OnRemoveFromQueue.InvokeAsync(Item);
    }

    private void ResetModalState()
    {
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

    private QualityProfile? GetProfile()
    {
        return _movie != null ? _profiles.GetValueOrDefault($"{_movie.ServerId}_{_movie.QualityProfileId}") :
            _series != null ? _profiles.GetValueOrDefault($"{_series.ServerId}_{_series.QualityProfileId}") : null;
    }

    private async Task LoadFilesAsync()
    {
        _episodes = [];
        _queuedEpisodeIds = _queueItems.Where(x => x.EpisodeId > 0).Select(x => x.EpisodeId).ToHashSet();
        _queuedMovieIds = _queueItems.Where(x => x.MovieId > 0).Select(x => x.MovieId).ToHashSet();

        if (_server == null)
            return;

        if (_series != null)
        {
            _episodes = await MediaServerService.GetEpisodesBySeriesIdAsync(_series.ServerId, _series.RemoteId);
            _episodes = _episodes.OrderBy(x => x.SeasonNumber).ThenBy(x => x.EpisodeNumber).ToList();
        }
    }

    private (string Label, string Css) FileStatus(bool hasFile, bool queued)
    {
        if (hasFile) return ("Downloaded", "app-badge-success");
        if (queued) return ("Downloading", "app-badge-info");
        return ("Missing", "app-badge-warning");
    }
}