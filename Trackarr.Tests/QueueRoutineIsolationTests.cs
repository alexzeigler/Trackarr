using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Trackarr.Core.Actions;
using Trackarr.Core.Health;
using Trackarr.Core.Server;
using Trackarr.Core.Settings;
using Trackarr.Routines;
using Trackarr.Services.Actions;
using Trackarr.Services.Media;
using Trackarr.Services.Settings;
using Trackarr.Services.UI;

namespace Trackarr.Tests;

/// <summary>
///     Regression guards for H3: an offline server, or one failed removal, must not stop queue work on the
///     other servers or the rest of the queue.
/// </summary>
public class QueueRoutineIsolationTests
{
    [Fact]
    public async Task Refresh_OfflineServer_DoesNotStopTheOthers()
    {
        var ctx = await CreateAsync();
        using var _ = ctx.Factory;

        var routine = new QueueRefreshRoutine(ctx.Media, ctx.Health, ctx.Settings,
            NullLogger<QueueRefreshRoutine>.Instance);
        var run = await routine.ExecuteAsync(CancellationToken.None);
        var result = run.Message;

        Assert.False(run.Success);
        Assert.Contains("1 server(s) failed", result);

        await using var db = ctx.Factory.CreateDbContext();
        var attemptServers = await db.DownloadAttempts.Select(a => a.ServerId).Distinct().ToListAsync();
        Assert.Equal([ctx.UpServerId], attemptServers);
    }

    [Fact]
    public async Task Evaluation_OfflineServer_DoesNotStopTheOthers()
    {
        var ctx = await CreateAsync();
        using var _ = ctx.Factory;

        var routine = new QueueEvaluationRoutine(ctx.Media, ctx.Health, ctx.Blacklist, ctx.Settings, ctx.Actions,
            NullLogger<QueueEvaluationRoutine>.Instance);
        var run = await routine.ExecuteAsync(CancellationToken.None);
        var result = run.Message;

        Assert.False(run.Success);
        Assert.Contains("Evaluated 2", result);
        Assert.Contains("1 server(s) failed", result);
    }

    [Fact]
    public async Task Evaluation_NewDownloads_AreScannedOnce()
    {
        // The fresh fetch scores downloads it hasn't seen; the routine must not scan them a second time in the
        // same run (a second scan seconds later reads as "no progress").
        var ctx = await CreateAsync();
        using var _ = ctx.Factory;

        var routine = new QueueEvaluationRoutine(ctx.Media, ctx.Health, ctx.Blacklist, ctx.Settings, ctx.Actions,
            NullLogger<QueueEvaluationRoutine>.Instance);
        await routine.ExecuteAsync(CancellationToken.None);

        await using var db = ctx.Factory.CreateDbContext();
        var scansPerAttempt = await db.HealthEvents.GroupBy(e => e.DownloadAttemptId)
            .Select(g => g.Sum(e => e.ScanCount)).ToListAsync();
        Assert.Equal(2, scansPerAttempt.Count);
        Assert.All(scansPerAttempt, scans => Assert.Equal(1, scans));
    }

    [Fact]
    public async Task Evaluation_FailedRemoval_DoesNotStopTheRestOfTheQueue()
    {
        var ctx = await CreateAsync();
        using var _ = ctx.Factory;
        await ctx.Blacklist.AddAsync(new BlacklistEntry { Pattern = "BadGroup", Type = BlacklistType.Keyword });

        // Queue item 1 already left the client, so its DELETE returns 404. Item 2 should still be removed.
        ctx.Fake.MissingQueueIds.Add(1);

        var routine = new QueueEvaluationRoutine(ctx.Media, ctx.Health, ctx.Blacklist, ctx.Settings, ctx.Actions,
            NullLogger<QueueEvaluationRoutine>.Instance);
        var run = await routine.ExecuteAsync(CancellationToken.None);
        var result = run.Message;

        Assert.Equal([1, 2], ctx.Fake.DeleteAttempts.Order());
        Assert.Contains("blacklisted 1", result);
        Assert.Contains("1 removal(s) failed", result);
    }

    [Fact]
    public async Task Evaluation_MonitorQueueOff_LeavesThatServerAlone()
    {
        // Regression guard for H6: Monitor Queue off only stopped the refresh routine.
        var ctx = await CreateAsync();
        using var _ = ctx.Factory;
        await ctx.Blacklist.AddAsync(new BlacklistEntry { Pattern = "BadGroup", Type = BlacklistType.Keyword });
        await SetServerAsync(ctx, s => s.MonitorQueue = false);

        var routine = new QueueEvaluationRoutine(ctx.Media, ctx.Health, ctx.Blacklist, ctx.Settings, ctx.Actions,
            NullLogger<QueueEvaluationRoutine>.Instance);
        await routine.ExecuteAsync(CancellationToken.None);

        Assert.Empty(ctx.Fake.DeleteAttempts);
        await using var db = ctx.Factory.CreateDbContext();
        Assert.False(await db.DownloadAttempts.AnyAsync(a => a.ServerId == ctx.UpServerId));
    }

    [Fact]
    public async Task Evaluation_AutoRemoveOff_KeepsBlacklistMatches()
    {
        // Regression guard for H6: blacklist matches were removed even with Auto Remove off.
        var ctx = await CreateAsync();
        using var _ = ctx.Factory;
        await ctx.Blacklist.AddAsync(new BlacklistEntry { Pattern = "BadGroup", Type = BlacklistType.Keyword });
        await SetServerAsync(ctx, s => s.AutoRemoveDownloads = false);

        var routine = new QueueEvaluationRoutine(ctx.Media, ctx.Health, ctx.Blacklist, ctx.Settings, ctx.Actions,
            NullLogger<QueueEvaluationRoutine>.Instance);
        var result = (await routine.ExecuteAsync(CancellationToken.None)).Message;

        Assert.Empty(ctx.Fake.DeleteAttempts);
        Assert.Contains("blacklisted 0", result);
    }

    [Fact]
    public async Task Evaluation_PassiveMode_SimulatesRemovals_AndKeepsTheDownloadsTracked()
    {
        var ctx = await CreateAsync();
        using var _ = ctx.Factory;
        await ctx.Blacklist.AddAsync(new BlacklistEntry { Pattern = "BadGroup", Type = BlacklistType.Keyword });
        await ctx.Settings.SaveCategoryAsync("General", new Dictionary<string, string> { ["PassiveMode"] = "true" });

        var routine = new QueueEvaluationRoutine(ctx.Media, ctx.Health, ctx.Blacklist, ctx.Settings, ctx.Actions,
            NullLogger<QueueEvaluationRoutine>.Instance);
        var result = (await routine.ExecuteAsync(CancellationToken.None)).Message;

        Assert.Empty(ctx.Fake.DeleteAttempts);
        Assert.Contains("would have removed 2", result);

        await using var db = ctx.Factory.CreateDbContext();
        Assert.All(await db.DownloadAttempts.Where(a => a.ServerId == ctx.UpServerId).ToListAsync(),
            a => Assert.Equal(AttemptStatus.Active, a.Status));
        Assert.Equal(2, await db.ActionLog.CountAsync(e =>
            e.Outcome == ActionOutcome.Simulated && e.Kind == ActionKind.RemoveFromQueue));
    }

    [Fact]
    public async Task Evaluation_ProtectedDownload_IsNotRemovedEvenOnABlacklistMatch()
    {
        // The blacklist path never checked protection; the gate now does, for every automatic removal.
        var ctx = await CreateAsync();
        using var _ = ctx.Factory;
        await ctx.Blacklist.AddAsync(new BlacklistEntry { Pattern = "BadGroup", Type = BlacklistType.Keyword });
        var routine = new QueueEvaluationRoutine(ctx.Media, ctx.Health, ctx.Blacklist, ctx.Settings, ctx.Actions,
            NullLogger<QueueEvaluationRoutine>.Instance);

        // A first run in Passive Mode only starts tracking the two downloads.
        await ctx.Settings.SaveCategoryAsync("General", new Dictionary<string, string> { ["PassiveMode"] = "true" });
        await routine.ExecuteAsync(CancellationToken.None);
        await using (var db = ctx.Factory.CreateDbContext())
        {
            var first = await db.DownloadAttempts.SingleAsync(a => a.ServerId == ctx.UpServerId && a.DownloadId == "dl-1");
            first.IsProtected = true;
            await db.SaveChangesAsync();
        }

        await ctx.Settings.SaveCategoryAsync("General", new Dictionary<string, string> { ["PassiveMode"] = "false" });
        var result = (await routine.ExecuteAsync(CancellationToken.None)).Message;

        Assert.Equal([2], ctx.Fake.DeleteAttempts);
        Assert.Contains("blacklisted 1", result);
        Assert.Contains("Kept 1 protected", result);
    }

    private static async Task SetServerAsync(Context ctx, Action<MediaServer> change)
    {
        await using var db = ctx.Factory.CreateDbContext();
        var up = await db.MediaServers.SingleAsync(s => s.Id == ctx.UpServerId);
        change(up);
        await db.SaveChangesAsync();
    }

    private static async Task<Context> CreateAsync()
    {
        var factory = new SqliteInMemoryFactory();
        var bus = new DataChangeBus();
        var settings = new SettingsService(factory, NullLogger<SettingsService>.Instance, bus);
        await settings.InitializeAsync();
        var health = new HealthService(factory, settings, bus);
        var fake = new FakeServers();
        var media = new MediaServerService(factory, fake, NullLogger<MediaServerService>.Instance, bus, health);

        // The offline server sorts first by priority, so before the fix it aborted the run before the other.
        var down = NewServer("Down Radarr", FakeServers.DownHost, priority: 1);
        var up = NewServer("Up Radarr", FakeServers.UpHost, priority: 2);
        await using (var db = factory.CreateDbContext())
        {
            db.MediaServers.AddRange(down, up);
            await db.SaveChangesAsync();
        }

        var actions = new ActionExecutor(media, settings, factory, bus, NullLogger<ActionExecutor>.Instance);
        return new Context(factory, media, health, settings, new BlacklistService(factory, bus), actions, fake, up.Id);
    }

    private static MediaServer NewServer(string name, string host, int priority)
    {
        return new MediaServer
        {
            Name = name,
            Host = host,
            Port = 7878,
            ApiKey = "test-key",
            ServiceType = ServiceType.Radarr,
            IsEnabled = true,
            Priority = priority,
            CreatedAt = DateTime.UtcNow
        };
    }

    private sealed record Context(
        SqliteInMemoryFactory Factory,
        MediaServerService Media,
        HealthService Health,
        SettingsService Settings,
        BlacklistService Blacklist,
        ActionExecutor Actions,
        FakeServers Fake,
        int UpServerId);

    /// <summary>The "down" host throws like an unreachable server; the "up" host serves a two-item queue.</summary>
    private sealed class FakeServers : HttpMessageHandler, IHttpClientFactory
    {
        public const string DownHost = "down.test";
        public const string UpHost = "up.test";

        public HashSet<int> MissingQueueIds { get; } = [];
        public ConcurrentQueue<int> DeleteAttempts { get; } = new();

        public HttpClient CreateClient(string name) => new(this, false);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.RequestUri!.Host == DownHost)
                throw new HttpRequestException("Connection refused");

            var path = request.RequestUri.AbsolutePath;

            if (request.Method == HttpMethod.Delete && path.Contains("/api/v3/queue/"))
            {
                var id = int.Parse(path[(path.LastIndexOf('/') + 1)..]);
                DeleteAttempts.Enqueue(id);
                return Task.FromResult(new HttpResponseMessage(MissingQueueIds.Contains(id)
                    ? HttpStatusCode.NotFound
                    : HttpStatusCode.OK));
            }

            if (path.EndsWith("/api/v3/queue"))
            {
                var records = new[] { QueueItem(1, "Movie.One-BadGroup"), QueueItem(2, "Movie.Two-BadGroup") };
                return Json(JsonSerializer.Serialize(new { page = 1, pageSize = 100, totalRecords = 2, records }));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        private static object QueueItem(int id, string title)
        {
            return new
            {
                id,
                movieId = 100 + id,
                title,
                downloadId = $"dl-{id}",
                size = 2000,
                sizeLeft = 1000,
                status = "downloading",
                trackedDownloadStatus = "ok",
                trackedDownloadState = "downloading",
                protocol = "torrent",
                downloadClient = "qBittorrent",
                indexer = "Indexer",
                languages = Array.Empty<object>(),
                quality = new { },
                customFormats = Array.Empty<object>(),
                statusMessages = Array.Empty<object>()
            };
        }

        private static Task<HttpResponseMessage> Json(string body)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        }
    }
}
