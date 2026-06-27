namespace Trackarr.Core.Routine;

public enum RoutineState
{
    Created,
    WaitingToRun,
    Running,
    Success,
    Canceled,
    Failed
}
