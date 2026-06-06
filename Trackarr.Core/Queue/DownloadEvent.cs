namespace Trackarr.Core.Queue;

public class DownloadEvent {
    public int Id { get; set; }
    public int ServerId { get; set; }
    public string? DownloadId { get; set; }
    public QueueState State { get; set; }
    public QueueState PreviousState { get; set; }
    public string? Message { get; set; }
    public DateTime Timestamp { get; set; }
}