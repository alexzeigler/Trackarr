using Trackarr.Core.Exception;
using Trackarr.Core.Server;

namespace Trackarr.Services.Servers;

/// <summary>
///     Runs work that calls Radarr or Sonarr and turns low-level failures into the domain exceptions: a
///     <see cref="MediaServerException" /> (including <see cref="ApiException" />) passes through, a timeout or socket
///     error becomes a <see cref="ConnectionException" />, and anything else is wrapped with the message given.
/// </summary>
internal static class ApiCall
{
    public static async Task<T> RunAsync<T>(MediaServer server, string unexpectedError, Func<Task<T>> operation)
    {
        try
        {
            return await operation();
        }
        catch (MediaServerException)
        {
            throw;
        }
        catch (TaskCanceledException ex)
        {
            throw new ConnectionException(server.Name, ex);
        }
        catch (HttpRequestException ex)
        {
            throw new ConnectionException(server.Name, ex);
        }
        catch (Exception ex)
        {
            throw new MediaServerException($"{server.Name}: {unexpectedError}", ex);
        }
    }

    public static async Task RunAsync(MediaServer server, string unexpectedError, Func<Task> operation)
    {
        await RunAsync(server, unexpectedError, async () =>
        {
            await operation();
            return true;
        });
    }
}
