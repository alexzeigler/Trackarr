namespace Trackarr.Core.Routine;

public sealed class RoutineRun
{
    public int Id { get; set; }

    public int RoutineDefinitionId { get; init; }
    public RoutineDefinition? Routine { get; set; }

    public DateTime StartTime { get; set; }
    public DateTime? EndTime { get; set; }

    public RoutineState State { get; set; }
    public string? ErrorMessage { get; set; }
}