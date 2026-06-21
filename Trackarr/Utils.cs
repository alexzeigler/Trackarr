using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using Trackarr.Core.Media.Shows;
using Trackarr.Core.Queue;
using Trackarr.Core.Routine;
using Trackarr.Core.Server;
using Trackarr.Data;

namespace Trackarr;

public static class Utils
{
    public static string StatusBadgeClass(string? status)
    {
        return status?.ToLowerInvariant() switch
        {
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
            "missing" => "status-badge status-missing",
            _ => "status-badge status-paused"
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
        return value?.ToString("MMM d, yyyy h:mm tt") ?? "—";
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
            case < 0:
                return "score-badge score-removed";
            case null:
                return "score-badge-muted";
            default:
                score = Math.Clamp((int)score, 0, 100);

                return score switch
                {
                    <= 39 => "score-badge-red",
                    <= 69 => "score-badge-orange",
                    <= 89 => "score-badge-yellow",
                    _ => "score-badge-green"
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

    public static MarkupString GetChecked(bool value, int size = 24)
    {
        if (value)
            return (MarkupString)
                $"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{size}\" height=\"{size}\" viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\" stroke-linecap=\"round\" stroke-linejoin=\"round\" class=\"icon text-green icon-2\"><path d=\"M5 12l5 5l10 -10\"></path></svg>";

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

    public static string RoutineStatusBadge(RoutineDefinition routine)
    {
        return RoutineStatusText(routine) switch
        {
            "Running" => "bg-primary-lt",
            "Paused" => "bg-warning-lt",
            "Disabled" => "bg-secondary-lt",
            "Failed" => "bg-danger-lt",
            _ => "bg-success-lt"
        };
    }

    public static string RoutineStatusText(RoutineDefinition routine)
    {
        if (routine.IsRunning) return "Running";

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