using System.Reflection;
using System.Text.Json;
using Trackarr.MockServer;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddUserSecrets(Assembly.GetExecutingAssembly());
builder.Logging.AddConsole(o => o.FormatterName = "clean")
    .AddConsoleFormatter<CleanConsoleFormatter, CleanConsoleFormatterOptions>();
builder.Services.AddHttpClient();
builder.Services.AddSingleton<MockDataStore>();
builder.Services.AddHostedService<QueueSimulator>();

var radarrPort = builder.Configuration.GetValue("MockServer:RadarrPort", 9877);
var sonarrPort = builder.Configuration.GetValue("MockServer:SonarrPort", 9878);
builder.WebHost.UseUrls($"http://127.0.0.1:{radarrPort}", $"http://127.0.0.1:{sonarrPort}");

var app = builder.Build();

var store = app.Services.GetRequiredService<MockDataStore>();
var log = app.Services.GetRequiredService<ILogger<Program>>();

await store.SeedAsync(app.Services.GetRequiredService<IHttpClientFactory>(), app.Configuration);

app.Use(async (ctx, next) =>
{
    var mode = store.GetMode(ctx);
    log.LogInformation("[{Mode}] {Method} {Path}{Query}", mode, ctx.Request.Method, ctx.Request.Path,
        ctx.Request.QueryString);
    await next();
});

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

app.MapPost("/api/v3/command", async (HttpContext ctx) =>
{
    var body = await ctx.Request.ReadFromJsonAsync<JsonElement>();
    var name = body.TryGetProperty("name", out var n) ? n.GetString() : "Unknown";
    return store.CreateCommand(name ?? "Unknown", body, store.GetMode(ctx));
});

app.MapGet("/api/v3/command", () => store.GetCommands());

// --- Mock Management Endpoints ---

app.MapGet("/mock/status", () => store.GetMockStatus());

app.MapPost("/mock/regenerate", () =>
{
    store.RegenerateFakeData(app.Configuration);
    return store.GetMockStatus();
});

app.Run();
