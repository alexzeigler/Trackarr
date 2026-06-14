using Trackarr.Core.Server;

namespace Trackarr.Core.Health;

public class SourceHealthSummary {
    public int Id { get; set; }

    public int? ServerId { get; set; }
    public MediaServer? Server { get; set; }

    public ServiceType? ServiceType { get; set; }

    public string? Indexer { get; set; }
    public string? DownloadClient { get; set; }
    public string? Protocol { get; set; }
    public string? ReleaseKey { get; set; }
    public string? ReasonKey { get; set; }

    public int AttemptCount { get; set; }
    public int FailureCount { get; set; }
    public int RecoveryCount { get; set; }
    public int SuccessCount { get; set; }
    public int RemovalCount { get; set; }

    public decimal AverageScore { get; set; }
    public int WorstScore { get; set; }
    public int BestScore { get; set; }

    public int ReputationScore { get; set; } = 70;

    public DateTime? FirstSeenAt { get; set; }
    public DateTime? LastSeenAt { get; set; }
    public DateTime? LastFailureAt { get; set; }
    public DateTime? LastSuccessAt { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}