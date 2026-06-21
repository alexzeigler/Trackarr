using Trackarr.Core.Health;
using Trackarr.Core.Media.Missing;
using Trackarr.Core.Server;

namespace Trackarr.Core.Queue;

public enum HistoryTab
{
    DownloadEvents,
    Events,
    Searches,
    Health,
    Changes
}

public class HistoryFilter
{
    public DateTime From { get; set; }
    public DateTime To { get; set; }
    public int? ServerId { get; set; }
    public string? MediaType { get; set; }
}

public class HistoryPageData
{
    public List<MediaServer> Servers { get; set; } = [];
    public HistoryStats Stats { get; set; } = new();
    public List<HistoryActivityPoint> Activity { get; set; } = [];
    public List<HistoryBreakdownItem> Breakdown { get; set; } = [];
    public List<HistoryRow> Rows { get; set; } = [];
    public SearchStats SearchStats { get; set; } = new();
}

public class HistoryStats
{
    public int TotalDownloads { get; set; }
    public int Completed { get; set; }
    public int Failed { get; set; }
    public int Removed { get; set; }
    public int Added { get; set; }

    public decimal CompletedPercent { get; set; }
    public decimal FailedPercent { get; set; }
    public decimal RemovedPercent { get; set; }
    public decimal AddedPercent { get; set; }

    public TimeSpan? AverageCompletionTime { get; set; }
    public long DataDownloaded { get; set; }
}

public class HistoryActivityPoint
{
    public DateTime Date { get; set; }
    public int Completed { get; set; }
    public int Failed { get; set; }
    public int Removed { get; set; }
    public int Added { get; set; }
    public long Bytes { get; set; }
    public int SearchesPushed { get; set; }
    public int Cascaded { get; set; }
    public int Abandoned { get; set; }
    public int Resolved { get; set; }
}

public class HistoryBreakdownItem
{
    public string Label { get; set; } = string.Empty;
    public int Count { get; set; }
    public decimal Percent { get; set; }
}

public class HistoryRow
{
    public HistoryTab Tab { get; set; }
    public DateTime OccurredAt { get; set; }
    public string Event { get; set; } = string.Empty;
    public string? Item { get; set; } = string.Empty;
    public TrackedMediaType MediaType { get; set; } = TrackedMediaType.Unknown;
    public string Server { get; set; } = string.Empty;
    public string Profile { get; set; } = "—";
    public string Status { get; set; } = string.Empty;
    public string Details { get; set; } = string.Empty;
    public long SortValue { get; set; }
}