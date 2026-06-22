namespace Trackarr.Core.Queue;

public class QueuePageData
{
    public QueueStats Stats { get; set; } = new();
    public List<QueueStatusBreakdown> StatusBreakdown { get; set; } = [];
    public List<QueueScoreBucket> ScoreDistribution { get; set; } = [];
    public List<QueueActivityPoint> Activity { get; set; } = [];
}

public enum Trend
{
    Up,
    Flat,
    Down
}

public class QueueStats
{
    public int TotalInQueue { get; set; }
    public int Downloading { get; set; }
    public int AtRisk { get; set; }
    public int AvgHealthScore { get; set; }
    public long DataRemaining { get; set; }
    public TimeSpan? OldestInQueue { get; set; }
    public Trend ActiveTrend { get; set; } = Trend.Flat;
    public Trend AtRiskTrend { get; set; } = Trend.Flat;
    public Trend ScoreTrend { get; set; } = Trend.Flat;
}

public class QueueStatusBreakdown
{
    public string Label { get; set; } = string.Empty;
    public int Count { get; set; }
    public decimal Percent { get; set; }
}

public class QueueScoreBucket
{
    public string Label { get; set; } = string.Empty;
    public int Count { get; set; }
}

public class QueueActivityPoint
{
    public string Label { get; set; } = string.Empty;
    public int Added { get; set; }
    public int Completed { get; set; }
    public int Removed { get; set; }
    public int Failed { get; set; }
}
