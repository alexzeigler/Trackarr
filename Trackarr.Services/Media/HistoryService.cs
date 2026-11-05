using Microsoft.EntityFrameworkCore;
using Trackarr.Core.Health;
using Trackarr.Core.Media.Missing;
using Trackarr.Core.Queue;
using Trackarr.Data;

namespace Trackarr.Services.Media;

public sealed class HistoryService(IDbContextFactory<AppDbContext> dbFactory)
{
    public async Task<HistoryPageData> GetHistoryAsync(HistoryFilter filter,
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        var from = DateTime.SpecifyKind(filter.From.Date, DateTimeKind.Local).ToUniversalTime();
        var to = DateTime.SpecifyKind(filter.To.Date.AddDays(1).AddTicks(-1), DateTimeKind.Local).ToUniversalTime();

        var servers = await db.MediaServers.AsNoTracking().OrderBy(server => server.Name)
            .ToListAsync(cancellationToken);

        var serverLookup = servers.ToDictionary(server => server.Id, server => server.Name);

        var attemptsQuery = db.DownloadAttempts.AsNoTracking().Include(attempt => attempt.Server)
            .Include(attempt => attempt.HealthSnapshot).Where(attempt =>
                (attempt.FirstSeenAt >= from && attempt.FirstSeenAt <= to) ||
                (attempt.CompletedAt.HasValue && attempt.CompletedAt.Value >= from &&
                 attempt.CompletedAt.Value <= to) ||
                (attempt.FailedAt.HasValue && attempt.FailedAt.Value >= from && attempt.FailedAt.Value <= to) ||
                (attempt.RemovedAt.HasValue && attempt.RemovedAt.Value >= from && attempt.RemovedAt.Value <= to));

        if (filter.ServerId.HasValue)
            attemptsQuery = attemptsQuery.Where(attempt => attempt.ServerId == filter.ServerId.Value);

        if (!string.IsNullOrWhiteSpace(filter.MediaType))
            attemptsQuery = attemptsQuery.Where(attempt => attempt.MediaType.ToString() == filter.MediaType);

        var attempts = await attemptsQuery.OrderByDescending(attempt => attempt.LastSeenAt)
            .ToListAsync(cancellationToken);

        var attemptIds = attempts.Select(attempt => attempt.Id).ToList();

        var healthEvents = await db.HealthEvents.AsNoTracking().Include(ev => ev.DownloadAttempt)
            .ThenInclude(attempt => attempt!.Server).Where(ev => ev.OccurredAt >= from && ev.OccurredAt <= to)
            .Where(ev => attemptIds.Contains(ev.DownloadAttemptId)).OrderByDescending(ev => ev.OccurredAt)
            .ToListAsync(cancellationToken);

        var searchEvents = await db.SearchEvents.AsNoTracking().Where(e => e.OccurredAt >= from && e.OccurredAt <= to)
            .Where(e => filter.ServerId == null || e.ServerId == filter.ServerId).OrderByDescending(e => e.OccurredAt)
            .ToListAsync(cancellationToken);

        var remoteIds = searchEvents.Select(e => e.RemoteId).Distinct().ToList();

        var seriesTitles = await db.Series.AsNoTracking().Where(s => remoteIds.Contains(s.RemoteId))
            .ToDictionaryAsync(s => (s.ServerId, s.RemoteId), s => s.Title, cancellationToken);

        var movieTitles = await db.Movies.AsNoTracking().Where(m => remoteIds.Contains(m.RemoteId))
            .ToDictionaryAsync(m => (m.ServerId, m.RemoteId), m => m.Title, cancellationToken);

        var titleLookup = seriesTitles.Concat(movieTitles).ToDictionary(kv => kv.Key, kv => kv.Value);

        var rows = new List<HistoryRow>();

        foreach (var attempt in attempts)
        {
            rows.Add(CreateAttemptRow(attempt, attempt.FirstSeenAt, "Download Added", "Added",
                "Added to download queue"));

            if (attempt.CompletedAt.HasValue || attempt.WasSuccessful || attempt.WasImported)
            {
                var completedAt = attempt.CompletedAt ?? attempt.LastSeenAt;
                rows.Add(CreateAttemptRow(attempt, completedAt, "Download Completed", "Completed",
                    CompletedDetails(attempt)));
            }

            if (attempt.FailedAt.HasValue ||
                string.Equals(attempt.Status.ToString(), "Failed", StringComparison.OrdinalIgnoreCase))
            {
                var failedAt = attempt.FailedAt ?? attempt.LastSeenAt;
                rows.Add(CreateAttemptRow(attempt, failedAt, "Download Failed", "Failed",
                    attempt.FinalMessage ?? "Download failed"));
            }

            if (attempt.RemovedAt.HasValue ||
                string.Equals(attempt.Status.ToString(), "Removed", StringComparison.OrdinalIgnoreCase))
            {
                var removedAt = attempt.RemovedAt ?? attempt.LastSeenAt;
                var message = attempt.WasRemovedByTrackarr
                    ? "Removed by Trackarr"
                    : attempt.FinalMessage ?? "Download removed";
                rows.Add(CreateAttemptRow(attempt, removedAt, "Download Removed", "Removed", message));
            }
        }

        foreach (var healthEvent in healthEvents)
        {
            if (healthEvent.DownloadAttempt == null) continue;

            rows.Add(new HistoryRow
            {
                Tab = HistoryTab.Health,
                OccurredAt = healthEvent.OccurredAt,
                Event = DisplayEventType(healthEvent.EventType),
                Item = healthEvent.DownloadAttempt.Title,
                MediaType = healthEvent.DownloadAttempt.MediaType,
                Server =
                    healthEvent.DownloadAttempt.Server?.Name ?? healthEvent.DownloadAttempt.ServiceType.ToString(),
                Profile = "—",
                Status = StatusFromSeverity(healthEvent.Severity),
                Details = healthEvent.Message,
                SortValue = healthEvent.OccurredAt.Ticks
            });
        }

        foreach (var e in searchEvents)
        {
            var (label, status, tab) = MapMissingEvent(e.EventType);

            rows.Add(new HistoryRow
            {
                Tab = tab,
                OccurredAt = e.OccurredAt,
                SortValue = e.OccurredAt.Ticks,
                Event = label,
                Status = status,
                MediaType = e.EpisodeId.HasValue ? TrackedMediaType.Episode : TrackedMediaType.Series,
                Item = titleLookup.GetValueOrDefault((e.ServerId, e.RemoteId), $"#{e.RemoteId}"),
                Server = serverLookup.GetValueOrDefault(e.ServerId, "—"),
                Profile = "",
                Details = BuildDetail(e)
            });
        }

        var stats = BuildStats(attempts);
        var searchStats = BuildSearchStats(searchEvents);
        var activity = BuildActivity(attempts, searchEvents, from, to);
        var breakdown = BuildBreakdown(stats);

        return new HistoryPageData
        {
            Servers = servers,
            Stats = stats,
            SearchStats = searchStats,
            Activity = activity,
            Breakdown = breakdown,
            Rows = rows.OrderByDescending(row => row.OccurredAt).ToList()
        };
    }

    private static HistoryRow CreateAttemptRow(DownloadAttempt attempt, DateTime occurredAt, string ev, string status,
        string details)
    {
        return new HistoryRow
        {
            Tab = HistoryTab.DownloadEvents,
            OccurredAt = occurredAt,
            Event = ev,
            Item = attempt.Title,
            MediaType = attempt.MediaType,
            Server = attempt.Server?.Name ?? attempt.ServiceType.ToString(),
            Profile = "—",
            Status = status,
            Details = details,
            SortValue = occurredAt.Ticks
        };
    }

    private static (string Label, string Status, HistoryTab Tab) MapMissingEvent(MissingEventType type)
    {
        return type switch
        {
            MissingEventType.SearchPushed => ("Search Pushed", "Pending", HistoryTab.Searches),
            MissingEventType.SearchFailed => ("Search Failed", "Failed", HistoryTab.Searches),
            MissingEventType.DowngradedToFallback => ("Downgraded to Fallback", "Downgraded", HistoryTab.Changes),
            MissingEventType.CascadedToEpisode => ("Cascaded to Episode", "Cascaded", HistoryTab.Changes),
            MissingEventType.Resolved => ("Resolved", "Completed", HistoryTab.DownloadEvents),
            MissingEventType.Abandoned => ("Abandoned", "Failed", HistoryTab.Changes),
            MissingEventType.Retried => ("Retried After Abandon", "Pending", HistoryTab.Changes),
            MissingEventType.ProfileRestored => ("Quality Profile Restored", "Completed", HistoryTab.Changes),
            _ => (type.ToString(), "Unknown", HistoryTab.Events)
        };
    }

    private static string BuildDetail(MissingSearchEvent e)
    {
        if (!string.IsNullOrWhiteSpace(e.Detail)) return e.Detail;
        var parts = new List<string> { e.Tier.ToString() };
        if (e.FailedSearchCount > 0) parts.Add($"{e.FailedSearchCount} failed");
        return string.Join(" · ", parts);
    }

    private static HistoryStats BuildStats(List<DownloadAttempt> attempts)
    {
        var total = attempts.Count;
        var completed = attempts.Count(IsCompleted);
        var failed = attempts.Count(IsFailed);
        var removed = attempts.Count(IsRemoved);
        var downloaded = attempts.Where(IsCompleted).Sum(attempt => attempt.HealthSnapshot?.LastSize ?? 0);

        var durations = attempts
            .Where(attempt => IsCompleted(attempt) && attempt.CompletedAt.HasValue &&
                              attempt.CompletedAt.Value > attempt.FirstSeenAt).Select(attempt =>
                (attempt.CompletedAt!.Value - attempt.FirstSeenAt).TotalSeconds).ToList();

        return new HistoryStats
        {
            TotalDownloads = total,
            Completed = completed,
            Failed = failed,
            Removed = removed,
            Added = total,
            CompletedPercent = Percent(completed, total),
            FailedPercent = Percent(failed, total),
            RemovedPercent = Percent(removed, total),
            AddedPercent = Percent(total, total),
            AverageCompletionTime = durations.Count == 0 ? null : TimeSpan.FromSeconds(durations.Average()),
            DataDownloaded = downloaded
        };
    }

    private static SearchStats BuildSearchStats(List<MissingSearchEvent> events)
    {
        return new SearchStats
        {
            SearchesPushed = events.Count(e => e.EventType == MissingEventType.SearchPushed),
            Cascaded = events.Count(e => e.EventType == MissingEventType.CascadedToEpisode),
            Abandoned = events.Count(e => e.EventType == MissingEventType.Abandoned),
            Resolved = events.Count(e => e.EventType == MissingEventType.Resolved)
        };
    }

    private static List<HistoryActivityPoint> BuildActivity(List<DownloadAttempt> attempts,
        List<MissingSearchEvent> searchEvents, DateTime from, DateTime to)
    {
        var days = Enumerable.Range(0, Math.Max(1, (to.Date - from.Date).Days + 1))
            .Select(offset => from.Date.AddDays(offset)).ToList();

        return days.Select(day =>
        {
            var next = day.AddDays(1);

            int Search(MissingEventType type)
            {
                return searchEvents.Count(e => e.EventType == type && e.OccurredAt >= day && e.OccurredAt < next);
            }

            return new HistoryActivityPoint
            {
                Date = day,
                Added = attempts.Count(a => a.FirstSeenAt >= day && a.FirstSeenAt < next),
                Completed =
                    attempts.Count(a =>
                        a.CompletedAt.HasValue && a.CompletedAt.Value >= day && a.CompletedAt.Value < next),
                Failed =
                    attempts.Count(a => a.FailedAt.HasValue && a.FailedAt.Value >= day && a.FailedAt.Value < next),
                Removed =
                    attempts.Count(a =>
                        a.RemovedAt.HasValue && a.RemovedAt.Value >= day && a.RemovedAt.Value < next),
                Bytes =
                    attempts.Where(a =>
                            a.CompletedAt.HasValue && a.CompletedAt.Value >= day && a.CompletedAt.Value < next)
                        .Sum(a => a.HealthSnapshot?.LastSize ?? 0),
                SearchesPushed = Search(MissingEventType.SearchPushed),
                Cascaded = Search(MissingEventType.CascadedToEpisode),
                Abandoned = Search(MissingEventType.Abandoned),
                Resolved = Search(MissingEventType.Resolved)
            };
        }).ToList();
    }

    private static List<HistoryBreakdownItem> BuildBreakdown(HistoryStats stats)
    {
        var total = Math.Max(1, stats.TotalDownloads);

        return
        [
            new HistoryBreakdownItem
            {
                Label = "Completed", Count = stats.Completed, Percent = Percent(stats.Completed, total)
            },
            new HistoryBreakdownItem { Label = "Failed", Count = stats.Failed, Percent = Percent(stats.Failed, total) },
            new HistoryBreakdownItem
            {
                Label = "Removed", Count = stats.Removed, Percent = Percent(stats.Removed, total)
            },
            new HistoryBreakdownItem { Label = "Added", Count = stats.Added, Percent = Percent(stats.Added, total) }
        ];
    }

    private static string CompletedDetails(DownloadAttempt attempt)
    {
        var duration = attempt.CompletedAt.HasValue && attempt.CompletedAt.Value > attempt.FirstSeenAt
            ? attempt.CompletedAt.Value - attempt.FirstSeenAt
            : (TimeSpan?)null;

        var size = attempt.HealthSnapshot?.LastSize;

        return duration == null && size == null
            ? "Download completed"
            : $"Downloaded in {FormatDuration(duration)}{(size.HasValue ? $" • {FormatBytes(size.Value)}" : "")}";
    }

    private static bool IsCompleted(DownloadAttempt attempt)
    {
        return attempt.CompletedAt.HasValue || attempt.WasSuccessful || attempt.WasImported ||
               string.Equals(attempt.Status.ToString(), "Completed", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(attempt.Status.ToString(), "Complete", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsFailed(DownloadAttempt attempt)
    {
        return attempt.FailedAt.HasValue ||
               string.Equals(attempt.Status.ToString(), "Failed", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsRemoved(DownloadAttempt attempt)
    {
        return attempt.RemovedAt.HasValue ||
               string.Equals(attempt.Status.ToString(), "Removed", StringComparison.OrdinalIgnoreCase);
    }

    private static decimal Percent(int value, int total)
    {
        return total <= 0 ? 0 : Math.Round(value / (decimal)total * 100, 1);
    }

    private static string StatusFromSeverity(Severity severity)
    {
        return severity.ToString().ToLowerInvariant() switch
        {
            "success" => "Success",
            "info" => "Success",
            "warning" => "Warning",
            "error" => "Failed",
            "critical" => "Failed",
            "severe" => "Failed",
            _ => severity.ToString()
        };
    }

    private static string DisplayEventType(HealthEventType eventType)
    {
        var value = eventType.ToString();
        return string.Concat(value.Select((ch, index) => index > 0 && char.IsUpper(ch) ? $" {ch}" : ch.ToString()));
    }

    private static string FormatDuration(TimeSpan? value)
    {
        if (value == null || value.Value.TotalSeconds <= 0) return "—";

        if (value.Value.TotalDays >= 1) return $"{(int)value.Value.TotalDays}d {value.Value.Hours}h";

        if (value.Value.TotalHours >= 1) return $"{(int)value.Value.TotalHours}h {value.Value.Minutes}m";

        return $"{Math.Max(1, value.Value.Minutes)}m";
    }

    private static string FormatBytes(long bytes)
    {
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
}
