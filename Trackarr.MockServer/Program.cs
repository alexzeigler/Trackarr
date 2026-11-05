using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Trackarr.MockServer;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddUserSecrets(Assembly.GetExecutingAssembly());
builder.Logging.AddConsole(o => o.FormatterName = "clean")
    .AddConsoleFormatter<CleanConsoleFormatter, CleanConsoleFormatterOptions>();
builder.Services.AddHttpClient();
builder.Services.AddSingleton<MockDataStore>();
builder.Services.AddSingleton<RequestTracker>();
builder.Services.AddHostedService<QueueSimulator>();

var radarrPort = builder.Configuration.GetValue("MockServer:RadarrPort", 9877);
var sonarrPort = builder.Configuration.GetValue("MockServer:SonarrPort", 9878);
builder.WebHost.UseUrls($"http://127.0.0.1:{radarrPort}", $"http://127.0.0.1:{sonarrPort}");

var app = builder.Build();

var store = app.Services.GetRequiredService<MockDataStore>();
var tracker = app.Services.GetRequiredService<RequestTracker>();
var log = app.Services.GetRequiredService<ILogger<Program>>();

await store.SeedAsync(app.Services.GetRequiredService<IHttpClientFactory>(), app.Configuration);

app.Use(async (ctx, next) =>
{
    var path = ctx.Request.Path.Value ?? "/";
    if (path == "/" || path.StartsWith("/mock/"))
    {
        await next();
        return;
    }

    var mode = store.GetMode(ctx);
    var method = ctx.Request.Method;
    var query = ctx.Request.QueryString.Value ?? "";
    log.LogInformation("[{Mode}] {Method} {Path}{Query}", mode, method, path, ctx.Request.QueryString);

    string? requestBody = null;
    if (ctx.Request.ContentLength is > 0)
    {
        ctx.Request.EnableBuffering();
        using var reader = new StreamReader(ctx.Request.Body, leaveOpen: true);
        requestBody = await reader.ReadToEndAsync();
        ctx.Request.Body.Position = 0;
    }

    var requestHeaders = ctx.Request.Headers.ToDictionary(h => h.Key, h => h.Value.ToString());
    var remoteIp = ResolveRemoteIp(ctx);

    var originalBody = ctx.Response.Body;
    using var capture = new MemoryStream();
    ctx.Response.Body = capture;

    var stopwatch = Stopwatch.StartNew();

    // Simulate real-world network/processing latency so local ping/uptime data isn't just OS jitter.
    await Task.Delay(Random.Shared.Next(20, 61));

    await next();

    stopwatch.Stop();

    capture.Seek(0, SeekOrigin.Begin);
    var responseBody = await new StreamReader(capture).ReadToEndAsync();
    capture.Seek(0, SeekOrigin.Begin);
    await capture.CopyToAsync(originalBody);
    ctx.Response.Body = originalBody;

    var responseHeaders = ctx.Response.Headers.ToDictionary(h => h.Key, h => h.Value.ToString());

    tracker.Record(new RequestEntry(DateTime.UtcNow, method, path, query, mode.ToString(), ctx.Response.StatusCode,
        requestBody, responseBody, remoteIp, ctx.Request.Host.Value ?? "", ctx.Request.Scheme, ctx.Request.Protocol,
        ctx.Request.Headers.UserAgent.ToString() is { Length: > 0 } ua ? ua : null, stopwatch.ElapsedMilliseconds,
        ctx.Request.ContentLength, capture.Length, ctx.Request.ContentType, ctx.Response.ContentType, requestHeaders,
        responseHeaders));
});

string ResolveRemoteIp(HttpContext ctx)
{
    var forwarded = ctx.Request.Headers["X-Forwarded-For"].FirstOrDefault();
    if (!string.IsNullOrWhiteSpace(forwarded))
        return forwarded.Split(',')[0].Trim();

    var remote = ctx.Connection.RemoteIpAddress;
    if (remote is null) return "unknown";
    return remote.IsIPv4MappedToIPv6 ? remote.MapToIPv4().ToString() : remote.ToString();
}

// --- Radarr/Sonarr v3 API Endpoints ---

app.MapGet("/api/v3/system/status", (HttpContext ctx) => store.GetSystemStatus(store.GetMode(ctx)));

app.MapGet("/api/v3/qualityprofile", (HttpContext ctx) => store.GetProfiles(store.GetMode(ctx)));

app.MapGet("/api/v3/wanted/missing", (HttpContext ctx) =>
{
    var page = int.TryParse(ctx.Request.Query["page"], out var p) ? p : 1;
    var pageSize = int.TryParse(ctx.Request.Query["pageSize"], out var ps) ? ps : 100;
    var mode = store.GetMode(ctx);

    return mode == MockMode.Sonarr ? store.GetMissingEpisodes(page, pageSize) : store.GetMissingMovies(page, pageSize);
});

app.MapGet("/api/v3/movie/{id:int}", (int id) => store.GetMovie(id));

app.MapPut("/api/v3/movie/{id:int}", async (int id, HttpContext ctx) =>
{
    var body = await ctx.Request.ReadFromJsonAsync<JsonElement>();
    return store.UpdateMovie(id, body);
});

app.MapGet("/api/v3/series/{id:int}", (int id) => store.GetSeries(id));

app.MapGet("/api/v3/episode/{id:int}", (int id) => store.GetEpisode(id));

app.MapGet("/api/v3/episode", (int? seriesId) =>
{
    if (seriesId is null) return Results.BadRequest("seriesId is required");
    return store.GetEpisodesBySeries(seriesId.Value);
});

app.MapGet("/api/v3/queue", (HttpContext ctx) =>
{
    var page = int.TryParse(ctx.Request.Query["page"], out var p) ? p : 1;
    var pageSize = int.TryParse(ctx.Request.Query["pageSize"], out var ps) ? ps : 100;
    return store.GetQueue(page, pageSize, store.GetMode(ctx));
});

app.MapDelete("/api/v3/queue/{id:int}", (int id) =>
{
    store.RemoveFromQueue(id);
    return Results.Ok();
});

app.MapGet("/api/v3/history", () => new { Records = Array.Empty<object>() });

app.MapGet("/api/v3/health", () => store.GetHealth());

app.MapPost("/api/v3/command", async (HttpContext ctx) =>
{
    var body = await ctx.Request.ReadFromJsonAsync<JsonElement>();
    var name = body.TryGetProperty("name", out var n) ? n.GetString() : "Unknown";
    return store.CreateCommand(name ?? "Unknown", body, store.GetMode(ctx));
});

app.MapGet("/api/v3/command", () => store.GetCommands());

// --- Mock Management Endpoints ---

app.MapGet("/mock/status", () =>
{
    var s = store.GetMockStatus();
    return new
    {
        s.RadarrPort,
        s.SonarrPort,
        s.Movies,
        s.Series,
        s.Episodes,
        s.MovieQueueItems,
        s.SeriesQueueItems,
        s.MissingMovies,
        s.MissingEpisodes,
        s.ProfileChangedItems,
        s.PendingSearches,
        TotalRequests = tracker.TotalCount
    };
});

app.MapGet("/mock/requests", () => tracker.GetRecent());

app.MapPost("/mock/regenerate", () =>
{
    store.RegenerateFakeData(app.Configuration);
    return store.GetMockStatus();
});

app.MapPost("/mock/health", (HttpContext ctx) =>
{
    var enabled = string.Equals(ctx.Request.Query["enabled"], "true", StringComparison.OrdinalIgnoreCase);
    store.SetHealthWarning(enabled);
    return Results.Ok(store.GetHealth());
});

// --- Dashboard ---

app.MapGet("/", () => Results.Content(DashboardPage.Html, "text/html"));

app.Run();
