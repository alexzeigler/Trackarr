using Microsoft.EntityFrameworkCore;
using Trackarr.Components;
using Trackarr.Core.Routine;
using Trackarr.Data;
using Trackarr.Services;
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
builder.Services.AddSingleton<IRoutine, MovieFetchRoutine>();

builder.Services.AddSingleton<EventService>();
builder.Services.AddSingleton<RoutineRunner>();
builder.Services.AddSingleton<RoutineService>();
builder.Services.AddSingleton<MediaServerService>();

builder.Services.AddHttpClient();

WebApplication app = builder.Build();

if (app.Environment.IsDevelopment()) {
    app.UseExceptionHandler("/Error", true);
}
else {
    app.UseExceptionHandler("/Error", true);
    app.UseHsts();
}

using (IServiceScope scope = app.Services.CreateScope()) {
    IDbContextFactory<AppDbContext> factory =
        scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();

    await using AppDbContext db = await factory.CreateDbContextAsync();

    await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;");
    await db.Database.ExecuteSqlRawAsync("PRAGMA busy_timeout=5000;");
}

app.UseHttpsRedirection();
app.UseAntiforgery();

app.MapStaticAssets();

app.MapRazorComponents<App>().AddInteractiveServerRenderMode()
    .AddAdditionalAssemblies(typeof(Trackarr.Client._Imports).Assembly);

app.Run();