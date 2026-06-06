namespace Trackarr.Core.Routine;

public sealed class RoutineDefinition {
    public int Id { get; set; }
    public required string Key { get; set; }
    public required string Name { get; set; }
    public required string Description { get; set; }
    public bool Enabled { get; set; }
    public int IntervalSeconds { get; set; }
    public DateTime? LastRun { get; set; }
    public DateTime? NextRun { get; set; }
    public bool IsRunning { get; set; }
    public bool IsPaused { get; set; }
    public bool LastRunSuccess { get; set; }

    public List<RoutineRun> Runs { get; set; } = [];
    public int LastRunTime { get; set; }
}