namespace Trackarr.Core.Routine;

public interface IRoutine
{
    int Id { get; set; }
    string Key { get; }
    string Name { get; set; }
    string Description { get; set; }
    bool Enabled { get; set; }
    int LastRunTime { get; set; }
    int IntervalSeconds { get; set; }
    DateTime LastRun { get; set; }
    DateTimeOffset NextRun { get; set; }
    bool IsRunning { get; set; }
    bool IsPaused { get; set; }
    bool LastRunSuccess { get; set; }
    IEnumerable<RoutineRun>? Runs { get; set; }
    Task<string> ExecuteAsync(CancellationToken cancellationToken);
}
