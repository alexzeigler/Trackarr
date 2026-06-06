using Trackarr.Core.Exception;

namespace Trackarr.Core.Routine;

public abstract class Routine : IRoutine {
    public int Id { get; set; }
    public virtual string Key { get; set; } = string.Empty;
    public virtual string Name { get; set; } = string.Empty;
    public virtual string Description { get; set; } = string.Empty;
    public virtual bool Enabled { get; set; } = false;
    public int LastRunTime { get; set; }
    public virtual int IntervalSeconds { get; set; }
    public DateTime LastRun { get; set; }
    public virtual DateTimeOffset NextRun { get; set; }
    public bool IsRunning { get; set; }
    public bool IsPaused { get; set; }
    public bool LastRunSuccess { get; set; }
    public IEnumerable<RoutineRun>? Runs { get; set; }

    public virtual Task<string> ExecuteAsync(CancellationToken cancellationToken) {
        throw new TaskNotDefinedException("No task defined for routine ", this);
    }

    public virtual Task RegisterRoutineInDbAsync(CancellationToken cancellationToken) {
        throw new TaskNotDefinedException("No task defined for routine ", this);
    }
}