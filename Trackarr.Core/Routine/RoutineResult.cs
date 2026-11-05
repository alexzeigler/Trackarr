namespace Trackarr.Core.Routine;

/// <summary>
///     What a routine run reports back to the runner: its summary message and whether it fully succeeded.
///     A run can finish without throwing and still be a failure (say one of three servers was unreachable),
///     so success is reported here rather than inferred from "no exception". A plain string converts to a
///     successful result.
/// </summary>
public sealed record RoutineResult(string Message, bool Success = true)
{
    public static implicit operator RoutineResult(string message) => new(message);

    public static RoutineResult Failed(string message) => new(message, false);
}
