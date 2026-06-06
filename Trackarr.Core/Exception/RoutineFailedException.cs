using Trackarr.Core.Routine;

namespace Trackarr.Core.Exception;

public class RoutineFailedException(string message, IRoutine routine)
    : System.Exception($"{message}: {routine.Name} (Id: {routine.Id})");