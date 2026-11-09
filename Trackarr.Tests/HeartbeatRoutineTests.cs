using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Trackarr.Core.Server;
using Trackarr.Routines;

namespace Trackarr.Tests;

public class HeartbeatRoutineTests
{
    private static TestServices CreateService(SqliteInMemoryFactory factory, IHttpClientFactory httpClientFactory)
    {
        return new TestServices(factory, httpClientFactory);
    }

    private static MediaServer NewServer(string name) =>
        new()
        {
            Name = name,
            Host = "localhost",
            Port = 7878,
            ApiKey = "test-key",
            ServiceType = ServiceType.Radarr,
            IsEnabled = true,
            CreatedAt = DateTime.UtcNow
        };

    [Fact]
    public async Task ExecuteAsync_ServerUnreachable_DisablesServer_AfterThreeFailuresInARow()
    {
        using var factory = new SqliteInMemoryFactory();
        var httpFactory = new SwitchableHttpClientFactory();
        var service = CreateService(factory, httpFactory);

        var server = NewServer("Down Radarr");
        await service.Servers.AddAsync(server);

        httpFactory.Handler.Fail = true;

        var routine = new HeartbeatRoutine(service.Servers, service.Connection, service.Media,
            NullLogger<HeartbeatRoutine>.Instance);
        for (var run = 1; run < HeartbeatRoutine.FailuresBeforeDisable; run++)
        {
            var early = (await routine.ExecuteAsync(CancellationToken.None)).Message;
            Assert.True((await service.Servers.GetByIdAsync(server.Id))!.IsEnabled, $"disabled after {run} failure(s)");
            Assert.Contains("1 not responding", early);
        }

        var result = (await routine.ExecuteAsync(CancellationToken.None)).Message;

        Assert.False((await service.Servers.GetByIdAsync(server.Id))!.IsEnabled);
        Assert.Contains("1 disabled", result);
    }

    [Fact]
    public async Task ExecuteAsync_OneBlipBetweenSuccesses_NeverDisables()
    {
        // AUDIT B13: a single failed ping (DNS blip, Radarr restarting) used to disable the server.
        using var factory = new SqliteInMemoryFactory();
        var httpFactory = new SwitchableHttpClientFactory();
        var service = CreateService(factory, httpFactory);

        var server = NewServer("Flaky Radarr");
        await service.Servers.AddAsync(server);
        var routine = new HeartbeatRoutine(service.Servers, service.Connection, service.Media,
            NullLogger<HeartbeatRoutine>.Instance);

        foreach (var fail in new[] { true, true, false, true, true, false })
        {
            httpFactory.Handler.Fail = fail;
            await routine.ExecuteAsync(CancellationToken.None);
            Assert.True((await service.Servers.GetByIdAsync(server.Id))!.IsEnabled);
        }
    }

    [Fact]
    public async Task ExecuteAsync_ServerReachable_LeavesEnabled()
    {
        using var factory = new SqliteInMemoryFactory();
        var httpFactory = new SwitchableHttpClientFactory();
        var service = CreateService(factory, httpFactory);

        var server = NewServer("Up Radarr");
        await service.Servers.AddAsync(server);

        var routine = new HeartbeatRoutine(service.Servers, service.Connection, service.Media,
            NullLogger<HeartbeatRoutine>.Instance);
        var result = (await routine.ExecuteAsync(CancellationToken.None)).Message;

        var updated = await service.Servers.GetByIdAsync(server.Id);

        Assert.True(updated!.IsEnabled);
        Assert.DoesNotContain("disabled", result);
    }

    [Fact]
    public async Task ExecuteAsync_AlreadyDisabledServer_IsSkipped()
    {
        using var factory = new SqliteInMemoryFactory();
        var httpFactory = new SwitchableHttpClientFactory();
        var service = CreateService(factory, httpFactory);

        var server = NewServer("Disabled Radarr");
        await service.Servers.AddAsync(server);
        await service.Servers.UpdateStatusAsync(server.Id, s => s.IsEnabled = false);

        httpFactory.Handler.Fail = true;

        var routine = new HeartbeatRoutine(service.Servers, service.Connection, service.Media,
            NullLogger<HeartbeatRoutine>.Instance);
        var result = (await routine.ExecuteAsync(CancellationToken.None)).Message;

        Assert.Contains("0 servers checked", result);
    }

    [Fact]
    public async Task ExecuteAsync_RecoveredServer_ReenablesAndLogsEvent()
    {
        using var factory = new SqliteInMemoryFactory();
        var httpFactory = new SwitchableHttpClientFactory();
        var service = CreateService(factory, httpFactory);

        var server = NewServer("Recovering Radarr");
        await service.Servers.AddAsync(server);
        await service.Servers.UpdateStatusAsync(server.Id, s =>
        {
            s.IsEnabled = false;
            s.DisabledByHeartbeat = true;
        });

        var routine = new HeartbeatRoutine(service.Servers, service.Connection, service.Media,
            NullLogger<HeartbeatRoutine>.Instance);
        var result = (await routine.ExecuteAsync(CancellationToken.None)).Message;

        var updated = await service.Servers.GetByIdAsync(server.Id);

        Assert.True(updated!.IsEnabled);
        Assert.False(updated.DisabledByHeartbeat);
        Assert.Contains("1 re-enabled", result);

        var events = await service.Servers.GetRecentHealthEventsAsync(server.Id, 10);
        Assert.Contains(events, e => e.EventType == ServerHealthEventType.Resolved);
    }

    [Fact]
    public async Task ExecuteAsync_RecoveredServer_PullsFullSyncNotJustPing()
    {
        using var factory = new SqliteInMemoryFactory();
        var httpFactory = new SwitchableHttpClientFactory();
        var service = CreateService(factory, httpFactory);

        var server = NewServer("Recovering Radarr");
        await service.Servers.AddAsync(server);
        await service.Servers.UpdateStatusAsync(server.Id, s =>
        {
            s.IsEnabled = false;
            s.DisabledByHeartbeat = true;
            s.LastQueueFetchAt = default;
        });

        var routine = new HeartbeatRoutine(service.Servers, service.Connection, service.Media,
            NullLogger<HeartbeatRoutine>.Instance);
        await routine.ExecuteAsync(CancellationToken.None);

        var updated = await service.Servers.GetByIdAsync(server.Id);

        Assert.True(updated!.IsEnabled);
        Assert.NotEqual(default, updated.LastQueueFetchAt);
    }

    [Fact]
    public async Task ExecuteAsync_ServerFailure_LogsDownEvent()
    {
        using var factory = new SqliteInMemoryFactory();
        var httpFactory = new SwitchableHttpClientFactory();
        var service = CreateService(factory, httpFactory);

        var server = NewServer("Flaky Radarr");
        await service.Servers.AddAsync(server);

        httpFactory.Handler.Fail = true;

        var routine = new HeartbeatRoutine(service.Servers, service.Connection, service.Media,
            NullLogger<HeartbeatRoutine>.Instance);
        for (var run = 0; run < HeartbeatRoutine.FailuresBeforeDisable; run++)
            await routine.ExecuteAsync(CancellationToken.None);

        var events = await service.Servers.GetRecentHealthEventsAsync(server.Id, 10);

        Assert.Contains(events, e => e.EventType == ServerHealthEventType.Down);
    }

    [Fact]
    public async Task ExecuteAsync_ServerSuccess_RecordsFullUptime()
    {
        using var factory = new SqliteInMemoryFactory();
        var httpFactory = new SwitchableHttpClientFactory();
        var service = CreateService(factory, httpFactory);

        var server = NewServer("Healthy Radarr");
        await service.Servers.AddAsync(server);

        var routine = new HeartbeatRoutine(service.Servers, service.Connection, service.Media,
            NullLogger<HeartbeatRoutine>.Instance);
        await routine.ExecuteAsync(CancellationToken.None);

        var updated = await service.Servers.GetByIdAsync(server.Id);

        Assert.Equal(100, updated!.UptimePercent);
    }

    [Fact]
    public async Task ExecuteAsync_ServerFailureThenSuccess_UptimeReflectsRatio()
    {
        using var factory = new SqliteInMemoryFactory();
        var httpFactory = new SwitchableHttpClientFactory();
        var service = CreateService(factory, httpFactory);

        var server = NewServer("Recovering Uptime Radarr");
        await service.Servers.AddAsync(server);

        var routine = new HeartbeatRoutine(service.Servers, service.Connection, service.Media,
            NullLogger<HeartbeatRoutine>.Instance);

        httpFactory.Handler.Fail = true;
        await routine.ExecuteAsync(CancellationToken.None);

        httpFactory.Handler.Fail = false;
        await routine.ExecuteAsync(CancellationToken.None);

        var updated = await service.Servers.GetByIdAsync(server.Id);

        Assert.Equal(50, updated!.UptimePercent);
    }

    private class SwitchableHttpClientFactory : IHttpClientFactory
    {
        public SwitchableHandler Handler { get; } = new();
        public HttpClient CreateClient(string name) => new(Handler);
    }

    private class SwitchableHandler : HttpMessageHandler
    {
        public bool Fail { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (Fail)
                throw new HttpRequestException("Connection refused");

            var path = request.RequestUri?.AbsolutePath ?? "";

            if (path.EndsWith("/api/v3/system/status"))
            {
                var body = JsonSerializer.Serialize(
                    new { appName = "Radarr", version = "5.0.0", instanceName = "test" });
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/json")
                });
            }

            if (path.EndsWith("/api/v3/qualityprofile"))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("[]", Encoding.UTF8, "application/json")
                });
            }

            if (path.EndsWith("/api/v3/queue") || path.EndsWith("/api/v3/wanted/missing"))
            {
                var body = JsonSerializer.Serialize(new { page = 1, pageSize = 100, totalRecords = 0, records = Array.Empty<object>() });
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/json")
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }
}
