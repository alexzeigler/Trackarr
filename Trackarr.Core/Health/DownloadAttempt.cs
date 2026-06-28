using Trackarr.Core.Server;

namespace Trackarr.Core.Health;

public class DownloadAttempt
{
    #region Identity

    public int Id { get; set; }

    public int? ServerId { get; set; }

    public ServiceType ServiceType { get; set; }

    public string DownloadId { get; set; } = string.Empty;
    public int? RemoteId { get; set; }

    #endregion

    #region Properties

    public string Title { get; set; } = string.Empty;
    public string NormalizedTitle { get; set; } = string.Empty;
    public string? ReleaseKey { get; set; }

    public string? DownloadClient { get; set; }
    public string? Indexer { get; set; }
    public string? Protocol { get; set; }

    public TrackedMediaType MediaType { get; set; } = TrackedMediaType.Unknown;

    public int? MovieId { get; set; }
    public int? SeriesId { get; set; }
    public int? EpisodeId { get; set; }

    public DateTime FirstSeenAt { get; set; }
    public DateTime LastSeenAt { get; set; }

    public DateTime? CompletedAt { get; set; }
    public DateTime? RemovedAt { get; set; }
    public DateTime? FailedAt { get; set; }
    public DateTime? ReplacedAt { get; set; }

    public AttemptStatus Status { get; set; } = AttemptStatus.Active;

    public string? FinalReasonKey { get; set; }
    public string? FinalMessage { get; set; }

    public int ReplacementAttemptNumber { get; set; }

    public bool WasRemovedByTrackarr { get; set; }
    public bool WasImported { get; set; }
    public bool WasSuccessful { get; set; }
    public bool IsProtected { get; set; }

    #endregion

    #region Navigation

    public MediaServer? Server { get; set; }
    public HealthSnapshot? HealthSnapshot { get; set; }
    public List<DownloadHealthEvent> HealthEvents { get; set; } = [];

    #endregion
}
