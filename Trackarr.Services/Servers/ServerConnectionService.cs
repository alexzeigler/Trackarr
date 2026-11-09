using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Trackarr.Core.Exception;
using Trackarr.Core.Server;
using Trackarr.Data;
using Trackarr.Integrations.Media.Arr.Dtos;
using Trackarr.Services.UI;

namespace Trackarr.Services.Servers;

/// <summary>
///     Asks a server about itself: the connection test the heartbeat and the Test buttons run, its metadata (app,
///     version) and quality profiles, and its own health checks.
/// </summary>
public sealed class ServerConnectionService(
    IDbContextFactory<AppDbContext> dbFactory,
    MediaServerClients clients,
    ServerRepository servers,
    QualityProfileService profiles,
    DataChangeBus changeBus,
    ILogger<ServerConnectionService> logger)
{
    /// <summary>
    ///     Checks the server answers and accepts the API key, and stores the result on a saved server. A server that
    ///     can't be reached or rejects the key is stored that way, then the error is thrown.
    /// </summary>
    public async Task<(bool Success, long ElapsedMilliseconds, string? ReasonPhrase)> TestConnectionAsync(
        MediaServer server)
    {
        server.NormalizeAddress();
        logger.LogInformation("Testing connection to {name} at {url}", server.Name, server.BaseUrl);
        var sw = Stopwatch.StartNew();

        try
        {
            // The factory refuses an unknown service type, and the client a missing API key.
            var response = (await clients.For(server, 10).CheckConnectionAsync()).ReasonPhrase;

            sw.Stop();

            server.IsOnline = true;
            server.LastPingedAt = DateTime.UtcNow;
            server.LastPingMs = sw.ElapsedMilliseconds;
            server.IsAuthenticated = true;
            server.LastAuthenticatedAt = DateTime.UtcNow;

            if (server.Id != 0)
            {
                await servers.UpdateStatusAsync(server.Id, s =>
                {
                    s.IsOnline = server.IsOnline;
                    s.LastPingedAt = server.LastPingedAt;
                    s.LastPingMs = server.LastPingMs;
                    s.IsAuthenticated = server.IsAuthenticated;
                    s.LastAuthenticatedAt = server.LastAuthenticatedAt;
                });
            }

            return (true, sw.ElapsedMilliseconds, response);
        }
        catch (ConnectionException ex)
        {
            server.IsOnline = false;
            server.LastPingedAt = DateTime.UtcNow;
            server.LastErrorAt = DateTime.UtcNow;
            server.LastError = ex.Message;

            if (server.Id != 0)
            {
                await servers.UpdateStatusAsync(server.Id, s =>
                {
                    s.IsOnline = server.IsOnline;
                    s.LastPingedAt = server.LastPingedAt;
                    s.LastErrorAt = server.LastErrorAt;
                    s.LastError = server.LastError;
                });
            }

            throw;
        }
        catch (ApiException ex) when (ex.IsAuthFailure)
        {
            // Reachable, but the API key was rejected: record that so the UI stops showing it as authenticated.
            sw.Stop();
            server.IsOnline = true;
            server.LastPingedAt = DateTime.UtcNow;
            server.LastPingMs = sw.ElapsedMilliseconds;
            server.IsAuthenticated = false;
            server.LastErrorAt = DateTime.UtcNow;
            server.LastError = ex.Message;

            if (server.Id != 0)
            {
                await servers.UpdateStatusAsync(server.Id, s =>
                {
                    s.IsOnline = server.IsOnline;
                    s.LastPingedAt = server.LastPingedAt;
                    s.LastPingMs = server.LastPingMs;
                    s.IsAuthenticated = server.IsAuthenticated;
                    s.LastErrorAt = server.LastErrorAt;
                    s.LastError = server.LastError;
                });
            }

            throw;
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
            throw new MediaServerException($"{server.Name}: Unexpected error testing connection.", ex);
        }
    }

    /// <summary>
    ///     Fetches the stored server's metadata and quality profiles and stores them. The metadata is also set on
    ///     <paramref name="server" />. Throws a <see cref="MediaServerException" /> when the server can't be read.
    /// </summary>
    public async Task<ServerMetadata> RefreshMetadataAsync(MediaServer server)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var mediaServer = await db.MediaServers.FindAsync(server.Id) ??
                          throw new MediaServerException($"{server.Name}: Server not found.");

        if (string.IsNullOrWhiteSpace(mediaServer.ApiKey))
            throw new MediaServerException($"{server.Name}: API key is required.");

        ServerMetadata stored;
        try
        {
            // The stored row: its API key is the one decrypted from the database.
            var metadata = await clients.For(mediaServer, 10).GetSystemStatusAsync();

            metadata.ServerId = mediaServer.Id;

            var existing = await db.ServerMetadata.FindAsync(mediaServer.Id);

            if (existing is null)
                db.ServerMetadata.Add(metadata);
            else
                db.Entry(existing).CurrentValues.SetValues(metadata);

            await profiles.GetProfilesAsync(server);

            await db.SaveChangesAsync();
            stored = existing ?? metadata;
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
            throw new MetadataException(server.Name, ex);
        }

        server.Metadata = stored;
        changeBus.NotifyChanged();
        return stored;
    }

    /// <summary>
    ///     <see cref="RefreshMetadataAsync" />, logging a failure instead of throwing it: a server's settings must save
    ///     while it's down. False when the metadata couldn't be read.
    /// </summary>
    public async Task<bool> TryRefreshMetadataAsync(MediaServer server)
    {
        try
        {
            await RefreshMetadataAsync(server);
            return true;
        }
        catch (MediaServerException ex)
        {
            logger.LogWarning("Could not refresh metadata for {name}: {message}", server.Name, ex.Message);
            return false;
        }
    }

    /// <summary>The server's own health checks. Empty when it has no API key or can't be read.</summary>
    public async Task<List<RemoteHealthMessage>> GetRemoteHealthAsync(MediaServer server)
    {
        if (string.IsNullOrWhiteSpace(server.ApiKey))
            return [];

        try
        {
            return [.. await clients.For(server, 10).GetHealthAsync()];
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Failed to fetch remote health for {name}", server.Name);
            return [];
        }
    }
}
