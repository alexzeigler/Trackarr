namespace Trackarr.Core.Media.Missing;

public class MissingSearchEvent
{
    public int Id { get; set; }

    public int ServerId { get; set; }
    public int RemoteId { get; set; }
    public int? EpisodeId { get; set; }

    public MissingEventType EventType { get; set; }
    public MissingSearchTier Tier { get; set; }
    public int FailedSearchCount { get; set; }

    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
    public string? Detail { get; set; }
}
