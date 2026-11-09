using Trackarr.Core.Server;
using Trackarr.Integrations.Media.Arr;
using Trackarr.Services.Settings;

namespace Trackarr.Services.Servers;

/// <summary>
///     Radarr and Sonarr clients for a server, each with that server's <see cref="RequestTimeout" />. Every service
///     that talks to Radarr or Sonarr gets its clients here, so the timeout rule lives in one place.
/// </summary>
public sealed class MediaServerClients(IHttpClientFactory httpClientFactory, SettingsService? settings = null)
{
    private readonly ArrClientFactory _factory = new(httpClientFactory);

    /// <summary>A Radarr or Sonarr client, whichever the server is.</summary>
    /// <param name="server">The server to call.</param>
    /// <param name="fallbackSeconds">The timeout used only when there are no settings (tests).</param>
    public ArrClientBase For(MediaServer server, int fallbackSeconds)
    {
        return _factory.Create(server, RequestTimeout(server, fallbackSeconds));
    }

    /// <inheritdoc cref="For" />
    public RadarrClient Radarr(MediaServer server, int fallbackSeconds)
    {
        return _factory.Radarr(server, RequestTimeout(server, fallbackSeconds));
    }

    /// <inheritdoc cref="For" />
    public SonarrClient Sonarr(MediaServer server, int fallbackSeconds)
    {
        return _factory.Sonarr(server, RequestTimeout(server, fallbackSeconds));
    }

    /// <summary>The server's own Request Timeout wins, then the global one (Settings > Servers).</summary>
    public TimeSpan RequestTimeout(MediaServer server, int fallbackSeconds)
    {
        return TimeSpan.FromSeconds(Math.Max(1, settings?.GetRequestTimeout(server) ?? fallbackSeconds));
    }
}
