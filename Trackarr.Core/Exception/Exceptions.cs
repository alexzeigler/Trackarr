namespace Trackarr.Core.Exception;

public class MediaServerException(string message, System.Exception? inner = null) : System.Exception(message, inner);

public class ConnectionException(string serverName, System.Exception? inner = null)
    : MediaServerException($"Could not reach {serverName}{Describe(inner)}", inner)
{
    /// <summary>The underlying reason in plain words, so "could not reach" says why (TLS, DNS, refused...).</summary>
    private static string Describe(System.Exception? inner)
    {
        if (inner is null) return "";

        for (var e = inner; e is not null; e = e.InnerException)
        {
            switch (e)
            {
                case System.Security.Authentication.AuthenticationException tls:
                    return $": HTTPS certificate problem ({tls.Message.TrimEnd('.')}). If the server uses a " +
                           "self-signed certificate, turn on \"Skip certificate validation\" for it.";
                case System.Net.Sockets.SocketException socket:
                    return socket.SocketErrorCode switch
                    {
                        System.Net.Sockets.SocketError.ConnectionRefused =>
                            ": connection refused. Check the port, and that HTTPS is only on if the server serves HTTPS on it.",
                        System.Net.Sockets.SocketError.HostNotFound or System.Net.Sockets.SocketError.NoData =>
                            ": host name not found.",
                        _ => $": {socket.Message}"
                    };
                case TimeoutException:
                    return ": timed out.";
            }
        }

        if (inner is TaskCanceledException) return ": timed out.";

        // Plain HTTP spoken to an HTTPS port (or the reverse) usually surfaces as a malformed response.
        if (inner.Message.Contains("invalid", StringComparison.OrdinalIgnoreCase) ||
            inner.Message.Contains("SSL", StringComparison.OrdinalIgnoreCase))
            return $": {inner.Message.TrimEnd('.')}. Check that the HTTPS setting matches the server.";

        return $": {inner.Message.TrimEnd('.')}.";
    }
}

public class ApiException(string serverName, string endpoint, int statusCode, System.Exception? inner = null)
    : MediaServerException($"{serverName} returned {statusCode} from {endpoint}", inner)
{
    public int StatusCode { get; } = statusCode;

    /// <summary>True for 401/403: the server answered, but rejected the API key.</summary>
    public bool IsAuthFailure => StatusCode is 401 or 403;
}

public class MetadataException(string serverName, System.Exception? inner = null)
    : MediaServerException($"Failed to fetch metadata for {serverName}", inner);

