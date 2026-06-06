namespace Trackarr.Core.Routine;

public interface IRoutine {
    public int Id { get; set; }
    public string Key { get; }
    public string Name { get; set; }
    public string Description { get; set; }
    public bool Enabled { get; set; }
    public int LastRunTime { get; set; }
    public int IntervalSeconds { get; set; }
    public DateTime LastRun { get; set; }
    public DateTimeOffset NextRun { get; set; }
    public bool IsRunning { get; set; }
    public bool IsPaused { get; set; }
    public bool LastRunSuccess { get; set; }
    IEnumerable<RoutineRun>? Runs { get; set; }
    public Task<string> ExecuteAsync(CancellationToken cancellationToken);
    public Task RegisterRoutineInDbAsync(CancellationToken cancellationToken);
}