using Trackarr.Core.Exception;
using Trackarr.Core.Server;

namespace Trackarr.Integrations.Media.Arr;

/// <summary>
///     Makes Radarr and Sonarr clients from a server's settings. Each client gets its own <see cref="HttpClient" />
///     from <see cref="IHttpClientFactory" /> (handlers are pooled, so that's cheap) with the server's timeout. Phase 2
///     still passes today's <see cref="MediaServer" /> entity; Phase 3 gives each app its own server type.
/// </summary>
public sealed class ArrClientFactory(IHttpClientFactory httpClients)
{
    /// <summary>The named HttpClient for Radarr/Sonarr: outgoing proxy and normal TLS checks.</summary>
    public const string HttpClientName = "MediaServer";

    /// <summary>The same, accepting any TLS certificate, for servers with "Skip certificate validation" on.</summary>
    public const string InsecureHttpClientName = "MediaServerInsecure";

    public ArrClientBase Create(MediaServer server, TimeSpan timeout)
    {
        return server.ServiceType switch
        {
            ServiceType.Radarr => Radarr(server, timeout),
            ServiceType.Sonarr => Sonarr(server, timeout),
            _ => throw new MediaServerException($"{server.Name}: Unknown service type.")
        };
    }

    public RadarrClient Radarr(MediaServer server, TimeSpan timeout)
    {
        return new RadarrClient(Http(server, timeout), server);
    }

    public SonarrClient Sonarr(MediaServer server, TimeSpan timeout)
    {
        return new SonarrClient(Http(server, timeout), server);
    }

    private HttpClient Http(MediaServer server, TimeSpan timeout)
    {
        var client = httpClients.CreateClient(server.SkipCertificateValidation ? InsecureHttpClientName : HttpClientName);
        client.Timeout = timeout <= TimeSpan.Zero ? TimeSpan.FromSeconds(1) : timeout;
        return client;
    }
}
