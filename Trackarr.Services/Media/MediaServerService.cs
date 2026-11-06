using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Trackarr.Core.Exception;
using Trackarr.Core.Health;
using Trackarr.Core.Media;
using Trackarr.Core.Media.Missing;
using Trackarr.Core.Media.Movies;
using Trackarr.Core.Media.Shows;
using Trackarr.Core.Queue;
using Trackarr.Core.Server;
using Trackarr.Data;
using Trackarr.Services.Diagnostics;
using Trackarr.Services.Settings;
using Trackarr.Services.UI;

namespace Trackarr.Services.Media;

public class MediaServerService(
    IDbContextFactory<AppDbContext> dbFactory,
    IHttpClientFactory httpClientFactory,
    ILogger<MediaServerService> logger,
    DataChangeBus changeBus,
    HealthService healthService,
    SettingsService? settings = null,
    EventService? eventService = null)
{
    /// <summary>Named HttpClients for Radarr/Sonarr, registered in Program.cs (outgoing proxy, TLS options).</summary>
    public const string HttpClientName = "MediaServer";

    public const string InsecureHttpClientName = "MediaServerInsecure";
    private static readonly TimeSpan QueueCacheTtl = TimeSpan.FromSeconds(30);

    #region Media

    private static readonly TimeSpan ProfileCacheTtl = TimeSpan.FromSeconds(60);
    private static readonly ConcurrentDictionary<int, DateTime> ProfileFetchTimes = new();

    public async Task<List<QualityProfile>> GetProfilesAsync(MediaServer server, bool forceRefresh = false)
    {
        if (string.IsNullOrWhiteSpace(server.ApiKey))
            throw new MediaServerException($"{server.Name}: API key is required.");

        await using var db = await dbFactory.CreateDbContextAsync();

        if (!forceRefresh && server.Id != 0 && ProfileFetchTimes.TryGetValue(server.Id, out var last) &&
            DateTime.UtcNow - last < ProfileCacheTtl)
            return await db.QualityProfiles.Where(p => p.ServerId == server.Id).ToListAsync();

        logger.LogInformation("Fetching quality profiles for {name}", server.Name);

        return await ExecuteApiAsync(server, "Unexpected error fetching profiles.", async () =>
        {
            var client = CreateClient(server, 10);
            var url = $"{server.BaseUrl}/api/v3/qualityprofile";

            using var response = await client.GetAsync(url);

            if (!response.IsSuccessStatusCode)
                throw new ApiException(server.Name, "/api/v3/qualityprofile", (int)response.StatusCode);

            var json = await response.Content.ReadAsStringAsync();
            var profiles = JsonSerializer.Deserialize<List<QualityProfile>>(json, _jsonOptions) ?? [];

            if (!await db.MediaServers.AnyAsync(s => s.Id == server.Id))
                return profiles;

            foreach (var profile in profiles)
                profile.ServerId = server.Id;

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
            ProfileFetchTimes[server.Id] = DateTime.UtcNow;

            return await db.QualityProfiles.Where(p => p.ServerId == server.Id).ToListAsync();
        });
    }

    /// <summary>
    ///     Sets the movie's or series' quality profile and returns the profile it had before (null if the
    ///     response had none). With <paramref name="onlyIfCurrent" />, nothing changes unless the current
    ///     profile is that one, so a revert never overwrites a profile someone changed by hand.
    /// </summary>
    public async Task<int?> SetQualityProfileAsync(MediaServer server, int remoteId, int profileId,
        int? onlyIfCurrent = null)
    {
        if (string.IsNullOrWhiteSpace(server.ApiKey))
            throw new MediaServerException($"{server.Name}: API key is required.");

        var resource = server.ServiceType == ServiceType.Radarr ? "movie" : "series";

        var error = $"Error setting profile on {resource} {remoteId}.";
        var previous = await ExecuteApiAsync<int?>(server, error, async () =>
        {
            var client = CreateClient(server, 15);
            var getUrl = $"{server.BaseUrl}/api/v3/{resource}/{remoteId}";

            using var getResponse = await client.GetAsync(getUrl);
            if (!getResponse.IsSuccessStatusCode)
                throw new ApiException(server.Name, $"/api/v3/{resource}/{remoteId}", (int)getResponse.StatusCode);

            var json = await getResponse.Content.ReadAsStringAsync();
            var node = JsonNode.Parse(json) ?? throw new MediaServerException($"{server.Name}: Empty {resource} body.");

            var current = node["qualityProfileId"]?.GetValue<int>();
            if (current == profileId) return current;
            if (onlyIfCurrent is { } expected && current != expected)
            {
                logger.LogInformation(
                    "Left profile {current} on {resource} {id} for {name}; expected {expected}, so it was changed outside Trackarr",
                    current, resource, remoteId, server.Name, expected);
                return current;
            }

            node["qualityProfileId"] = profileId;

            var putUrl = $"{server.BaseUrl}/api/v3/{resource}/{remoteId}";
            using var content = new StringContent(node.ToJsonString(), Encoding.UTF8, "application/json");
            using var putResponse = await client.PutAsync(putUrl, content);

            if (!putResponse.IsSuccessStatusCode)
                throw new ApiException(server.Name, $"/api/v3/{resource}/{remoteId}", (int)putResponse.StatusCode);

            logger.LogInformation("Set profile {profile} on {resource} {id} for {name}", profileId, resource, remoteId,
                server.Name);
            return current;
        });
        changeBus.NotifyChanged();
        return previous;
    }

    public async Task<HashSet<string>> GetImportedDownloadIdsAsync(MediaServer server, IEnumerable<string> downloadIds)
    {
        if (string.IsNullOrWhiteSpace(server.ApiKey))
            throw new MediaServerException($"{server.Name}: API key is required.");

        var ids = downloadIds.Where(e => !string.IsNullOrWhiteSpace(e)).Select(e => e.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (ids.Count == 0) return [];

        var imported = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        return await ExecuteApiAsync(server, "Error fetching history.", async () =>
        {
            var client = CreateClient(server, 15);

            foreach (var downloadId in ids)
            {
                var url = $"{server.BaseUrl}/api/v3/history?downloadId={downloadId}&eventType=3";

                using var response = await client.GetAsync(url);

                if (!response.IsSuccessStatusCode)
                    throw new ApiException(server.Name, "/api/v3/history", (int)response.StatusCode);

                var json = await response.Content.ReadAsStringAsync();
                var history = JsonSerializer.Deserialize<HistoryResponse>(json, _jsonOptions);

                if (history?.Records.Count > 0)
                    imported.Add(downloadId);
            }

            return imported;
        });
    }

    public async Task<Dictionary<int, Episode>> GetQueueEpisodesBySeriesAsync(int serverId,
        IEnumerable<DownloadItem> downloads)
    {
        var seriesIds = downloads.Where(item => item.SeriesId > 0).Select(item => item.SeriesId).Distinct().ToList();

        if (seriesIds.Count == 0) return [];

        await using var db = await dbFactory.CreateDbContextAsync();

        var episodes = await db.Episodes.Where(e => e.ServerId == serverId && seriesIds.Contains(e.RemoteSeriesId))
            .ToListAsync();

        return episodes.GroupBy(e => e.RemoteId).ToDictionary(group => group.Key, group => group.First());
    }

    public async Task<Dictionary<int, Episode>> GetQueueEpisodesAsync(int serverId, IEnumerable<DownloadItem> downloads)
    {
        var episodeIds = downloads.Where(item => item.EpisodeId > 0).Select(item => item.EpisodeId).Distinct().ToList();

        if (episodeIds.Count == 0) return [];

        await using var db = await dbFactory.CreateDbContextAsync();

        var episodes = await db.Episodes.Where(e => e.ServerId == serverId && episodeIds.Contains(e.RemoteId))
            .ToListAsync();

        return episodes.GroupBy(e => e.RemoteId).ToDictionary(group => group.Key, group => group.First());
    }

    #endregion

    #region Movies

    public async Task<List<Movie>> GetMissingMoviesAsync(MediaServer server, bool forceRefresh = false)
    {
        logger.LogInformation("Fetching missing movies for {name}", server.Name);

        if (server.ServiceType is not ServiceType.Radarr)
            throw new MediaServerException($"{server.Name}: Missing movies not supported for {server.ServiceType}.");
        if (!server.IsEnabled)
        {
            await using var db = await dbFactory.CreateDbContextAsync();
            return await db.Movies.Where(m => m.ServerId == server.Id).ToListAsync();
        }

        return await ExecuteApiAsync(server, "Unexpected error fetching missing movies.", async () =>
        {
            await using var db = await dbFactory.CreateDbContextAsync();

            if (!forceRefresh)
            {
                // Only the missing ones: the Movies table also holds every movie seen in the queue.
                var cached = await db.Movies.Where(m => m.ServerId == server.Id && m.Monitored && !m.HasFile)
                    .ToListAsync();

                if (cached.Count != 0)
                {
                    await EnsureMediaHealthSummariesAsync(db, cached, DateTime.UtcNow);
                    await db.SaveChangesAsync();
                    return cached;
                }
            }

            List<Movie> items = [];
            var page = 1;
            const int pageSize = 100;

            while (true)
            {
                var (batch, total) = await FetchMissingMoviesPageAsync(server, page, pageSize);

                foreach (var item in batch)
                    item.ServerId = server.Id;

                items.AddRange(batch);

                if (items.Count >= total || batch.Count < pageSize)
                    break;

                page++;
            }

            // One query for the existing rows instead of one per movie (P2).
            var movieRemoteIds = items.Select(i => i.RemoteId).Distinct().ToList();
            var existingMovies = await db.Movies
                .Where(m => m.ServerId == server.Id && movieRemoteIds.Contains(m.RemoteId))
                .ToDictionaryAsync(m => m.RemoteId);

            foreach (var item in items)
            {
                var existing = existingMovies.GetValueOrDefault(item.RemoteId);

                if (existing is null)
                {
                    db.Movies.Add(item);
                }
                else
                {
                    item.Id = existing.Id;
                    db.Entry(existing).CurrentValues.SetValues(item);
                }
            }

            await db.SaveChangesAsync();

            await EnsureMediaHealthSummariesAsync(db, items, DateTime.UtcNow);
            await db.SaveChangesAsync();

            logger.LogInformation("Fetched {count} missing movies for {name}", items.Count, server.Name);
            return items;
        });
    }

    public async Task<Dictionary<int, Movie>> GetParentMovieAsync(int serverId, IEnumerable<DownloadItem> downloads)
    {
        var movieIds = downloads.Where(item => item.MovieId > 0).Select(item => item.MovieId).Distinct().ToList();

        if (movieIds.Count == 0) return [];

        await using var db = await dbFactory.CreateDbContextAsync();

        var movies = await db.Movies.AsNoTracking()
            .Where(movie => movie.ServerId == serverId && movieIds.Contains(movie.RemoteId)).ToListAsync();

        // Read only (P3): the queue refresh already creates these summaries when it stores the queue.

        return movies.GroupBy(movie => movie.RemoteId).ToDictionary(group => group.Key, group => group.First());
    }

    private async Task<(List<Movie> Items, int TotalRecords)> FetchMissingMoviesPageAsync(MediaServer server, int page,
        int pageSize)
    {
        if (string.IsNullOrWhiteSpace(server.ApiKey))
            throw new MediaServerException($"{server.Name}: API key is required.");

        try
        {
            var client = CreateClient(server, 15);
            var url = $"{server.BaseUrl}/api/v3/wanted/missing?page={page}&pageSize={pageSize}";

            using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);

            if (!response.IsSuccessStatusCode)
                throw new ApiException(server.Name, "/api/v3/wanted/missing", (int)response.StatusCode);

            var json = await response.Content.ReadAsStringAsync();
            var missing = JsonSerializer.Deserialize<MissingMovieResponse>(json, _jsonOptions);

            var items = missing?.Records.Select(x => x.ToEntity(server.Id)).ToList() ?? [];

            return (items, missing?.TotalRecords ?? 0);
        }
        catch (MediaServerException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new MediaServerException($"{server.Name}: Error fetching missing movies page {page}.", ex);
        }
    }

    public async Task<Movie?> GetMovieByIdAsync(MediaServer server, int radarrMovieId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();

        var existing = await db.Movies.FirstOrDefaultAsync(m => m.ServerId == server.Id && m.RemoteId == radarrMovieId);

        return await ExecuteApiAsync(server, $"Unexpected error fetching movie {radarrMovieId}.", async () =>
        {
            var client = CreateClient(server, 10);
            var url = $"{server.BaseUrl}/api/v3/movie/{radarrMovieId}";

            using var response = await client.GetAsync(url);

            if (!response.IsSuccessStatusCode)
                throw new ApiException(server.Name, $"/api/v3/movie/{radarrMovieId}", (int)response.StatusCode);

            var json = await response.Content.ReadAsStringAsync();
            var x = JsonSerializer.Deserialize<MovieRecord>(json, _jsonOptions);

            if (x is null)
            {
                if (existing is not null)
                {
                    await EnsureMediaHealthSummaryAsync(db, existing, DateTime.UtcNow);
                    await db.SaveChangesAsync();
                }

                return existing;
            }

            var movie = x.ToEntity(server.Id);

            if (existing is null)
            {
                db.Movies.Add(movie);
            }
            else
            {
                movie.Id = existing.Id;
                db.Entry(existing).CurrentValues.SetValues(movie);
            }

            await db.SaveChangesAsync();

            var saved = existing ?? movie;

            await EnsureMediaHealthSummaryAsync(db, saved, DateTime.UtcNow);
            await db.SaveChangesAsync();

            return saved;
        });
    }

    #endregion

    #region Series

    public async Task<List<Episode>> GetMissingEpisodesAsync(MediaServer server, bool forceRefresh = false)
    {
        logger.LogInformation("Fetching missing episodes for {name}", server.Name);

        if (server.ServiceType is not ServiceType.Sonarr)
            throw new MediaServerException($"{server.Name}: Missing episodes not supported for {server.ServiceType}.");

        if (!server.IsEnabled)
        {
            await using var db = await dbFactory.CreateDbContextAsync();
            return await db.Episodes.Where(e => e.ServerId == server.Id && !e.HasFile).ToListAsync();
        }

        return await ExecuteApiAsync(server, "Unexpected error fetching missing episodes.", async () =>
        {
            await using var db = await dbFactory.CreateDbContextAsync();

            if (!forceRefresh)
            {
                var cached = await db.Episodes.Where(e => e.ServerId == server.Id && !e.HasFile).ToListAsync();

                if (cached.Count != 0)
                    return cached;
            }

            List<Episode> items = [];
            var page = 1;
            const int pageSize = 100;

            while (true)
            {
                var (batch, total) = await FetchMissingEpisodesPageAsync(server, page, pageSize);

                foreach (var item in batch)
                    item.ServerId = server.Id;

                items.AddRange(batch);

                if (items.Count >= total || batch.Count < pageSize)
                    break;

                page++;
            }

            // One query for the existing rows instead of one per episode (P2).
            var episodeRemoteIds = items.Select(i => i.RemoteId).Distinct().ToList();
            var existingEpisodes = await db.Episodes
                .Where(e => e.ServerId == server.Id && episodeRemoteIds.Contains(e.RemoteId))
                .ToDictionaryAsync(e => e.RemoteId);

            foreach (var item in items)
            {
                var existing = existingEpisodes.GetValueOrDefault(item.RemoteId);

                if (existing is null)
                {
                    db.Episodes.Add(item);
                }
                else
                {
                    item.Id = existing.Id;
                    db.Entry(existing).CurrentValues.SetValues(item);
                }
            }

            await db.SaveChangesAsync();

            var series = await UpsertSeriesFromMissingEpisodesAsync(db, server.Id, items);
            await db.SaveChangesAsync();

            await EnsureMediaHealthSummariesAsync(db, series, DateTime.UtcNow);
            await db.SaveChangesAsync();

            logger.LogInformation("Fetched {count} missing episodes for {name}", items.Count, server.Name);
            return items;
        });
    }

    public async Task<Dictionary<int, Series>> GetParentSeriesAsync(int serverId, IEnumerable<DownloadItem> downloads)
    {
        var seriesIds = downloads.Where(item => item.SeriesId > 0).Select(item => item.SeriesId).Distinct().ToList();

        if (seriesIds.Count == 0) return [];

        await using var db = await dbFactory.CreateDbContextAsync();

        var series = await db.Series.AsNoTracking()
            .Where(item => item.ServerId == serverId && seriesIds.Contains(item.RemoteId)).ToListAsync();

        // Read only (P3): the queue refresh already creates these summaries when it stores the queue.

        return series.GroupBy(item => item.RemoteId).ToDictionary(group => group.Key, group => group.First());
    }

    private async Task<(List<Episode> Items, int TotalRecords)> FetchMissingEpisodesPageAsync(MediaServer server,
        int page, int pageSize)
    {
        if (string.IsNullOrWhiteSpace(server.ApiKey))
            throw new MediaServerException($"{server.Name}: API key is required.");

        try
        {
            var client = CreateClient(server, 15);
            var url = $"{server.BaseUrl}/api/v3/wanted/missing?page={page}&pageSize={pageSize}&includeSeries=true";

            using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);

            if (!response.IsSuccessStatusCode)
                throw new ApiException(server.Name, "/api/v3/wanted/missing", (int)response.StatusCode);

            var json = await response.Content.ReadAsStringAsync();
            var missing = JsonSerializer.Deserialize<MissingShowResponse>(json, _jsonOptions);

            var items = missing?.Records.Select(x => x.ToEntity(server.Id)).ToList() ?? [];

            return (items, missing?.TotalRecords ?? 0);
        }
        catch (MediaServerException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new MediaServerException($"{server.Name}: Error fetching missing episodes page {page}.", ex);
        }
    }

    public async Task<Series?> GetSeriesByIdAsync(MediaServer server, int sonarrSeriesId)
    {
        if (server.ServiceType is not ServiceType.Sonarr)
            throw new MediaServerException($"{server.Name}: Series fetching not supported for {server.ServiceType}.");

        await using var db = await dbFactory.CreateDbContextAsync();

        var existing =
            await db.Series.FirstOrDefaultAsync(s => s.ServerId == server.Id && s.RemoteId == sonarrSeriesId);

        return await ExecuteApiAsync(server, $"Unexpected error fetching series {sonarrSeriesId}.", async () =>
        {
            var client = CreateClient(server, 10);
            var url = $"{server.BaseUrl}/api/v3/series/{sonarrSeriesId}";

            using var response = await client.GetAsync(url);

            if (!response.IsSuccessStatusCode)
                throw new ApiException(server.Name, $"/api/v3/series/{sonarrSeriesId}", (int)response.StatusCode);

            var json = await response.Content.ReadAsStringAsync();
            var x = JsonSerializer.Deserialize<SeriesRecord>(json, _jsonOptions);

            if (x is null)
            {
                if (existing is not null)
                {
                    await EnsureMediaHealthSummaryAsync(db, existing, DateTime.UtcNow);
                    await db.SaveChangesAsync();
                }

                return existing;
            }

            var series = x.ToEntity(server.Id);

            if (existing is null)
            {
                db.Series.Add(series);
            }
            else
            {
                series.Id = existing.Id;
                db.Entry(existing).CurrentValues.SetValues(series);
            }

            await db.SaveChangesAsync();

            var saved = existing ?? series;

            await EnsureMediaHealthSummaryAsync(db, saved, DateTime.UtcNow);
            await db.SaveChangesAsync();

            return saved;
        });
    }

    /// <summary>
    ///     Upserts series data already embedded in a wanted/missing response (via includeSeries=true)
    ///     instead of issuing a follow-up GET /api/v3/series/{id} per series.
    /// </summary>
    private async Task<List<Series>> UpsertSeriesFromMissingEpisodesAsync(AppDbContext db, int serverId,
        IEnumerable<Episode> episodes)
    {
        List<Series> saved = [];

        var groups = episodes.Where(e => e.RemoteSeriesId != 0 && !string.IsNullOrWhiteSpace(e.SeriesJson))
            .GroupBy(e => e.RemoteSeriesId).ToList();

        // One query for the existing rows instead of one per series (P2).
        var seriesRemoteIds = groups.Select(g => g.Key).ToList();
        var existingSeries = await db.Series.Where(s => s.ServerId == serverId && seriesRemoteIds.Contains(s.RemoteId))
            .ToDictionaryAsync(s => s.RemoteId);

        foreach (var group in groups)
        {
            var record = JsonSerializer.Deserialize<SeriesRecord>(group.First().SeriesJson!, _jsonOptions);
            if (record is null) continue;

            var series = record.ToEntity(serverId);

            var existing = existingSeries.GetValueOrDefault(series.RemoteId);

            if (existing is null)
            {
                db.Series.Add(series);
                saved.Add(series);
            }
            else
            {
                series.Id = existing.Id;
                db.Entry(existing).CurrentValues.SetValues(series);
                saved.Add(existing);
            }
        }

        return saved;
    }

    public async Task<List<Episode>> GetEpisodesBySeriesIdAsync(int seriesServerId, int seriesRemoteId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();

        return await db.Episodes.AsNoTracking()
            .Where(e => e.ServerId == seriesServerId && e.RemoteSeriesId == seriesRemoteId).OrderBy(e => e.SeasonNumber)
            .ThenBy(e => e.EpisodeNumber).ToListAsync();
    }

    public async Task SyncSeriesEpisodesAsync(MediaServer server, int seriesId)
    {
        if (string.IsNullOrWhiteSpace(server.ApiKey))
            return;

        await ExecuteApiAsync(server, $"Error syncing episodes for series {seriesId}.", async () =>
        {
            var client = CreateClient(server, 15);
            var url = $"{server.BaseUrl}/api/v3/episode?seriesId={seriesId}";

            using var response = await client.GetAsync(url);

            if (!response.IsSuccessStatusCode)
                throw new ApiException(server.Name, "/api/v3/episode", (int)response.StatusCode);

            var json = await response.Content.ReadAsStringAsync();
            var records = JsonSerializer.Deserialize<List<EpisodeRecord>>(json, _jsonOptions) ?? [];

            await using var db = await dbFactory.CreateDbContextAsync();

            var seriesEntity =
                await db.Series.FirstOrDefaultAsync(s => s.ServerId == server.Id && s.RemoteId == seriesId);

            // One query for the existing rows instead of one per episode (P2).
            var remoteIds = records.Select(r => r.Id).Distinct().ToList();
            var existingById = await db.Episodes.Where(e => e.ServerId == server.Id && remoteIds.Contains(e.RemoteId))
                .ToDictionaryAsync(e => e.RemoteId);

            foreach (var record in records)
            {
                var episode = record.ToEntity(server.Id);

                if (seriesEntity is not null)
                    episode.SeriesDbId = seriesEntity.Id;

                var existing = existingById.GetValueOrDefault(episode.RemoteId);

                if (existing is null)
                {
                    db.Episodes.Add(episode);
                }
                else
                {
                    episode.Id = existing.Id;
                    db.Entry(existing).CurrentValues.SetValues(episode);
                }
            }

            await db.SaveChangesAsync();
        });
    }

    public async Task<List<(int EpisodeId, bool HasFile, bool Monitored)>> GetSeriesEpisodeStatesAsync(
        MediaServer server, int seriesId)
    {
        if (string.IsNullOrWhiteSpace(server.ApiKey))
            throw new MediaServerException($"{server.Name}: API key is required.");

        return await ExecuteApiAsync(server, $"Error fetching episodes for series {seriesId}.", async () =>
        {
            var client = CreateClient(server, 10);
            var url = $"{server.BaseUrl}/api/v3/episode?seriesId={seriesId}";

            using var response = await client.GetAsync(url);

            if (!response.IsSuccessStatusCode)
                throw new ApiException(server.Name, "/api/v3/episode", (int)response.StatusCode);

            var json = await response.Content.ReadAsStringAsync();
            var episodes = JsonSerializer.Deserialize<List<EpisodeRecord>>(json, _jsonOptions) ?? [];

            return episodes.Select(e => (e.Id, e.HasFile, e.Monitored)).ToList();
        });
    }

    #endregion

    #region CRUD

    public async Task<List<MediaServer>> GetAllServersAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.MediaServers.Include(s => s.Metadata).Include(q => q.FallbackProfile)
            .OrderBy(s => s.ServiceType).ThenBy(s => s.Name).ToListAsync();
    }

    public async Task<MediaServer?> GetServerByIdAsync(int id)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.MediaServers.FindAsync(id);
    }

    public async Task CreateServerAsync(MediaServer server)
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

        try
        {
            server.Metadata = await GetSystemStatusAsync(server);
        }
        catch (MediaServerException ex)
        {
            logger.LogWarning("Could not fetch metadata for new server {name}: {message}", server.Name, ex.Message);
        }

        changeBus.NotifyChanged();
    }

    public async Task UpdateServerAsync(MediaServer server)
    {
        server.NormalizeAddress();
        await using var db = await dbFactory.CreateDbContextAsync();

        if (server.IsDefault)
            await ClearDefaultAsync(db, server.ServiceType, server.Id);

        var tracked = await db.MediaServers.FindAsync(server.Id) ??
                      throw new MediaServerException($"{server.Name}: Server not found.");

        var entry = db.Entry(tracked);
        entry.CurrentValues.SetValues(server);

        // The caller's copy is whatever the page loaded. Status the routines wrote since then (ping, auth,
        // queue/missing counts, errors) must not be rolled back by saving a form, so keep the stored values.
        foreach (var name in RoutineOwnedServerFields)
        {
            var property = entry.Property(name);
            property.CurrentValue = property.OriginalValue;
            property.IsModified = false;
        }

        await db.SaveChangesAsync();

        // The config save above already succeeded — a server being unreachable shouldn't fail the
        // whole update (e.g. disabling a dead server must work even though it can't be reached).
        try
        {
            server.Metadata = await GetSystemStatusAsync(server);
        }
        catch (MediaServerException ex)
        {
            logger.LogWarning("Could not refresh metadata for {name} after update: {message}", server.Name, ex.Message);
        }

        logger.LogInformation("Updated server: {name}", server.Name);
        changeBus.NotifyChanged();
    }

    /// <summary>
    ///     Full per-server sync: metadata, queue (scoring any download not seen before), and missing media.
    ///     Shared by the "Sync Now" UI action and the heartbeat's recovery path.
    /// </summary>
    public async Task SyncServerAsync(MediaServer server)
    {
        await UpdateServerAsync(server);

        // A fresh fetch scores every download it hasn't seen before (see GetQueueAsync). Scoring the ones it
        // already knows again here would count an extra scan seconds after the last one.
        await GetQueueAsync(server, true);

        if (server.ServiceType == ServiceType.Sonarr)
            await GetMissingEpisodesAsync(server, true);
        else
            await GetMissingMoviesAsync(server, true);
    }

    public async Task DeleteServerAsync(int id)
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

    #endregion

    #region Connection

    public async Task<(bool Success, long ElapsedMilliseconds, string? ReasonPhrase)> TestConnectionAsync(
        MediaServer server)
    {
        server.NormalizeAddress();
        logger.LogInformation("Testing connection to {name} at {url}", server.Name, server.BaseUrl);
        var sw = Stopwatch.StartNew();

        try
        {
            var client = CreateClient(server, 10);

            var response = server.ServiceType switch
            {
                ServiceType.Radarr or ServiceType.Sonarr => await TestArrAsync(client, server),
                _ => throw new MediaServerException($"{server.Name}: Unknown service type.")
            };

            sw.Stop();

            server.IsOnline = true;
            server.LastPingedAt = DateTime.UtcNow;
            server.LastPingMs = sw.ElapsedMilliseconds;
            server.IsAuthenticated = true;
            server.LastAuthenticatedAt = DateTime.UtcNow;

            if (server.Id != 0)
            {
                await UpdateServerStatsAsync(server.Id, s =>
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
                await UpdateServerStatsAsync(server.Id, s =>
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
                await UpdateServerStatsAsync(server.Id, s =>
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

    /// <summary>Calls the status endpoint and returns its reason phrase. The response is disposed here.</summary>
    private static async Task<string?> TestArrAsync(HttpClient client, MediaServer server)
    {
        if (string.IsNullOrWhiteSpace(server.ApiKey))
            throw new MediaServerException($"{server.Name}: API key is required.");

        var url = $"{server.BaseUrl}/api/v3/system/status";
        using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);

        if (!response.IsSuccessStatusCode)
            throw new ApiException(server.Name, "/api/v3/system/status", (int)response.StatusCode);

        return response.ReasonPhrase;
    }

    private async Task<ServerMetadata> GetSystemStatusAsync(MediaServer server)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var mediaServer = await db.MediaServers.FindAsync(server.Id) ??
                          throw new MediaServerException($"{server.Name}: Server not found.");

        if (string.IsNullOrWhiteSpace(mediaServer.ApiKey))
            throw new MediaServerException($"{server.Name}: API key is required.");

        try
        {
            var client = CreateClient(mediaServer, 10);
            var url = $"{mediaServer.BaseUrl}/api/v3/system/status";

            using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);

            if (!response.IsSuccessStatusCode)
                throw new ApiException(server.Name, "/api/v3/system/status", (int)response.StatusCode);

            await using var json = await response.Content.ReadAsStreamAsync();
            var metadata = await JsonSerializer.DeserializeAsync<ServerMetadata>(json, _jsonOptions) ??
                           throw new MetadataException(server.Name);

            metadata.ServerId = mediaServer.Id;

            var existing = await db.ServerMetadata.FindAsync(mediaServer.Id);

            if (existing is null)
                db.ServerMetadata.Add(metadata);
            else
                db.Entry(existing).CurrentValues.SetValues(metadata);

            await GetProfilesAsync(server);

            await db.SaveChangesAsync();
            return existing ?? metadata;
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
    }

    public async Task UpdateServerStatsAsync(int serverId, Action<MediaServer> update)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var server = await db.MediaServers.FindAsync(serverId);

        if (server is null)
            return;

        update(server);
        db.Entry(server).State = EntityState.Modified;
        await db.SaveChangesAsync();
        changeBus.NotifyChanged();
    }

    #endregion

    #region Health

    public async Task<List<RemoteHealthMessage>> GetRemoteHealthAsync(MediaServer server)
    {
        if (string.IsNullOrWhiteSpace(server.ApiKey))
            return [];

        try
        {
            var client = CreateClient(server, 10);
            var url = $"{server.BaseUrl}/api/v3/health";

            using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            if (!response.IsSuccessStatusCode)
                return [];

            await using var stream = await response.Content.ReadAsStreamAsync();
            return await JsonSerializer.DeserializeAsync<List<RemoteHealthMessage>>(stream, _jsonOptions) ?? [];
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Failed to fetch remote health for {name}", server.Name);
            return [];
        }
    }

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

        await UpdateServerStatsAsync(serverId, s => s.UptimePercent = uptimePercent);

        return uptimePercent;
    }

    public async Task<List<ServerStatusSample>> GetStatusSamplesAsync(int serverId, DateTime since)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.ServerStatusSamples.Where(s => s.ServerId == serverId && s.SampledAt >= since)
            .OrderBy(s => s.SampledAt).ToListAsync();
    }

    #endregion

    #region Queue

    public async Task<List<DownloadItem>> GetQueueAsync(MediaServer server, bool forceRefresh = false)
    {
        if (server.ServiceType is not (ServiceType.Radarr or ServiceType.Sonarr))
            throw new MediaServerException($"{server.Name}: Queue fetching not supported for {server.ServiceType}.");
        if (!server.IsEnabled)
        {
            await using var db = await dbFactory.CreateDbContextAsync();
            var cached = await db.DownloadQueue.Where(q => q.ServerId == server.Id).ToListAsync();
            server.Queue = cached;
            return cached;
        }

        // One rewrite per server at a time: two overlapping refreshes (routine + UI) would both delete and then
        // both insert, leaving duplicate rows.
        var queueLock = QueueLocks.GetOrAdd(server.Id, _ => new SemaphoreSlim(1, 1));
        await queueLock.WaitAsync();
        try
        {
            var (items, fresh) = await FetchAndStoreQueueAsync(server, forceRefresh);

            // Every download is scored the moment it first shows up, whoever fetched the queue (a routine, the
            // Queue page, a sync). Done inside the lock so two overlapping fetches can't both score a new item.
            // Only on a fresh fetch: cached rows can include downloads already removed or imported.
            if (fresh && (settings?.ShouldMonitorQueue(server) ?? true))
            {
                try
                {
                    var evaluated = await healthService.EvaluateNewItemsAsync(items);
                    if (evaluated > 0)
                        logger.LogInformation("Evaluated {count} new queue item(s) on {name}", evaluated, server.Name);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // The fetch itself worked; the next refresh retries anything left unscored.
                    logger.LogError(ex, "Could not evaluate new queue items on {name}", server.Name);
                }
            }

            return items;
        }
        finally
        {
            queueLock.Release();
        }
    }

    private static readonly ConcurrentDictionary<int, SemaphoreSlim> QueueLocks = new();

    private static readonly string[] RoutineOwnedServerFields =
    [
        nameof(MediaServer.IsOnline), nameof(MediaServer.LastPingedAt), nameof(MediaServer.LastPingMs),
        nameof(MediaServer.IsAuthenticated), nameof(MediaServer.LastAuthenticatedAt),
        nameof(MediaServer.LastQueueFetchAt), nameof(MediaServer.LastQueueCount),
        nameof(MediaServer.LastMissingFetchAt), nameof(MediaServer.LastMissingCount),
        nameof(MediaServer.LastErrorAt), nameof(MediaServer.LastError),
        nameof(MediaServer.UptimePercent), nameof(MediaServer.DownloadsProcessed), nameof(MediaServer.CreatedAt)
    ];

    private async Task<(List<DownloadItem> Items, bool Fresh)> FetchAndStoreQueueAsync(MediaServer server,
        bool forceRefresh)
    {
        return await ExecuteApiAsync<(List<DownloadItem>, bool)>(server, "Unexpected error fetching queue.", async () =>
        {
            await using var db = await dbFactory.CreateDbContextAsync();

            if (!forceRefresh)
            {
                var lastFetch = await db.MediaServers.Where(s => s.Id == server.Id).Select(s => s.LastQueueFetchAt)
                    .FirstOrDefaultAsync();

                if (lastFetch is { } fetchedAt && DateTime.UtcNow - fetchedAt < QueueCacheTtl)
                {
                    var cached = await db.DownloadQueue.Where(q => q.ServerId == server.Id).ToListAsync();
                    await EnsureQueueParentMediaSummariesAsync(db, server.Id, cached, DateTime.UtcNow);
                    await db.SaveChangesAsync();
                    server.Queue = cached;
                    return (cached, false);
                }
            }

            List<DownloadItem> items = [];
            List<QueueRecord> records = [];
            var page = 1;
            const int pageSize = 100;

            while (true)
            {
                var (batch, total, batchRecords) = await FetchQueuePageAsync(server, page, pageSize);

                foreach (var item in batch)
                    item.ServerId = server.Id;

                items.AddRange(batch);
                records.AddRange(batchRecords);

                if (items.Count >= total || batch.Count < pageSize)
                    break;

                page++;
            }

            // Delete + insert as one transaction: a reader in between (e.g. the evaluation routine) sees the old
            // rows until commit instead of an empty queue, which would mark every active download Disappeared.
            await using (var tx = await db.Database.BeginTransactionAsync())
            {
                await db.Database.ExecuteSqlRawAsync("DELETE FROM DownloadQueue WHERE ServerId = {0}", server.Id);

                if (items.Count != 0)
                    db.DownloadQueue.UpdateRange(items);

                await db.SaveChangesAsync();
                await tx.CommitAsync();
            }

            // The queue response already embeds the parent movie/series/episode (includeMovie/includeSeries/
            // includeEpisode), so hydrate local entities from that instead of re-fetching each one by id.
            switch (server.ServiceType)
            {
                case ServiceType.Radarr:
                    await UpsertEmbeddedMoviesAsync(db, server.Id, records);
                    break;

                case ServiceType.Sonarr:
                    await UpsertEmbeddedSeriesAsync(db, server.Id, records);
                    await UpsertEmbeddedEpisodesAsync(db, server.Id, records);
                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(server), server.ServiceType, "Unknown server type.");
            }

            await EnsureQueueParentMediaSummariesAsync(db, server.Id, items, DateTime.UtcNow);
            await db.SaveChangesAsync();

            await UpdateServerStatsAsync(server.Id, s =>
            {
                s.LastQueueFetchAt = DateTime.UtcNow;
                s.LastQueueCount = items.Count;
            });

            logger.LogInformation("Fetched {count} queue items for {name}", items.Count, server.Name);

            return (items, true);
        });
    }

    private async Task<(List<DownloadItem> Items, int TotalRecords, List<QueueRecord> Records)> FetchQueuePageAsync(
        MediaServer server, int page, int pageSize)
    {
        if (string.IsNullOrWhiteSpace(server.ApiKey))
            throw new MediaServerException($"{server.Name}: API key is required.");

        try
        {
            var client = CreateClient(server, 15);
            var url =
                $"{server.BaseUrl}/api/v3/queue?page={page}&pageSize={pageSize}&includeUnknownMovieItems=true&includeMovie=true&includeSeries=true&includeEpisode=true";

            using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);

            if (!response.IsSuccessStatusCode)
                throw new ApiException(server.Name, "/api/v3/queue", (int)response.StatusCode);

            await using var stream = await response.Content.ReadAsStreamAsync();
            var queue = await JsonSerializer.DeserializeAsync<QueueResponse>(stream, _jsonOptions);
            var records = queue?.Records ?? [];

            var items = records.Select(x => new DownloadItem
            {
                RemoteId = x.Id,
                Title = x.Title,
                Size = x.Size,
                SizeLeft = x.SizeLeft,
                TimeLeft = x.TimeLeft,
                Status = x.Status,
                TrackedDownloadStatus = x.TrackedDownloadStatus,
                TrackedDownloadState = x.TrackedDownloadState,
                ErrorMessage = x.ErrorMessage,
                DownloadId = x.DownloadId,
                Protocol = x.Protocol,
                DownloadClient = x.DownloadClient,
                DownloadClientHasPostImportCategory = x.DownloadClientHasPostImportCategory,
                Indexer = x.Indexer,
                OutputPath = x.OutputPath,
                EstimatedCompletionTime = x.EstimatedCompletionTime,
                Added = x.Added,
                MovieId = x.MovieId,
                SeriesId = x.SeriesId,
                EpisodeId = x.EpisodeId,
                ServerId = server.Id,
                State = MapQueueState(x.TrackedDownloadState),
                LanguagesJson = SerializeJsonElement(x.Languages, "[]"),
                QualityJson = SerializeJsonElement(x.Quality, "null"),
                CustomFormatsJson = SerializeJsonElement(x.CustomFormats, "[]"),
                StatusMessagesJson = SerializeJsonElement(x.StatusMessages, "[]")
            }).ToList();

            return (items, queue?.TotalRecords ?? 0, records);
        }
        catch (MediaServerException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new MediaServerException($"{server.Name}: Error fetching queue page {page}.", ex);
        }
    }

    private static async Task UpsertEmbeddedMoviesAsync(AppDbContext db, int serverId, IEnumerable<QueueRecord> records)
    {
        var movieRecords = records.Where(r => r.Movie is { ValueKind: JsonValueKind.Object })
            .Select(r => JsonSerializer.Deserialize<MovieRecord>(r.Movie!.Value.GetRawText(), _jsonOptions))
            .Where(m => m is not null).Select(m => m!).DistinctBy(m => m.Id).ToList();

        if (movieRecords.Count == 0) return;

        var remoteIds = movieRecords.Select(m => m.Id).ToList();
        var existingByRemote = await db.Movies.Where(m => m.ServerId == serverId && remoteIds.Contains(m.RemoteId))
            .ToDictionaryAsync(m => m.RemoteId);

        foreach (var record in movieRecords)
        {
            var movie = record.ToEntity(serverId);

            if (existingByRemote.TryGetValue(record.Id, out var current))
            {
                movie.Id = current.Id;
                db.Entry(current).CurrentValues.SetValues(movie);
            }
            else
            {
                db.Movies.Add(movie);
            }
        }

        await db.SaveChangesAsync();
    }

    private static async Task UpsertEmbeddedSeriesAsync(AppDbContext db, int serverId, IEnumerable<QueueRecord> records)
    {
        var seriesRecords = records.Where(r => r.Series is { ValueKind: JsonValueKind.Object })
            .Select(r => JsonSerializer.Deserialize<SeriesRecord>(r.Series!.Value.GetRawText(), _jsonOptions))
            .Where(s => s is not null).Select(s => s!).DistinctBy(s => s.Id).ToList();

        if (seriesRecords.Count == 0) return;

        var remoteIds = seriesRecords.Select(s => s.Id).ToList();
        var existingByRemote = await db.Series.Where(s => s.ServerId == serverId && remoteIds.Contains(s.RemoteId))
            .ToDictionaryAsync(s => s.RemoteId);

        foreach (var record in seriesRecords)
        {
            var series = record.ToEntity(serverId);

            if (existingByRemote.TryGetValue(record.Id, out var current))
            {
                series.Id = current.Id;
                db.Entry(current).CurrentValues.SetValues(series);
            }
            else
            {
                db.Series.Add(series);
            }
        }

        await db.SaveChangesAsync();
    }

    private static async Task UpsertEmbeddedEpisodesAsync(AppDbContext db, int serverId,
        IEnumerable<QueueRecord> records)
    {
        var episodeRecords = records.Where(r => r.Episode is { ValueKind: JsonValueKind.Object })
            .Select(r => JsonSerializer.Deserialize<EpisodeRecord>(r.Episode!.Value.GetRawText(), _jsonOptions))
            .Where(e => e is not null).Select(e => e!).DistinctBy(e => e.Id).ToList();

        if (episodeRecords.Count == 0) return;

        var remoteIds = episodeRecords.Select(e => e.Id).ToList();
        var existingByRemote = await db.Episodes.Where(e => e.ServerId == serverId && remoteIds.Contains(e.RemoteId))
            .ToDictionaryAsync(e => e.RemoteId);

        var seriesIds = episodeRecords.Select(e => e.SeriesId).Distinct().ToList();
        var seriesByRemote = await db.Series.Where(s => s.ServerId == serverId && seriesIds.Contains(s.RemoteId))
            .ToDictionaryAsync(s => s.RemoteId);

        foreach (var record in episodeRecords)
        {
            var episode = record.ToEntity(serverId);

            if (seriesByRemote.TryGetValue(record.SeriesId, out var series))
                episode.SeriesDbId = series.Id;

            if (existingByRemote.TryGetValue(record.Id, out var current))
            {
                episode.Id = current.Id;
                db.Entry(current).CurrentValues.SetValues(episode);
            }
            else
            {
                db.Episodes.Add(episode);
            }
        }

        await db.SaveChangesAsync();
    }

    public async Task RemoveFromQueueAsync(MediaServer server, int queueId, bool blocklist = true,
        bool removeFromClient = true)
    {
        if (string.IsNullOrWhiteSpace(server.ApiKey))
            throw new MediaServerException($"{server.Name}: API key is required.");

        await ExecuteApiAsync(server, $"Unexpected error removing queue item {queueId}.", async () =>
        {
            var client = CreateClient(server, 15);
            var url =
                $"{server.BaseUrl}/api/v3/queue/{queueId}?removeFromClient={removeFromClient.ToString().ToLowerInvariant()}&blocklist={blocklist.ToString().ToLowerInvariant()}&skipRedownload=false";

            using var response = await client.DeleteAsync(url);

            if (!response.IsSuccessStatusCode)
                throw new ApiException(server.Name, $"/api/v3/queue/{queueId}", (int)response.StatusCode);

            logger.LogInformation("Removed queue item {id} from {name}", queueId, server.Name);
        });
        changeBus.NotifyChanged();
    }

    private async Task TriggerSearchAsync(MediaServer? server, string commandName, string paramName, object value)
    {
        if (string.IsNullOrWhiteSpace(server?.ApiKey))
            throw new MediaServerException($"{server?.Name}: API key is required.");

        var ids = value switch
        {
            int single => [single],
            IEnumerable<int> many => many.ToArray(),
            _ => Array.Empty<int>()
        };

        var commandId = await ExecuteApiAsync(server, $"Error triggering {commandName}.", async () =>
        {
            var client = CreateClient(server, 15);
            var url = $"{server.BaseUrl}/api/v3/command";
            var payload = JsonSerializer.Serialize(new Dictionary<string, object>
            {
                ["name"] = commandName, [paramName] = value
            });

            using var content = new StringContent(payload, Encoding.UTF8, "application/json");
            using var response = await client.PostAsync(url, content);

            if (!response.IsSuccessStatusCode)
                throw new ApiException(server.Name, "/api/v3/command", (int)response.StatusCode);

            var body = await response.Content.ReadAsStringAsync();
            var id = TryReadCommand(body)?.Id;

            logger.LogInformation("Triggered {cmd} (command {commandId}) on {name} for {param} {ids}", commandName, id,
                server.Name, paramName, string.Join(", ", ids));
            return id;
        });

        // Following the search up (does the item exist, did it find anything) can take minutes, so it runs on
        // its own and never holds up the routine or page that asked for the search.
        _ = Task.Run(() => FollowSearchAsync(server, commandName, paramName, ids, commandId));
    }

    private static readonly TimeSpan SearchFollowTimeout = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan SearchPollInterval = TimeSpan.FromSeconds(5);

    /// <summary>
    ///     Reports what came of a search pushed to Radarr/Sonarr: (a) whether every item we asked about exists on
    ///     the server, and (b) once the command finishes, whether it found and grabbed a release. The result goes
    ///     to the Events page and the log.
    /// </summary>
    private async Task FollowSearchAsync(MediaServer server, string commandName, string paramName,
        IReadOnlyList<int> ids, int? commandId)
    {
        var label = $"{commandName} on {server.Name}";
        try
        {
            var resource = paramName switch
            {
                "movieIds" => "movie",
                "episodeIds" => "episode",
                "seriesId" => "series",
                _ => null
            };

            if (resource is not null && ids.Count > 0)
            {
                var notFound = new List<int>();
                var client = CreateClient(server, 15);
                foreach (var id in ids)
                {
                    using var response = await client.GetAsync($"{server.BaseUrl}/api/v3/{resource}/{id}");
                    if (response.StatusCode == HttpStatusCode.NotFound) notFound.Add(id);
                }

                if (notFound.Count > 0)
                {
                    await ReportSearchAsync(LogLevel.Warning,
                        $"{label}: {notFound.Count} of {ids.Count} {resource}(s) we searched for don't exist on the server " +
                        $"(IDs {string.Join(", ", notFound)}). They may have been deleted in {server.ServiceType}.");
                }
            }

            if (commandId is null)
            {
                await ReportSearchAsync(LogLevel.Warning,
                    $"{label}: the server accepted the search but didn't return a command ID, so its result can't be checked.");
                return;
            }

            var deadline = DateTime.UtcNow + SearchFollowTimeout;
            SearchCommandStatus? command = null;
            while (DateTime.UtcNow < deadline)
            {
                await Task.Delay(SearchPollInterval);

                var client = CreateClient(server, 15);
                using var response = await client.GetAsync($"{server.BaseUrl}/api/v3/command/{commandId}");
                if (!response.IsSuccessStatusCode) continue;

                command = TryReadCommand(await response.Content.ReadAsStringAsync());
                if (command?.IsFinished == true) break;
            }

            var what = $"{ids.Count} item(s)";
            if (command?.IsFinished != true)
            {
                await ReportSearchAsync(LogLevel.Warning,
                    $"{label}: still not finished after {SearchFollowTimeout.TotalMinutes:0} minutes (status: {command?.Status ?? "unknown"}).");
                return;
            }

            if (!string.Equals(command.Status, "completed", StringComparison.OrdinalIgnoreCase))
            {
                await ReportSearchAsync(LogLevel.Warning,
                    $"{label}: search for {what} {command.Status}. {command.Message}".TrimEnd());
                return;
            }

            var grabbed = command.ReportsDownloaded;
            var message = grabbed switch
            {
                > 0 => $"{label}: search for {what} found and grabbed {grabbed} release(s).",
                0 => $"{label}: search for {what} finished but found nothing to grab.",
                _ =>
                    $"{label}: search for {what} finished. {command.Message ?? "The server didn't say whether it found anything."}"
            };
            await ReportSearchAsync(grabbed == 0 ? LogLevel.Warning : LogLevel.Information, message);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not follow up {label}", label);
        }
    }

    private async Task ReportSearchAsync(LogLevel level, string message)
    {
        if (eventService is not null)
            await eventService.LogAsync(message, "Search", level, null);
        else
            logger.Log(level, "{message}", message);
    }

    private static SearchCommandStatus? TryReadCommand(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<SearchCommandStatus>(json, _jsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed record SearchCommandStatus(int Id, string? Status, string? Message)
    {
        public bool IsFinished => Status?.ToLowerInvariant() is "completed" or "failed" or "aborted" or "cancelled";

        /// <summary>
        ///     Radarr/Sonarr finish a search with a message like "Movie search completed. 1 reports downloaded.";
        ///     null when the message doesn't say.
        /// </summary>
        public int? ReportsDownloaded =>
            Message is not null && Regex.Match(Message, @"(\d+)\s+reports?\s+downloaded", RegexOptions.IgnoreCase) is
                { Success: true } m
                ? int.Parse(m.Groups[1].Value)
                : null;
    }

    public Task TriggerMovieSearchAsync(MediaServer? server, int movieId)
    {
        return TriggerMoviesSearchAsync(server, [movieId]);
    }

    /// <summary>One MoviesSearch command for several movies, instead of one command each.</summary>
    public Task TriggerMoviesSearchAsync(MediaServer? server, IReadOnlyCollection<int> movieIds)
    {
        return TriggerSearchAsync(server, "MoviesSearch", "movieIds", movieIds.ToArray());
    }

    /// <summary>One EpisodeSearch command for several episodes, instead of one command each.</summary>
    public Task TriggerEpisodesSearchAsync(MediaServer server, IReadOnlyCollection<int> episodeIds)
    {
        return TriggerSearchAsync(server, "EpisodeSearch", "episodeIds", episodeIds.ToArray());
    }

    public Task TriggerSeriesSearchAsync(MediaServer? server, int seriesId)
    {
        return TriggerSearchAsync(server, "SeriesSearch", "seriesId", seriesId);
    }

    public Task TriggerEpisodeSearchAsync(MediaServer server, int episodeId)
    {
        return TriggerEpisodesSearchAsync(server, [episodeId]);
    }

    public async Task<ActiveSearches> GetActiveSearchesAsync(MediaServer server)
    {
        var result = new ActiveSearches();

        if (string.IsNullOrWhiteSpace(server.ApiKey))
            throw new MediaServerException($"{server.Name}: API key is required.");

        return await ExecuteApiAsync(server, "Error fetching commands.", async () =>
        {
            var client = CreateClient(server, 10);
            var url = $"{server.BaseUrl}/api/v3/command";

            using var response = await client.GetAsync(url);

            if (!response.IsSuccessStatusCode)
                throw new ApiException(server.Name, "/api/v3/command", (int)response.StatusCode);

            var json = await response.Content.ReadAsStringAsync();
            var commands = JsonSerializer.Deserialize<List<CommandRecord>>(json, _jsonOptions) ?? [];

            foreach (var cmd in commands.Where(cmd =>
                         cmd.Status is not ("completed" or "failed" or "aborted" or "cancelled")))
            {
                switch (cmd.Name)
                {
                    case "MoviesSearch" when cmd.Body?.MovieIds is { } m:
                        foreach (var id in m) result.MovieIds.Add(id);
                        break;
                    case "SeriesSearch" when cmd.Body?.SeriesId is { } s:
                        result.SeriesIds.Add(s);
                        break;
                    case "EpisodeSearch" when cmd.Body?.EpisodeIds is { } e:
                        foreach (var id in e) result.EpisodeIds.Add(id);
                        break;
                }
            }

            return result;
        });
    }

    #endregion

    #region Helpers

    private static readonly JsonSerializerOptions _compactJsonOptions = new() { WriteIndented = false };

    private static readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

    private static string SerializeJsonElement(JsonElement value, string fallback)
    {
        return value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null
            ? fallback
            : JsonSerializer.Serialize(value, _compactJsonOptions);
    }

    /// <param name="fallbackSeconds">Used only when no settings are available (tests).</param>
    private HttpClient CreateClient(MediaServer server, int fallbackSeconds)
    {
        // The server's own Request Timeout wins, then the global one (Settings > Servers).
        var timeoutSeconds = Math.Max(1, settings?.GetRequestTimeout(server) ?? fallbackSeconds);

        var client =
            httpClientFactory.CreateClient(server.SkipCertificateValidation ? InsecureHttpClientName : HttpClientName);
        client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);

        if (!string.IsNullOrWhiteSpace(server.ApiKey))
            client.DefaultRequestHeaders.TryAddWithoutValidation("X-Api-Key", server.ApiKey);

        return client;
    }

    /// <summary>
    ///     Runs an Arr API call and translates low-level failures into the domain exception taxonomy:
    ///     our own <see cref="MediaServerException" /> (incl. <see cref="ApiException" />) pass through,
    ///     timeouts/socket errors become <see cref="ConnectionException" />, and anything else is wrapped
    ///     with <paramref name="unexpectedError" />. Centralizes what was a 4-arm try/catch repeated per call.
    /// </summary>
    private static async Task<T> ExecuteApiAsync<T>(MediaServer server, string unexpectedError, Func<Task<T>> operation)
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

    private static async Task ExecuteApiAsync(MediaServer server, string unexpectedError, Func<Task> operation)
    {
        await ExecuteApiAsync(server, unexpectedError, async () =>
        {
            await operation();
            return true;
        });
    }

    private static async Task ClearDefaultAsync(AppDbContext db, ServiceType type, int? excludeId)
    {
        var defaults = await db.MediaServers
            .Where(s => s.ServiceType == type && s.IsDefault && (excludeId == null || s.Id != excludeId)).ToListAsync();

        foreach (var s in defaults)
            s.IsDefault = false;

        if (defaults.Count > 0)
            await db.SaveChangesAsync();
    }

    private static QueueState MapQueueState(string? state)
    {
        return state?.ToLowerInvariant() switch
        {
            "downloading" => QueueState.Downloading,
            "importpending" => QueueState.PendingImport,
            "importing" => QueueState.Importing,
            "failed" => QueueState.Failed,
            _ => QueueState.Unknown
        };
    }

    private async Task EnsureMediaHealthSummaryAsync<TMedia>(AppDbContext db, TMedia media, DateTime now)
        where TMedia : MediaItem
    {
        await EnsureMediaHealthSummariesAsync(db, [media], now);
    }

    private async Task EnsureMediaHealthSummariesAsync<TMedia>(AppDbContext db, IEnumerable<TMedia> mediaItems,
        DateTime now) where TMedia : MediaItem
    {
        var items = mediaItems.Cast<MediaItem>().Where(e => e.Id > 0)
            .Where(e => GetTrackedMediaType(e) is not TrackedMediaType.Unknown)
            .GroupBy(e => new { Type = GetTrackedMediaType(e), e.Id }).Select(e => e.First()).ToList();

        if (items.Count == 0) return;

        foreach (var group in items.GroupBy(GetTrackedMediaType))
        {
            var ids = group.Select(e => e.Id).ToList();

            var existingIds = group.Key switch
            {
                TrackedMediaType.Movie => await db.MediaHealthSummaries
                    .Where(e => e.MediaType == TrackedMediaType.Movie)
                    .Where(e => e.MovieId.HasValue && ids.Contains(e.MovieId.Value)).Select(e => e.MovieId!.Value)
                    .ToListAsync(),

                TrackedMediaType.Series => await db.MediaHealthSummaries
                    .Where(e => e.MediaType == TrackedMediaType.Series)
                    .Where(e => e.SeriesId.HasValue && ids.Contains(e.SeriesId.Value)).Select(e => e.SeriesId!.Value)
                    .ToListAsync(),

                _ => []
            };

            var existing = existingIds.ToHashSet();

            foreach (var item in group.Where(e => !existing.Contains(e.Id)))
                db.MediaHealthSummaries.Add(CreateMediaHealthSummary(item, group.Key, now));
        }
    }

    private async Task EnsureQueueParentMediaSummariesAsync(AppDbContext db, int serverId,
        IEnumerable<DownloadItem> downloads, DateTime now)
    {
        IEnumerable<DownloadItem> downloadItems = downloads as DownloadItem[] ?? downloads.ToArray();
        var movieIds = downloadItems.Where(e => e.MovieId > 0).Select(e => e.MovieId).Distinct().ToList();

        var seriesIds = downloadItems.Where(e => e.SeriesId > 0).Select(e => e.SeriesId).Distinct().ToList();

        if (movieIds.Count > 0)
        {
            var movies = await db.Movies.Where(e => e.ServerId == serverId && movieIds.Contains(e.RemoteId))
                .ToListAsync();

            await EnsureMediaHealthSummariesAsync(db, movies, now);
        }

        if (seriesIds.Count > 0)
        {
            var series = await db.Series.Where(e => e.ServerId == serverId && seriesIds.Contains(e.RemoteId))
                .ToListAsync();

            await EnsureMediaHealthSummariesAsync(db, series, now);
        }
    }

    private static MediaHealthSummary CreateMediaHealthSummary(MediaItem item, TrackedMediaType mediaType, DateTime now)
    {
        MediaHealthSummary summary = new()
        {
            MediaType = mediaType, CreatedAt = now, UpdatedAt = now, CurrentState = HealthState.Unknown
        };

        switch (item)
        {
            case Movie:
                summary.MovieId = item.Id;
                break;

            case Series:
                summary.SeriesId = item.Id;
                break;
        }

        return summary;
    }

    private static TrackedMediaType GetTrackedMediaType(MediaItem item)
    {
        return item switch
        {
            Movie => TrackedMediaType.Movie,
            Series => TrackedMediaType.Series,
            _ => TrackedMediaType.Unknown
        };
    }

    #endregion
}
