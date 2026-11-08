using Trackarr.Core.Actions;
using Trackarr.Core.Server;

namespace Trackarr.Services.Actions;

/// <summary>Who asked for an action. Passive Mode applies to everything that isn't <see cref="User" />.</summary>
public static class ActionSource
{
    public const string User = "user";

    public static string Routine(string key)
    {
        return $"routine:{key}";
    }
}

public enum SearchKind
{
    Movies,
    Episodes,
    Series
}

/// <summary>
///     Something Trackarr wants to change on a Radarr or Sonarr server. It only happens through
///     <see cref="ActionExecutor" />, which decides whether it's sent and logs it either way.
/// </summary>
public abstract record ServerAction(MediaServer Server, string Reason, string Source)
{
    public abstract ActionKind Kind { get; }

    /// <summary>What it acts on, as the action log shows it.</summary>
    public abstract string Target { get; }

    /// <summary>Started by Trackarr on its own (a routine), not by a click.</summary>
    public bool IsAutomatic => Source != ActionSource.User;
}

/// <summary>Removes a download from the queue. By default it's also removed from the client and blocklisted.</summary>
public sealed record RemoveFromQueue(
    MediaServer Server,
    int QueueId,
    string DownloadId,
    string Title,
    string Reason,
    string Source,
    bool Blocklist = true,
    bool RemoveFromClient = true) : ServerAction(Server, Reason, Source)
{
    public override ActionKind Kind => ActionKind.RemoveFromQueue;
    public override string Target => Title;
}

/// <summary>
///     One search command: MoviesSearch or EpisodeSearch for all <see cref="Ids" />, or a SeriesSearch for each
///     series in them.
/// </summary>
public sealed record TriggerSearch(
    MediaServer Server,
    SearchKind What,
    IReadOnlyList<int> Ids,
    string Description,
    string Reason,
    string Source) : ServerAction(Server, Reason, Source)
{
    public override ActionKind Kind => What switch
    {
        SearchKind.Movies => ActionKind.SearchMovies,
        SearchKind.Episodes => ActionKind.SearchEpisodes,
        _ => ActionKind.SearchSeries
    };

    public override string Target => Description;
}

/// <summary>
///     Sets a movie's or series' quality profile. With <see cref="OnlyIfCurrent" />, nothing changes unless that's
///     the current profile, so a revert never overwrites a profile someone changed by hand.
/// </summary>
public sealed record SetQualityProfile(
    MediaServer Server,
    int RemoteItemId,
    int ProfileId,
    int? OnlyIfCurrent,
    string Title,
    string Reason,
    string Source) : ServerAction(Server, Reason, Source)
{
    public override ActionKind Kind => ActionKind.SetQualityProfile;
    public override string Target => Title;
}

/// <param name="Outcome">What happened.</param>
/// <param name="Error">Why it failed or was skipped.</param>
/// <param name="PreviousProfileId">For <see cref="SetQualityProfile" />: the profile it had before.</param>
public sealed record ActionResult(ActionOutcome Outcome, string? Error = null, int? PreviousProfileId = null)
{
    public bool Executed => Outcome == ActionOutcome.Executed;
}
