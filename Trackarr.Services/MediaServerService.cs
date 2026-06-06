using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Text.Json;
using Trackarr.Core.Media;
using Trackarr.Core.Queue;
using Trackarr.Core.Server;
using Trackarr.Data;

namespace Trackarr.Services;

public class MediaServerService(IDbContextFactory<AppDbContext> dbFactory, IHttpClientFactory httpClientFactory, ILogger<MediaServerService> logger, EventService eventService) {
    #region Movies

    public async Task<(bool Success, List<Movie> Items, string Message)> GetMissingMovies(MediaServer server, bool forceRefresh = false) {
        try {
            logger.LogInformation("Fetching missing movies for {name}", server.Name);

            if (server.ServiceType is not ServiceType.Radarr) {
                logger.LogWarning("Missing movies fetching not supported for {type}", server.ServiceType);
                return (false, [], $"{server.ServiceType} does not support missing movies fetching.");
            }

            await using AppDbContext db = await dbFactory.CreateDbContextAsync();

            if (!forceRefresh) {
                List<Movie> cached = await db.Movies.Where(m => m.ServerId == server.Id).ToListAsync();

                if (cached.Count != 0) {
                    logger.LogInformation("Returning cached missing movies ({count} items) for {name}", cached.Count, server.Name);
                    return (true, cached, $"Returned {cached.Count} cached missing movies from {server.Name}.");
                }
            }

            List<Movie> items = [];
            int page = 1;
            const int pageSize = 100;

            while (true) {
                (bool success, List<Movie> batch, int total) = await FetchMissingMoviesPageAsync(server, page, pageSize);

                if (!success) {
                    logger.LogError("Failed to fetch missing movies page {page} for {name}", page, server.Name);
                    return (false, [], "Failed to fetch missing movies.");
                }

                foreach (Movie item in batch)
                    item.ServerId = server.Id;

                items.AddRange(batch);

                if (items.Count >= total || batch.Count < pageSize)
                    break;

                page++;
            }

            foreach (Movie item in items) {
                Movie? existing = await db.Movies.FirstOrDefaultAsync(m => m.ServerId == item.ServerId && m.RemoteId == item.RemoteId);

                if (existing is null) {
                    db.Movies.Add(item);
                    continue;
                }

                db.Entry(existing).CurrentValues.SetValues(item);
            }

            await db.SaveChangesAsync();

            logger.LogInformation("Fetched {count} missing movies for {name}", items.Count, server.Name);
            return (true, items, $"Fetched {items.Count} missing movies from {server.Name}.");
        }
        catch (TaskCanceledException ex) {
            logger.LogError("Request timed out: {message}", ex.Message);
            return (false, [], "Request timed out.");
        }
        catch (HttpRequestException ex) {
            logger.LogError("Could not reach server: {message}", ex.Message);
            return (false, [], $"Could not reach server: {ex.Message}");
        }
        catch (Exception ex) {
            logger.LogError("Unexpected error: {message}", ex.Message);
            return (false, [], $"Unexpected error: {ex.Message}");
        }
    }

    private async Task<(bool Success, List<Movie> Items, int TotalRecords)> FetchMissingMoviesPageAsync(MediaServer server, int page, int pageSize) {
        if (string.IsNullOrWhiteSpace(server.ApiKey)) {
            logger.LogError("API key is required for {name}", server.Name);
            return (false, [], 0);
        }

        try {
            JsonSerializerOptions options = new() { PropertyNameCaseInsensitive = true };
            HttpClient client = CreateClient(15);
            string url = $"{server.BaseUrl}/api/v3/wanted/missing?apikey={server.ApiKey}&page={page}&pageSize={pageSize}";

            using HttpResponseMessage response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);

            if (!response.IsSuccessStatusCode) {
                logger.LogError("Failed to fetch missing movies page: {status}", response.StatusCode);
                return (false, [], 0);
            }

            await using Stream stream = await response.Content.ReadAsStreamAsync();
            MissingMovieResponse? missing = await JsonSerializer.DeserializeAsync<MissingMovieResponse>(stream, options);
            foreach (MissingMovieRecord x in missing.Records) {
                _ = JsonSerializer.Serialize(x.OriginalLanguage);
                _ = JsonSerializer.Serialize(x.AlternateTitles);
                _ = JsonSerializer.Serialize(x.Images);
                _ = JsonSerializer.Serialize(x.Genres);
                _ = JsonSerializer.Serialize(x.Keywords);
                _ = JsonSerializer.Serialize(x.Tags);
                _ = JsonSerializer.Serialize(x.Ratings);
                _ = JsonSerializer.Serialize(x.Collection);
                _ = JsonSerializer.Serialize(x.Statistics);
            }

            List<Movie>? items = missing?.Records.Select(x => new Movie {
                RemoteId = x.Id,
                Title = x.Title,
                OriginalTitle = x.OriginalTitle,
                SortTitle = x.SortTitle,
                Status = x.Status,
                Overview = x.Overview,
                Website = x.Website,
                Year = x.Year,
                YouTubeTrailerId = x.YouTubeTrailerId,
                Studio = x.Studio,
                Path = x.Path,
                QualityProfileId = x.QualityProfileId,
                HasFile = x.HasFile,
                MovieFileId = x.MovieFileId,
                Monitored = x.Monitored,
                MinimumAvailability = x.MinimumAvailability,
                IsAvailable = x.IsAvailable,
                FolderName = x.FolderName,
                Runtime = x.Runtime,
                CleanTitle = x.CleanTitle,
                ImdbId = x.ImdbId,
                TmdbId = x.TmdbId,
                TitleSlug = x.TitleSlug,
                Certification = x.Certification,
                SizeOnDisk = x.SizeOnDisk,
                InCinemas = x.InCinemas,
                PhysicalRelease = x.PhysicalRelease,
                DigitalRelease = x.DigitalRelease,
                ReleaseDate = x.ReleaseDate,
                Added = x.Added,
                LastSearchTime = x.LastSearchTime,
                Popularity = x.Popularity,
                ServerId = server.Id,
                OriginalLanguageJson = JsonSerializer.Serialize(x.OriginalLanguage),
                AlternateTitlesJson = JsonSerializer.Serialize(x.AlternateTitles),
                ImagesJson = JsonSerializer.Serialize(x.Images),
                GenresJson = JsonSerializer.Serialize(x.Genres),
                KeywordsJson = JsonSerializer.Serialize(x.Keywords),
                TagsJson = JsonSerializer.Serialize(x.Tags),
                RatingsJson = JsonSerializer.Serialize(x.Ratings),
                CollectionJson = JsonSerializer.Serialize(x.Collection),
                StatisticsJson = JsonSerializer.Serialize(x.Statistics)
            }).ToList();

            return (true, items ?? [], missing?.TotalRecords ?? 0);
        }
        catch (Exception ex) {
            logger.LogError("Error fetching missing movies page: {message}", ex.Message);
            return (false, [], 0);
        }
    }

    #endregion

    #region Media

    public async Task<(bool Success, List<QualityProfile> Profiles, string Message)> GetProfilesAsync(MediaServer server) {
        logger.LogInformation("Fetching quality profiles for {name}", server.Name);

        if (string.IsNullOrWhiteSpace(server.ApiKey))
            return (false, [], "API key is required.");

        try {
            JsonSerializerOptions options = new() { PropertyNameCaseInsensitive = true };
            HttpClient client = CreateClient(10);
            string url = $"{server.BaseUrl}/api/v3/qualityprofile?apikey={server.ApiKey}";

            using HttpResponseMessage response = await client.GetAsync(url);

            if (!response.IsSuccessStatusCode) {
                logger.LogError("Failed to fetch profiles: {status}", response.StatusCode);
                return (false, [], $"Failed to fetch profiles: {response.StatusCode}");
            }

            string json = await response.Content.ReadAsStringAsync();
            List<QualityProfile> profiles = JsonSerializer.Deserialize<List<QualityProfile>>(json, options) ?? [];

            await using AppDbContext db = await dbFactory.CreateDbContextAsync();
            MediaServer? tracked = await db.MediaServers.FindAsync(server.Id);
            if (tracked is null) return (true, profiles, $"Fetched {profiles.Count} profiles from {server.Name}.");
            tracked.Profiles = profiles;
            await db.SaveChangesAsync();

            return (true, profiles, $"Fetched {profiles.Count} profiles from {server.Name}.");
        }
        catch (Exception ex) {
            logger.LogError("Error fetching profiles: {message}", ex.Message);
            return (false, [], $"Error fetching profiles: {ex.Message}");
        }
    }

    #endregion

    #region CRUD

    public async Task<(bool Success, List<MediaServer> Items)> GetAllServersAsync() {
        try {
            await using AppDbContext db = await dbFactory.CreateDbContextAsync();
            return (true, await db.MediaServers.OrderBy(s => s.ServiceType).ThenBy(s => s.Name).ToListAsync());
        }
        catch (Exception e) {
            eventService.LogAsync("Could not fetch all servers: {message}", e.Message, LogLevel.Error).Wait();
            return (false, []);
        }
    }

    public async Task<MediaServer?> GetServerByIdAsync(int id) {
        await using AppDbContext db = await dbFactory.CreateDbContextAsync();
        return await db.MediaServers.FindAsync(id);
    }

    public async Task<MediaServer> CreateServerAsync(MediaServer server) {
        await using AppDbContext db = await dbFactory.CreateDbContextAsync();
        if (server.IsDefault)
            await ClearDefaultAsync(db, server.ServiceType, null);
        db.MediaServers.Add(server);
        await db.SaveChangesAsync();
        await GetSystemStatusAsync(server);
        logger.LogInformation("Created server: {name}", server.Name);
        return server;
    }

    public async Task<MediaServer> UpdateServerAsync(MediaServer server) {
        await using AppDbContext db = await dbFactory.CreateDbContextAsync();
        if (server.IsDefault)
            await ClearDefaultAsync(db, server.ServiceType, server.Id);

        MediaServer? tracked = await db.MediaServers.FindAsync(server.Id);
        if (tracked is null) return server;

        db.Entry(tracked).CurrentValues.SetValues(server);
        await db.SaveChangesAsync();
        await GetSystemStatusAsync(tracked);
        logger.LogInformation("Updated server: {name}", server.Name);
        return tracked;
    }

    public async Task DeleteServerAsync(int id) {
        await using AppDbContext db = await dbFactory.CreateDbContextAsync();
        MediaServer? server = await db.MediaServers.FindAsync(id);
        if (server is null) return;
        db.MediaServers.Remove(server);
        await db.SaveChangesAsync();
        logger.LogInformation("Deleted server: {name}", server.Name);
    }

    #endregion

    #region Connection

    public async Task<(bool Success, string Message, long? PingTime)> TestConnectionAsync(MediaServer server) {
        logger.LogInformation("Testing connection to {name}", server.Name);
        Stopwatch sw = Stopwatch.StartNew();
        try {
            HttpClient client = CreateClient(10);
            (bool success, string message) = server.ServiceType switch {
                ServiceType.Radarr => await TestArrAsync(client, server, "Radarr"),
                ServiceType.Sonarr => await TestArrAsync(client, server, "Sonarr"),
                _ => (false, "Unknown service type.")
            };

            sw.Stop();
            if (!success || server.Id == 0) return (success, message, sw.ElapsedMilliseconds);

            await using AppDbContext db = await dbFactory.CreateDbContextAsync();
            MediaServer? tracked = await db.MediaServers.FindAsync(server.Id);
            if (tracked is null) return (success, message, sw.ElapsedMilliseconds);
            tracked.LastPingTime = sw.ElapsedMilliseconds;
            db.MediaServers.Update(tracked);
            await db.SaveChangesAsync();

            return (success, message, sw.ElapsedMilliseconds);
        }
        catch (TaskCanceledException) {
            logger.LogError("Connection timed out after 10 seconds.");
            return (false, "Connection timed out after 10 seconds.", null);
        }
        catch (HttpRequestException ex) {
            logger.LogError("Could not reach server: {message}", ex.Message);
            return (false, $"Could not reach server: {ex.Message}", null);
        }
        catch (Exception ex) {
            logger.LogError("Unexpected error: {message}", ex.Message);
            return (false, $"Unexpected error: {ex.Message}", null);
        }
    }

    private async Task<(bool Success, string Message)> TestArrAsync(HttpClient client, MediaServer server, string appName) {
        if (string.IsNullOrWhiteSpace(server.ApiKey))
            return (false, "API key is required.");

        string url = $"{server.BaseUrl}/api/v3/system/status?apikey={server.ApiKey}";
        using HttpResponseMessage response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);

        if (response.IsSuccessStatusCode)
            return (true, $"Connected to {appName} successfully.");

        logger.LogError("Failed to connect: {status}", response.StatusCode);
        return (false, $"Failed to connect to {appName}. Server returned status code: {response.StatusCode}");
    }

    public async Task<(bool Success, ServerMetadata? Metadata, string Message)> GetSystemStatusAsync(MediaServer server) {
        await using AppDbContext db = await dbFactory.CreateDbContextAsync();
        MediaServer? mediaServer = await db.MediaServers.FindAsync(server.Id);
        if (string.IsNullOrWhiteSpace(mediaServer?.ApiKey))
            return (false, null, "API key is required.");

        try {
            JsonSerializerOptions options = new() { PropertyNameCaseInsensitive = true };
            HttpClient client = CreateClient(10);
            string url = $"{mediaServer.BaseUrl}/api/v3/system/status?apikey={mediaServer.ApiKey}";

            using HttpResponseMessage response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);

            if (!response.IsSuccessStatusCode)
                return (false, null, $"Failed to fetch system status: {response.StatusCode}");

            await using Stream json = await response.Content.ReadAsStreamAsync();
            ServerMetadata? metadata = await JsonSerializer.DeserializeAsync<ServerMetadata>(json, options);

            if (metadata == null)
                return (false, null, "Failed to deserialize system status.");

            metadata.ServerId = mediaServer.Id;

            ServerMetadata? existing = await db.ServerMetadata.FindAsync(mediaServer.Id);
            if (existing is null)
                db.ServerMetadata.Add(metadata);
            else
                db.Entry(existing).CurrentValues.SetValues(metadata);

            await db.SaveChangesAsync();

            return (true, metadata, "OK");
        }
        catch (TaskCanceledException) {
            return (false, null, "Request timed out.");
        }
        catch (HttpRequestException ex) {
            return (false, null, $"Connection error: {ex.Message}");
        }
        catch (Exception ex) {
            return (false, null, $"Unexpected error: {ex.Message}");
        }
    }

    #endregion

    #region Queue

    public async Task<(bool Success, List<DownloadItem> Items, string Message)> GetQueueAsync(MediaServer server, bool forceRefresh = false) {
        try {
            logger.LogInformation("Fetching queue for {name}", server.Name);

            if (server.ServiceType is not (ServiceType.Radarr or ServiceType.Sonarr)) {
                logger.LogWarning("Queue fetching not supported for {type}", server.ServiceType);
                return (false, [], $"{server.ServiceType} does not support queue fetching.");
            }

            await using AppDbContext db = await dbFactory.CreateDbContextAsync();

            if (!forceRefresh) {
                List<DownloadItem> cached = await db.DownloadQueue.Where(q => q.ServerId == server.Id).ToListAsync();

                if (cached.Count != 0) {
                    server.Queue = cached;
                    logger.LogInformation("Returning cached queue ({count} items) for {name}", cached.Count, server.Name);
                    return (true, cached, $"Returned {cached.Count} cached downloads from {server.Name}.");
                }
            }

            List<DownloadItem> items = [];
            int page = 1;
            const int pageSize = 100;

            while (true) {
                (bool success, List<DownloadItem> batch, int total) = await FetchQueuePageAsync(server, page, pageSize);
                if (!success) {
                    logger.LogError("Failed to fetch queue page {page} for {name}", page, server.Name);
                    return (false, [], "Failed to fetch queue.");
                }

                foreach (DownloadItem item in batch)
                    item.ServerId = server.Id;

                items.AddRange(batch);

                if (items.Count >= total || batch.Count < pageSize)
                    break;
                page++;
            }

            await db.Database.ExecuteSqlRawAsync("DELETE FROM DownloadQueue WHERE ServerId = {0}", server.Id);

            if (items.Count != 0)
                db.DownloadQueue.UpdateRange(items);

            await db.SaveChangesAsync();

            server.Queue = items;
            db.MediaServers.Update(server);
            await db.SaveChangesAsync();

            logger.LogInformation("Fetched {count} queue items for {name}", items.Count, server.Name);
            return (true, items, $"Fetched {items.Count} queued downloads from {server.Name}.");
        }
        catch (TaskCanceledException ex) {
            logger.LogError("Request timed out: {message}", ex.Message);
            return (false, [], "Request timed out.");
        }
        catch (HttpRequestException ex) {
            logger.LogError("Could not reach server: {message}", ex.Message);
            return (false, [], $"Could not reach server: {ex.Message}");
        }
        catch (Exception ex) {
            logger.LogError("Unexpected error: {message}", ex.Message);
            return (false, [], $"Unexpected error: {ex.Message}");
        }
    }

    private async Task<(bool Success, List<DownloadItem> Items, int TotalRecords)> FetchQueuePageAsync(MediaServer server, int page, int pageSize) {
        if (string.IsNullOrWhiteSpace(server.ApiKey)) {
            logger.LogError("API key is required for {name}", server.Name);
            return (false, [], 0);
        }

        try {
            JsonSerializerOptions options = new() { PropertyNameCaseInsensitive = true };
            HttpClient client = CreateClient(15);
            string url = $"{server.BaseUrl}/api/v3/queue?apikey={server.ApiKey}&page={page}&pageSize={pageSize}&includeUnknownMovieItems=true&includeMovie=true&includeSeries=true";

            using HttpResponseMessage response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);

            if (!response.IsSuccessStatusCode) {
                logger.LogError("Failed to fetch queue page: {status}", response.StatusCode);
                return (false, [], 0);
            }

            await using Stream stream = await response.Content.ReadAsStreamAsync();
            QueueResponse? queue = await JsonSerializer.DeserializeAsync<QueueResponse>(stream, options);

            List<DownloadItem>? items = queue?.Records.Select(x => new DownloadItem {
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
                CustomFormatScore = x.CustomFormatScore,
                MovieId = x.MovieId,
                SeriesId = x.SeriesId,
                EpisodeId = x.EpisodeId,
                ServerId = server.Id,
                State = MapQueueState(x.TrackedDownloadState),
                QueueStatus = MapQueueStatus(x.Status, x.TrackedDownloadStatus),
                LanguagesJson = JsonSerializer.Serialize(x.Languages),
                QualityJson = JsonSerializer.Serialize(x.Quality),
                CustomFormatsJson = JsonSerializer.Serialize(x.CustomFormats),
                StatusMessagesJson = JsonSerializer.Serialize(x.StatusMessages),
            }).ToList();

            return (true, items ?? [], queue?.TotalRecords ?? 0);
        }
        catch (Exception ex) {
            logger.LogError("Error fetching queue page: {message}", ex.Message);
            return (false, [], 0);
        }
    }

    #endregion

    #region Helpers

    private HttpClient CreateClient(int timeoutSeconds) {
        HttpClient client = httpClientFactory.CreateClient("MediaServerTest");
        client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
        return client;
    }

    private static async Task ClearDefaultAsync(AppDbContext db, ServiceType type, int? excludeId) {
        List<MediaServer> defaults = await db.MediaServers.Where(s => s.ServiceType == type && s.IsDefault && (excludeId == null || s.Id != excludeId)).ToListAsync();

        foreach (MediaServer s in defaults)
            s.IsDefault = false;

        if (defaults.Count > 0)
            await db.SaveChangesAsync();
    }

    private static QueueState MapQueueState(string? state) {
        return state?.ToLowerInvariant() switch {
            "downloading" => QueueState.Downloading,
            "importpending" => QueueState.PendingImport,
            "importing" => QueueState.Importing,
            "failed" => QueueState.Failed,
            _ => QueueState.Unknown
        };
    }

    private static QueueStatus MapQueueStatus(string? status, string? trackedStatus) {
        if (status?.Equals("warning", StringComparison.OrdinalIgnoreCase) == true)
            return QueueStatus.Warning;

        if (trackedStatus?.Equals("warning", StringComparison.OrdinalIgnoreCase) == true)
            return QueueStatus.Warning;

        if (status?.Equals("completed", StringComparison.OrdinalIgnoreCase) == true)
            return QueueStatus.Completed;

        return QueueStatus.Ok;
    }

    #endregion
}