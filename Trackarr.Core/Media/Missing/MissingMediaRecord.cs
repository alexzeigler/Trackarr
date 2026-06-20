namespace Trackarr.Core.Media.Missing;

public class MissingMediaRecord
{
    public int Id { get; set; }

    public int ServerId { get; set; }
    public int RemoteId { get; set; }
    public int? EpisodeId { get; set; }

    public MissingSearchTier Tier { get; set; }
    public MissingSearchState SearchState { get; set; }

    public int FailedSearchCount { get; set; }
    public DateTime FirstSeenMissingAt { get; set; }
    public DateTime? LastSearchPushedAt { get; set; }
    public DateTime LastEvaluatedAt { get; set; }

    public bool LoweredToFallback { get; set; }

    public string Key => EpisodeId is null
        ? $"{ServerId}_{RemoteId}"
        : $"{ServerId}_{RemoteId}_{EpisodeId}";
}