using Trackarr.Core.Queue;

namespace Trackarr.Core.Media.Missing;

public class MissingPageData
{
    public MissingStats Stats { get; set; } = new();
    public List<MissingStateBreakdown> StateBreakdown { get; set; } = [];
    public List<MissingActivityPoint> Activity { get; set; } = [];
}

public class MissingStats
{
    public int TotalMissing { get; set; }
    public int SearchReady { get; set; }
    public int SearchesPushed24H { get; set; }
    public int Resolved24H { get; set; }
    public int FailedSearches { get; set; }
    public int Abandoned { get; set; }
    public Trend SearchTrend { get; set; } = Trend.Flat;
    public Trend ResolvedTrend { get; set; } = Trend.Flat;
}

public class MissingStateBreakdown
{
    public string Label { get; set; } = string.Empty;
    public int Count { get; set; }
    public decimal Percent { get; set; }
}

public class MissingActivityPoint
{
    public string Label { get; set; } = string.Empty;
    public int Pushed { get; set; }
    public int Cascaded { get; set; }
    public int Resolved { get; set; }
    public int Failed { get; set; }
    public int Abandoned { get; set; }
}
