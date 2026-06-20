using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
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

namespace Trackarr.Services.Media;

public class MediaServerService(
    IDbContextFactory<AppDbContext> dbFactory,
    IHttpClientFactory httpClientFactory,
    ILogger<MediaServerService> logger)
{
    private static readonly TimeSpan QueueCacheTtl = TimeSpan.FromSeconds(30);

    public record CommandRecord(int Id, string Name, string Status, CommandBody? Body);

    public record CommandBody(List<int>? MovieIds, int? SeriesId, List<int>? EpisodeIds);

    #region Media

    private static readonly TimeSpan ProfileCacheTtl = TimeSpan.FromSeconds(60);
    private static readonly Dictionary<int, DateTime> ProfileFetchTimes = new();

    public async Task<List<QualityProfile>> GetProfilesAsync(MediaServer server, bool forceRefresh = false)
    {
        if (string.IsNullOrWhiteSpace(server.ApiKey))
            throw new MediaServerException($"{server.Name}: API key is required.");

        await using var db = await dbFactory.CreateDbContextAsync();

        if (!forceRefresh && server.Id != 0
                          && ProfileFetchTimes.TryGetValue(server.Id, out var last)
                          && DateTime.UtcNow - last < ProfileCacheTtl)
            return await db.QualityProfiles.Where(p => p.ServerId == server.Id).ToListAsync();

        logger.LogInformation("Fetching quality profiles for {name}", server.Name);

        try
        {
            JsonSerializerOptions options = new() { PropertyNameCaseInsensitive = true };
            var client = CreateClient(10);
            var url = $"{server.BaseUrl}/api/v3/qualityprofile?apikey={server.ApiKey}";

            using var response = await client.GetAsync(url);

            if (!response.IsSuccessStatusCode)
                throw new ApiException(server.Name, "/api/v3/qualityprofile", (int)response.StatusCode);

            var json = await response.Content.ReadAsStringAsync();
            var profiles = JsonSerializer.Deserialize<List<QualityProfile>>(json, options) ?? [];

            if (!await db.MediaServers.AnyAsync(s => s.Id == server.Id))
                return profiles;

            foreach (var profile in profiles)
                profile.ServerId = server.Id;

            var existing = await db.QualityProfiles
                .Where(p => p.ServerId == server.Id)
                .ToListAsync();

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
            throw new MediaServerException($"{server.Name}: Unexpected error fetching profiles.", ex);
        }
    }

    public async Task SetQualityProfileAsync(MediaServer server, int remoteId, int profileId)
    {
        if (string.IsNullOrWhiteSpace(server.ApiKey))
            throw new MediaServerException($"{server.Name}: API key is required.");

        var resource = server.ServiceType == ServiceType.Radarr ? "movie" : "series";

        try
        {
            var client = CreateClient(15);
            var getUrl = $"{server.BaseUrl}/api/v3/{resource}/{remoteId}?apikey={server.ApiKey}";

            using var getResponse = await client.GetAsync(getUrl);
            if (!getResponse.IsSuccessStatusCode)
                throw new ApiException(server.Name, $"/api/v3/{resource}/{remoteId}", (int)getResponse.StatusCode);

            var json = await getResponse.Content.ReadAsStringAsync();
            var node = JsonNode.Parse(json)
                       ?? throw new MediaServerException($"{server.Name}: Empty {resource} body.");

            node["qualityProfileId"] = profileId;

            var putUrl = $"{server.BaseUrl}/api/v3/{resource}/{remoteId}?apikey={server.ApiKey}";
            using var content = new StringContent(node.ToJsonString(), Encoding.UTF8, "application/json");
            using var putResponse = await client.PutAsync(putUrl, content);

            if (!putResponse.IsSuccessStatusCode)
                throw new ApiException(server.Name, $"/api/v3/{resource}/{remoteId}", (int)putResponse.StatusCode);

            logger.LogInformation("Set profile {profile} on {resource} {id} for {name}", profileId, resource, remoteId,
                server.Name);
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
            throw new MediaServerException($"{server.Name}: Error setting profile on {resource} {remoteId}.", ex);
        }
    }

    #endregion

    #region Movies

    public async Task<List<Movie>> GetMissingMovies(MediaServer server, bool forceRefresh = false)
    {
        logger.LogInformation("Fetching missing movies for {name}", server.Name);

        if (server.ServiceType is not ServiceType.Radarr)
            throw new MediaServerException($"{server.Name}: Missing movies not supported for {server.ServiceType}.");

        try
        {
            await using var db = await dbFactory.CreateDbContextAsync();

            if (!forceRefresh)
            {
                var cached = await db.Movies.Where(m => m.ServerId == server.Id).ToListAsync();

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

            foreach (var item in items)
            {
                var existing =
                    await db.Movies.FirstOrDefaultAsync(m =>
                        m.ServerId == item.ServerId && m.RemoteId == item.RemoteId);

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
            throw new MediaServerException($"{server.Name}: Unexpected error fetching missing movies.", ex);
        }
    }

    public async Task<Dictionary<int, Movie>> GetParentMovieAsync(int serverId, IEnumerable<DownloadItem> downloads)
    {
        var movieIds = downloads.Where(item => item.MovieId > 0).Select(item => item.MovieId).Distinct().ToList();

        if (movieIds.Count == 0) return [];

        await using var db = await dbFactory.CreateDbContextAsync();

        var movies = await db.Movies.Where(movie => movie.ServerId == serverId && movieIds.Contains(movie.RemoteId))
            .ToListAsync();

        await EnsureMediaHealthSummariesAsync(db, movies, DateTime.UtcNow);
        await db.SaveChangesAsync();

        return movies.GroupBy(movie => movie.RemoteId).ToDictionary(group => group.Key, group => group.First());
    }

    private async Task<(List<Movie> Items, int TotalRecords)> FetchMissingMoviesPageAsync(MediaServer server, int page,
        int pageSize)
    {
        if (string.IsNullOrWhiteSpace(server.ApiKey))
            throw new MediaServerException($"{server.Name}: API key is required.");

        try
        {
            JsonSerializerOptions options = new() { PropertyNameCaseInsensitive = true };
            var client = CreateClient(15);
            var url = $"{server.BaseUrl}/api/v3/wanted/missing?apikey={server.ApiKey}&page={page}&pageSize={pageSize}";

            using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);

            if (!response.IsSuccessStatusCode)
                throw new ApiException(server.Name, "/api/v3/wanted/missing", (int)response.StatusCode);

            var json = await response.Content.ReadAsStringAsync();
            var missing = JsonSerializer.Deserialize<MissingMovieResponse>(json, options);

            var items = missing?.Records.Select(x => new Movie
            {
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
            }).ToList() ?? [];

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

        try
        {
            JsonSerializerOptions options = new() { PropertyNameCaseInsensitive = true };
            var client = CreateClient(10);
            var url = $"{server.BaseUrl}/api/v3/movie/{radarrMovieId}?apikey={server.ApiKey}";

            using var response = await client.GetAsync(url);

            if (!response.IsSuccessStatusCode)
                throw new ApiException(server.Name, $"/api/v3/movie/{radarrMovieId}", (int)response.StatusCode);

            var json = await response.Content.ReadAsStringAsync();
            var x = JsonSerializer.Deserialize<MovieRecord>(json, options);

            if (x is null)
            {
                if (existing is not null)
                {
                    await EnsureMediaHealthSummaryAsync(db, existing, DateTime.UtcNow);
                    await db.SaveChangesAsync();
                }

                return existing;
            }

            Movie movie = new()
            {
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
            throw new MediaServerException($"{server.Name}: Unexpected error fetching movie {radarrMovieId}.", ex);
        }
    }

    #endregion

    #region Series

    public async Task<List<Episode>> GetMissingEpisodes(MediaServer server, bool forceRefresh = false)
    {
        logger.LogInformation("Fetching missing episodes for {name}", server.Name);

        if (server.ServiceType is not ServiceType.Sonarr)
            throw new MediaServerException($"{server.Name}: Missing episodes not supported for {server.ServiceType}.");

        try
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

            foreach (var item in items)
            {
                var existing =
                    await db.Episodes.FirstOrDefaultAsync(e =>
                        e.ServerId == item.ServerId && e.RemoteId == item.RemoteId);

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

            logger.LogInformation("Fetched {count} missing episodes for {name}", items.Count, server.Name);
            return items;
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
            throw new MediaServerException($"{server.Name}: Unexpected error fetching missing episodes.", ex);
        }
    }

    public async Task<Dictionary<int, Series>> GetParentSeriesAsync(int serverId, IEnumerable<DownloadItem> downloads)
    {
        var seriesIds = downloads.Where(item => item.SeriesId > 0).Select(item => item.SeriesId).Distinct().ToList();

        if (seriesIds.Count == 0) return [];

        await using var db = await dbFactory.CreateDbContextAsync();

        var series = await db.Series.Where(item => item.ServerId == serverId && seriesIds.Contains(item.RemoteId))
            .ToListAsync();

        await EnsureMediaHealthSummariesAsync(db, series, DateTime.UtcNow);
        await db.SaveChangesAsync();

        return series.GroupBy(item => item.RemoteId).ToDictionary(group => group.Key, group => group.First());
    }

    private async Task<(List<Episode> Items, int TotalRecords)> FetchMissingEpisodesPageAsync(MediaServer server,
        int page, int pageSize)
    {
        if (string.IsNullOrWhiteSpace(server.ApiKey))
            throw new MediaServerException($"{server.Name}: API key is required.");

        try
        {
            JsonSerializerOptions options = new() { PropertyNameCaseInsensitive = true };
            var client = CreateClient(15);
            var url =
                $"{server.BaseUrl}/api/v3/wanted/missing?apikey={server.ApiKey}&page={page}&pageSize={pageSize}&includeSeries=true";

            using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);

            if (!response.IsSuccessStatusCode)
                throw new ApiException(server.Name, "/api/v3/wanted/missing", (int)response.StatusCode);

            var json = await response.Content.ReadAsStringAsync();
            var missing = JsonSerializer.Deserialize<MissingShowResponse>(json, options);

            var items = missing
                            ?.Records
                            .Select(x => new Episode
                            {
                                RemoteId = x.Id,
                                RemoteSeriesId = x.SeriesId,
                                TvdbId = x.TvdbId,
                                EpisodeFileId = x.EpisodeFileId,

                                SeasonNumber = x.SeasonNumber,
                                EpisodeNumber = x.EpisodeNumber,
                                AbsoluteEpisodeNumber = x.AbsoluteEpisodeNumber,

                                SceneAbsoluteEpisodeNumber = x.SceneAbsoluteEpisodeNumber,
                                SceneEpisodeNumber = x.SceneEpisodeNumber,
                                SceneSeasonNumber = x.SceneSeasonNumber,

                                Title = x.Title,
                                AirDate = x.AirDate,
                                AirDateUtc = x.AirDateUtc,
                                EndTime = x.EndTime,
                                GrabDate = x.GrabDate,

                                Runtime = x.Runtime,
                                FinaleType = x.FinaleType,
                                Overview = x.Overview,

                                HasFile = x.HasFile,
                                Monitored = x.Monitored,
                                UnverifiedSceneNumbering = x.UnverifiedSceneNumbering,

                                LastSearchTime = x.LastSearchTime,

                                ServerId = server.Id,

                                ImagesJson = JsonSerializer.Serialize(x.Images),
                                EpisodeFileJson = JsonSerializer.Serialize(x.EpisodeFile),
                                SeriesJson = JsonSerializer.Serialize(x.Series)
                            })
                            .ToList() ??
                        [];

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

        try
        {
            JsonSerializerOptions options = new() { PropertyNameCaseInsensitive = true };
            var client = CreateClient(10);
            var url = $"{server.BaseUrl}/api/v3/series/{sonarrSeriesId}?apikey={server.ApiKey}";

            using var response = await client.GetAsync(url);

            if (!response.IsSuccessStatusCode)
                throw new ApiException(server.Name, $"/api/v3/series/{sonarrSeriesId}", (int)response.StatusCode);

            var json = await response.Content.ReadAsStringAsync();
            var x = JsonSerializer.Deserialize<SeriesRecord>(json, options);

            if (x is null)
            {
                if (existing is not null)
                {
                    await EnsureMediaHealthSummaryAsync(db, existing, DateTime.UtcNow);
                    await db.SaveChangesAsync();
                }

                return existing;
            }

            Series series = new()
            {
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
            throw new MediaServerException($"{server.Name}: Unexpected error fetching series {sonarrSeriesId}.", ex);
        }
    }

    public async Task<Episode?> GetEpisodeByIdAsync(MediaServer server, int sonarrEpisodeId)
    {
        if (server.ServiceType is not ServiceType.Sonarr)
            throw new MediaServerException($"{server.Name}: Episode fetching not supported for {server.ServiceType}.");

        await using var db = await dbFactory.CreateDbContextAsync();

        var existing =
            await db.Episodes.FirstOrDefaultAsync(e => e.ServerId == server.Id && e.RemoteId == sonarrEpisodeId);

        try
        {
            JsonSerializerOptions options = new() { PropertyNameCaseInsensitive = true };
            var client = CreateClient(10);
            var url = $"{server.BaseUrl}/api/v3/episode/{sonarrEpisodeId}?apikey={server.ApiKey}";

            using var response = await client.GetAsync(url);

            if (!response.IsSuccessStatusCode)
                throw new ApiException(server.Name, $"/api/v3/episode/{sonarrEpisodeId}", (int)response.StatusCode);

            var json = await response.Content.ReadAsStringAsync();
            var x = JsonSerializer.Deserialize<EpisodeRecord>(json, options);

            if (x is null)
                return existing;

            Episode episode = new()
            {
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
                EpisodeFileJson = JsonSerializer.Serialize(x.EpisodeFile)
            };

            var series = await db.Series.FirstOrDefaultAsync(s => s.ServerId == server.Id && s.RemoteId == x.SeriesId);

            if (series is not null)
                episode.SeriesDbId = series.Id;

            if (existing is null)
            {
                db.Episodes.Add(episode);
            }
            else
            {
                episode.Id = existing.Id;
                db.Entry(existing).CurrentValues.SetValues(episode);
            }

            await db.SaveChangesAsync();
            return existing ?? episode;
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
            throw new MediaServerException($"{server.Name}: Unexpected error fetching episode {sonarrEpisodeId}.", ex);
        }
    }

    public async Task<List<(int EpisodeId, bool HasFile, bool Monitored)>> GetSeriesEpisodeStatesAsync(
        MediaServer server, int seriesId)
    {
        if (string.IsNullOrWhiteSpace(server.ApiKey))
            throw new MediaServerException($"{server.Name}: API key is required.");

        try
        {
            JsonSerializerOptions options = new() { PropertyNameCaseInsensitive = true };
            var client = CreateClient(10);
            var url = $"{server.BaseUrl}/api/v3/episode?seriesId={seriesId}&apikey={server.ApiKey}";

            using var response = await client.GetAsync(url);

            if (!response.IsSuccessStatusCode)
                throw new ApiException(server.Name, "/api/v3/episode", (int)response.StatusCode);

            var json = await response.Content.ReadAsStringAsync();
            var episodes = JsonSerializer.Deserialize<List<EpisodeRecord>>(json, options) ?? [];

            return episodes.Select(e => (e.Id, e.HasFile, e.Monitored)).ToList();
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
            throw new MediaServerException($"{server.Name}: Error fetching episodes for series {seriesId}.", ex);
        }
    }

    #endregion

    #region CRUD

    public async Task<List<MediaServer>> GetAllServersAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.MediaServers.Include(s => s.Metadata).Include(q => q.FallbackProfile)
            .OrderBy(s => s.ServiceType).ThenBy(s => s.Name)
            .ToListAsync();
    }

    public async Task<MediaServer?> GetServerByIdAsync(int id)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.MediaServers.FindAsync(id);
    }

    public async Task CreateServerAsync(MediaServer server)
    {
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

        server.Metadata = await GetSystemStatusAsync(server);
    }

    public async Task UpdateServerAsync(MediaServer server)
    {
        await using var db = await dbFactory.CreateDbContextAsync();

        if (server.IsDefault)
            await ClearDefaultAsync(db, server.ServiceType, server.Id);

        var tracked = await db.MediaServers.FindAsync(server.Id) ??
                      throw new MediaServerException($"{server.Name}: Server not found.");

        db.Entry(tracked).CurrentValues.SetValues(server);
        await db.SaveChangesAsync();

        await GetSystemStatusAsync(tracked);
        server.Metadata = await GetSystemStatusAsync(server);

        logger.LogInformation("Updated server: {name}", server.Name);
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
    }

    #endregion

    #region Connection

    public async Task<(bool Success, long ElapsedMilliseconds, string? ReasonPhrase)> TestConnectionAsync(
        MediaServer server)
    {
        logger.LogInformation("Testing connection to {name}", server.Name);
        var sw = Stopwatch.StartNew();

        try
        {
            var client = CreateClient(10);

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
                await UpdateServerStatsAsync(server.Id, s =>
                {
                    s.IsOnline = server.IsOnline;
                    s.LastPingedAt = server.LastPingedAt;
                    s.LastPingMs = server.LastPingMs;
                    s.IsAuthenticated = server.IsAuthenticated;
                    s.LastAuthenticatedAt = server.LastAuthenticatedAt;
                });

            return (true, sw.ElapsedMilliseconds, response.ReasonPhrase);
        }
        catch (ConnectionException ex)
        {
            server.IsOnline = false;
            server.LastPingedAt = DateTime.UtcNow;
            server.LastErrorAt = DateTime.UtcNow;
            server.LastError = ex.Message;

            if (server.Id != 0)
                await UpdateServerStatsAsync(server.Id, s =>
                {
                    s.IsOnline = server.IsOnline;
                    s.LastPingedAt = server.LastPingedAt;
                    s.LastErrorAt = server.LastErrorAt;
                    s.LastError = server.LastError;
                });

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

    private static async Task<HttpResponseMessage> TestArrAsync(HttpClient client, MediaServer server)
    {
        if (string.IsNullOrWhiteSpace(server.ApiKey))
            throw new MediaServerException($"{server.Name}: API key is required.");

        var url = $"{server.BaseUrl}/api/v3/system/status?apikey={server.ApiKey}";
        var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);

        return !response.IsSuccessStatusCode
            ? throw new ApiException(server.Name, "/api/v3/system/status", (int)response.StatusCode)
            : response;
    }

    public async Task<ServerMetadata> GetSystemStatusAsync(MediaServer server)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var mediaServer = await db.MediaServers.FindAsync(server.Id) ??
                          throw new MediaServerException($"{server.Name}: Server not found.");

        if (string.IsNullOrWhiteSpace(mediaServer.ApiKey))
            throw new MediaServerException($"{server.Name}: API key is required.");

        try
        {
            JsonSerializerOptions options = new() { PropertyNameCaseInsensitive = true };
            var client = CreateClient(10);
            var url = $"{mediaServer.BaseUrl}/api/v3/system/status?apikey={mediaServer.ApiKey}";

            using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);

            if (!response.IsSuccessStatusCode)
                throw new ApiException(server.Name, "/api/v3/system/status", (int)response.StatusCode);

            await using var json = await response.Content.ReadAsStreamAsync();
            var metadata = await JsonSerializer.DeserializeAsync<ServerMetadata>(json, options) ??
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
    }

    #endregion

    #region Queue

    public async Task<List<DownloadItem>> GetQueueAsync(MediaServer server, bool forceRefresh = false)
    {
        if (server.ServiceType is not (ServiceType.Radarr or ServiceType.Sonarr))
            throw new MediaServerException($"{server.Name}: Queue fetching not supported for {server.ServiceType}.");

        try
        {
            await using var db = await dbFactory.CreateDbContextAsync();


            if (!forceRefresh)
            {
                var lastFetch = await db.MediaServers
                    .Where(s => s.Id == server.Id)
                    .Select(s => s.LastQueueFetchAt)
                    .FirstOrDefaultAsync();

                if (lastFetch is { } fetchedAt && DateTime.UtcNow - fetchedAt < QueueCacheTtl)
                {
                    var cached = await db.DownloadQueue.Where(q => q.ServerId == server.Id).ToListAsync();
                    await EnsureQueueParentMediaSummariesAsync(db, server.Id, cached, DateTime.UtcNow);
                    await db.SaveChangesAsync();
                    server.Queue = cached;
                    return cached;
                }
            }

            List<DownloadItem> items = [];
            var page = 1;
            const int pageSize = 100;

            while (true)
            {
                var (batch, total) = await FetchQueuePageAsync(server, page, pageSize);

                foreach (var item in batch)
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

            await EnsureQueueParentMediaSummariesAsync(db, server.Id, items, DateTime.UtcNow);
            await db.SaveChangesAsync();

            await UpdateServerStatsAsync(server.Id,
                s =>
                {
                    s.LastQueueFetchAt = DateTime.UtcNow;
                    s.LastQueueCount = items.Count;
                });

            logger.LogInformation("Fetched {count} queue items for {name}", items.Count, server.Name);

            switch (server.ServiceType)
            {
                case ServiceType.Radarr:
                {
                    foreach (var movieId in items.Select(i => i.MovieId).Where(id => id != 0).Distinct())
                        await GetMovieByIdAsync(server, movieId);

                    break;
                }

                case ServiceType.Sonarr:
                {
                    foreach (var seriesId in items.Select(i => i.SeriesId).Where(id => id != 0).Distinct())
                        await GetSeriesByIdAsync(server, seriesId);

                    foreach (var episodeId in items.Select(i => i.EpisodeId).Where(id => id != 0).Distinct())
                        await GetEpisodeByIdAsync(server, episodeId);

                    break;
                }

                default:
                    throw new ArgumentOutOfRangeException();
            }

            return items;
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
            throw new MediaServerException($"{server.Name}: Unexpected error fetching queue.", ex);
        }
    }

    private async Task<(List<DownloadItem> Items, int TotalRecords)> FetchQueuePageAsync(MediaServer server, int page,
        int pageSize)
    {
        if (string.IsNullOrWhiteSpace(server.ApiKey))
            throw new MediaServerException($"{server.Name}: API key is required.");

        try
        {
            JsonSerializerOptions options = new() { PropertyNameCaseInsensitive = true };
            var client = CreateClient(15);
            var url =
                $"{server.BaseUrl}/api/v3/queue?apikey={server.ApiKey}&page={page}&pageSize={pageSize}&includeUnknownMovieItems=true&includeMovie=true&includeSeries=true";

            using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);

            if (!response.IsSuccessStatusCode)
                throw new ApiException(server.Name, "/api/v3/queue", (int)response.StatusCode);

            await using var stream = await response.Content.ReadAsStreamAsync();
            var queue = await JsonSerializer.DeserializeAsync<QueueResponse>(stream, options);


            var items = queue
                            ?.Records
                            .Select(x => new DownloadItem
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
                                CustomFormatScore = x.CustomFormatScore,
                                MovieId = x.MovieId,
                                SeriesId = x.SeriesId,
                                EpisodeId = x.EpisodeId,
                                ServerId = server.Id,
                                State = MapQueueState(x.TrackedDownloadState),
                                QueueStatus = MapQueueStatus(x.Status, x.TrackedDownloadStatus),
                                LanguagesJson = SerializeJsonElement(x.Languages, "[]"),
                                QualityJson = SerializeJsonElement(x.Quality, "null"),
                                CustomFormatsJson = SerializeJsonElement(x.CustomFormats, "[]"),
                                StatusMessagesJson = SerializeJsonElement(x.StatusMessages, "[]")
                            })
                            .ToList() ??
                        [];

            return (items, queue?.TotalRecords ?? 0);
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

    public async Task RemoveFromQueueAsync(MediaServer server, int queueId, bool blocklist = true,
        bool removeFromClient = true)
    {
        if (string.IsNullOrWhiteSpace(server.ApiKey))
            throw new MediaServerException($"{server.Name}: API key is required.");

        try
        {
            var client = CreateClient(15);
            var url =
                $"{server.BaseUrl}/api/v3/queue/{queueId}?apikey={server.ApiKey}&removeFromClient={removeFromClient.ToString().ToLowerInvariant()}&blocklist={blocklist.ToString().ToLowerInvariant()}&skipRedownload=false";

            using var response = await client.DeleteAsync(url);

            if (!response.IsSuccessStatusCode)
                throw new ApiException(server.Name, $"/api/v3/queue/{queueId}", (int)response.StatusCode);

            logger.LogInformation("Removed queue item {id} from {name}", queueId, server.Name);
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
            throw new MediaServerException($"{server.Name}: Unexpected error removing queue item {queueId}.", ex);
        }
    }

    public async Task TriggerSearchAsync(MediaServer server, string commandName, string paramName, object value)
    {
        if (string.IsNullOrWhiteSpace(server.ApiKey))
            throw new MediaServerException($"{server.Name}: API key is required.");

        try
        {
            var client = CreateClient(15);
            var url = $"{server.BaseUrl}/api/v3/command?apikey={server.ApiKey}";
            var payload = JsonSerializer.Serialize(new Dictionary<string, object>
            {
                ["name"] = commandName,
                [paramName] = value
            });

            using var content = new StringContent(payload, Encoding.UTF8, "application/json");
            using var response = await client.PostAsync(url, content);

            if (!response.IsSuccessStatusCode)
                throw new ApiException(server.Name, "/api/v3/command", (int)response.StatusCode);

            logger.LogInformation("Triggered {cmd} on {name}", commandName, server.Name);
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
            throw new MediaServerException($"{server.Name}: Error triggering {commandName}.", ex);
        }
    }

    public Task TriggerMovieSearchAsync(MediaServer server, int movieId)
    {
        return TriggerSearchAsync(server, "MoviesSearch", "movieIds", new[] { movieId });
    }

    public Task TriggerSeriesSearchAsync(MediaServer server, int seriesId)
    {
        return TriggerSearchAsync(server, "SeriesSearch", "seriesId", seriesId);
    }

    public Task TriggerEpisodeSearchAsync(MediaServer server, int episodeId)
    {
        return TriggerSearchAsync(server, "EpisodeSearch", "episodeIds", new[] { episodeId });
    }

    public async Task<ActiveSearches> GetActiveSearchesAsync(MediaServer server)
    {
        var result = new ActiveSearches();

        if (string.IsNullOrWhiteSpace(server.ApiKey))
            throw new MediaServerException($"{server.Name}: API key is required.");

        try
        {
            JsonSerializerOptions options = new() { PropertyNameCaseInsensitive = true };
            var client = CreateClient(10);
            var url = $"{server.BaseUrl}/api/v3/command?apikey={server.ApiKey}";

            using var response = await client.GetAsync(url);

            if (!response.IsSuccessStatusCode)
                throw new ApiException(server.Name, "/api/v3/command", (int)response.StatusCode);

            var json = await response.Content.ReadAsStringAsync();
            var commands = JsonSerializer.Deserialize<List<CommandRecord>>(json, options) ?? [];

            foreach (var cmd in commands)
            {
                if (cmd.Status is "completed" or "failed" or "aborted" or "cancelled") continue;

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
            throw new MediaServerException($"{server.Name}: Error fetching commands.", ex);
        }
    }

    #endregion

    #region Helpers

    private static readonly JsonSerializerOptions CompactJsonOptions = new()
    {
        WriteIndented = false
    };

    private static string SerializeJsonElement(JsonElement value, string fallback)
    {
        return value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null
            ? fallback
            : JsonSerializer.Serialize(value, CompactJsonOptions);
    }

    private HttpClient CreateClient(int timeoutSeconds)
    {
        var client = httpClientFactory.CreateClient("MediaServerTest");
        client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
        return client;
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

    private static QueueStatus MapQueueStatus(string? status, string? trackedStatus)
    {
        if (status?.Equals("warning", StringComparison.OrdinalIgnoreCase) == true)
            return QueueStatus.Warning;

        if (trackedStatus?.Equals("warning", StringComparison.OrdinalIgnoreCase) == true)
            return QueueStatus.Warning;

        if (status?.Equals("completed", StringComparison.OrdinalIgnoreCase) == true)
            return QueueStatus.Completed;

        return QueueStatus.Ok;
    }

    private async Task EnsureMediaHealthSummaryAsync<TMedia>(AppDbContext db, TMedia media, DateTime now)
        where TMedia : MediaItem
    {
        await EnsureMediaHealthSummariesAsync(db, [media], now);
    }

    private async Task EnsureMediaHealthSummariesAsync<TMedia>(AppDbContext db, IEnumerable<TMedia> mediaItems,
        DateTime now) where TMedia : MediaItem
    {
        var items = mediaItems
            .Cast<MediaItem>()
            .Where(e => e.Id > 0)
            .Where(e => GetTrackedMediaType(e) is not TrackedMediaType.Unknown)
            .GroupBy(e => new
            {
                Type = GetTrackedMediaType(e),
                e.Id
            })
            .Select(e => e.First())
            .ToList();

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
            MediaType = mediaType,
            CreatedAt = now,
            UpdatedAt = now,
            CurrentState = HealthState.Unknown
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