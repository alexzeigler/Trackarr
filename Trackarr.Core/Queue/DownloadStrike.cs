using System.Text.Json.Serialization;

namespace Trackarr.Core.Queue;

public class DownloadStrike {
    [JsonIgnore] public int Id { get; set; }

    public int DownloadItemId { get; set; }

    [JsonIgnore] public DownloadItem DownloadItem { get; set; } = null!;

    public DownloadStrikeReason Reason { get; set; }
    public DownloadStrikeSeverity Severity { get; set; }

    public string? Message { get; set; }
    public string? Fingerprint { get; set; }

    public string? ObservedStatus { get; set; }
    public string? ObservedErrorMessage { get; set; }
    public decimal? ObservedProgress { get; set; }
    public long? ObservedSize { get; set; }
    public long? ObservedSizeLeft { get; set; }
    public string? ObservedDownloadId { get; set; }
    public string? ObservedDownloadClient { get; set; }
    public string? ObservedIndexer { get; set; }

    public bool CountsAgainstDownload { get; set; } = true;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ResolvedAtUtc { get; set; }

    public string? Resolution { get; set; }

    [JsonIgnore]
    public bool IsActive => ResolvedAtUtc is null;
}