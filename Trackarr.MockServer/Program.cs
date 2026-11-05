using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Trackarr.MockServer;

// The mock sits between Trackarr and a real Radarr/Sonarr:
//   - every GET is forwarded to the live server, so Trackarr sees real queue/missing/library data;
//   - search commands and queue removals are answered here and only logged, never forwarded;
//   - every /api request must carry the mock's own API key (set on the dashboard);
//   - live source URLs/keys live in user secrets and can be edited from the dashboard, which saves
//     them and restarts the server in-process. Intercepted history survives the restart.

var intercepts = new InterceptStore();
var tracker = new RequestTracker();
bool restartRequested;

do
{
    restartRequested = false;
    await RunServerAsync();
} while (restartRequested);

async Task RunServerAsync()
{
    var builder = WebApplication.CreateBuilder(args);
    builder.Configuration.AddUserSecrets(Assembly.GetExecutingAssembly());
    builder.Logging.AddConsole(o => o.FormatterName = "clean")
        .AddConsoleFormatter<CleanConsoleFormatter, CleanConsoleFormatterOptions>();

    var skipCertValidation = builder.Configuration.GetValue("MockServer:SkipCertificateValidation", false);
    builder.Services.AddHttpClient(LiveProxy.HttpClientName, c => c.Timeout = TimeSpan.FromSeconds(30))
        .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = skipCertValidation
                ? HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
                : null
        });
    builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
    builder.Services.AddSingleton<LiveProxy>();
    builder.Services.AddSingleton(intercepts);
    builder.Services.AddSingleton<MockSettings>();
    builder.Services.AddSingleton(tracker);
    builder.Services.AddSingleton<UserSecretsStore>();

    var radarrPort = builder.Configuration.GetValue("MockServer:RadarrPort", 9877);
    var sonarrPort = builder.Configuration.GetValue("MockServer:SonarrPort", 9878);
    builder.WebHost.UseUrls($"http://127.0.0.1:{radarrPort}", $"http://127.0.0.1:{sonarrPort}");

    var app = builder.Build();

    var proxy = app.Services.GetRequiredService<LiveProxy>();
    var settings = app.Services.GetRequiredService<MockSettings>();
    var secrets = app.Services.GetRequiredService<UserSecretsStore>();
    intercepts.Logger = app.Services.GetRequiredService<ILogger<InterceptStore>>();
    var log = app.Services.GetRequiredService<ILogger<Program>>();
    var startedAt = DateTime.UtcNow; // lets the dashboard tell when a restart has actually happened

    LogStartup();

    const string HandlingKey = "mock.handling";

    static bool IsDashboardPath(string path)
    {
        return path is "/" or "/favicon.ico" || path.StartsWith("/mock/");
    }

    // ── Request capture (runs for everything Trackarr sends, including rejected requests) ──

    app.Use(async (ctx, next) =>
    {
        var path = ctx.Request.Path.Value ?? "/";
        if (IsDashboardPath(path))
        {
            await next();
            return;
        }

        var mode = proxy.GetMode(ctx);
        var method = ctx.Request.Method;
        var query = ctx.Request.QueryString.Value ?? "";

        string? requestBody = null;
        if (ctx.Request.ContentLength is > 0)
        {
            ctx.Request.EnableBuffering();
            using var reader = new StreamReader(ctx.Request.Body, leaveOpen: true);
            requestBody = await reader.ReadToEndAsync();
            ctx.Request.Body.Position = 0;
        }

        var requestHeaders = ctx.Request.Headers.ToDictionary(h => h.Key,
            h => h.Key.Equals("X-Api-Key", StringComparison.OrdinalIgnoreCase) ? "(redacted)" : h.Value.ToString());
        var remoteIp = ResolveRemoteIp(ctx);

        var originalBody = ctx.Response.Body;
        using var capture = new MemoryStream();
        ctx.Response.Body = capture;

        var stopwatch = Stopwatch.StartNew();
        await next();
        stopwatch.Stop();

        capture.Seek(0, SeekOrigin.Begin);
        var responseBody = await new StreamReader(capture).ReadToEndAsync();
        capture.Seek(0, SeekOrigin.Begin);
        await capture.CopyToAsync(originalBody);
        ctx.Response.Body = originalBody;

        var handling = ctx.Items[HandlingKey] as string ?? "proxied";
        log.LogInformation("[{Mode}] {Method} {Path}{Query} → {Status} ({Handling}, {Ms} ms)", mode, method, path,
            RedactQuery(query), ctx.Response.StatusCode, handling, stopwatch.ElapsedMilliseconds);

        var responseHeaders = ctx.Response.Headers.ToDictionary(h => h.Key, h => h.Value.ToString());

        tracker.Record(new RequestEntry(DateTime.UtcNow, method, path, RedactQuery(query), mode.ToString(), handling,
            ctx.Response.StatusCode, requestBody, responseBody, remoteIp, ctx.Request.Host.Value ?? "",
            ctx.Request.Scheme, ctx.Request.Protocol,
            ctx.Request.Headers.UserAgent.ToString() is { Length: > 0 } ua ? ua : null, stopwatch.ElapsedMilliseconds,
            ctx.Request.ContentLength, capture.Length, ctx.Request.ContentType, ctx.Response.ContentType,
            requestHeaders, responseHeaders));
    });

    // ── API key check ──

    app.Use(async (ctx, next) =>
    {
        if (IsDashboardPath(ctx.Request.Path.Value ?? "/") || settings.IsAuthorized(ctx))
        {
            await next();
            return;
        }

        ctx.Items[HandlingKey] = "rejected";
        ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await ctx.Response.WriteAsJsonAsync(new { message = "Unauthorized" });
    });

    // ── Intercepted endpoints (answered here, never forwarded) ──

    app.MapPost("/api/v3/command", async (HttpContext ctx) =>
    {
        ctx.Items[HandlingKey] = "intercepted";
        var body = await ctx.Request.ReadFromJsonAsync<JsonElement>();
        return Results.Json(intercepts.CreateCommand(body, proxy.GetMode(ctx)),
            statusCode: StatusCodes.Status201Created);
    });

    // Commands come from our own log: the live server never saw the searches Trackarr sent us.
    app.MapGet("/api/v3/command", (HttpContext ctx) =>
    {
        ctx.Items[HandlingKey] = "intercepted";
        return intercepts.GetCommands(proxy.GetMode(ctx));
    });

    app.MapGet("/api/v3/command/{id:int}", (int id, HttpContext ctx) =>
    {
        ctx.Items[HandlingKey] = "intercepted";
        return intercepts.GetCommand(proxy.GetMode(ctx), id) is { } cmd ? Results.Json(cmd) : Results.NotFound();
    });

    app.MapDelete("/api/v3/queue/{id:int}", async (int id, HttpContext ctx) =>
    {
        ctx.Items[HandlingKey] = "intercepted";
        await RecordRemovalAsync(ctx, [id]);
        return Results.Ok();
    });

    app.MapDelete("/api/v3/queue/bulk", async (HttpContext ctx) =>
    {
        ctx.Items[HandlingKey] = "intercepted";
        var body = await ctx.Request.ReadFromJsonAsync<JsonElement>();
        var ids = body.TryGetProperty("ids", out var arr)
            ? arr.EnumerateArray().Select(e => e.GetInt32()).ToList()
            : [];
        await RecordRemovalAsync(ctx, ids);
        return Results.Ok();
    });

    // Queue is proxied, but with already-"removed" items stripped out.
    app.MapGet("/api/v3/queue", async (HttpContext ctx) =>
    {
        var mode = proxy.GetMode(ctx);
        var result = await proxy.GetAsync(mode, "/api/v3/queue", ctx.Request.QueryString, ctx.RequestAborted);
        var body = result.StatusCode is >= 200 and < 300
            ? intercepts.FilterQueueResponse(mode, result.Body)
            : result.Body;
        return Results.Content(body, result.ContentType ?? "application/json", statusCode: result.StatusCode);
    });

    // ── Everything else: GET is forwarded live, any other method is logged and faked ──

    app.Map("/{**rest}", async (HttpContext ctx) =>
    {
        var path = ctx.Request.Path.Value ?? "/";
        var mode = proxy.GetMode(ctx);

        if (HttpMethods.IsGet(ctx.Request.Method) || HttpMethods.IsHead(ctx.Request.Method))
        {
            var result = await proxy.GetAsync(mode, path, ctx.Request.QueryString, ctx.RequestAborted);
            return Results.Content(result.Body, result.ContentType ?? "application/json",
                statusCode: result.StatusCode);
        }

        ctx.Items[HandlingKey] = "intercepted";
        string? body = null;
        if (ctx.Request.ContentLength is > 0 || ctx.Request.Headers.TransferEncoding.Count > 0)
            body = await new StreamReader(ctx.Request.Body).ReadToEndAsync();
        intercepts.RecordWrite(mode, ctx.Request.Method, path, body);

        // Mirror what Radarr/Sonarr return: PUT → 202 echoing the resource, POST → 201, DELETE → 200.
        var status = HttpMethods.IsPut(ctx.Request.Method) ? StatusCodes.Status202Accepted :
            HttpMethods.IsPost(ctx.Request.Method) ? StatusCodes.Status201Created : StatusCodes.Status200OK;
        return string.IsNullOrEmpty(body)
            ? Results.StatusCode(status)
            : Results.Content(body, "application/json", statusCode: status);
    });

    // ── Dashboard endpoints ──

    app.MapGet("/mock/status", () =>
    {
        var counts = intercepts.GetCounts();
        var radarr = proxy.GetSource(MockMode.Radarr);
        var sonarr = proxy.GetSource(MockMode.Sonarr);
        return new
        {
            proxy.RadarrPort,
            proxy.SonarrPort,
            RadarrSource = radarr?.Url,
            SonarrSource = sonarr?.Url,
            settings.ApiKey,
            StartedAt = startedAt,
            TotalRequests = tracker.TotalCount,
            RejectedRequests = tracker.RejectedCount,
            counts.Searches,
            SearchesFound = counts.Found,
            SearchesMissed = counts.Missed,
            counts.Removals,
            counts.Writes
        };
    });

    app.MapGet("/mock/requests", () => tracker.GetRecent());
    app.MapGet("/mock/commands", () => intercepts.GetCommandsForDashboard());
    app.MapGet("/mock/writes", () => intercepts.GetWrites());
    app.MapGet("/mock/removals", () => intercepts.GetRemovals());

    app.MapPost("/mock/removals/{mode}/{id:int}/restore",
        (MockMode mode, int id) => intercepts.RestoreRemoval(mode, id) ? Results.Ok() : Results.NotFound());

    app.MapDelete("/mock/removals", () =>
    {
        intercepts.ClearRemovals();
        return Results.Ok();
    });

    app.MapPost("/mock/apikey", async (HttpContext ctx) =>
    {
        var body = await ctx.Request.ReadFromJsonAsync<JsonElement>();
        var key = body.TryGetProperty("apiKey", out var k) ? k.GetString()?.Trim() : null;
        if (string.IsNullOrEmpty(key)) key = MockSettings.GenerateKey();
        if (key.Length < 8) return Results.BadRequest(new { message = "API key must be at least 8 characters" });
        settings.SetApiKey(key);
        return Results.Ok(new { apiKey = key });
    });

    app.MapGet("/", () => Results.Content(DashboardPage.Html, "text/html"));
    app.MapGet("/favicon.ico", () => Results.NoContent());

    // Values shown in the edit form: whatever config resolved (user secrets win over appsettings).
    app.MapGet("/mock/sources",
        () => new
        {
            RadarrUrl = app.Configuration["MockServer:RadarrSourceUrl"],
            RadarrApiKey = app.Configuration["MockServer:RadarrSourceApiKey"],
            SonarrUrl = app.Configuration["MockServer:SonarrSourceUrl"],
            SonarrApiKey = app.Configuration["MockServer:SonarrSourceApiKey"],
            secrets.SecretsPath
        });

    app.MapPost("/mock/sources", async (HttpContext ctx) =>
    {
        var body = await ctx.Request.ReadFromJsonAsync<JsonElement>();

        string? Field(string name)
        {
            return body.TryGetProperty(name, out var v) ? v.GetString()?.Trim() : null;
        }

        var values = new Dictionary<string, string?>
        {
            ["MockServer:RadarrSourceUrl"] = Field("radarrUrl")?.TrimEnd('/'),
            ["MockServer:RadarrSourceApiKey"] = Field("radarrApiKey"),
            ["MockServer:SonarrSourceUrl"] = Field("sonarrUrl")?.TrimEnd('/'),
            ["MockServer:SonarrSourceApiKey"] = Field("sonarrApiKey")
        };

        foreach (var key in new[] { "MockServer:RadarrSourceUrl", "MockServer:SonarrSourceUrl" })
        {
            if (values[key] is { Length: > 0 } url &&
                !(Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https"))
                return Results.BadRequest(new { message = $"'{url}' is not a valid http(s) URL" });
        }

        try
        {
            secrets.Save(values);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return Results.BadRequest(new { message = $"Could not save user secrets: {ex.Message}" });
        }

        // Respond first, then restart so the dashboard gets its answer before the listener goes away.
        log.LogInformation("Live sources changed — restarting");
        restartRequested = true;
        var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
        _ = Task.Run(async () =>
        {
            await Task.Delay(500);
            lifetime.StopApplication();
        });
        return Results.Ok(new { restarting = true });
    });

    await app.RunAsync();

    // ── Helpers ──

    async Task RecordRemovalAsync(HttpContext ctx, List<int> ids)
    {
        var mode = proxy.GetMode(ctx);
        var blocklist = string.Equals(ctx.Request.Query["blocklist"], "true", StringComparison.OrdinalIgnoreCase);
        var removeFromClient = !string.Equals(ctx.Request.Query["removeFromClient"], "false",
            StringComparison.OrdinalIgnoreCase);

        // If Trackarr deletes something we never proxied (e.g. the mock just restarted), look it up live
        // so the dashboard can still show what it was.
        if (ids.Any(id => !intercepts.HasSnapshot(mode, id)) &&
            await proxy.GetLiveQueueRecordsAsync(mode, ctx.RequestAborted) is { } records)
            intercepts.AddSnapshots(mode, records);

        foreach (var id in ids)
            intercepts.RecordRemoval(mode, id, blocklist, removeFromClient);
    }

    static string RedactQuery(string query)
    {
        return Regex.Replace(query, "(?i)(apikey=)[^&]*", "$1(redacted)");
    }

    string ResolveRemoteIp(HttpContext ctx)
    {
        var forwarded = ctx.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(forwarded))
            return forwarded.Split(',')[0].Trim();

        var remote = ctx.Connection.RemoteIpAddress;
        if (remote is null) return "unknown";
        return remote.IsIPv4MappedToIPv6 ? remote.MapToIPv4().ToString() : remote.ToString();
    }

    void LogStartup()
    {
        log.LogInformation("=== Mock Server Ready ===");
        log.LogInformation("  Dashboard:   http://127.0.0.1:{Port}/", radarrPort);
        foreach (var mode in new[] { MockMode.Radarr, MockMode.Sonarr })
        {
            var port = mode == MockMode.Radarr ? radarrPort : sonarrPort;
            var source = proxy.GetSource(mode);
            if (source is null)
            {
                log.LogWarning("  {Mode} :{Port} → no {Mode}SourceUrl configured; reads will return 502", mode, port,
                    mode);
            }
            else
                log.LogInformation("  {Mode} :{Port} → {Url}", mode, port, source.Url);
        }

        log.LogInformation("  API key is shown on the dashboard; requests without it get 401");
    }
}
