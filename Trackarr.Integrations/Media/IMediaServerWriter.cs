namespace Trackarr.Integrations.Media;

/// <summary>
///     Changes things on a Radarr or Sonarr server. Only the action gate (<c>ActionExecutor</c>) should be given one
///     (ROADMAP 4.5), so Passive Mode and the action log can't be skipped.
/// </summary>
public interface IMediaServerWriter
{
    /// <summary>Removes a queue item. Removing one row of a multi-row download (a season pack) removes all of it.</summary>
    Task RemoveFromQueueAsync(int queueId, bool removeFromClient, bool blocklist, CancellationToken ct = default);

    /// <summary>
    ///     Sets a movie's or series' quality profile. With <paramref name="onlyIfCurrent" />, nothing changes unless
    ///     that's the current profile, so a revert never overwrites a profile someone changed by hand.
    /// </summary>
    Task<ProfileChange> SetQualityProfileAsync(int remoteId, int profileId, int? onlyIfCurrent = null,
        CancellationToken ct = default);

    /// <summary>
    ///     Starts searches and returns the command IDs the server gave them. A movie or episode search is one command
    ///     for all <paramref name="ids" />; a series search is one command per series.
    /// </summary>
    Task<IReadOnlyList<int>> SearchAsync(ArrSearch kind, IReadOnlyCollection<int> ids,
        CancellationToken ct = default);
}

public enum ArrSearch
{
    /// <summary>Radarr's MoviesSearch.</summary>
    Movies,

    /// <summary>Sonarr's EpisodeSearch.</summary>
    Episodes,

    /// <summary>Sonarr's SeriesSearch.</summary>
    Series
}

/// <param name="Previous">The profile before the call, or null when the response had none.</param>
/// <param name="Outcome">What happened.</param>
public sealed record ProfileChange(int? Previous, ProfileChangeOutcome Outcome);

public enum ProfileChangeOutcome
{
    /// <summary>The profile was set.</summary>
    Changed,

    /// <summary>It already had that profile, so nothing was sent.</summary>
    AlreadySet,

    /// <summary>It didn't have the expected profile (someone changed it by hand), so it was left alone.</summary>
    LeftAlone
}
