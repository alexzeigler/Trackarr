using Microsoft.EntityFrameworkCore;
using Trackarr.Components;
using Trackarr.Core.Routine;
using Trackarr.Data;
using Trackarr.Services.Diagnostics;
using Trackarr.Services.Media;
using Trackarr.Services.Routines;
using Trackarr.Tasks;
using _Imports = Trackarr.Client._Imports;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents().AddInteractiveServerComponents();

var solutionRoot = Directory.GetParent(Directory.GetCurrentDirectory())?.FullName ?? "";
var dbPath = Path.Combine(solutionRoot, "Trackarr.Data", "trackarr.db");

builder.Services.AddDbContextFactory<AppDbContext>(options =>
{
    options.UseSqlite($"Data Source={dbPath};Cache=Shared;Pooling=True", sqlite => { sqlite.CommandTimeout(30); });
});

builder.Services.AddHostedService<RoutineHostedService>();

builder.Services.AddSingleton<IRoutine, HeartbeatRoutine>();
builder.Services.AddSingleton<IRoutine, QueueRefreshRoutine>();
builder.Services.AddSingleton<IRoutine, QueueEvaluationRoutine>();
builder.Services.AddSingleton<IRoutine, MissingMediaFetchRoutine>();

builder.Services.AddSingleton<EventService>();
builder.Services.AddSingleton<RoutineRunner>();
builder.Services.AddSingleton<RoutineService>();
builder.Services.AddSingleton<MediaServerService>();
builder.Services.AddSingleton<MissingService>();
builder.Services.AddSingleton<HealthService>();
builder.Services.AddSingleton<HistoryService>();

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

if (app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", true);
}
else
{
    app.UseExceptionHandler("/Error", true);
    app.UseHsts();
}

using (var scope = app.Services.CreateScope())
{
    var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();

    await using var db = await factory.CreateDbContextAsync();

    await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;");
    await db.Database.ExecuteSqlRawAsync("PRAGMA busy_timeout=5000;");
}

using (var scope = app.Services.CreateScope())
{
    var eventService = scope.ServiceProvider.GetRequiredService<EventService>();
    await eventService.IngestCrashReportsAsync();
}

app.UseHttpsRedirection();
app.UseAntiforgery();

app.MapStaticAssets();

app.MapRazorComponents<App>().AddInteractiveServerRenderMode().AddAdditionalAssemblies(typeof(_Imports).Assembly);

if (CrashReporter.LastError is not null)
    Console.Error.WriteLine($"[CrashReporter] {CrashReporter.LastError}");

app.Run();