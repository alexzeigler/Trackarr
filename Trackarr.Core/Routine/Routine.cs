namespace Trackarr.Core.Routine;

public abstract class Routine : IRoutine
{
    #region Methods

    public abstract Task<string> ExecuteAsync(CancellationToken cancellationToken);

    #endregion

    #region Configuration

    public int Id { get; set; }
    public virtual string Key { get; set; } = string.Empty;
    public virtual string Name { get; set; } = string.Empty;
    public virtual string Description { get; set; } = string.Empty;
    public virtual bool Enabled { get; set; } = true;
    public int LastRunTime { get; set; }
    public virtual int IntervalSeconds { get; set; }
    public DateTime LastRun { get; set; }
    public virtual DateTimeOffset NextRun { get; set; }

    #endregion

    #region State

    public bool IsRunning { get; set; }
    public bool IsPaused { get; set; }
    public bool LastRunSuccess { get; set; }
    public IEnumerable<RoutineRun>? Runs { get; set; }

    #endregion
}
