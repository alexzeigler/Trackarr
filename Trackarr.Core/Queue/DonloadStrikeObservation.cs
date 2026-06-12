namespace Trackarr.Core.Queue;

public sealed class DownloadStrikeObservation {
    public string? Status { get; set; }
    public string? ErrorMessage { get; set; }
    public decimal? Progress { get; set; }
    public long? Size { get; set; }
    public long? SizeLeft { get; set; }
    public string? DownloadId { get; set; }
    public string? DownloadClient { get; set; }
    public string? Indexer { get; set; }
}