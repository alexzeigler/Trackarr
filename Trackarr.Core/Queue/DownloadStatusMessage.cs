namespace Trackarr.Core.Queue;

public class DownloadStatusMessage {
    public string? Title { get; set; }
    public List<string> Messages { get; set; } = [];
}