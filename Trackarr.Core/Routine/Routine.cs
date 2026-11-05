namespace Trackarr.Core.Routine;

public abstract class Routine : IRoutine
{
    #region Methods

    public abstract Task<RoutineResult> ExecuteAsync(CancellationToken cancellationToken);

    #endregion

    #region Configuration

    public virtual string Key { get; set; } = string.Empty;
    public virtual string Name { get; set; } = string.Empty;
    public virtual string Description { get; set; } = string.Empty;
    public virtual bool Enabled { get; set; } = true;
    public virtual int IntervalSeconds { get; set; }
    public virtual DateTimeOffset NextRun { get; set; }

    #endregion
}
