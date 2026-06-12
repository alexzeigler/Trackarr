namespace Trackarr.Core.Queue;

public sealed class DownloadStrikePolicy {
    public TimeSpan NoProgressGracePeriod { get; set; } = TimeSpan.FromMinutes(30);
    public TimeSpan NoProgressStrikeInterval { get; set; } = TimeSpan.FromMinutes(30);
    public int MaxNoProgressStrikes { get; set; } = 3;
    public int MaxActiveStrikesBeforeFailed { get; set; } = 3;
}