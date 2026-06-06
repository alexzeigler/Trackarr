using Trackarr.Core.Routine;

namespace Trackarr.Core.Exception;

public class TaskNotDefinedException(string message, IRoutine routine) : System.Exception($"{message}: {routine.Name} (Id: {routine.Id})");