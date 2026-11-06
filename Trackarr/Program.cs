using System.Net;
using System.Reflection;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Trackarr;
using Trackarr.Components;
using Trackarr.Core.Routine;
using Trackarr.Data;
using Trackarr.Routines;
using Trackarr.Services.Diagnostics;
using Trackarr.Services.Media;
using Trackarr.Services.Routines;
using Trackarr.Services.Settings;
using Trackarr.Services.SystemInfo;
using Trackarr.Services.UI;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddInMemoryCollection();

builder.Services.AddRazorComponents().AddInteractiveServerComponents();

var connectionString = ResolveConnectionString(builder);

// Logs, crash reports and keys live next to the database (/config in Docker) so they survive image updates.
AppPaths.DataDirectory = ResolveDataDirectory(connectionString, builder.Environment.ContentRootPath);

static string ResolveDataDirectory(string connectionString, string contentRootPath)
{
    var dataSource = new SqliteConnectionStringBuilder(connectionString).DataSource;
    var directory = string.IsNullOrWhiteSpace(dataSource) || dataSource == ":memory:"
        ? null
        : Path.GetDirectoryName(dataSource);
    return directory ?? Path.Combine(contentRootPath, "data");
}

var dataProtectionPath = builder.Configuration["DataProtection:KeysPath"] ??
                         Path.Combine(AppPaths.DataDirectory, "keys");
Directory.CreateDirectory(dataProtectionPath);
builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(dataProtectionPath))
    .SetApplicationName("Trackarr");

// Port, bind address, URL base and reverse proxy trust come from Settings > Network (read once here, so changes
// apply after a restart). ASPNETCORE_URLS / --urls still wins when set, so Rider launch profiles and older
// Docker templates keep working.
var hosting = HostingOptions.Load(connectionString, builder.Configuration["urls"]);
builder.WebHost.UseUrls(hosting.ListenUrls);
builder.Services.AddSingleton(hosting);

builder.Services.AddDbContextFactory<AppDbContext>(options =>
{
    options.UseSqlite(connectionString, sqlite => { sqlite.CommandTimeout(30); });
});

builder.Services.AddHostedService<RoutineHostedService>();

builder.Services.AddSingleton<IRoutine, HeartbeatRoutine>();
builder.Services.AddSingleton<IRoutine, QueueRefreshRoutine>();
builder.Services.AddSingleton<IRoutine, QueueEvaluationRoutine>();
builder.Services.AddSingleton<IRoutine, MissingMediaFetchRoutine>();
builder.Services.AddSingleton<IRoutine, DatabaseCleanupRoutine>();
builder.Services.AddSingleton<IRoutine, HealthCheckRoutine>();

builder.Services.AddSingleton<EventService>();
builder.Services.AddSingleton<RoutineRunner>();
builder.Services.AddSingleton<RoutineService>();
builder.Services.AddSingleton<MediaServerService>();
builder.Services.AddSingleton<MissingService>();
builder.Services.AddSingleton<ServerSyncService>();
builder.Services.AddSingleton<HealthService>();
builder.Services.AddSingleton<HistoryService>();
builder.Services.AddSingleton<QueueStatsService>();
builder.Services.AddSingleton<MissingStatsService>();
builder.Services.AddSingleton<SystemStatusService>();
builder.Services.AddSingleton<GitHubReleaseService>();
builder.Services.AddSingleton<SettingsService>();
builder.Services.AddSingleton<BlacklistService>();
builder.Services.AddSingleton<DataChangeBus>();

builder.Services.AddScoped<AppStateService>();

builder.Services.AddHttpClient();

// Clients for Radarr/Sonarr. Both honour the outgoing proxy from Settings > Network (handlers are recycled every
// couple of minutes, which is when a proxy change takes effect). The "insecure" one accepts any TLS certificate,
// for servers with "Skip certificate validation" on (self-signed certificates on Radarr/Sonarr's own SSL port).
builder.Services.AddHttpClient(MediaServerService.HttpClientName)
    .ConfigurePrimaryHttpMessageHandler(sp => CreateMediaServerHandler(sp, false));
builder.Services.AddHttpClient(MediaServerService.InsecureHttpClientName)
    .ConfigurePrimaryHttpMessageHandler(sp => CreateMediaServerHandler(sp, true));

static SocketsHttpHandler CreateMediaServerHandler(IServiceProvider sp, bool acceptAnyCertificate)
{
    var settings = sp.GetRequiredService<SettingsService>();
    var handler = new SocketsHttpHandler();

    var proxyUrl = settings.GetString("Network", "ProxyUrl", "").Trim();
    if (Uri.TryCreate(proxyUrl, UriKind.Absolute, out var proxyUri))
    {
        var proxy = new WebProxy(proxyUri)
        {
            BypassProxyOnLocal = settings.GetBool("Network", "ProxyBypassLocal", true)
        };

        // http://user:pass@proxy:3128 style credentials.
        if (!string.IsNullOrEmpty(proxyUri.UserInfo))
        {
            var parts = Uri.UnescapeDataString(proxyUri.UserInfo).Split(':', 2);
            proxy.Credentials = new NetworkCredential(parts[0], parts.Length > 1 ? parts[1] : "");
        }

        handler.Proxy = proxy;
        handler.UseProxy = true;
    }

    if (acceptAnyCertificate)
        handler.SslOptions.RemoteCertificateValidationCallback = (_, _, _, _) => true;

    return handler;
}

AppDomain.CurrentDomain.UnhandledException += (_, e) =>
{
    if (e.ExceptionObject is Exception ex)
        CrashReporter.Write(ex, "AppDomain");
};

TaskScheduler.UnobservedTaskException += (_, e) =>
{
    CrashReporter.Write(e.Exception, "UnobservedTask");
    e.SetObserved();
};

var app = builder.Build();

// Behind a reverse proxy: take the client IP, scheme, host and path prefix from X-Forwarded-* headers, but only
// from trusted proxies (by default loopback and private networks, where Docker and LAN proxies live).
if (hosting.ReverseProxy)
{
    var forwarded = new ForwardedHeadersOptions
    {
        ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto |
                           ForwardedHeaders.XForwardedHost | ForwardedHeaders.XForwardedPrefix
    };
    forwarded.KnownIPNetworks.Clear();
    forwarded.KnownProxies.Clear();
    var trusted = HostingOptions.ParseTrustedProxies(hosting.TrustedProxies, out var invalid);
    foreach (var network in trusted)
        forwarded.KnownIPNetworks.Add(network);
    foreach (var entry in invalid)
        app.Logger.LogWarning("Ignoring invalid trusted proxy '{Entry}' in Settings > Network", entry);

    app.UseForwardedHeaders(forwarded);
}

// URL base, e.g. /trackarr when served at https://example.com/trackarr. Requests without the prefix still work
// (the healthcheck uses that).
if (hosting.UrlBase.Length > 0)
    app.UsePathBase(hosting.UrlBase);

app.UseExceptionHandler("/Error", true);
if (!app.Environment.IsDevelopment()) app.UseHsts();

using (var scope = app.Services.CreateScope())
{
    var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();

    await using var db = await factory.CreateDbContextAsync();

    // Copy the database before an upgrade changes it. A failed backup is logged but doesn't stop the upgrade:
    // refusing to start would leave the user with no working Trackarr at all.
    try
    {
        var version = typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion.Split('+')[0] ?? "unknown";
        var backup = await DatabaseBackup.BackupBeforeMigrateAsync(db, Path.Combine(AppPaths.DataDirectory, "backups"),
            version);
        if (backup is not null)
            app.Logger.LogInformation("Backed up the database to {Path} before upgrading it", backup);
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Couldn't back up the database before upgrading it. Upgrading anyway");
    }

    await db.Database.MigrateAsync();

    await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;");
    // No busy_timeout pragma: it only applied to this one pooled connection. Microsoft.Data.Sqlite already retries
    // SQLITE_BUSY until the 30 second command timeout on every connection.
}

using (var scope = app.Services.CreateScope())
{
    var eventService = scope.ServiceProvider.GetRequiredService<EventService>();
    await eventService.IngestCrashReportsAsync();
}

var settingsService = app.Services.GetRequiredService<SettingsService>();
await settingsService.InitializeAsync();
Utils.Configure(settingsService);
settingsService.OnSettingsChanged += () => Utils.Configure(settingsService);

ApplyConsoleLogLevel(app.Configuration, settingsService);
settingsService.OnSettingsChanged += () => ApplyConsoleLogLevel(app.Configuration, settingsService);

if (app.Environment.IsDevelopment()) app.UseHttpsRedirection();

app.UseAntiforgery();

app.UseStaticFiles();
app.MapStaticAssets();

app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

// For the Docker HEALTHCHECK: healthy when the app is up and the database opens.
app.MapGet("/health", async (IDbContextFactory<AppDbContext> dbFactory, CancellationToken ct) =>
{
    await using var db = await dbFactory.CreateDbContextAsync(ct);
    return await db.Database.CanConnectAsync(ct) ? Results.Text("Healthy") : Results.StatusCode(503);
}).ExcludeFromDescription();

if (CrashReporter.LastError is not null)
    Console.Error.WriteLine($"[CrashReporter] {CrashReporter.LastError}");

// The Docker HEALTHCHECK reads this, so it still finds the app after the port is changed in settings.
try
{
    File.WriteAllText(Path.Combine(Path.GetTempPath(), "trackarr-health-url"), hosting.LocalHealthUrl);
}
catch (IOException)
{
}

app.Logger.LogInformation("Listening on {Urls}{Base}", hosting.ListenUrls,
    hosting.UrlBase.Length > 0 ? $" with URL base {hosting.UrlBase}" : "");

app.Run();
return;

static void ApplyConsoleLogLevel(IConfiguration configuration, SettingsService settings)
{
    var level = settings.GetString("General", "ConsoleLogLevel", "Information");
    configuration["Logging:LogLevel:Default"] = level;
    (configuration as IConfigurationRoot)?.Reload();
}

static string ResolveConnectionString(WebApplicationBuilder builder)
{
    var configuredConnectionString = builder.Configuration.GetConnectionString("DefaultConnection");

    if (!string.IsNullOrWhiteSpace(configuredConnectionString))
        return BuildSqliteConnectionString(configuredConnectionString, builder.Environment.ContentRootPath);

    var runningInContainer = string.Equals(Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER"), "true",
        StringComparison.OrdinalIgnoreCase);

    string dbPath;

    if (runningInContainer)
    {
        dbPath = "/config/trackarr.db";
    }
    else if (builder.Environment.IsDevelopment())
    {
        var solutionRoot = Directory.GetParent(Directory.GetCurrentDirectory())?.FullName ??
                           Directory.GetCurrentDirectory();
        dbPath = Path.Combine(solutionRoot, "Trackarr.Data", "trackarr.db");
    }
    else
    {
        dbPath = Path.Combine(builder.Environment.ContentRootPath, "data", "trackarr.db");
    }

    return BuildSqliteConnectionString($"Data Source={dbPath}", builder.Environment.ContentRootPath);
}

static string BuildSqliteConnectionString(string connectionString, string contentRootPath)
{
    var sqliteConnectionString = new SqliteConnectionStringBuilder(connectionString)
    {
        // Private cache (the default): shared cache is discouraged with WAL and can raise SQLITE_LOCKED.
        Cache = SqliteCacheMode.Default, Pooling = true, ForeignKeys = true
    };

    var dataSource = sqliteConnectionString.DataSource;

    if (string.IsNullOrWhiteSpace(dataSource) || dataSource == ":memory:") return sqliteConnectionString.ToString();
    var resolvedPath = Path.IsPathRooted(dataSource) ? dataSource : Path.Combine(contentRootPath, dataSource);

    var directory = Path.GetDirectoryName(resolvedPath);

    if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);

    sqliteConnectionString.DataSource = resolvedPath;

    return sqliteConnectionString.ToString();
}
