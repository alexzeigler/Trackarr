namespace Trackarr.Core.Routine;

/// <summary>
///     A background routine's code-owned defaults and work. Run state (last run, running, paused, history) lives
///     on <see cref="RoutineDefinition" />, which the runner reads and the UI shows.
/// </summary>
public interface IRoutine
{
    string Key { get; }
    string Name { get; set; }
    string Description { get; set; }
    bool Enabled { get; set; }
    int IntervalSeconds { get; set; }
    DateTimeOffset NextRun { get; set; }
    Task<RoutineResult> ExecuteAsync(CancellationToken cancellationToken);
}
