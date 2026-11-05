using Microsoft.AspNetCore.DataProtection;
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

var dataProtectionPath = builder.Configuration["DataProtection:KeysPath"] ?? "/config/keys";

Directory.CreateDirectory(dataProtectionPath);

builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(dataProtectionPath))
    .SetApplicationName("Trackarr");

builder.Services.AddRazorComponents().AddInteractiveServerComponents();

var connectionString = ResolveConnectionString(builder);

// Logs and crash reports live next to the database (/config in Docker) so they survive image updates.
AppPaths.DataDirectory = ResolveDataDirectory(connectionString, builder.Environment.ContentRootPath);

static string ResolveDataDirectory(string connectionString, string contentRootPath)
{
    var dataSource = new SqliteConnectionStringBuilder(connectionString).DataSource;
    var directory = string.IsNullOrWhiteSpace(dataSource) || dataSource == ":memory:"
        ? null
        : Path.GetDirectoryName(dataSource);
    return directory ?? Path.Combine(contentRootPath, "data");
}

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

app.UseExceptionHandler("/Error", true);
if (!app.Environment.IsDevelopment()) app.UseHsts();

using (var scope = app.Services.CreateScope())
{
    var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();

    await using var db = await factory.CreateDbContextAsync();

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
