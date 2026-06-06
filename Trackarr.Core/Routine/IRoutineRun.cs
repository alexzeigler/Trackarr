namespace Trackarr.Core.Routine;

public interface IRoutineRun {
    public int Id { get; set; }
    public IRoutine? Routine { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public RoutineState State { get; set; }
    public string? ErrorMessage { get; set; }
}

public enum RoutineState {
    Created,
    WaitingToRun,
    Running,
    Success,
    Canceled,
    Failed
}