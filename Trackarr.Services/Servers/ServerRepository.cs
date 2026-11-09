using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Trackarr.Core.Exception;
using Trackarr.Core.Server;
using Trackarr.Data;
using Trackarr.Services.UI;

namespace Trackarr.Services.Servers;

/// <summary>
///     Stores the servers and what the routines learn about them: status, heartbeat samples and health events. It
///     only reads and writes the database. Nothing here calls Radarr or Sonarr.
/// </summary>
public sealed class ServerRepository(
    IDbContextFactory<AppDbContext> dbFactory,
    DataChangeBus changeBus,
    ILogger<ServerRepository> logger)
{
    /// <summary>The fields the routines write. Saving a server from a form keeps their stored values.</summary>
    private static readonly string[] RoutineOwnedFields =
    [
        nameof(MediaServer.IsOnline), nameof(MediaServer.LastPingedAt), nameof(MediaServer.LastPingMs),
        nameof(MediaServer.IsAuthenticated), nameof(MediaServer.LastAuthenticatedAt),
        nameof(MediaServer.LastQueueFetchAt), nameof(MediaServer.LastQueueCount),
        nameof(MediaServer.LastMissingFetchAt), nameof(MediaServer.LastMissingCount),
        nameof(MediaServer.LastErrorAt), nameof(MediaServer.LastError),
        nameof(MediaServer.UptimePercent), nameof(MediaServer.CreatedAt)
    ];

    #region Servers

    public async Task<List<MediaServer>> GetAllAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.MediaServers.Include(s => s.Metadata).Include(q => q.FallbackProfile)
            .OrderBy(s => s.ServiceType).ThenBy(s => s.Name).ToListAsync();
    }

    public async Task<MediaServer?> GetByIdAsync(int id)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.MediaServers.FindAsync(id);
    }

    /// <summary>
    ///     Stores a new server with the quality profiles the form fetched, and points its fallback profile at the stored
    ///     copy of the one picked.
    /// </summary>
    public async Task AddAsync(MediaServer server)
    {
        server.NormalizeAddress();
        await using var db = await dbFactory.CreateDbContextAsync();

        var profiles = server.Profiles;
        var fallbackRemoteId = server.FallbackProfile?.RemoteId;

        server.Profiles = null;
        server.FallbackProfile = null;
        server.FallBackProfileId = null;

        db.MediaServers.Add(server);
        await db.SaveChangesAsync();

        if (profiles is { Count: > 0 })
        {
            foreach (var p in profiles)
            {
                p.Id = 0;
                p.ServerId = server.Id;
            }

            db.QualityProfiles.AddRange(profiles);
            await db.SaveChangesAsync();

            if (fallbackRemoteId is { } remote)
            {
                var fallback = profiles.FirstOrDefault(p => p.RemoteId == remote);
                if (fallback is not null)
                {
                    server.FallBackProfileId = fallback.Id;
                    await db.SaveChangesAsync();
                }
            }
        }

        changeBus.NotifyChanged();
    }

    /// <summary>Saves a server's settings. What the routines wrote since the caller loaded it is kept.</summary>
    public async Task UpdateAsync(MediaServer server)
    {
        server.NormalizeAddress();
        await using var db = await dbFactory.CreateDbContextAsync();

        var tracked = await db.MediaServers.FindAsync(server.Id) ??
                      throw new MediaServerException($"{server.Name}: Server not found.");

        var entry = db.Entry(tracked);
        entry.CurrentValues.SetValues(server);

        // The caller's copy is whatever the page loaded. Status the routines wrote since then (ping, auth,
        // queue/missing counts, errors) must not be rolled back by saving a form, so keep the stored values.
        foreach (var name in RoutineOwnedFields)
        {
            var property = entry.Property(name);
            property.CurrentValue = property.OriginalValue;
            property.IsModified = false;
        }

        await db.SaveChangesAsync();

        logger.LogInformation("Updated server: {name}", server.Name);
        changeBus.NotifyChanged();
    }

    public async Task DeleteAsync(int id)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var server = await db.MediaServers.FindAsync(id);

        if (server is null)
            return;

        db.MediaServers.Remove(server);
        await db.SaveChangesAsync();

        logger.LogInformation("Deleted server: {name}", server.Name);
        changeBus.NotifyChanged();
    }

    /// <summary>Changes status fields on the stored server, such as the last ping or queue count.</summary>
    public async Task UpdateStatusAsync(int serverId, Action<MediaServer> update)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var server = await db.MediaServers.FindAsync(serverId);

        if (server is null)
            return;

        // The server is tracked, so only the fields the update changed are written. Marking the whole row modified
        // would also rewrite the encrypted API key on every heartbeat, and blank it for good if it can't be decrypted.
        update(server);
        await db.SaveChangesAsync();
        changeBus.NotifyChanged();
    }

    #endregion

    #region Heartbeat history

    public async Task LogHealthEventAsync(int serverId, ServerHealthEventType type, string message,
        string? source = null)
    {
        await using var db = await dbFactory.CreateDbContextAsync();

        db.ServerHealthEvents.Add(new ServerHealthEvent
        {
            ServerId = serverId, EventType = type, Message = message, Source = source
        });

        await db.SaveChangesAsync();
        changeBus.NotifyChanged();
    }

    public async Task<List<ServerHealthEvent>> GetRecentHealthEventsAsync(int serverId, int take = 50)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.ServerHealthEvents.Where(e => e.ServerId == serverId).OrderByDescending(e => e.OccurredAt)
            .Take(take).ToListAsync();
    }

    /// <summary>
    ///     Records a heartbeat sample and recomputes the server's all-time uptime percentage from the
    ///     full sample history (rather than an incremented/decremented heuristic score).
    /// </summary>
    public async Task<double> RecordStatusSampleAsync(int serverId, long? pingMs, bool success)
    {
        await using var db = await dbFactory.CreateDbContextAsync();

        db.ServerStatusSamples.Add(new ServerStatusSample { ServerId = serverId, PingMs = pingMs, Success = success });

        await db.SaveChangesAsync();

        var total = await db.ServerStatusSamples.CountAsync(s => s.ServerId == serverId);
        var successes = await db.ServerStatusSamples.CountAsync(s => s.ServerId == serverId && s.Success);
        var uptimePercent = total == 0 ? 100 : Math.Round(successes / (double)total * 100, 1);

        await UpdateStatusAsync(serverId, s => s.UptimePercent = uptimePercent);

        return uptimePercent;
    }

    /// <summary>How many of the latest status samples, up to <paramref name="max" />, failed in a row.</summary>
    public async Task<int> CountRecentFailuresAsync(int serverId, int max)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var latest = await db.ServerStatusSamples.AsNoTracking().Where(s => s.ServerId == serverId)
            .OrderByDescending(s => s.SampledAt).ThenByDescending(s => s.Id).Take(max).Select(s => s.Success)
            .ToListAsync();
        return latest.TakeWhile(success => !success).Count();
    }

    public async Task<List<ServerStatusSample>> GetStatusSamplesAsync(int serverId, DateTime since)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.ServerStatusSamples.Where(s => s.ServerId == serverId && s.SampledAt >= since)
            .OrderBy(s => s.SampledAt).ToListAsync();
    }

    #endregion
}
