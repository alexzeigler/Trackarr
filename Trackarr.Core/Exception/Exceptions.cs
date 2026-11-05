using Trackarr.Core.Routine;

namespace Trackarr.Core.Exception;

public class Exceptions(string message, IRoutine routine)
    : System.Exception($"{message}: {routine.Name} (Id: {routine.Id})");

public class MediaServerException(string message, System.Exception? inner = null) : System.Exception(message, inner);

public class ConnectionException(string serverName, System.Exception? inner = null)
    : MediaServerException($"Could not reach {serverName}", inner);

public class ApiException(string serverName, string endpoint, int statusCode, System.Exception? inner = null)
    : MediaServerException($"{serverName} returned {statusCode} from {endpoint}", inner)
{
    public int StatusCode { get; } = statusCode;

    /// <summary>True for 401/403: the server answered, but rejected the API key.</summary>
    public bool IsAuthFailure => StatusCode is 401 or 403;
}

public class MetadataException(string serverName, System.Exception? inner = null)
    : MediaServerException($"Failed to fetch metadata for {serverName}", inner);

public class TaskNotDefinedException(string message, IRoutine routine)
    : System.Exception($"{message}: {routine.Name} (Id: {routine.Id})");
