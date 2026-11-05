using System.Text.Json;
using System.Text.Json.Nodes;

namespace Trackarr.MockServer;

public enum MockMode
{
    Radarr,
    Sonarr
}

public record MockStatus(
    int RadarrPort,
    int SonarrPort,
    int Movies,
    int Series,
    int Episodes,
    int MovieQueueItems,
    int SeriesQueueItems,
    int MissingMovies,
    int MissingEpisodes,
    int ProfileChangedItems,
    int PendingSearches);

public class FakeQueueItem
{
    public int Id { get; set; }
    public int MovieId { get; set; }
    public int SeriesId { get; set; }
    public int EpisodeId { get; set; }
    public string Title { get; set; } = "";
    public long Size { get; set; }
    public long SizeLeft { get; set; }
    public string Status { get; set; } = "downloading";
    public string TrackedDownloadStatus { get; set; } = "ok";
    public string TrackedDownloadState { get; set; } = "downloading";
    public string? ErrorMessage { get; set; }
    public string DownloadClient { get; set; } = "qBittorrent";
    public string Protocol { get; set; } = "torrent";
    public string Indexer { get; set; } = "Prowlarr";
    public string DownloadId { get; set; } = "";
    public DateTime Added { get; set; }
    public string QualityName { get; set; } = "Bluray-1080p";
    public bool IsMovie { get; set; }

    // Momentum: 0.0 = dead slow, 1.0 = screaming fast. Drives progress per tick.
    public double Momentum { get; set; }

    // True = stuck on "Downloading metadata" forever
    public bool StuckOnMetadata { get; set; }

    // Ticks spent in metadata phase before real download begins
    public int MetadataTicks { get; set; }

    // How many ticks before metadata resolves (0 = already resolved)
    public int MetadataTicksNeeded { get; set; }

    // Ticks spent in warning state (longer = harder to recover)
    public int WarnTicks { get; set; }
}

public class MockCommand
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public DateTime QueuedAt { get; set; }
    public JsonElement Body { get; set; }
    public List<int> TargetIds { get; set; } = [];
    public bool IsMovie { get; set; }
    public bool WillSucceed { get; set; }
    public bool Processed { get; set; }

    public string Status
    {
        get
        {
            var elapsed = (DateTime.UtcNow - QueuedAt).TotalSeconds;
            return elapsed < 2 ? "queued" : elapsed < 6 ? "started" : "completed";
        }
    }
}

public class MockDataStore(ILogger<MockDataStore> logger)
{
    private const double BaseSearchSuccessRate = 0.85;
    private const double ProfileChangedSearchSuccessRate = 0.95;

    private static readonly string[] DownloadClients = ["qBittorrent", "SABnzbd", "NZBGet", "Transmission", "Deluge"];
    private static readonly string[] Indexers = ["NZBgeek", "DrunkenSlug", "Jackett", "Prowlarr", "1337x"];
    private static readonly string[] Protocols = ["torrent", "usenet"];

    private static readonly string[] Qualities =
        ["Bluray-1080p", "WEBDL-1080p", "HDTV-720p", "Bluray-2160p", "WEBDL-2160p", "WEBDL-720p"];

    private static readonly string[] Groups =
        ["SPARKS", "FLUX", "NTb", "RARBG", "YTS", "FGT", "EZTV", "LOL", "GLHF", "NTG"];

    private readonly List<MockCommand> _commands = [];
    private readonly Dictionary<int, string> _episodes = [];
    private readonly Dictionary<int, List<int>> _episodesBySeries = [];
    private readonly object _lock = new();
    private readonly List<int> _missingEpisodeIds = [];
    private readonly List<int> _missingMovieIds = [];

    private readonly List<FakeQueueItem> _movieQueue = [];
    private readonly List<object> _healthMessages = [];

    private readonly Dictionary<int, string> _movies = [];
    private readonly HashSet<int> _profileChangedItems = [];
    private readonly Random _rng = new();
    private readonly Dictionary<int, string> _series = [];
    private readonly List<FakeQueueItem> _seriesQueue = [];
    private int _nextCommandId = 1;

    private int _nextQueueId = 1000;

    private string _radarrProfiles = "[]";
    private string _sonarrProfiles = "[]";
    public int RadarrPort { get; private set; }
    public int SonarrPort { get; private set; }

    public MockMode GetMode(HttpContext ctx)
    {
        return ctx.Connection.LocalPort == SonarrPort ? MockMode.Sonarr : MockMode.Radarr;
    }

    // ── Seeding ──

    public async Task SeedAsync(IHttpClientFactory httpFactory, IConfiguration config)
    {
        RadarrPort = config.GetValue("MockServer:RadarrPort", 9877);
        SonarrPort = config.GetValue("MockServer:SonarrPort", 9878);

        var radarrUrl = config["MockServer:RadarrSourceUrl"];
        var radarrKey = config["MockServer:RadarrSourceApiKey"];
        var sonarrUrl = config["MockServer:SonarrSourceUrl"];
        var sonarrKey = config["MockServer:SonarrSourceApiKey"];

        if (!string.IsNullOrWhiteSpace(radarrUrl))
            await SeedRadarr(httpFactory, radarrUrl.TrimEnd('/'), radarrKey);
        else
            logger.LogWarning("No RadarrSourceUrl configured — Radarr endpoints will return empty data");

        if (!string.IsNullOrWhiteSpace(sonarrUrl))
            await SeedSonarr(httpFactory, sonarrUrl.TrimEnd('/'), sonarrKey);
        else
            logger.LogWarning("No SonarrSourceUrl configured — Sonarr endpoints will return empty data");

        GenerateFakeData(config);

        logger.LogInformation("=== Mock Server Ready ===");
        logger.LogInformation("  Radarr port:     {RadarrPort}", RadarrPort);
        logger.LogInformation("  Sonarr port:     {SonarrPort}", SonarrPort);
        logger.LogInformation("  Movies loaded:   {MovieCount}", _movies.Count);
        logger.LogInformation("  Series loaded:   {SeriesCount}", _series.Count);
        logger.LogInformation("  Episodes loaded: {EpisodeCount}", _episodes.Count);
        logger.LogInformation("  Movie queue:     {Count} items", _movieQueue.Count);
        logger.LogInformation("  Series queue:    {Count} items", _seriesQueue.Count);
        logger.LogInformation("  Missing movies:  {Count}", _missingMovieIds.Count);
        logger.LogInformation("  Missing episodes:{Count}", _missingEpisodeIds.Count);
    }

    private async Task SeedRadarr(IHttpClientFactory httpFactory, string url, string? apiKey)
    {
        logger.LogInformation("Seeding from Radarr at {Url}", url);
        var client = CreateClient(httpFactory, apiKey);
        try
        {
            var moviesJson = await client.GetStringAsync($"{url}/api/v3/movie");
            foreach (var movie in JsonSerializer.Deserialize<JsonArray>(moviesJson) ?? [])
            {
                if (movie is null) continue;
                var id = movie["id"]?.GetValue<int>() ?? 0;
                if (id > 0) _movies[id] = movie.ToJsonString();
            }

            _radarrProfiles = await client.GetStringAsync($"{url}/api/v3/qualityprofile");
            var profileCount = JsonSerializer.Deserialize<JsonArray>(_radarrProfiles)?.Count ?? 0;
            logger.LogInformation("  {Movies} movies, {Profiles} quality profiles", _movies.Count, profileCount);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to seed from Radarr at {Url}", url);
        }
    }

    private async Task SeedSonarr(IHttpClientFactory httpFactory, string url, string? apiKey)
    {
        logger.LogInformation("Seeding from Sonarr at {Url}", url);
        var client = CreateClient(httpFactory, apiKey);
        try
        {
            var seriesJson = await client.GetStringAsync($"{url}/api/v3/series");
            foreach (var series in JsonSerializer.Deserialize<JsonArray>(seriesJson) ?? [])
            {
                if (series is null) continue;
                var id = series["id"]?.GetValue<int>() ?? 0;
                if (id > 0) _series[id] = series.ToJsonString();
            }

            var semaphore = new SemaphoreSlim(5);
            var tasks = _series.Keys.Select(async seriesId =>
            {
                await semaphore.WaitAsync();
                try
                {
                    var epsJson = await client.GetStringAsync($"{url}/api/v3/episode?seriesId={seriesId}");
                    foreach (var ep in JsonSerializer.Deserialize<JsonArray>(epsJson) ?? [])
                    {
                        if (ep is null) continue;
                        var epId = ep["id"]?.GetValue<int>() ?? 0;
                        if (epId <= 0) continue;
                        lock (_lock)
                        {
                            _episodes[epId] = ep.ToJsonString();
                            if (!_episodesBySeries.ContainsKey(seriesId)) _episodesBySeries[seriesId] = [];
                            _episodesBySeries[seriesId].Add(epId);
                        }
                    }
                }
                catch (Exception ex)
                {
                    logger.LogWarning("  Failed episodes for series {Id}: {Msg}", seriesId, ex.Message);
                }
                finally
                {
                    semaphore.Release();
                }
            });
            await Task.WhenAll(tasks);
            _sonarrProfiles = await client.GetStringAsync($"{url}/api/v3/qualityprofile");
            var profileCount = JsonSerializer.Deserialize<JsonArray>(_sonarrProfiles)?.Count ?? 0;
            logger.LogInformation("  {Series} series, {Episodes} episodes, {Profiles} quality profiles", _series.Count,
                _episodes.Count, profileCount);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to seed from Sonarr at {Url}", url);
        }
    }

    // ── Fake Data Generation ──

    private void GenerateFakeData(IConfiguration config)
    {
        var missingCount = config.GetValue("MockServer:FakeMissingCount", 10);

        var movieIds = _movies.Keys.ToList();

        // Season 0 (specials) is excluded so generated "missing" episodes look like normal
        // gaps in a season instead of always landing on the specials nobody searches for.
        var episodeIds = _episodes.Where(kv => GetJsonIntProp(kv.Value, "seasonNumber") != 0).Select(kv => kv.Key)
            .ToList();

        if (movieIds.Count > 0)
        {
            _missingMovieIds.AddRange(movieIds.OrderBy(_ => _rng.Next()).Take(Math.Min(missingCount, movieIds.Count)));
            logger.LogInformation("Generated {Count} missing movies", _missingMovieIds.Count);
        }

        if (episodeIds.Count > 0)
        {
            _missingEpisodeIds.AddRange(episodeIds.OrderBy(_ => _rng.Next())
                .Take(Math.Min(missingCount, episodeIds.Count)));
            logger.LogInformation("Generated {Count} missing episodes", _missingEpisodeIds.Count);
        }

        // Queue starts empty — Trackarr is responsible for pushing searches against the missing
        // list above, and a successful search (see ProcessCompletedSearches) is what populates it.
        logger.LogInformation("Queue starts empty; populated only by Trackarr-initiated searches");
    }

    private FakeQueueItem? CreateQueueItemForId(bool isMovie, int targetId)
    {
        if (isMovie && !_movies.ContainsKey(targetId)) return null;
        if (!isMovie && !_episodes.ContainsKey(targetId)) return null;

        var quality = Qualities[_rng.Next(Qualities.Length)];
        var group = Groups[_rng.Next(Groups.Length)];
        var stuckOnMeta = _rng.NextDouble() < 0.08;
        var item = new FakeQueueItem
        {
            Id = _nextQueueId++,
            IsMovie = isMovie,
            Size = _rng.NextInt64(700_000_000, 25_000_000_000),
            DownloadClient = DownloadClients[_rng.Next(DownloadClients.Length)],
            Protocol = Protocols[_rng.Next(Protocols.Length)],
            Indexer = Indexers[_rng.Next(Indexers.Length)],
            DownloadId = Guid.NewGuid().ToString("N"),
            Added = DateTime.UtcNow,
            QualityName = quality,
            Momentum = _rng.NextDouble() * 0.8 + 0.05,
            StuckOnMetadata = stuckOnMeta,
            MetadataTicksNeeded = stuckOnMeta ? int.MaxValue : _rng.Next(3, 15),
            Status = "downloading",
            ErrorMessage = stuckOnMeta ? "qBittorrent is downloading metadata" : null
        };
        item.SizeLeft = item.Size;

        if (isMovie)
        {
            item.MovieId = targetId;
            var title = GetJsonStringProp(_movies[targetId], "title") ?? "Unknown";
            var year = GetJsonIntProp(_movies[targetId], "year");
            item.Title = $"{title.Replace(' ', '.')}.{year}.{quality.Replace('-', '.')}.x264-{group}";
        }
        else
        {
            item.EpisodeId = targetId;
            item.SeriesId = GetJsonIntProp(_episodes[targetId], "seriesId");
            var seriesTitle = _series.TryGetValue(item.SeriesId, out var sj)
                ? GetJsonStringProp(sj, "title") ?? "Unknown"
                : "Unknown";
            var sNum = GetJsonIntProp(_episodes[targetId], "seasonNumber");
            var eNum = GetJsonIntProp(_episodes[targetId], "episodeNumber");
            item.Title =
                $"{seriesTitle.Replace(' ', '.')}.S{sNum:D2}E{eNum:D2}.{quality.Replace('-', '.')}.x264-{group}";
        }

        return item;
    }

    // ── Queue Tick (State Machine) ──

    public void TickQueue()
    {
        lock (_lock)
        {
            ProcessCompletedSearches();
            TickList(_movieQueue, "Movie");
            TickList(_seriesQueue, "Series");
        }
    }

    private void TickList(List<FakeQueueItem> queue, string label)
    {
        int completed = 0, imported = 0, warned = 0, failed = 0, resumed = 0;
        var toRemove = new List<int>();

        foreach (var item in queue)
        {
            var roll = _rng.NextDouble();

            switch (item.TrackedDownloadState)
            {
                case "downloading":
                    // Metadata phase — hasn't started real download yet
                    if (item.MetadataTicksNeeded > item.MetadataTicks)
                    {
                        item.MetadataTicks++;
                        if (item.StuckOnMetadata)
                        {
                            item.ErrorMessage = "qBittorrent is downloading metadata";
                            if (roll < 0.005)
                            {
                                item.TrackedDownloadState = "failedPending";
                                item.TrackedDownloadStatus = "error";
                                item.ErrorMessage = "Download failed: unable to retrieve metadata";
                                failed++;
                            }
                        }

                        break;
                    }

                    item.ErrorMessage = null;

                    // Warning state — might recover
                    if (item.Status == "warning")
                    {
                        item.WarnTicks++;
                        var recoverChance = Math.Max(0.05, 0.35 - item.WarnTicks * 0.02);
                        if (roll < recoverChance)
                        {
                            item.Status = "downloading";
                            item.TrackedDownloadStatus = "ok";
                            item.ErrorMessage = null;
                            item.WarnTicks = 0;
                            item.Momentum = Math.Max(0.05, item.Momentum * 0.5);
                            resumed++;
                        }
                        else if (roll < recoverChance + 0.02)
                        {
                            item.TrackedDownloadState = "failedPending";
                            item.TrackedDownloadStatus = "error";
                            item.ErrorMessage = "Download failed: tracker returned an error";
                            failed++;
                        }

                        break;
                    }

                    // Progress — momentum determines how much we advance
                    var progressPercent = item.Momentum * (0.5 + _rng.NextDouble() * 1.5);
                    var bytesThisTick = (long)(item.Size * progressPercent / 100.0);
                    bytesThisTick = Math.Max(bytesThisTick, 100_000);
                    item.SizeLeft = Math.Max(0, item.SizeLeft - bytesThisTick);

                    // Nudge momentum — fast stays fast, slow stays slow, with drift
                    item.Momentum += (_rng.NextDouble() - 0.48) * 0.08;
                    item.Momentum = Math.Clamp(item.Momentum, 0.01, 1.0);

                    if (item.SizeLeft == 0)
                    {
                        item.TrackedDownloadState = "importPending";
                        item.Status = "completed";
                        item.TrackedDownloadStatus = "ok";
                        completed++;
                        break;
                    }

                    // Chance to stall — lower momentum = higher chance
                    var stallChance = 0.02 + (1.0 - item.Momentum) * 0.03;
                    if (roll < stallChance)
                    {
                        item.Status = "warning";
                        item.TrackedDownloadStatus = "warning";
                        item.WarnTicks = 0;
                        item.ErrorMessage = roll < stallChance * 0.5
                            ? "No connections were made because no connectable peers were found"
                            : "Download stalled - no progress in the last 30 minutes";
                        warned++;
                    }

                    break;

                case "importPending":
                    if (roll < 0.15)
                    {
                        toRemove.Add(item.Id);
                        imported++;
                        if (item.IsMovie) _missingMovieIds.Remove(item.MovieId);
                        else _missingEpisodeIds.Remove(item.EpisodeId);
                    }

                    break;

                case "failedPending":
                    break;
            }
        }

        queue.RemoveAll(q => toRemove.Contains(q.Id));

        if (completed + imported + warned + failed + resumed > 0)
        {
            logger.LogInformation(
                "[{Label}] +{Done} done, +{Imp} imported, +{Warn} warned, +{Fail} failed, +{Res} resumed ({Total} in queue)",
                label, completed, imported, warned, failed, resumed, queue.Count);
        }
    }

    // ── Search Command Handling ──

    public object CreateCommand(string name, JsonElement body, MockMode mode)
    {
        lock (_lock)
        {
            var cmd = new MockCommand { Id = _nextCommandId++, Name = name, QueuedAt = DateTime.UtcNow, Body = body };

            if (name is "MoviesSearch" && body.TryGetProperty("movieIds", out var mIds))
            {
                cmd.IsMovie = true;
                cmd.TargetIds = mIds.EnumerateArray().Select(e => e.GetInt32()).ToList();
                cmd.WillSucceed = cmd.TargetIds.Any(id => _rng.NextDouble() < GetSearchRate(id));
                logger.LogInformation("[{Mode}] Search: {Name} movieIds=[{Ids}] → will {Result}", mode, name,
                    string.Join(",", cmd.TargetIds), cmd.WillSucceed ? "find" : "miss");
            }
            else if (name is "EpisodeSearch" && body.TryGetProperty("episodeIds", out var eIds))
            {
                cmd.IsMovie = false;
                cmd.TargetIds = eIds.EnumerateArray().Select(e => e.GetInt32()).ToList();
                cmd.WillSucceed = cmd.TargetIds.Any(id => _rng.NextDouble() < GetSearchRate(id));
                logger.LogInformation("[{Mode}] Search: {Name} episodeIds=[{Ids}] → will {Result}", mode, name,
                    string.Join(",", cmd.TargetIds), cmd.WillSucceed ? "find" : "miss");
            }
            else if (name is "SeriesSearch" && body.TryGetProperty("seriesId", out var sId))
            {
                cmd.IsMovie = false;
                var seriesId = sId.GetInt32();
                cmd.TargetIds = _episodesBySeries.TryGetValue(seriesId, out var eps)
                    ? eps.Where(id => _missingEpisodeIds.Contains(id)).ToList()
                    : [];
                cmd.WillSucceed = cmd.TargetIds.Count > 0 && _rng.NextDouble() < GetSearchRate(seriesId);
                logger.LogInformation("[{Mode}] Search: {Name} seriesId={Id} ({EpCount} missing eps) → will {Result}",
                    mode, name, seriesId, cmd.TargetIds.Count, cmd.WillSucceed ? "find" : "miss");
            }
            else
            {
                logger.LogInformation("[{Mode}] Command: {Name}", mode, name);
            }

            _commands.Add(cmd);
            if (_commands.Count > 100) _commands.RemoveRange(0, _commands.Count - 100);

            return SerializeCommand(cmd);
        }
    }

    private double GetSearchRate(int itemId)
    {
        return _profileChangedItems.Contains(itemId) ? ProfileChangedSearchSuccessRate : BaseSearchSuccessRate;
    }

    private void ProcessCompletedSearches()
    {
        foreach (var cmd in _commands)
        {
            if (cmd.Processed || cmd.Status != "completed" || !cmd.WillSucceed || cmd.TargetIds.Count == 0)
                continue;

            cmd.Processed = true;

            foreach (var targetId in cmd.TargetIds)
            {
                if (cmd.IsMovie)
                {
                    if (!_missingMovieIds.Contains(targetId)) continue;
                    if (_movieQueue.Any(q => q.MovieId == targetId)) continue;
                    var item = CreateQueueItemForId(true, targetId);
                    if (item is null) continue;
                    _movieQueue.Add(item);
                    logger.LogInformation("[Search] Found movie download: {Title}", item.Title);
                }
                else
                {
                    if (!_missingEpisodeIds.Contains(targetId)) continue;
                    if (_seriesQueue.Any(q => q.EpisodeId == targetId)) continue;
                    var item = CreateQueueItemForId(false, targetId);
                    if (item is null) continue;
                    _seriesQueue.Add(item);
                    logger.LogInformation("[Search] Found episode download: {Title}", item.Title);
                }
            }
        }
    }

    public List<object> GetCommands()
    {
        lock (_lock)
        {
            return _commands.Select(SerializeCommand).ToList();
        }
    }

    private static object SerializeCommand(MockCommand cmd)
    {
        return new
        {
            cmd.Id,
            cmd.Name,
            CommandName = cmd.Name switch
            {
                "MoviesSearch" => "Movies Search",
                "EpisodeSearch" => "Episode Search",
                "SeriesSearch" => "Series Search",
                _ => cmd.Name
            },
            cmd.Status,
            Queued = cmd.QueuedAt,
            Started = cmd.Status is "started" or "completed" ? cmd.QueuedAt.AddSeconds(2) : (DateTime?)null,
            Ended = cmd.Status is "completed" ? cmd.QueuedAt.AddSeconds(6) : (DateTime?)null,
            cmd.Body,
            Priority = "normal",
            Trigger = "manual"
        };
    }

    // ── Profile Change Tracking ──

    public IResult UpdateMovie(int id, JsonElement body)
    {
        if (!_movies.TryGetValue(id, out var stored)) return Results.NotFound();

        var oldProfileId = GetJsonIntProp(stored, "qualityProfileId");
        var newProfileId = body.TryGetProperty("qualityProfileId", out var qp) ? qp.GetInt32() : oldProfileId;

        _movies[id] = body.GetRawText();

        if (newProfileId != oldProfileId)
        {
            _profileChangedItems.Add(id);
            logger.LogInformation("[Profile] Movie {Id} profile changed {Old} → {New} (search rate now {Rate}%)", id,
                oldProfileId, newProfileId, (int)(ProfileChangedSearchSuccessRate * 100));
        }

        return Results.Content(_movies[id], "application/json");
    }

    // ── Endpoint Methods ──

    public object GetSystemStatus(MockMode mode)
    {
        var appName = mode == MockMode.Sonarr ? "Sonarr" : "Radarr";
        return new
        {
            AppName = appName,
            InstanceName = $"Mock{appName}",
            Version = "4.0.0.0",
            BuildTime = DateTime.UtcNow.AddDays(-30),
            IsDebug = false,
            IsProduction = true,
            IsAdmin = false,
            IsUserInteractive = true,
            StartupPath = "/app",
            AppData = "/config",
            OsName = "MockOS",
            OsVersion = "1.0",
            IsNetCore = true,
            IsLinux = false,
            IsOsx = false,
            IsWindows = true,
            IsDocker = false,
            Mode = "console",
            Branch = "main",
            DatabaseType = "sqlite",
            DatabaseVersion = "3.45.0",
            Authentication = "none",
            MigrationVersion = 1,
            UrlBase = "",
            RuntimeVersion = "10.0.0",
            RuntimeName = "net10.0",
            StartTime = DateTime.UtcNow.AddHours(-1),
            PackageVersion = "4.0.0.0",
            PackageAuthor = "MockServer",
            PackageUpdateMechanism = "builtIn",
            PackageUpdateMechanismMessage = ""
        };
    }

    public IResult GetProfiles(MockMode mode)
    {
        var json = mode == MockMode.Sonarr ? _sonarrProfiles : _radarrProfiles;
        return Results.Content(json, "application/json");
    }

    public List<object> GetHealth()
    {
        lock (_lock)
        {
            return _healthMessages.ToList();
        }
    }

    public void SetHealthWarning(bool enabled)
    {
        lock (_lock)
        {
            _healthMessages.Clear();

            if (enabled)
            {
                _healthMessages.Add(new
                {
                    source = "DownloadClientCheck",
                    type = "warning",
                    message = "Unable to communicate with qBittorrent",
                    wikiUrl = "https://wiki.servarr.com/radarr/system#health-checks"
                });
            }
        }

        logger.LogInformation("Mock health warning {State}", enabled ? "enabled" : "cleared");
    }

    public IResult GetMissingMovies(int page, int pageSize)
    {
        lock (_lock)
        {
            var skip = (page - 1) * pageSize;
            var ids = _missingMovieIds.Skip(skip).Take(pageSize).ToList();
            var records = new JsonArray();
            foreach (var movieId in ids)
            {
                if (!_movies.TryGetValue(movieId, out var movieJson)) continue;
                var node = JsonNode.Parse(movieJson)!.AsObject();
                node["hasFile"] = false;
                node["monitored"] = true;
                records.Add(node);
            }

            var response = new JsonObject
            {
                ["page"] = page,
                ["pageSize"] = pageSize,
                ["totalRecords"] = _missingMovieIds.Count,
                ["records"] = records
            };
            return Results.Content(response.ToJsonString(), "application/json");
        }
    }

    public IResult GetMissingEpisodes(int page, int pageSize)
    {
        lock (_lock)
        {
            var skip = (page - 1) * pageSize;
            var ids = _missingEpisodeIds.Skip(skip).Take(pageSize).ToList();
            var records = new JsonArray();
            foreach (var epId in ids)
            {
                if (!_episodes.TryGetValue(epId, out var epJson)) continue;
                var node = JsonNode.Parse(epJson)!.AsObject();
                node["hasFile"] = false;
                node["monitored"] = true;
                var seriesId = node["seriesId"]?.GetValue<int>() ?? 0;
                if (_series.TryGetValue(seriesId, out var seriesJson))
                    node["series"] = JsonNode.Parse(seriesJson);
                node.TryAdd("endTime", DateTime.UtcNow.AddMinutes(-_rng.Next(60, 525600)));
                node.TryAdd("grabDate", DateTime.UtcNow.AddDays(-_rng.Next(1, 30)));
                records.Add(node);
            }

            var response = new JsonObject
            {
                ["page"] = page,
                ["pageSize"] = pageSize,
                ["sortKey"] = "airDateUtc",
                ["sortDirection"] = "descending",
                ["totalRecords"] = _missingEpisodeIds.Count,
                ["records"] = records
            };
            return Results.Content(response.ToJsonString(), "application/json");
        }
    }

    public IResult GetQueue(int page, int pageSize, MockMode mode)
    {
        lock (_lock)
        {
            var queue = mode == MockMode.Sonarr ? _seriesQueue : _movieQueue;
            var skip = (page - 1) * pageSize;
            var items = new JsonArray();
            foreach (var item in queue.Skip(skip).Take(pageSize))
                items.Add(SerializeQueueItem(item));

            var response = new JsonObject
            {
                ["page"] = page, ["pageSize"] = pageSize, ["totalRecords"] = queue.Count, ["records"] = items
            };
            return Results.Content(response.ToJsonString(), "application/json");
        }
    }

    public void RemoveFromQueue(int id)
    {
        lock (_lock)
        {
            _movieQueue.RemoveAll(q => q.Id == id);
            _seriesQueue.RemoveAll(q => q.Id == id);
        }
    }

    public IResult GetMovie(int id)
    {
        if (!_movies.TryGetValue(id, out var json)) return Results.NotFound();
        return Results.Content(WithMovieMissingState(id, json).ToJsonString(), "application/json");
    }

    public IResult GetSeries(int id)
    {
        return _series.TryGetValue(id, out var json) ? Results.Content(json, "application/json") : Results.NotFound();
    }

    public IResult GetEpisode(int id)
    {
        if (!_episodes.TryGetValue(id, out var json)) return Results.NotFound();
        return Results.Content(WithEpisodeMissingState(id, json).ToJsonString(), "application/json");
    }

    public IResult GetEpisodesBySeries(int seriesId)
    {
        if (!_episodesBySeries.TryGetValue(seriesId, out var epIds))
            return Results.Content("[]", "application/json");
        var items = epIds.Where(id => _episodes.ContainsKey(id))
            .Select(id => WithEpisodeMissingState(id, _episodes[id]).ToJsonString());
        return Results.Content($"[{string.Join(",", items)}]", "application/json");
    }

    // Episodes/movies marked missing only get hasFile=false/monitored=true synthesized on the
    // fly by GetMissingMovies/GetMissingEpisodes; the stored JSON (seeded from a real instance,
    // or fake-generated) keeps its original hasFile value. Apply the same override here so every
    // endpoint agrees on missing state - otherwise a full-library sync (e.g. /api/v3/episode by
    // series) overwrites Trackarr's "missing" record with stale hasFile=true data.
    private JsonObject WithMovieMissingState(int movieId, string json)
    {
        var node = JsonNode.Parse(json)!.AsObject();
        if (_missingMovieIds.Contains(movieId))
        {
            node["hasFile"] = false;
            node["monitored"] = true;
        }

        return node;
    }

    private JsonObject WithEpisodeMissingState(int episodeId, string json)
    {
        var node = JsonNode.Parse(json)!.AsObject();
        if (_missingEpisodeIds.Contains(episodeId))
        {
            node["hasFile"] = false;
            node["monitored"] = true;
        }

        return node;
    }

    public MockStatus GetMockStatus()
    {
        lock (_lock)
        {
            return new MockStatus(
                RadarrPort,
                SonarrPort,
                _movies.Count,
                _series.Count,
                _episodes.Count,
                _movieQueue.Count,
                _seriesQueue.Count,
                _missingMovieIds.Count,
                _missingEpisodeIds.Count,
                _profileChangedItems.Count,
                _commands.Count(c => !c.Processed && c.WillSucceed));
        }
    }

    public void RegenerateFakeData(IConfiguration config)
    {
        lock (_lock)
        {
            _movieQueue.Clear();
            _seriesQueue.Clear();
            _missingMovieIds.Clear();
            _missingEpisodeIds.Clear();
            _commands.Clear();
            _profileChangedItems.Clear();
            GenerateFakeData(config);
        }

        logger.LogInformation("Fake data regenerated");
    }

    // ── Queue Item Serialization (Radarr/Sonarr JSON format) ──

    private JsonNode SerializeQueueItem(FakeQueueItem item)
    {
        double? etaSeconds = null;
        if (item.SizeLeft > 0 && item.Momentum > 0.01 && item.MetadataTicksNeeded <= item.MetadataTicks)
        {
            var bytesPerTick = item.Size * item.Momentum / 100.0;
            var ticksLeft = item.SizeLeft / bytesPerTick;
            etaSeconds = ticksLeft * 2.0;
        }

        var eta = etaSeconds.HasValue ? DateTime.UtcNow.AddSeconds(etaSeconds.Value) : (DateTime?)null;
        var timeLeft = etaSeconds.HasValue ? TimeSpan.FromSeconds(etaSeconds.Value).ToString(@"hh\:mm\:ss") : null;

        var obj = new JsonObject
        {
            ["id"] = item.Id,
            ["movieId"] = item.IsMovie ? item.MovieId : 0,
            ["seriesId"] = item.IsMovie ? 0 : item.SeriesId,
            ["episodeId"] = item.IsMovie ? 0 : item.EpisodeId,
            ["size"] = item.Size,
            ["sizeleft"] = item.SizeLeft,
            ["timeleft"] = timeLeft,
            ["title"] = item.Title,
            ["added"] = item.Added,
            ["status"] = item.Status,
            ["trackedDownloadStatus"] = item.TrackedDownloadStatus,
            ["trackedDownloadState"] = item.TrackedDownloadState,
            ["errorMessage"] = item.ErrorMessage,
            ["downloadId"] = item.DownloadId,
            ["protocol"] = item.Protocol,
            ["downloadClient"] = item.DownloadClient,
            ["downloadClientHasPostImportCategory"] = false,
            ["indexer"] = item.Indexer,
            ["outputPath"] = $"/downloads/complete/{item.Title}",
            ["estimatedCompletionTime"] = eta,
            ["customFormatScore"] = 0
        };

        obj["languages"] = new JsonArray(new JsonObject { ["id"] = 1, ["name"] = "English" });
        obj["quality"] = new JsonObject
        {
            ["quality"] =
                new JsonObject
                {
                    ["id"] = 7, ["name"] = item.QualityName, ["source"] = "webdl", ["resolution"] = 1080
                },
            ["revision"] = new JsonObject { ["version"] = 1, ["real"] = 0, ["isRepack"] = false }
        };
        obj["customFormats"] = new JsonArray();

        if (item.ErrorMessage is not null)
        {
            obj["statusMessages"] = new JsonArray(new JsonObject
            {
                ["title"] = item.Title, ["messages"] = new JsonArray((JsonNode)item.ErrorMessage)
            });
        }
        else
            obj["statusMessages"] = new JsonArray();

        if (item.IsMovie && _movies.TryGetValue(item.MovieId, out var movieJson))
            obj["movie"] = JsonNode.Parse(movieJson);
        if (!item.IsMovie && _series.TryGetValue(item.SeriesId, out var seriesJson))
            obj["series"] = JsonNode.Parse(seriesJson);
        if (!item.IsMovie && _episodes.TryGetValue(item.EpisodeId, out var epJson))
            obj["episode"] = JsonNode.Parse(epJson);

        return obj;
    }

    // ── Helpers ──

    private static HttpClient CreateClient(IHttpClientFactory factory, string? apiKey)
    {
        var client = factory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(30);
        if (!string.IsNullOrWhiteSpace(apiKey))
            client.DefaultRequestHeaders.TryAddWithoutValidation("X-Api-Key", apiKey);
        return client;
    }

    private static string? GetJsonStringProp(string json, string prop)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.TryGetProperty(prop, out var val) ? val.GetString() : null;
    }

    private static int GetJsonIntProp(string json, string prop)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.TryGetProperty(prop, out var val) ? val.GetInt32() : 0;
    }
}
