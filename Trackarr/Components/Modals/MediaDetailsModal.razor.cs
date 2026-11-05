using System.Net;
using System.Text;
using System.Text.RegularExpressions;
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
    #region Lifecycle

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        await Js.InvokeVoidAsync("initTooltips");
    }

    #endregion

    private sealed class ActiveDownloadContext
    {
        public required DownloadItem QueueItem { get; init; }
        public DownloadAttempt? Attempt { get; init; }
        public HealthSnapshot? Snapshot { get; init; }
        public DownloadHealthEvent? LatestEvent { get; init; }
        public int FileCount { get; init; }
        public List<string> FileNames { get; init; } = [];
    }

    #region Parameters

    [Parameter] public DownloadItem? Item { get; set; }
    [Parameter] public EventCallback OnClose { get; set; }
    [Parameter] public EventCallback<DownloadItem> OnForceRecheck { get; set; }
    [Parameter] public EventCallback<DownloadItem> OnRemoveFromQueue { get; set; }

    #endregion

    #region State

    private const int EventPageSize = 10;

    private static readonly Regex SeasonOnlyRegex =
        new(@"\bS(\d{1,2})(?!E\d)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex EpisodeRegex =
        new(@"\bS(\d{1,2})E(\d{1,3})\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly Dictionary<string, QualityProfile> _profiles = [];
    private int _activeContextIndex;

    private List<ActiveDownloadContext> _activeContexts = [];

    private string _activeTab = "overview";
    private Dictionary<int, List<DownloadHealthEvent>> _attemptEvents = [];
    private List<DownloadAttempt> _downloadAttempts = [];
    private Episode? _episode;
    private List<Episode> _episodes = new();
    private Dictionary<int, int> _eventPageIndex = [];
    private List<DownloadHealthEvent> _healthEvents = [];
    private bool _isRefreshing;
    private MediaItem? _media;
    private MediaHealthSummary? _mediaHealthSummary;
    private HealthSnapshot? _mostRecentSnapshot;
    private Movie? _movie;

    private HashSet<int> _openSeasons = [];
    private HashSet<int> _queuedEpisodeIds = new();
    private HashSet<int> _queuedMovieIds = new();
    private DownloadItem? _queueItem;
    private List<DownloadItem> _queueItems = [];

    private int? _selectedAttemptId;
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

    private bool? IsFilePresent => _movie != null ? _movie.HasFile : _episode != null ? _episode.HasFile : null;

    private bool? IsMonitoredValue => _movie?.Monitored ?? _series?.Monitored ?? _episode?.Monitored;

    private string LastProgress => TimeAgo(_mostRecentSnapshot?.LastGoodObservationAt);

    private DownloadHealthEvent? LatestHealthEvent =>
        _healthEvents.OrderByDescending(e => e.OccurredAt).FirstOrDefault();

    private DownloadAttempt? CurrentAttempt =>
        _downloadAttempts.OrderByDescending(e => e.LastSeenAt).ThenByDescending(e => e.FirstSeenAt).FirstOrDefault();

    private string HealthStateDisplay => TextDisplay(_mostRecentSnapshot?.State.ToString());

    private string OverviewText => _episode?.Overview ?? _movie?.Overview ?? _series?.Overview ?? string.Empty;

    private string? StudioOrNetwork => _movie?.Studio ?? _series?.Network;

    private int ConfidencePercent
    {
        get
        {
            object? value = _mostRecentSnapshot?.Confidence;
            return value == null ? 0 : Math.Clamp(Convert.ToInt32(value), 0, 100);
        }
    }

    private string HealthStateBadgeClass => HealthBadgeClass(_mostRecentSnapshot?.State);

    private ActiveDownloadContext? SelectedContext =>
        _activeContexts.Count > 0 && _activeContextIndex >= 0 && _activeContextIndex < _activeContexts.Count
            ? _activeContexts[_activeContextIndex]
            : null;

    private HealthSnapshot? ActiveSnapshot => SelectedContext?.Snapshot ?? _mostRecentSnapshot;
    private DownloadHealthEvent? ActiveLatestEvent => SelectedContext?.LatestEvent ?? LatestHealthEvent;
    private DownloadAttempt? ActiveAttempt => SelectedContext?.Attempt ?? CurrentAttempt;
    private DownloadItem? ActiveQueueItem => SelectedContext?.QueueItem ?? _queueItem;

    private int ActiveConfidencePercent => ActiveSnapshot?.Confidence is { } c ? Math.Clamp(c, 0, 100) : 0;

    private string ActiveStatusText =>
        ActiveQueueItem != null ? TextDisplay(ActiveQueueItem.Status) : "Not downloading";

    private int ServerId => _server?.Id ?? _movie?.ServerId ?? _series?.ServerId ?? _episode?.ServerId ?? 0;
    private int? MovieRemoteId => _movie?.RemoteId;
    private int? SeriesRemoteId => _series?.RemoteId ?? _episode?.RemoteSeriesId;
    private int? EpisodeRemoteId => _episode?.RemoteId;

    #endregion

    #region Event Handlers

    private void ToggleSeason(int season)
    {
        if (!_openSeasons.Remove(season))
            _openSeasons.Add(season);
    }

    private async Task ToggleAttemptExpand(int attemptId)
    {
        if (_selectedAttemptId == attemptId)
        {
            _selectedAttemptId = null;
            return;
        }

        _selectedAttemptId = attemptId;

        if (!_attemptEvents.ContainsKey(attemptId))
        {
            var events = await HealthService.GetEventsAsync(attemptId);
            _attemptEvents[attemptId] = events;
        }
    }

    private void SetEventPage(int attemptId, int page)
    {
        var max = GetEventPageCount(attemptId) - 1;
        _eventPageIndex[attemptId] = Math.Clamp(page, 0, Math.Max(0, max));
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

    private async Task ForceRecheck()
    {
        if (Item != null) await OnForceRecheck.InvokeAsync(Item);
    }

    private async Task RemoveFromQueue()
    {
        if (Item != null) await OnRemoveFromQueue.InvokeAsync(Item);
    }

    private async Task RefreshAsync()
    {
        _isRefreshing = true;
        StateHasChanged();
        try
        {
            var delay = Task.Delay(1000);
            _attemptEvents.Clear();
            _eventPageIndex.Clear();
            _selectedAttemptId = null;
            await LoadHealthDataAsync();
            await BuildActiveContextsAsync();
            await delay;
        }
        finally
        {
            _isRefreshing = false;
            StateHasChanged();
        }
    }

    private string? _previewMessage;

    private async Task EvaluateItem()
    {
        if (_queueItem == null)
            return;

        _isRefreshing = true;
        StateHasChanged();
        try
        {
            // Preview only: a click mustn't count as a scan and push the item toward removal (M13).
            var delay = Task.Delay(1000);
            var decision = await HealthService.PreviewAsync(_queueItem);
            _previewMessage = Pages.Queue.DescribePreview(_queueItem, decision);
            await delay;
        }
        finally
        {
            _isRefreshing = false;
            StateHasChanged();
        }
    }

    #endregion

    #region Data Loading

    private async Task OpenMediaAsync(int? serverId, Movie? movie, Series? series, Episode? episode,
        DownloadItem? queueItem = null)
    {
        ResetModalState();

        _movie = movie;
        _series = series;
        _episode = episode;
        _media = (MediaItem?)movie ?? series;
        _queueItem = queueItem;

        _server = serverId.HasValue ? await MediaServerService.GetServerByIdAsync(serverId.Value) : null;
        await LoadProfilesAsync();

        await ResolveMediaAsync(_queueItem);
        await ResolveQueueAsync();
        await LoadHealthDataAsync();
        await LoadFilesAsync();
        await BuildActiveContextsAsync();

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

    private async Task LoadHealthDataAsync()
    {
        if (_queueItem != null)
        {
            _healthEvents = await HealthService.GetEventsForItemAsync(_queueItem);
            _mostRecentSnapshot = await HealthService.GetMostRecentSnapshotAsync(_queueItem);
            _mediaHealthSummary = await HealthService.GetMediaSummary(_queueItem);
        }
        else
        {
            _healthEvents =
                await HealthService.GetEventsForMediaAsync(ServerId, MovieRemoteId, SeriesRemoteId, EpisodeRemoteId);
            _mostRecentSnapshot =
                await HealthService.GetMostRecentSnapshotAsync(ServerId, MovieRemoteId, SeriesRemoteId,
                    EpisodeRemoteId);
            _mediaHealthSummary =
                await HealthService.GetMediaSummary(ServerId, MovieRemoteId, SeriesRemoteId, EpisodeRemoteId);
        }

        _downloadAttempts =
            (await HealthService.GetDownloadAttemptsAsync(ServerId, MovieRemoteId, SeriesRemoteId, null))
            .OrderByDescending(x => x.FirstSeenAt).ToList();
    }

    private async Task LoadProfilesAsync()
    {
        if (_server != null)
        {
            var profiles = await MediaServerService.GetProfilesAsync(_server);
            foreach (var p in profiles) _profiles[$"{p.ServerId}_{p.RemoteId}"] = p;
        }
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

    private async Task LoadFilesAsync()
    {
        _episodes = [];
        _queuedEpisodeIds = _queueItems.Where(x => x.EpisodeId > 0).Select(x => x.EpisodeId).ToHashSet();
        _queuedMovieIds = _queueItems.Where(x => x.MovieId > 0).Select(x => x.MovieId).ToHashSet();

        if (_server == null)
            return;

        if (_series is { ServerId: not null })
        {
            await MediaServerService.SyncSeriesEpisodesAsync(_server, _series.RemoteId);
            _episodes = await MediaServerService.GetEpisodesBySeriesIdAsync(_series.ServerId.Value, _series.RemoteId);
            _episodes = _episodes.OrderBy(x => x.SeasonNumber).ThenBy(x => x.EpisodeNumber).ToList();
        }
    }

    private async Task BuildActiveContextsAsync()
    {
        _activeContexts = [];
        _activeContextIndex = 0;

        // Group by the same key attempts are stored under, so items with no download ID (pending releases)
        // still show up and still match their health attempt.
        var groups = _queueItems.GroupBy(Trackarr.Services.Media.HealthService.GetDownloadId).ToList();

        foreach (var group in groups)
        {
            var primary = group.OrderBy(q => q.EpisodeId).First();
            var attempt = _downloadAttempts.FirstOrDefault(a =>
                a.DownloadId == group.Key && a.Status == AttemptStatus.Active);

            HealthSnapshot? snapshot = null;
            DownloadHealthEvent? latestEvent = null;

            if (attempt != null)
            {
                snapshot = await HealthService.GetSnapshotAsync(attempt.Id);
                var events = await HealthService.GetEventsAsync(attempt.Id, 1);
                latestEvent = events.FirstOrDefault();
            }

            var fileNames = group.Select(q => _episodes.FirstOrDefault(e => e.RemoteId == q.EpisodeId))
                .Where(ep => ep != null).OrderBy(ep => ep!.SeasonNumber).ThenBy(ep => ep!.EpisodeNumber)
                .Select(ep => EpisodeTitle(ep!)).Distinct().ToList();

            _activeContexts.Add(new ActiveDownloadContext
            {
                QueueItem = primary,
                Attempt = attempt,
                Snapshot = snapshot,
                LatestEvent = latestEvent,
                FileCount = fileNames.Count > 0 ? fileNames.Count : group.Count(),
                FileNames = fileNames
            });
        }
    }

    #endregion

    #region Display Helpers

    private static string HealthBadgeClass(HealthState? state)
    {
        return state?.ToString().ToLowerInvariant() switch
        {
            "healthy" => "badge bg-success-lt text-success",
            "ok" => "badge bg-success-lt text-success",
            "watching" => "badge bg-blue-lt text-blue",
            "warning" => "badge bg-warning-lt text-warning",
            "severe" => "badge bg-danger-lt text-danger",
            "failed" => "badge bg-danger-lt text-danger",
            "protected" => "badge bg-blue-lt text-blue",
            _ => "badge bg-secondary-lt text-secondary"
        };
    }

    private static string EpisodeTitle(Episode ep)
    {
        return $"{ep.Title} (S{ep.SeasonNumber:D2}E{ep.EpisodeNumber:D2})";
    }

    private static string SeasonLabel(int season)
    {
        return season == 0 ? "Specials" : $"Season {season}";
    }

    private static string AttemptStatusBadgeClass(AttemptStatus status)
    {
        return status.ToString() switch
        {
            "Active" => "badge bg-primary-lt text-primary",
            "Completed" => "badge bg-success-lt text-success",
            "Imported" => "badge bg-success-lt text-success",
            "Failed" => "badge bg-danger-lt text-danger",
            "Removed" => "badge bg-orange-lt text-orange",
            "Replaced" => "badge bg-blue-lt text-blue",
            _ => "badge bg-secondary-lt text-secondary"
        };
    }

    private (int Count, List<string> Names) GetAttemptFiles(DownloadAttempt attempt)
    {
        var queueForAttempt = _queueItems.Where(q => q.DownloadId == attempt.DownloadId).ToList();
        if (queueForAttempt.Count > 0)
        {
            var names = queueForAttempt.Select(q => _episodes.FirstOrDefault(e => e.RemoteId == q.EpisodeId))
                .Where(ep => ep != null).OrderBy(ep => ep!.SeasonNumber).ThenBy(ep => ep!.EpisodeNumber)
                .Select(ep => EpisodeTitle(ep!)).Distinct().ToList();
            return (names.Count > 0 ? names.Count : queueForAttempt.Count, names);
        }

        if (attempt.MediaType == TrackedMediaType.Movie)
            return (1, [attempt.Title ?? "Movie"]);

        var epMatch = EpisodeRegex.Match(attempt.Title ?? "");
        if (epMatch.Success && int.TryParse(epMatch.Groups[1].Value, out var epSeason) &&
            int.TryParse(epMatch.Groups[2].Value, out var epNum))
        {
            var ep = _episodes.FirstOrDefault(x => x.SeasonNumber == epSeason && x.EpisodeNumber == epNum);
            if (ep != null)
                return (1, [EpisodeTitle(ep)]);
            return (1, [$"S{epSeason:D2}E{epNum:D2}"]);
        }

        var seasonMatch = SeasonOnlyRegex.Match(attempt.Title ?? "");
        if (seasonMatch.Success && int.TryParse(seasonMatch.Groups[1].Value, out var season))
        {
            var eps = _episodes.Where(e => e.SeasonNumber == season).OrderBy(e => e.EpisodeNumber).ToList();
            if (eps.Count > 0)
                return (eps.Count, eps.Select(e => EpisodeTitle(e)).ToList());
        }

        return (0, []);
    }

    private string GetFileListTooltip(DownloadAttempt attempt)
    {
        var (_, names) = GetAttemptFiles(attempt);
        if (names.Count == 0) return "No file information available";
        if (names.Count == 1) return WebUtility.HtmlEncode(names[0]);

        var sb = new StringBuilder("<ol class='mb-0 ps-3' style='text-align:left'>");
        foreach (var name in names)
            sb.Append($"<li>{WebUtility.HtmlEncode(name)}</li>");
        sb.Append("</ol>");
        return sb.ToString();
    }

    private static string GetScoreMathTooltip(DownloadHealthEvent ev)
    {
        var delta = ev.ScoreDelta;
        if (delta == 0) return $"No score change. Score held at {ev.ScoreAfter:0.#}";

        var sign = delta > 0 ? "+" : "";
        var lines = new List<string>
        {
            $"Before: {ev.ScoreBefore:0.#}", $"Delta: {sign}{delta:0.#}", $"After: {ev.ScoreAfter:0.#}"
        };

        var clamped = ev.ScoreBefore + delta;
        if (Math.Abs(clamped - ev.ScoreAfter) > 0.1m)
            lines.Add("(clamped to range 0–100)");

        return string.Join("<br/>", lines.Select(WebUtility.HtmlEncode));
    }

    private static string GetConfidenceMathTooltip(DownloadHealthEvent ev)
    {
        var delta = ev.ConfidenceDelta;
        if (delta == 0) return $"No confidence change. Held at {ev.ConfidenceAfter}";

        var sign = delta > 0 ? "+" : "";
        return $"Before: {ev.ConfidenceBefore}<br/>Delta: {sign}{delta:0.#}<br/>After: {ev.ConfidenceAfter}";
    }

    private static string GetReasonTooltip(string? message)
    {
        if (string.IsNullOrWhiteSpace(message)) return "";
        var parts = message.Split(". ", StringSplitOptions.RemoveEmptyEntries).Select(s => s.TrimEnd('.') + ".");
        return string.Join("<br/>", parts.Select(WebUtility.HtmlEncode));
    }

    private int GetEventPage(int attemptId)
    {
        return _eventPageIndex.GetValueOrDefault(attemptId, 0);
    }

    private int GetEventPageCount(int attemptId)
    {
        return _attemptEvents.TryGetValue(attemptId, out var events)
            ? (int)Math.Ceiling(events.Count / (double)EventPageSize)
            : 0;
    }

    private List<DownloadHealthEvent> GetPagedEvents(int attemptId)
    {
        if (!_attemptEvents.TryGetValue(attemptId, out var events)) return [];
        var page = GetEventPage(attemptId);
        return events.Skip(page * EventPageSize).Take(EventPageSize).ToList();
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

    private void ResetModalState()
    {
        _previewMessage = null;
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
        _selectedAttemptId = null;
        _attemptEvents = [];
        _eventPageIndex = [];
        _activeContexts = [];
        _activeContextIndex = 0;
        _isRefreshing = false;
    }

    private QualityProfile? GetProfile()
    {
        return _movie != null ? _profiles.GetValueOrDefault($"{_movie.ServerId}_{_movie.QualityProfileId}") :
            _series != null ? _profiles.GetValueOrDefault($"{_series.ServerId}_{_series.QualityProfileId}") : null;
    }

    private (string Label, string Css) FileStatus(bool hasFile, bool queued)
    {
        if (hasFile) return ("Downloaded", "badge bg-success-lt text-success");
        if (queued) return ("Downloading", "badge bg-blue-lt text-blue");
        return ("Missing", "badge bg-warning-lt text-warning");
    }

    private static string SeverityBadgeClass(Severity severity)
    {
        return severity switch
        {
            Severity.Critical => "badge bg-danger-lt text-danger",
            Severity.Warning => "badge bg-warning-lt text-warning",
            Severity.Negative => "badge bg-warning-lt text-warning",
            _ => "badge bg-blue-lt text-blue"
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

    #endregion
}
