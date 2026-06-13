using System.Diagnostics;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Trackarr.Core.Exception;
using Trackarr.Core.Media.Movies;
using Trackarr.Core.Media.Shows;
using Trackarr.Core.Queue;
using Trackarr.Core.Server;
using Trackarr.Data;

namespace Trackarr.Services.Media;

public class MediaServerService(IDbContextFactory<AppDbContext> dbFactory, IHttpClientFactory httpClientFactory, ILogger<MediaServerService> logger) {
    #region Media

    public async Task<List<QualityProfile>> GetProfilesAsync(MediaServer server) {
        logger.LogInformation("Fetching quality profiles for {name}", server.Name);

        if (string.IsNullOrWhiteSpace(server.ApiKey))
            throw new MediaServerException($"{server.Name}: API key is required.");

        try {
            JsonSerializerOptions options = new() { PropertyNameCaseInsensitive = true };
            HttpClient client = CreateClient(10);
            string url = $"{server.BaseUrl}/api/v3/qualityprofile?apikey={server.ApiKey}";

            using HttpResponseMessage response = await client.GetAsync(url);

            if (!response.IsSuccessStatusCode)
                throw new ApiException(server.Name, "/api/v3/qualityprofile", (int)response.StatusCode);

            string json = await response.Content.ReadAsStringAsync();
            List<QualityProfile> profiles = JsonSerializer.Deserialize<List<QualityProfile>>(json, options) ?? [];

            await using AppDbContext db = await dbFactory.CreateDbContextAsync();
            MediaServer? tracked = await db.MediaServers.FindAsync(server.Id);
            if (tracked is not null) {
                tracked.Profiles = profiles;
                await db.SaveChangesAsync();
            }

            return profiles;
        }
        catch (MediaServerException) { throw; }
        catch (TaskCanceledException ex) { throw new ConnectionException(server.Name, ex); }
        catch (HttpRequestException ex) { throw new ConnectionException(server.Name, ex); }
        catch (Exception ex) { throw new MediaServerException($"{server.Name}: Unexpected error fetching profiles.", ex); }
    }

    #endregion

    #region Movies

    public async Task<List<Movie>> GetMissingMovies(MediaServer server, bool forceRefresh = false) {
        logger.LogInformation("Fetching missing movies for {name}", server.Name);

        if (server.ServiceType is not ServiceType.Radarr)
            throw new MediaServerException($"{server.Name}: Missing movies not supported for {server.ServiceType}.");

        try {
            await using AppDbContext db = await dbFactory.CreateDbContextAsync();

            if (!forceRefresh) {
                List<Movie> cached = await db.Movies.Where(m => m.ServerId == server.Id).ToListAsync();
                if (cached.Count != 0)
                    return cached;
            }

            List<Movie> items = [];
            int page = 1;
            const int pageSize = 100;

            while (true) {
                (List<Movie> batch, int total) = await FetchMissingMoviesPageAsync(server, page, pageSize);

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
                }
                else {
                    item.Id = existing.Id;
                    db.Entry(existing).CurrentValues.SetValues(item);
                }
            }

            await db.SaveChangesAsync();

            logger.LogInformation("Fetched {count} missing movies for {name}", items.Count, server.Name);
            return items;
        }
        catch (MediaServerException) { throw; }
        catch (TaskCanceledException ex) { throw new ConnectionException(server.Name, ex); }
        catch (HttpRequestException ex) { throw new ConnectionException(server.Name, ex); }
        catch (Exception ex) { throw new MediaServerException($"{server.Name}: Unexpected error fetching missing movies.", ex); }
    }
    public async Task<Dictionary<int, Movie>> GetParentMovieAsync(int serverId, IEnumerable<DownloadItem> downloads) {
        List<int> movieIds = downloads.Where(item => item.MovieId > 0).Select(item => item.MovieId).Distinct().ToList();

        if (movieIds.Count == 0) {
            return [];
        }

        await using AppDbContext db = await dbFactory.CreateDbContextAsync();

        List<Movie> movies = await db.Movies.AsNoTracking().Where(movie => movie.ServerId == serverId && movieIds.Contains(movie.RemoteId)).ToListAsync();

        return movies.GroupBy(movie => movie.RemoteId).ToDictionary(group => group.Key, group => group.First());
    }
    private async Task<(List<Movie> Items, int TotalRecords)> FetchMissingMoviesPageAsync(MediaServer server, int page, int pageSize) {
        if (string.IsNullOrWhiteSpace(server.ApiKey))
            throw new MediaServerException($"{server.Name}: API key is required.");

        try {
            JsonSerializerOptions options = new() { PropertyNameCaseInsensitive = true };
            HttpClient client = CreateClient(15);
            string url = $"{server.BaseUrl}/api/v3/wanted/missing?apikey={server.ApiKey}&page={page}&pageSize={pageSize}";

            using HttpResponseMessage response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);

            if (!response.IsSuccessStatusCode)
                throw new ApiException(server.Name, "/api/v3/wanted/missing", (int)response.StatusCode);

            string json = await response.Content.ReadAsStringAsync();
            MissingMovieResponse? missing = JsonSerializer.Deserialize<MissingMovieResponse>(json, options);

            List<Movie> items = missing
                                    ?.Records
                                    .Select(x => new Movie {
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
                                    })
                                    .ToList() ??
                                [];

            return (items, missing?.TotalRecords ?? 0);
        }
        catch (MediaServerException) { throw; }
        catch (Exception ex) { throw new MediaServerException($"{server.Name}: Error fetching missing movies page {page}.", ex); }
    }
    public async Task<Movie?> GetMovieByIdAsync(MediaServer server, int radarrMovieId) {
        await using AppDbContext db = await dbFactory.CreateDbContextAsync();

        Movie? existing = await db.Movies.FirstOrDefaultAsync(m => m.ServerId == server.Id && m.RemoteId == radarrMovieId);

        try {
            JsonSerializerOptions options = new() { PropertyNameCaseInsensitive = true };
            HttpClient client = CreateClient(10);
            string url = $"{server.BaseUrl}/api/v3/movie/{radarrMovieId}?apikey={server.ApiKey}";

            using HttpResponseMessage response = await client.GetAsync(url);

            if (!response.IsSuccessStatusCode)
                throw new ApiException(server.Name, $"/api/v3/movie/{radarrMovieId}", (int)response.StatusCode);

            string json = await response.Content.ReadAsStringAsync();
            MovieRecord? x = JsonSerializer.Deserialize<MovieRecord>(json, options);

            if (x is null) return existing;

            Movie movie = new() {
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
            };

            if (existing is null) {
                db.Movies.Add(movie);
            }
            else {
                movie.Id = existing.Id;
                db.Entry(existing).CurrentValues.SetValues(movie);
            }

            await db.SaveChangesAsync();
            return existing ?? movie;
        }
        catch (MediaServerException) { throw; }
        catch (TaskCanceledException ex) { throw new ConnectionException(server.Name, ex); }
        catch (HttpRequestException ex) { throw new ConnectionException(server.Name, ex); }
        catch (Exception ex) { throw new MediaServerException($"{server.Name}: Unexpected error fetching movie {radarrMovieId}.", ex); }
    }

    #endregion

    #region Series

    public async Task<List<Episode>> GetMissingEpisodes(MediaServer server, bool forceRefresh = false) {
        logger.LogInformation("Fetching missing episodes for {name}", server.Name);

        if (server.ServiceType is not ServiceType.Sonarr)
            throw new MediaServerException($"{server.Name}: Missing episodes not supported for {server.ServiceType}.");

        try {
            await using AppDbContext db = await dbFactory.CreateDbContextAsync();

            if (!forceRefresh) {
                List<Episode> cached = await db.Episodes.Where(e => e.ServerId == server.Id && !e.HasFile).ToListAsync();
                if (cached.Count != 0)
                    return cached;
            }

            List<Episode> items = [];
            int page = 1;
            const int pageSize = 100;

            while (true) {
                (List<Episode> batch, int total) = await FetchMissingEpisodesPageAsync(server, page, pageSize);

                foreach (Episode item in batch)
                    item.ServerId = server.Id;

                items.AddRange(batch);

                if (items.Count >= total || batch.Count < pageSize)
                    break;

                page++;
            }

            foreach (Episode item in items) {
                Episode? existing = await db.Episodes.FirstOrDefaultAsync(e => e.ServerId == item.ServerId && e.RemoteId == item.RemoteId);

                if (existing is null) {
                    db.Episodes.Add(item);
                }
                else {
                    item.Id = existing.Id;
                    db.Entry(existing).CurrentValues.SetValues(item);
                }
            }

            await db.SaveChangesAsync();

            logger.LogInformation("Fetched {count} missing episodes for {name}", items.Count, server.Name);
            return items;
        }
        catch (MediaServerException) { throw; }
        catch (TaskCanceledException ex) { throw new ConnectionException(server.Name, ex); }
        catch (HttpRequestException ex) { throw new ConnectionException(server.Name, ex); }
        catch (Exception ex) { throw new MediaServerException($"{server.Name}: Unexpected error fetching missing episodes.", ex); }
    }
    public async Task<Dictionary<int, Series>> GetParentSeriesAsync(int serverId, IEnumerable<DownloadItem> downloads) {
        List<int> seriesIds = downloads.Where(item => item.SeriesId > 0).Select(item => item.SeriesId).Distinct().ToList();

        if (seriesIds.Count == 0) {
            return [];
        }

        await using AppDbContext db = await dbFactory.CreateDbContextAsync();

        List<Series> series = await db.Series.AsNoTracking().Where(item => item.ServerId == serverId && seriesIds.Contains(item.RemoteId)).ToListAsync();

        return series.GroupBy(item => item.RemoteId).ToDictionary(group => group.Key, group => group.First());
    }
    private async Task<(List<Episode> Items, int TotalRecords)> FetchMissingEpisodesPageAsync(MediaServer server, int page, int pageSize) {
        if (string.IsNullOrWhiteSpace(server.ApiKey))
            throw new MediaServerException($"{server.Name}: API key is required.");

        try {
            JsonSerializerOptions options = new() { PropertyNameCaseInsensitive = true };
            HttpClient client = CreateClient(15);
            string url = $"{server.BaseUrl}/api/v3/wanted/missing?apikey={server.ApiKey}&page={page}&pageSize={pageSize}&includeSeries=true";

            using HttpResponseMessage response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);

            if (!response.IsSuccessStatusCode)
                throw new ApiException(server.Name, "/api/v3/wanted/missing", (int)response.StatusCode);

            string json = await response.Content.ReadAsStringAsync();
            MissingShowResponse? missing = JsonSerializer.Deserialize<MissingShowResponse>(json, options);

            List<Episode> items = missing
                                      ?.Records
                                      .Select(x => new Episode {
                                          RemoteId = x.Id,
                                          RemoteSeriesId = x.SeriesId,
                                          TvdbId = x.TvdbId,
                                          EpisodeFileId = x.EpisodeFileId,
                                          SeasonNumber = x.SeasonNumber,
                                          EpisodeNumber = x.EpisodeNumber,
                                          AbsoluteEpisodeNumber = x.AbsoluteEpisodeNumber,
                                          Title = x.Title,
                                          AirDate = x.AirDate,
                                          AirDateUtc = x.AirDateUtc,
                                          Overview = x.Overview,
                                          HasFile = x.HasFile,
                                          Monitored = x.Monitored,
                                          LastSearchTime = x.LastSearchTime,
                                          ServerId = server.Id,
                                          ImagesJson = JsonSerializer.Serialize(x.Images),
                                          EpisodeFileJson = JsonSerializer.Serialize(x.EpisodeFile),
                                          RatingsJson = JsonSerializer.Serialize(x.Ratings),
                                          LanguagesJson = JsonSerializer.Serialize(x.Languages)
                                      })
                                      .ToList() ??
                                  [];

            return (items, missing?.TotalRecords ?? 0);
        }
        catch (MediaServerException) { throw; }
        catch (Exception ex) { throw new MediaServerException($"{server.Name}: Error fetching missing episodes page {page}.", ex); }
    }

    public async Task<Series?> GetSeriesByIdAsync(MediaServer server, int sonarrSeriesId) {
        if (server.ServiceType is not ServiceType.Sonarr)
            throw new MediaServerException($"{server.Name}: Series fetching not supported for {server.ServiceType}.");

        await using AppDbContext db = await dbFactory.CreateDbContextAsync();

        Series? existing = await db.Series.FirstOrDefaultAsync(s => s.ServerId == server.Id && s.RemoteId == sonarrSeriesId);

        try {
            JsonSerializerOptions options = new() { PropertyNameCaseInsensitive = true };
            HttpClient client = CreateClient(10);
            string url = $"{server.BaseUrl}/api/v3/series/{sonarrSeriesId}?apikey={server.ApiKey}";

            using HttpResponseMessage response = await client.GetAsync(url);

            if (!response.IsSuccessStatusCode)
                throw new ApiException(server.Name, $"/api/v3/series/{sonarrSeriesId}", (int)response.StatusCode);

            string json = await response.Content.ReadAsStringAsync();
            SeriesRecord? x = JsonSerializer.Deserialize<SeriesRecord>(json, options);

            if (x is null)
                return existing;

            Series series = MapSeries(server, x);

            if (existing is null) {
                db.Series.Add(series);
            }
            else {
                series.Id = existing.Id;
                db.Entry(existing).CurrentValues.SetValues(series);
            }

            await db.SaveChangesAsync();
            return existing ?? series;
        }
        catch (MediaServerException) { throw; }
        catch (TaskCanceledException ex) { throw new ConnectionException(server.Name, ex); }
        catch (HttpRequestException ex) { throw new ConnectionException(server.Name, ex); }
        catch (Exception ex) { throw new MediaServerException($"{server.Name}: Unexpected error fetching series {sonarrSeriesId}.", ex); }
    }

    public async Task<Episode?> GetEpisodeByIdAsync(MediaServer server, int sonarrEpisodeId) {
        if (server.ServiceType is not ServiceType.Sonarr)
            throw new MediaServerException($"{server.Name}: Episode fetching not supported for {server.ServiceType}.");

        await using AppDbContext db = await dbFactory.CreateDbContextAsync();

        Episode? existing = await db.Episodes.FirstOrDefaultAsync(e => e.ServerId == server.Id && e.RemoteId == sonarrEpisodeId);

        try {
            JsonSerializerOptions options = new() { PropertyNameCaseInsensitive = true };
            HttpClient client = CreateClient(10);
            string url = $"{server.BaseUrl}/api/v3/episode/{sonarrEpisodeId}?apikey={server.ApiKey}";

            using HttpResponseMessage response = await client.GetAsync(url);

            if (!response.IsSuccessStatusCode)
                throw new ApiException(server.Name, $"/api/v3/episode/{sonarrEpisodeId}", (int)response.StatusCode);

            string json = await response.Content.ReadAsStringAsync();
            EpisodeRecord? x = JsonSerializer.Deserialize<EpisodeRecord>(json, options);

            if (x is null)
                return existing;

            Episode episode = MapEpisode(server, x);

            Series? series = await db.Series.FirstOrDefaultAsync(s => s.ServerId == server.Id && s.RemoteId == x.SeriesId);

            if (series is not null)
                episode.SeriesDbId = series.Id;

            if (existing is null) {
                db.Episodes.Add(episode);
            }
            else {
                episode.Id = existing.Id;
                db.Entry(existing).CurrentValues.SetValues(episode);
            }

            await db.SaveChangesAsync();
            return existing ?? episode;
        }
        catch (MediaServerException) { throw; }
        catch (TaskCanceledException ex) { throw new ConnectionException(server.Name, ex); }
        catch (HttpRequestException ex) { throw new ConnectionException(server.Name, ex); }
        catch (Exception ex) { throw new MediaServerException($"{server.Name}: Unexpected error fetching episode {sonarrEpisodeId}.", ex); }
    }

    #endregion

    #region CRUD

    public async Task<List<MediaServer>> GetAllServersAsync() {
        await using AppDbContext db = await dbFactory.CreateDbContextAsync();
        return await db.MediaServers.Include(s => s.Metadata).OrderBy(s => s.ServiceType).ThenBy(s => s.Name).ToListAsync();
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
        server.Metadata = await GetSystemStatusAsync(server);
        logger.LogInformation("Created server: {name}", server.Name);
        return server;
    }

    public async Task UpdateServerAsync(MediaServer server) {
        await using AppDbContext db = await dbFactory.CreateDbContextAsync();
        if (server.IsDefault)
            await ClearDefaultAsync(db, server.ServiceType, server.Id);

        MediaServer tracked = await db.MediaServers.FindAsync(server.Id) ?? throw new MediaServerException($"{server.Name}: Server not found.");

        db.Entry(tracked).CurrentValues.SetValues(server);
        await db.SaveChangesAsync();
        await GetSystemStatusAsync(tracked);
        server.Metadata = await GetSystemStatusAsync(server);
        logger.LogInformation("Updated server: {name}", server.Name);
    }

    public async Task DeleteServerAsync(int id) {
        await using AppDbContext db = await dbFactory.CreateDbContextAsync();
        MediaServer? server = await db.MediaServers.FindAsync(id);
        if (server is null)
            return;
        db.MediaServers.Remove(server);
        await db.SaveChangesAsync();
        logger.LogInformation("Deleted server: {name}", server.Name);
    }

    #endregion

    #region Connection

    public async Task<(bool Success, long ElapsedMilliseconds, string? ReasonPhrase)> TestConnectionAsync(MediaServer server) {
        logger.LogInformation("Testing connection to {name}", server.Name);
        Stopwatch sw = Stopwatch.StartNew();

        try {
            HttpClient client = CreateClient(10);
            Task<HttpResponseMessage> response;
            await (server.ServiceType switch {
                ServiceType.Radarr or ServiceType.Sonarr => response = TestArrAsync(client, server),
                _ => throw new MediaServerException($"{server.Name}: Unknown service type.")
            });

            sw.Stop();

            if (server.Id == 0)
                return (true, sw.ElapsedMilliseconds, response.Result.ReasonPhrase);
            await using AppDbContext db = await dbFactory.CreateDbContextAsync();
            MediaServer? tracked = await db.MediaServers.FindAsync(server.Id);
            if (tracked is null)
                return (true, sw.ElapsedMilliseconds, response.Result.ReasonPhrase);
            sw.Stop();

            if (server.Id == 0)
                return (true, sw.ElapsedMilliseconds, response.Result.ReasonPhrase);

            await UpdateServerStatsAsync(server.Id,
            s => {
                s.IsOnline = true;
                s.LastPingedAt = DateTime.UtcNow;
                s.LastPingMs = sw.ElapsedMilliseconds;
                s.IsAuthenticated = true;
                s.LastAuthenticatedAt = DateTime.UtcNow;
            });

            return (true, sw.ElapsedMilliseconds, response.Result.ReasonPhrase);
        }
        catch (ConnectionException ex) {
            if (server.Id != 0)
                await UpdateServerStatsAsync(server.Id,
                s => {
                    s.IsOnline = false;
                    s.LastPingedAt = DateTime.UtcNow;
                    s.LastErrorAt = DateTime.UtcNow;
                    s.LastError = ex.Message;
                });
            throw;
        }
        catch (MediaServerException) { throw; }
        catch (TaskCanceledException ex) { throw new ConnectionException(server.Name, ex); }
        catch (HttpRequestException ex) { throw new ConnectionException(server.Name, ex); }
        catch (Exception ex) { throw new MediaServerException($"{server.Name}: Unexpected error testing connection.", ex); }
    }

    private static async Task<HttpResponseMessage> TestArrAsync(HttpClient client, MediaServer server) {
        if (string.IsNullOrWhiteSpace(server.ApiKey))
            throw new MediaServerException($"{server.Name}: API key is required.");

        string url = $"{server.BaseUrl}/api/v3/system/status?apikey={server.ApiKey}";
        HttpResponseMessage response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);

        return !response.IsSuccessStatusCode ? throw new ApiException(server.Name, "/api/v3/system/status", (int)response.StatusCode) : response;
    }

    public async Task<ServerMetadata> GetSystemStatusAsync(MediaServer server) {
        await using AppDbContext db = await dbFactory.CreateDbContextAsync();
        MediaServer mediaServer = await db.MediaServers.FindAsync(server.Id) ?? throw new MediaServerException($"{server.Name}: Server not found.");

        if (string.IsNullOrWhiteSpace(mediaServer.ApiKey))
            throw new MediaServerException($"{server.Name}: API key is required.");

        try {
            JsonSerializerOptions options = new() { PropertyNameCaseInsensitive = true };
            HttpClient client = CreateClient(10);
            string url = $"{mediaServer.BaseUrl}/api/v3/system/status?apikey={mediaServer.ApiKey}";

            using HttpResponseMessage response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);

            if (!response.IsSuccessStatusCode)
                throw new ApiException(server.Name, "/api/v3/system/status", (int)response.StatusCode);

            await using Stream json = await response.Content.ReadAsStreamAsync();
            ServerMetadata metadata = await JsonSerializer.DeserializeAsync<ServerMetadata>(json, options) ?? throw new MetadataException(server.Name);

            metadata.ServerId = mediaServer.Id;

            ServerMetadata? existing = await db.ServerMetadata.FindAsync(mediaServer.Id);
            if (existing is null)
                db.ServerMetadata.Add(metadata);
            else
                db.Entry(existing).CurrentValues.SetValues(metadata);

            await db.SaveChangesAsync();
            return existing ?? metadata;
        }
        catch (MediaServerException) { throw; }
        catch (TaskCanceledException ex) { throw new ConnectionException(server.Name, ex); }
        catch (HttpRequestException ex) { throw new ConnectionException(server.Name, ex); }
        catch (Exception ex) { throw new MetadataException(server.Name, ex); }
    }

    public async Task UpdateServerStatsAsync(int serverId, Action<MediaServer> update) {
        await using AppDbContext db = await dbFactory.CreateDbContextAsync();
        MediaServer? server = await db.MediaServers.FindAsync(serverId);
        if (server is null) return;
        update(server);
        db.Entry(server).State = EntityState.Modified;
        await db.SaveChangesAsync();
    }

    #endregion

    #region Queue

    public async Task<List<DownloadItem>> GetQueueAsync(MediaServer server, bool forceRefresh = false) {
        if (server.ServiceType is not (ServiceType.Radarr or ServiceType.Sonarr))
            throw new MediaServerException($"{server.Name}: Queue fetching not supported for {server.ServiceType}.");

        try {
            await using AppDbContext db = await dbFactory.CreateDbContextAsync();

            if (!forceRefresh) {
                List<DownloadItem> cached = await db.DownloadQueue.Where(q => q.ServerId == server.Id).ToListAsync();
                if (cached.Count != 0) {
                    server.Queue = cached;
                    return cached;
                }
            }

            List<DownloadItem> items = [];
            int page = 1;
            const int pageSize = 100;

            while (true) {
                (List<DownloadItem> batch, int total) = await FetchQueuePageAsync(server, page, pageSize);

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

            await UpdateServerStatsAsync(server.Id,
            s => {
                s.LastQueueFetchAt = DateTime.UtcNow;
                s.LastQueueCount = items.Count;
            });

            logger.LogInformation("Fetched {count} queue items for {name}", items.Count, server.Name);
            switch (server.ServiceType) {
                case ServiceType.Radarr: {
                    foreach (int movieId in items.Select(i => i.MovieId).Where(id => id != 0).Distinct())
                        await GetMovieByIdAsync(server, movieId);
                    break;
                }

                case ServiceType.Sonarr: {
                    foreach (int seriesId in items.Select(i => i.SeriesId).Where(id => id != 0).Distinct())
                        await GetSeriesByIdAsync(server, seriesId);
                    foreach (int episodeId in items.Select(i => i.EpisodeId).Where(id => id != 0).Distinct())
                        await GetEpisodeByIdAsync(server, episodeId);
                    break;
                }

                default:
                    throw new ArgumentOutOfRangeException();
            }

            return items;
        }
        catch (MediaServerException) { throw; }
        catch (TaskCanceledException ex) { throw new ConnectionException(server.Name, ex); }
        catch (HttpRequestException ex) { throw new ConnectionException(server.Name, ex); }
        catch (Exception ex) { throw new MediaServerException($"{server.Name}: Unexpected error fetching queue.", ex); }
    }

    private async Task<(List<DownloadItem> Items, int TotalRecords)> FetchQueuePageAsync(MediaServer server, int page, int pageSize) {
        if (string.IsNullOrWhiteSpace(server.ApiKey))
            throw new MediaServerException($"{server.Name}: API key is required.");

        try {
            JsonSerializerOptions options = new() { PropertyNameCaseInsensitive = true };
            HttpClient client = CreateClient(15);
            string url = $"{server.BaseUrl}/api/v3/queue?apikey={server.ApiKey}&page={page}&pageSize={pageSize}&includeUnknownMovieItems=true&includeMovie=true&includeSeries=true";

            using HttpResponseMessage response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);

            if (!response.IsSuccessStatusCode)
                throw new ApiException(server.Name, "/api/v3/queue", (int)response.StatusCode);

            await using Stream stream = await response.Content.ReadAsStreamAsync();
            QueueResponse? queue = await JsonSerializer.DeserializeAsync<QueueResponse>(stream, options);

            List<DownloadItem> items = queue
                                           ?.Records
                                           .Select(x => new DownloadItem {
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
                                               StatusMessagesJson = JsonSerializer.Serialize(x.StatusMessages)
                                           })
                                           .ToList() ??
                                       [];

            return (items, queue?.TotalRecords ?? 0);
        }
        catch (MediaServerException) { throw; }
        catch (Exception ex) { throw new MediaServerException($"{server.Name}: Error fetching queue page {page}.", ex); }
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

    private static Episode MapEpisode(MediaServer server, EpisodeRecord x) => new() {
        RemoteId = x.Id,
        RemoteSeriesId = x.SeriesId,
        TvdbId = x.TvdbId,
        EpisodeFileId = x.EpisodeFileId,
        SeasonNumber = x.SeasonNumber,
        EpisodeNumber = x.EpisodeNumber,
        AbsoluteEpisodeNumber = x.AbsoluteEpisodeNumber,
        Title = x.Title,
        AirDate = x.AirDate,
        AirDateUtc = x.AirDateUtc,
        Overview = x.Overview,
        HasFile = x.HasFile,
        Monitored = x.Monitored,
        UnverifiedSceneNumbering = x.UnverifiedSceneNumbering,
        LastSearchTime = x.LastSearchTime,
        ServerId = server.Id,
        ImagesJson = JsonSerializer.Serialize(x.Images),
        EpisodeFileJson = JsonSerializer.Serialize(x.EpisodeFile),
        RatingsJson = JsonSerializer.Serialize(x.Ratings),
        LanguagesJson = JsonSerializer.Serialize(x.Languages)
    };

    private static Series MapSeries(MediaServer server, SeriesRecord x) => new() {
        RemoteId = x.Id,
        Title = x.Title,
        SortTitle = x.SortTitle,
        Status = x.Status,
        Ended = x.Ended,
        Overview = x.Overview,
        Network = x.Network,
        AirTime = x.AirTime,
        Path = x.Path,
        QualityProfileId = x.QualityProfileId,
        LanguageProfileId = x.LanguageProfileId,
        SeasonFolder = x.SeasonFolder,
        Monitored = x.Monitored,
        MonitorNewItems = x.MonitorNewItems,
        UseSceneNumbering = x.UseSceneNumbering,
        Runtime = x.Runtime,
        TvdbId = x.TvdbId,
        TvRageId = x.TvRageId,
        TvMazeId = x.TvMazeId,
        TmdbId = x.TmdbId,
        ImdbId = x.ImdbId,
        TitleSlug = x.TitleSlug,
        RootFolderPath = x.RootFolderPath,
        Certification = x.Certification,
        SeriesType = x.SeriesType,
        CleanTitle = x.CleanTitle,
        Year = x.Year,
        FirstAired = x.FirstAired,
        LastAired = x.LastAired,
        NextAiring = x.NextAiring,
        PreviousAiring = x.PreviousAiring,
        Added = x.Added,
        ServerId = server.Id,
        AlternateTitlesJson = JsonSerializer.Serialize(x.AlternateTitles),
        ImagesJson = JsonSerializer.Serialize(x.Images),
        OriginalLanguageJson = JsonSerializer.Serialize(x.OriginalLanguage),
        SeasonsJson = JsonSerializer.Serialize(x.Seasons),
        GenresJson = JsonSerializer.Serialize(x.Genres),
        TagsJson = JsonSerializer.Serialize(x.Tags),
        RatingsJson = JsonSerializer.Serialize(x.Ratings),
        StatisticsJson = JsonSerializer.Serialize(x.Statistics)
    };

    #endregion
}