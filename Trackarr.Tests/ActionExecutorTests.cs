using System.Collections.Concurrent;
using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Trackarr.Core.Actions;
using Trackarr.Core.Health;
using Trackarr.Core.Server;
using Trackarr.Services.Actions;
using Trackarr.Services.Media;
using Trackarr.Services.Settings;
using Trackarr.Services.UI;

namespace Trackarr.Tests;

/// <summary>The action gate (ROADMAP 4.8): Passive Mode, protection, failures and the action log.</summary>
public sealed class ActionExecutorTests : IDisposable
{
    private readonly SqliteFileFactory _factory = new();
    private readonly RecordingServer _http = new();

    public void Dispose()
    {
        _factory.Dispose();
    }

    [Fact]
    public async Task PassiveMode_NeverReachesTheServer()
    {
        var (gate, server) = await CreateAsync(passive: true);
        var source = ActionSource.Routine("test");

        var results = new[]
        {
            await gate.ExecuteAsync(new RemoveFromQueue(server, 1, "dl-1", "Movie One", "Stalled.", source)),
            await gate.ExecuteAsync(new TriggerSearch(server, SearchKind.Movies, [1, 2], "Two movies", "Missing.",
                source)),
            await gate.ExecuteAsync(new TriggerSearch(server, SearchKind.Series, [7], "A series", "Missing.",
                source)),
            await gate.ExecuteAsync(new SetQualityProfile(server, 1, 5, null, "Movie One", "Fallback.", source))
        };

        Assert.All(results, r => Assert.Equal(ActionOutcome.Simulated, r.Outcome));
        Assert.Empty(_http.Requests); // not a single request, not even the GET before a profile change

        await using var db = _factory.CreateDbContext();
        Assert.Equal(4, await db.ActionLog.CountAsync(e => e.Outcome == ActionOutcome.Simulated));
    }

    [Fact]
    public async Task PassiveMode_DoesNotStopWhatYouClick()
    {
        var (gate, server) = await CreateAsync(passive: true);

        var result = await gate.ExecuteAsync(new RemoveFromQueue(server, 3, "dl-3", "Movie Three", "Clicked.",
            ActionSource.User));

        Assert.Equal(ActionOutcome.Executed, result.Outcome);
        Assert.Equal(new[] { "DELETE /api/v3/queue/3" }, _http.Requests.ToArray());
    }

    [Fact]
    public async Task Executed_IsLoggedWithWhatWhyAndWho()
    {
        var (gate, server) = await CreateAsync();

        await gate.ExecuteAsync(new RemoveFromQueue(server, 3, "dl-3", "Movie Three", "Stalled for 6 scans.",
            ActionSource.Routine("queue-evaluation")));

        await using var db = _factory.CreateDbContext();
        var entry = await db.ActionLog.SingleAsync();
        Assert.Equal(ActionOutcome.Executed, entry.Outcome);
        Assert.Equal(ActionKind.RemoveFromQueue, entry.Kind);
        Assert.Equal("Movie Three", entry.Target);
        Assert.Equal("Stalled for 6 scans.", entry.Reason);
        Assert.Equal("routine:queue-evaluation", entry.Source);
        Assert.Equal(server.Name, entry.ServerName);
        Assert.Null(entry.Error);
    }

    [Fact]
    public async Task ServerError_IsAFailedOutcome_NotAnException()
    {
        var (gate, server) = await CreateAsync();
        _http.Fail = true;

        var result = await gate.ExecuteAsync(new TriggerSearch(server, SearchKind.Movies, [1], "Movie One",
            "Missing.", ActionSource.Routine("test")));

        Assert.Equal(ActionOutcome.Failed, result.Outcome);
        Assert.Contains("500", result.Error);
        await using var db = _factory.CreateDbContext();
        Assert.Equal(ActionOutcome.Failed, (await db.ActionLog.SingleAsync()).Outcome);
    }

    [Fact]
    public async Task RepeatedSimulation_UpdatesOneRow()
    {
        var (gate, server) = await CreateAsync(passive: true);
        var action = new RemoveFromQueue(server, 1, "dl-1", "Movie One", "Stalled.", ActionSource.Routine("test"));

        await gate.ExecuteAsync(action);
        await gate.ExecuteAsync(action);
        await gate.ExecuteAsync(action with { Title = "Movie Two" });

        await using var db = _factory.CreateDbContext();
        var rows = await db.ActionLog.OrderBy(e => e.Id).ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.Equal(2, rows[0].Count);
        Assert.Equal(1, rows[1].Count);
    }

    [Fact]
    public async Task ProtectedDownload_IsNeverRemovedByARoutine_ButYouCanStillRemoveIt()
    {
        var (gate, server) = await CreateAsync();
        await using (var db = _factory.CreateDbContext())
        {
            db.DownloadAttempts.Add(new DownloadAttempt
            {
                ServerId = server.Id, DownloadId = "dl-1", Title = "Movie One", Status = AttemptStatus.Active,
                IsProtected = true
            });
            await db.SaveChangesAsync();
        }

        var routine = await gate.ExecuteAsync(new RemoveFromQueue(server, 1, "dl-1", "Movie One", "Blacklisted.",
            ActionSource.Routine("queue-evaluation")));
        Assert.Equal(ActionOutcome.Skipped, routine.Outcome);
        Assert.Empty(_http.Requests);

        var user = await gate.ExecuteAsync(new RemoveFromQueue(server, 1, "dl-1", "Movie One", "Clicked.",
            ActionSource.User));
        Assert.Equal(ActionOutcome.Executed, user.Outcome);
    }

    [Fact]
    public void PassiveMode_IsOnlyEnforcedByTheGate()
    {
        // ROADMAP Phase 1 "done when": outside the gate, Passive Mode may only be read to show it (the UI, the
        // Queue page's "won't be removed because…") or to store it.
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "ActionExecutor.cs", "SettingsService.cs", "HealthService.cs", "TopbarStatus.razor",
            "ServerSettings.razor", "Actions.razor"
        };

        var root = RepositoryRoot();
        var readers = Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".cs", StringComparison.Ordinal) || f.EndsWith(".razor", StringComparison.Ordinal))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") &&
                        !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") &&
                        !f.Contains($"{Path.DirectorySeparatorChar}Trackarr.Tests{Path.DirectorySeparatorChar}") &&
                        !f.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}"))
            .Where(f =>
            {
                var text = File.ReadAllText(f);
                return text.Contains("IsPassiveMode(", StringComparison.Ordinal) ||
                       text.Contains("\"PassiveMode\"", StringComparison.Ordinal);
            })
            .Select(Path.GetFileName)
            .ToList();

        Assert.NotEmpty(readers);
        Assert.All(readers, f => Assert.Contains(f!, allowed));
    }

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Trackarr.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Trackarr.sln not found above the test output.");
    }

    private async Task<(ActionExecutor Gate, MediaServer Server)> CreateAsync(bool passive = false)
    {
        var bus = new DataChangeBus();
        var settings = new SettingsService(_factory, NullLogger<SettingsService>.Instance, bus);
        await settings.InitializeAsync();
        if (passive)
            await settings.SaveCategoryAsync("General", new Dictionary<string, string> { ["PassiveMode"] = "true" });

        var health = new HealthService(_factory, settings, bus);
        var media = new MediaServerService(_factory, _http, NullLogger<MediaServerService>.Instance, bus, health);

        var server = new MediaServer
        {
            Name = "Test Radarr", ServiceType = ServiceType.Radarr, Host = "radarr.test", Port = 7878,
            ApiKey = "key", IsEnabled = true, CreatedAt = DateTime.UtcNow
        };
        await using (var db = _factory.CreateDbContext())
        {
            db.MediaServers.Add(server);
            await db.SaveChangesAsync();
        }

        return (new ActionExecutor(media, settings, _factory, bus, NullLogger<ActionExecutor>.Instance), server);
    }

    /// <summary>Records every request ("METHOD /path") and answers like Radarr, or with 500 when told to fail.</summary>
    private sealed class RecordingServer : HttpMessageHandler, IHttpClientFactory
    {
        public ConcurrentQueue<string> Requests { get; } = new();
        public bool Fail { get; set; }

        public HttpClient CreateClient(string name)
        {
            return new HttpClient(this, false);
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Enqueue($"{request.Method} {request.RequestUri!.AbsolutePath}");
            if (Fail) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));

            var body = request.RequestUri.AbsolutePath.EndsWith("/command", StringComparison.Ordinal)
                ? """{"id":1,"name":"MoviesSearch","status":"queued"}"""
                : """{"id":1,"qualityProfileId":4}""";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
            });
        }
    }
}
