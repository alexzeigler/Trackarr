using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Trackarr.Core.Exception;
using Trackarr.Core.Server;
using Trackarr.Data;

namespace Trackarr.Services.Servers;

/// <summary>
///     Each server's quality profiles, fetched from Radarr or Sonarr and stored. A fetch is reused for a minute, so
///     opening a page or a form doesn't ask the server every time.
/// </summary>
public sealed class QualityProfileService(
    IDbContextFactory<AppDbContext> dbFactory,
    MediaServerClients clients,
    ILogger<QualityProfileService> logger)
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(60);

    /// <summary>
    ///     When each server's profiles were last fetched. An instance field, not a static one (AUDIT A5): the app has
    ///     one service, and each test its own.
    /// </summary>
    private readonly ConcurrentDictionary<int, DateTime> _fetchedAt = new();

    /// <summary>
    ///     The server's profiles. For a server that isn't stored yet (the Add Server form), they're returned without
    ///     being stored.
    /// </summary>
    public async Task<List<QualityProfile>> GetProfilesAsync(MediaServer server, bool forceRefresh = false)
    {
        if (string.IsNullOrWhiteSpace(server.ApiKey))
            throw new MediaServerException($"{server.Name}: API key is required.");

        await using var db = await dbFactory.CreateDbContextAsync();

        if (!forceRefresh && server.Id != 0 && _fetchedAt.TryGetValue(server.Id, out var last) &&
            DateTime.UtcNow - last < CacheTtl)
            return await db.QualityProfiles.Where(p => p.ServerId == server.Id).ToListAsync();

        logger.LogInformation("Fetching quality profiles for {name}", server.Name);

        return await ApiCall.RunAsync(server, "Unexpected error fetching profiles.", async () =>
        {
            var profiles = (await clients.For(server, 10).GetQualityProfilesAsync())
                .Select(p => new QualityProfile { RemoteId = p.Id, Name = p.Name }).ToList();

            if (!await db.MediaServers.AnyAsync(s => s.Id == server.Id))
                return profiles;

            var existing = await db.QualityProfiles.Where(p => p.ServerId == server.Id).ToListAsync();
            var byRemote = existing.ToDictionary(p => p.RemoteId);

            foreach (var profile in profiles)
            {
                profile.ServerId = server.Id;

                if (byRemote.TryGetValue(profile.RemoteId, out var current))
                {
                    profile.Id = current.Id;
                    db.Entry(current).CurrentValues.SetValues(profile);
                }
                else
                {
                    db.QualityProfiles.Add(profile);
                }
            }

            await db.SaveChangesAsync();
            _fetchedAt[server.Id] = DateTime.UtcNow;

            return await db.QualityProfiles.Where(p => p.ServerId == server.Id).ToListAsync();
        });
    }
}
