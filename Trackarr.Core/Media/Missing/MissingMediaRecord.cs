namespace Trackarr.Core.Media.Missing;

public class MissingMediaRecord
{
    #region Computed

    public string Key => EpisodeId is null ? $"{ServerId}_{RemoteId}" : $"{ServerId}_{RemoteId}_{EpisodeId}";

    #endregion

    #region Identity

    public int Id { get; set; }

    public int ServerId { get; set; }
    public int RemoteId { get; set; }
    public int? EpisodeId { get; set; }

    #endregion

    #region Properties

    public MissingSearchTier Tier { get; set; }
    public MissingSearchState SearchState { get; set; }

    public int FailedSearchCount { get; set; }
    public DateTime FirstSeenMissingAt { get; set; }
    public DateTime? LastSearchPushedAt { get; set; }
    public DateTime LastEvaluatedAt { get; set; }

    public bool LoweredToFallback { get; set; }

    /// <summary>
    ///     Remote quality profile ID the movie/series had before Trackarr lowered it to the fallback, so it can be
    ///     put back once the item imports. Null if it was never lowered, or was lowered before this was tracked.
    /// </summary>
    public int? OriginalQualityProfileId { get; set; }

    #endregion
}
