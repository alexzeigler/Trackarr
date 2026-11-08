namespace Trackarr.Core.Actions;

/// <summary>What Trackarr asked Radarr or Sonarr to do.</summary>
public enum ActionKind
{
    RemoveFromQueue,
    SearchMovies,
    SearchEpisodes,
    SearchSeries,
    SetQualityProfile
}

public enum ActionOutcome
{
    /// <summary>Sent, and the server accepted it.</summary>
    Executed,

    /// <summary>Passive Mode was on, so nothing was sent. This is the dry-run report.</summary>
    Simulated,

    /// <summary>Not sent because a rule said no at the last moment, such as a protected download.</summary>
    Skipped,

    /// <summary>Sent, and the server or the connection failed.</summary>
    Failed
}

/// <summary>
///     One row of the action log: every change Trackarr made, would have made in Passive Mode, or tried to make on a
///     server. Written only by the action gate. A simulated action that repeats run after run updates its row
///     (<see cref="LastOccurredAt" />, <see cref="Count" />) instead of adding one each time.
/// </summary>
public class ActionLogEntry
{
    public int Id { get; set; }

    public DateTime OccurredAt { get; set; }
    public DateTime LastOccurredAt { get; set; }
    public int Count { get; set; } = 1;

    /// <summary>No foreign key: the log outlives a deleted server, so the name is kept too.</summary>
    public int ServerId { get; set; }

    public string ServerName { get; set; } = string.Empty;

    public ActionKind Kind { get; set; }

    /// <summary>What it acted on, for people: a title, or a few titles for a batch search.</summary>
    public string Target { get; set; } = string.Empty;

    public string Reason { get; set; } = string.Empty;

    /// <summary>"user", or "routine:" and the routine's key.</summary>
    public string Source { get; set; } = string.Empty;

    public ActionOutcome Outcome { get; set; }
    public string? Error { get; set; }
}
