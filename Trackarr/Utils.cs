using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using Trackarr.Core.Media.Shows;
using Trackarr.Core.Queue;
using Trackarr.Core.Routine;
using Trackarr.Core.Server;
using Trackarr.Data;
using Trackarr.Services.Settings;

namespace Trackarr;

public static class Utils
{
    private static string _dateFormat = "MMM d, yyyy h:mm tt";
    private static bool _useTimeAgo = true;
    private static int _timeAgoThresholdHours = 24;

    public static void Configure(SettingsService settings)
    {
        _dateFormat = settings.GetString("Display", "DateFormat", "MMM d, yyyy h:mm tt");
        _useTimeAgo = settings.GetBool("Display", "UseTimeAgo", true);
        _timeAgoThresholdHours = settings.GetInt("Display", "TimeAgoThresholdHours", 24);
    }

    public static string StatusBadgeClass(string? status)
    {
        return status?.ToLowerInvariant() switch
        {
            "stuck" => "badge bg-warning-lt text-warning",
            "stalled" => "badge bg-warning-lt text-warning",
            "warning" => "badge bg-warning-lt text-warning",
            "waiting" => "badge bg-secondary-lt text-secondary",
            "delay" => "badge bg-secondary-lt text-secondary",
            "paused" => "badge bg-secondary-lt text-secondary",
            "queued" => "badge bg-purple-lt text-purple",
            "importing" => "badge bg-blue-lt text-blue",
            "importpending" => "badge bg-blue-lt text-blue",
            "importblocked" => "badge bg-warning-lt text-warning",
            "downloading" => "badge bg-success-lt text-success",
            "completed" => "badge bg-success-lt text-success",
            "failed" => "badge bg-danger-lt text-danger",
            "error" => "badge bg-danger-lt text-danger",
            "failedpending" => "badge bg-danger-lt text-danger",
            "downloadclientunavailable" => "badge bg-danger-lt text-danger",
            "missing" => "badge bg-orange-lt text-orange",
            _ => "badge bg-secondary-lt text-secondary"
        };
    }

    public static string YesNo(bool value)
    {
        return value ? "Yes" : "No";
    }

    public static string TextDisplay(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "—" : value;
    }

    public static string DateTimeDisplay(DateTime? value)
    {
        return value is { } v ? ToLocal(v).ToString(_dateFormat) : "—";
    }

    /// <summary>
    ///     Converts a stored time to the server's local time zone (the container's TZ). Times are stored in UTC,
    ///     but SQLite returns them with Kind = Unspecified, which ToLocalTime() would treat as already local and
    ///     leave unchanged, so treat anything not explicitly local as UTC.
    /// </summary>
    public static DateTime ToLocal(DateTime value)
    {
        return value.Kind == DateTimeKind.Local ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc).ToLocalTime();
    }

    public static string TimeAgo(DateTime? value)
    {
        if (value == null) return "—";

        var utcValue = value.Value.Kind switch
        {
            DateTimeKind.Utc => value.Value,
            DateTimeKind.Local => value.Value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value.Value, DateTimeKind.Utc)
        };

        var diff = DateTime.UtcNow - utcValue;

        if (diff.TotalSeconds < 0) diff = TimeSpan.Zero;

        if (!_useTimeAgo || diff.TotalHours > _timeAgoThresholdHours)
            return DateTimeDisplay(value);

        return diff switch
        {
            { TotalSeconds: < 60 } => "just now",
            { TotalMinutes: < 60 } => $"{(int)diff.TotalMinutes}m ago",
            { TotalHours: < 24 } => $"{(int)diff.TotalHours}h ago",
            { TotalDays: < 7 } => $"{(int)diff.TotalDays}d ago",
            { TotalDays: < 30 } => $"{(int)(diff.TotalDays / 7)}w ago",
            _ => $"{(int)(diff.TotalDays / 30)}mo ago"
        };
    }

    public static string IntDisplay(int? value)
    {
        return value?.ToString() ?? "—";
    }

    public static string EnumDisplay<T>(T? value) where T : struct, Enum
    {
        return value?.ToString() ?? "—";
    }

    public static string GetEpisodeString(Episode episode)
    {
        return $"(S{episode.SeasonNumber:D2}E{episode.EpisodeNumber:D2})";
    }

    public static string GetScoreBadgeClass(int? score)
    {
        switch (score)
        {
            case null:
            case <= 0:
                return "score-badge score-badge-muted";
            default:
                score = Math.Clamp((int)score, 0, 100);

                return score switch
                {
                    <= 39 => "score-badge score-badge-red",
                    <= 69 => "score-badge score-badge-orange",
                    <= 89 => "score-badge score-badge-yellow",
                    _ => "score-badge score-badge-green"
                };
        }
    }

    public static string NormalizeStatus(string? status)
    {
        return string.IsNullOrWhiteSpace(status)
            ? string.Empty
            : status.Trim().Replace("-", "").Replace("_", "").Replace(" ", "").ToLowerInvariant();
    }

    public static string GetStatusText(DownloadItem item)
    {
        var status = string.IsNullOrWhiteSpace(item.Status) ? "Unknown" : item.Status;
        return CultureInfo.CurrentCulture.TextInfo.ToTitleCase(status.ToLower());
    }

    public static string GetMediaType(DownloadItem item)
    {
        return item.MovieId > 0 ? "Movie" : "Series";
    }

    public static long GetSortTicks(DateTime? value)
    {
        return value?.ToUniversalTime().Ticks ?? 0;
    }

    public static string GetStatusBucket(string? status)
    {
        if (IsStatus(status, "stuck", "stalled", "failed", "error", "warning", "blocked")) return "Stuck";
        if (IsStatus(status, "waiting", "queued", "pending", "delay", "paused")) return "Waiting";
        if (IsStatus(status, "downloading", "importing")) return "Downloading";
        return IsStatus(status, "missing") ? "Missing" : string.Empty;
    }

    public static bool IsStatus(string? status, params string[] statuses)
    {
        return !string.IsNullOrWhiteSpace(status) &&
               statuses.Any(value => status.Contains(value, StringComparison.OrdinalIgnoreCase));
    }

    public static MarkupString GetServerIcon(ServiceType type, bool large = false)
    {
        var size = large ? "32" : "20";

        var src = type switch
        {
            ServiceType.Radarr => "img/Radarr.svg",
            ServiceType.Sonarr => "img/Sonarr.svg",
            _ => ""
        };

        return (MarkupString)$"<img src=\"{src}\" width=\"{size}\" height=\"{size}\" alt=\"{type}\" />";
    }

    public static string GetTypeBadgeClass(ServiceType type)
    {
        return type switch
        {
            ServiceType.Radarr => "app-badge app-badge-radarr",
            ServiceType.Sonarr => "app-badge app-badge-sonarr",
            _ => "app-badge app-badge-muted"
        };
    }

    public static string MediaTypeBadgeClass(string? type)
    {
        return type?.ToLowerInvariant() switch
        {
            "movie" => "badge bg-purple-lt text-purple",
            "episode" => "badge bg-blue-lt text-blue",
            "series" => "badge bg-indigo-lt text-indigo",
            "season" => "badge bg-indigo-lt text-indigo",
            _ => "badge bg-secondary-lt text-secondary"
        };
    }

    public static string EventTypeBadgeClass(string? eventType)
    {
        return eventType?.ToLowerInvariant() switch
        {
            "progressincreased" => "badge bg-success-lt text-success",
            "sizeleftdecreased" => "badge bg-success-lt text-success",
            "sustainedprogress" => "badge bg-success-lt text-success",
            "errorcleared" => "badge bg-success-lt text-success",
            "recoverystarted" => "badge bg-success-lt text-success",
            "recoverycreditapplied" => "badge bg-success-lt text-success",
            "importedsuccessfully" => "badge bg-success-lt text-success",
            "noprogress" => "badge bg-warning-lt text-warning",
            "warningreported" => "badge bg-warning-lt text-warning",
            "stalled" => "badge bg-warning-lt text-warning",
            "stallednoconnections" => "badge bg-warning-lt text-warning",
            "statusworsened" => "badge bg-warning-lt text-warning",
            "errorreported" => "badge bg-danger-lt text-danger",
            "errorrepeated" => "badge bg-danger-lt text-danger",
            "failedexternally" => "badge bg-danger-lt text-danger",
            "removedbytrackarr" => "badge bg-orange-lt text-orange",
            "firstseen" => "badge bg-blue-lt text-blue",
            "queueseen" => "badge bg-blue-lt text-blue",
            "queuemissing" => "badge bg-blue-lt text-blue",
            "fetchingmetadata" => "badge bg-blue-lt text-blue",
            "manualoverride" => "badge bg-purple-lt text-purple",
            _ => "badge bg-secondary-lt text-secondary"
        };
    }

    public static MarkupString GetChecked(bool value, int size = 24)
    {
        if (value)
        {
            return (MarkupString)
                $"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{size}\" height=\"{size}\" viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\" stroke-linecap=\"round\" stroke-linejoin=\"round\" class=\"icon text-green icon-2\"><path d=\"M5 12l5 5l10 -10\"></path></svg>";
        }

        return (MarkupString)
            $"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{size}\" height=\"{size}\" viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\" stroke-linecap=\"round\" stroke-linejoin=\"round\" class=\"icon text-red icon-2\">\n\t<path stroke=\"none\" d=\"M0 0h24v24H0z\" fill=\"none\" />\n\t<path d=\"M18 6l-12 12\" />\n\t<path d=\"M6 6l12 12\" />\n</svg>";
    }

    public static string IntervalText(int seconds)
    {
        return seconds switch
        {
            <= 0 => "Manual",
            < 60 => $"{seconds}s",
            < 3600 => $"{seconds / 60}m",
            _ => seconds < 86400 ? $"{seconds / 3600}h" : $"{seconds / 86400}d"
        };
    }

    public static string DurationText(DateTime? startTime, DateTime? endTime)
    {
        if (startTime == null) return "—";

        var end = endTime ?? DateTime.UtcNow;
        var duration = end.ToUniversalTime() - startTime.Value.ToUniversalTime();

        if (duration.TotalMilliseconds < 0) return "—";

        return duration switch
        {
            { TotalMilliseconds: < 1000 } => $"{(int)duration.TotalMilliseconds}ms",
            { TotalSeconds: < 60 } => $"{duration.TotalSeconds:0.0}s",
            { TotalMinutes: < 60 } => $"{(int)duration.TotalMinutes}m {duration.Seconds}s",
            { TotalHours: < 24 } => $"{(int)duration.TotalHours}h {duration.Minutes}m",
            _ => $"{(int)duration.TotalDays}d {duration.Hours}h"
        };
    }

    public static string RoutineStatusBadge(RoutineDefinition routine, bool isQueued = false)
    {
        return RoutineStatusText(routine, isQueued) switch
        {
            "Running" => "badge bg-primary-lt text-primary",
            "Queued" => "badge bg-blue-lt text-blue",
            "Paused" => "badge bg-warning-lt text-warning",
            "Disabled" => "badge bg-secondary-lt text-secondary",
            "Failed" => "badge bg-danger-lt text-danger",
            _ => "badge bg-success-lt text-success"
        };
    }

    public static string RoutineStatusText(RoutineDefinition routine, bool isQueued = false)
    {
        if (routine.IsRunning) return "Running";

        if (isQueued) return "Queued";

        if (!routine.Enabled) return "Disabled";

        if (routine.IsPaused) return "Paused";

        if (routine.LastRun != null && !routine.LastRunSuccess) return "Failed";

        return "Enabled";
    }

    public static string RoutineResultText(RoutineDefinition routine)
    {
        if (routine.LastRun == null) return "Never ran";

        return routine.LastRunSuccess ? "Success" : "Failed";
    }

    public static string RoutineResultClass(RoutineDefinition routine)
    {
        if (routine.LastRun == null) return "text-muted";

        return routine.LastRunSuccess ? "text-success" : "text-danger";
    }

    public static async Task<int> GetTotalEpisodeCountAsync(AppDbContext db, Series? series,
        CancellationToken cancellationToken = default)
    {
        if (series == null) return 0;

        return await db.Episodes.AsNoTracking()
            .CountAsync(x => x.ServerId == series.ServerId && x.RemoteSeriesId == series.RemoteId, cancellationToken);
    }

    public static (string Class, string Icon) Delta(decimal value, bool goodIsPositive)
    {
        if (value <= 0) return ("text-warning", "ti ti-minus");
        return goodIsPositive ? ("text-green", "ti ti-trending-up") : ("text-red", "ti ti-trending-down");
    }
}
