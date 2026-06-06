namespace Trackarr.Core.Queue;

public class Strike {
    public int Id { get; set; }
    public int ServerId { get; set; }
    public string DownloadId { get; set; } = "";
    public StrikeReason Reason { get; set; }
    public QueueState QueueState { get; set; }
    public QueueStatus QueueStatus { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public enum StrikeReason {
    StalledNoConnections,
    StalledMetadata,
    DownloadFailed,
    ImportFailed,
    Blocked,
    Stuck,
    NoPeers,
    DownloadClientError
}