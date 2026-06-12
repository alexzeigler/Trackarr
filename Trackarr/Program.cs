using Microsoft.EntityFrameworkCore;
using Trackarr.Components;
using Trackarr.Core.Queue;
using Trackarr.Core.Routine;
using Trackarr.Data;
using Trackarr.Services.Diagnostics;
using Trackarr.Services.Media;
using Trackarr.Services.Routines;
using Trackarr.Services.Strikes;
using Trackarr.Tasks;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents().AddInteractiveServerComponents();

string solutionRoot = Directory.GetParent(Directory.GetCurrentDirectory())?.FullName ?? "";
string dbPath = Path.Combine(solutionRoot, "Trackarr.Data", "trackarr.db");

builder.Services.AddDbContextFactory<AppDbContext>(options => {
    options.UseSqlite($"Data Source={dbPath};Cache=Shared;Pooling=True", sqlite => { sqlite.CommandTimeout(30); });
});

builder.Services.AddHostedService<RoutineHostedService>();

builder.Services.AddSingleton<IRoutine, HeartbeatRoutine>();
builder.Services.AddSingleton<IRoutine, QueueRefreshRoutine>();
builder.Services.AddSingleton<IRoutine, MissingMediaFetchRoutine>();
builder.Services.AddSingleton<IRoutine, DownloadStrikeEvaluationRoutine>();

builder.Services.AddSingleton<EventService>();
builder.Services.AddSingleton<RoutineRunner>();
builder.Services.AddSingleton<RoutineService>();
builder.Services.AddSingleton<MediaServerService>();

builder.Services.AddSingleton<DownloadStrikePolicy>();
builder.Services.AddScoped<DownloadStrikeService>();
builder.Services.AddScoped<DownloadStrikeEvaluator>();

builder.Services.AddHttpClient();

AppDomain.CurrentDomain.UnhandledException += (_, e) => {
    if (e.ExceptionObject is Exception ex)
        CrashReporter.Write(ex, "AppDomain");
};

TaskScheduler.UnobservedTaskException += (_, e) => {
    CrashReporter.Write(e.Exception, "UnobservedTask");
    e.SetObserved();
};

WebApplication app = builder.Build();

if (app.Environment.IsDevelopment()) {
    app.UseExceptionHandler("/Error", true);
}
else {
    app.UseExceptionHandler("/Error", true);
    app.UseHsts();
}

using (IServiceScope scope = app.Services.CreateScope()) {
    IDbContextFactory<AppDbContext> factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();

    await using AppDbContext db = await factory.CreateDbContextAsync();

    await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;");
    await db.Database.ExecuteSqlRawAsync("PRAGMA busy_timeout=5000;");
}

using (IServiceScope scope = app.Services.CreateScope()) {
    EventService eventService = scope.ServiceProvider.GetRequiredService<EventService>();
    await eventService.IngestCrashReportsAsync();
}

app.UseHttpsRedirection();
app.UseAntiforgery();

app.MapStaticAssets();

app.MapRazorComponents<App>().AddInteractiveServerRenderMode().AddAdditionalAssemblies(typeof(Trackarr.Client._Imports).Assembly);

if (CrashReporter.LastError is not null)
    Console.Error.WriteLine($"[CrashReporter] {CrashReporter.LastError}");

app.Run();