using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Trackarr.Core.Health;
using Trackarr.Core.Queue;
using Trackarr.Core.Routine;
using Trackarr.Core.Server;
using Trackarr.Core.SystemInfo;
using Trackarr.Routines;
using Trackarr.Services.SystemInfo;

namespace Trackarr.Tests;

public class HealthCheckRoutineTests
{
    private static SystemStatusService CreateStatusService(SqliteInMemoryFactory factory)
    {
        return new SystemStatusService(factory);
    }

    private static HealthCheckRoutine CreateRoutine(SqliteInMemoryFactory factory,
        SystemStatusService? statusService = null, IHttpClientFactory? httpFactory = null)
    {
        statusService ??= CreateStatusService(factory);
        httpFactory ??= new SuccessHttpClientFactory();
        return new HealthCheckRoutine(factory, statusService, httpFactory, NullLogger<HealthCheckRoutine>.Instance);
    }

    #region Defaults

    [Fact]
    public void Defaults_AreCorrect()
    {
        using var factory = new SqliteInMemoryFactory();
        var routine = CreateRoutine(factory);

        Assert.Equal("health_check", routine.Key);
        Assert.Equal("Health Check", routine.Name);
        Assert.True(routine.Enabled);
        Assert.Equal(600, routine.IntervalSeconds);
    }

    #endregion

    #region Database Integrity

    [Fact]
    public async Task Database_Healthy_NoMessages()
    {
        using var factory = new SqliteInMemoryFactory();
        var status = CreateStatusService(factory);
        var routine = CreateRoutine(factory, status);

        await routine.ExecuteAsync(CancellationToken.None);

        var messages = await status.GetStatusMessagesAsync();
        Assert.DoesNotContain(messages, m => m.Source == "Database");
    }

    #endregion

    #region Internet Connectivity

    [Fact]
    public async Task Internet_Reachable_NoWarning()
    {
        using var factory = new SqliteInMemoryFactory();
        var status = CreateStatusService(factory);
        var routine = CreateRoutine(factory, status, new SuccessHttpClientFactory());

        await routine.ExecuteAsync(CancellationToken.None);

        var messages = await status.GetStatusMessagesAsync();
        Assert.DoesNotContain(messages, m => m.Source == "Network");
    }

    [Fact]
    public async Task Internet_Unreachable_WarnsNetwork()
    {
        using var factory = new SqliteInMemoryFactory();
        var status = CreateStatusService(factory);
        var routine = CreateRoutine(factory, status, new FailingHttpClientFactory());

        await routine.ExecuteAsync(CancellationToken.None);

        var messages = await status.GetStatusMessagesAsync();
        var msg = Assert.Single(messages, m => m.Source == "Network");
        Assert.Equal(StatusLevel.Warning, msg.Level);
        Assert.Contains("internet", msg.Message, StringComparison.OrdinalIgnoreCase);
    }

    #endregion

    #region Server Health

    [Fact]
    public async Task Server_Offline_ReportsError()
    {
        using var factory = new SqliteInMemoryFactory();
        await using (var db = factory.CreateDbContext())
        {
            db.MediaServers.Add(new MediaServer
            {
                Name = "Radarr",
                ServiceType = ServiceType.Radarr,
                Host = "localhost",
                Port = 7878,
                IsEnabled = true,
                IsOnline = false,
                LastError = "Connection refused"
            });
            await db.SaveChangesAsync();
        }

        var status = CreateStatusService(factory);
        var routine = CreateRoutine(factory, status);

        await routine.ExecuteAsync(CancellationToken.None);

        var messages = await status.GetStatusMessagesAsync();
        var msg = Assert.Single(messages, m => m.Source == "Radarr");
        Assert.Equal(StatusLevel.Error, msg.Level);
        Assert.Contains("offline", msg.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Connection refused", msg.Message);
    }

    [Fact]
    public async Task Server_AuthFailed_ReportsError()
    {
        using var factory = new SqliteInMemoryFactory();
        await using (var db = factory.CreateDbContext())
        {
            db.MediaServers.Add(new MediaServer
            {
                Name = "Sonarr",
                ServiceType = ServiceType.Sonarr,
                Host = "localhost",
                Port = 8989,
                IsEnabled = true,
                IsOnline = true,
                IsAuthenticated = false
            });
            await db.SaveChangesAsync();
        }

        var status = CreateStatusService(factory);
        var routine = CreateRoutine(factory, status);

        await routine.ExecuteAsync(CancellationToken.None);

        var messages = await status.GetStatusMessagesAsync();
        var msg = Assert.Single(messages, m => m.Source == "Sonarr");
        Assert.Equal(StatusLevel.Error, msg.Level);
        Assert.Contains("authentication", msg.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Server_StalePing_ReportsWarning()
    {
        using var factory = new SqliteInMemoryFactory();
        await using (var db = factory.CreateDbContext())
        {
            db.MediaServers.Add(new MediaServer
            {
                Name = "Radarr",
                ServiceType = ServiceType.Radarr,
                Host = "localhost",
                Port = 7878,
                IsEnabled = true,
                IsOnline = true,
                IsAuthenticated = true,
                LastPingedAt = DateTime.UtcNow.AddMinutes(-20)
            });
            await db.SaveChangesAsync();
        }

        var status = CreateStatusService(factory);
        var routine = CreateRoutine(factory, status);

        await routine.ExecuteAsync(CancellationToken.None);

        var messages = await status.GetStatusMessagesAsync();
        Assert.Contains(messages,
            m => m.Source == "Radarr" && m.Level == StatusLevel.Warning && m.Message.Contains("15 minutes"));
    }

    [Fact]
    public async Task Server_StaleQueue_ReportsWarning()
    {
        using var factory = new SqliteInMemoryFactory();
        await using (var db = factory.CreateDbContext())
        {
            db.MediaServers.Add(new MediaServer
            {
                Name = "Radarr",
                ServiceType = ServiceType.Radarr,
                Host = "localhost",
                Port = 7878,
                IsEnabled = true,
                IsOnline = true,
                IsAuthenticated = true,
                LastPingedAt = DateTime.UtcNow,
                LastQueueFetchAt = DateTime.UtcNow.AddMinutes(-10)
            });
            await db.SaveChangesAsync();
        }

        var status = CreateStatusService(factory);
        var routine = CreateRoutine(factory, status);

        await routine.ExecuteAsync(CancellationToken.None);

        var messages = await status.GetStatusMessagesAsync();
        Assert.Contains(messages,
            m => m.Source == "Radarr" && m.Level == StatusLevel.Warning &&
                 m.Message.Contains("stale", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Server_Healthy_NoMessages()
    {
        using var factory = new SqliteInMemoryFactory();
        await using (var db = factory.CreateDbContext())
        {
            db.MediaServers.Add(new MediaServer
            {
                Name = "Radarr",
                ServiceType = ServiceType.Radarr,
                Host = "localhost",
                Port = 7878,
                IsEnabled = true,
                IsOnline = true,
                IsAuthenticated = true,
                LastPingedAt = DateTime.UtcNow,
                LastQueueFetchAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var status = CreateStatusService(factory);
        var routine = CreateRoutine(factory, status);

        await routine.ExecuteAsync(CancellationToken.None);

        var messages = await status.GetStatusMessagesAsync();
        Assert.DoesNotContain(messages, m => m.Source == "Radarr");
    }

    [Fact]
    public async Task Server_Disabled_Ignored()
    {
        using var factory = new SqliteInMemoryFactory();
        await using (var db = factory.CreateDbContext())
        {
            db.MediaServers.Add(new MediaServer
            {
                Name = "Disabled",
                ServiceType = ServiceType.Radarr,
                Host = "localhost",
                Port = 7878,
                IsEnabled = false,
                IsOnline = false
            });
            await db.SaveChangesAsync();
        }

        var status = CreateStatusService(factory);
        var routine = CreateRoutine(factory, status);

        await routine.ExecuteAsync(CancellationToken.None);

        var messages = await status.GetStatusMessagesAsync();
        Assert.DoesNotContain(messages, m => m.Source == "Disabled");
    }

    [Fact]
    public async Task NoServers_ReportsNotice()
    {
        using var factory = new SqliteInMemoryFactory();
        var status = CreateStatusService(factory);
        var routine = CreateRoutine(factory, status);

        await routine.ExecuteAsync(CancellationToken.None);

        var messages = await status.GetStatusMessagesAsync();
        var msg = Assert.Single(messages, m => m.Source == "Configuration");
        Assert.Equal(StatusLevel.Notice, msg.Level);
    }

    #endregion

    #region Download Health

    [Fact]
    public async Task Downloads_Critical_ReportsWarning()
    {
        using var factory = new SqliteInMemoryFactory();
        await using (var db = factory.CreateDbContext())
        {
            var server = new MediaServer
            {
                Name = "Radarr", ServiceType = ServiceType.Radarr, Host = "localhost", Port = 7878
            };
            db.MediaServers.Add(server);
            await db.SaveChangesAsync();

            var attempt = new DownloadAttempt
            {
                ServerId = server.Id,
                DownloadId = "dl-1",
                Title = "Test",
                ServiceType = ServiceType.Radarr,
                Status = AttemptStatus.Active
            };
            db.DownloadAttempts.Add(attempt);
            await db.SaveChangesAsync();

            db.HealthSnapshots.Add(new HealthSnapshot { DownloadAttemptId = attempt.Id, State = HealthState.Critical });
            await db.SaveChangesAsync();
        }

        var status = CreateStatusService(factory);
        var routine = CreateRoutine(factory, status);

        await routine.ExecuteAsync(CancellationToken.None);

        var messages = await status.GetStatusMessagesAsync();
        Assert.Contains(messages,
            m => m.Source == "Downloads" && m.Level == StatusLevel.Warning &&
                 m.Message.Contains("critical", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Downloads_RemoveEligible_ReportsWarning()
    {
        using var factory = new SqliteInMemoryFactory();
        await using (var db = factory.CreateDbContext())
        {
            var server = new MediaServer
            {
                Name = "Radarr", ServiceType = ServiceType.Radarr, Host = "localhost", Port = 7878
            };
            db.MediaServers.Add(server);
            await db.SaveChangesAsync();

            var attempt = new DownloadAttempt
            {
                ServerId = server.Id,
                DownloadId = "dl-1",
                Title = "Test",
                ServiceType = ServiceType.Radarr,
                Status = AttemptStatus.Active
            };
            db.DownloadAttempts.Add(attempt);
            await db.SaveChangesAsync();

            db.HealthSnapshots.Add(new HealthSnapshot { DownloadAttemptId = attempt.Id, State = HealthState.Remove });
            await db.SaveChangesAsync();
        }

        var status = CreateStatusService(factory);
        var routine = CreateRoutine(factory, status);

        await routine.ExecuteAsync(CancellationToken.None);

        var messages = await status.GetStatusMessagesAsync();
        Assert.Contains(messages, m => m.Source == "Downloads" && m.Level == StatusLevel.Warning);
    }

    [Fact]
    public async Task Downloads_Healthy_NoCriticalMessages()
    {
        using var factory = new SqliteInMemoryFactory();
        await using (var db = factory.CreateDbContext())
        {
            var server = new MediaServer
            {
                Name = "Radarr",
                ServiceType = ServiceType.Radarr,
                Host = "localhost",
                Port = 7878,
                IsEnabled = true,
                IsOnline = true,
                IsAuthenticated = true,
                LastPingedAt = DateTime.UtcNow,
                LastQueueFetchAt = DateTime.UtcNow
            };
            db.MediaServers.Add(server);
            await db.SaveChangesAsync();

            var attempt = new DownloadAttempt
            {
                ServerId = server.Id,
                DownloadId = "dl-1",
                Title = "Test",
                ServiceType = ServiceType.Radarr,
                Status = AttemptStatus.Active
            };
            db.DownloadAttempts.Add(attempt);
            await db.SaveChangesAsync();

            db.HealthSnapshots.Add(new HealthSnapshot { DownloadAttemptId = attempt.Id, State = HealthState.Healthy });
            db.DownloadQueue.Add(new DownloadItem
            {
                ServerId = server.Id,
                DownloadId = "dl-1",
                Title = "Test",
                Size = 1000,
                SizeLeft = 500
            });
            await db.SaveChangesAsync();
        }

        var status = CreateStatusService(factory);
        var routine = CreateRoutine(factory, status);

        await routine.ExecuteAsync(CancellationToken.None);

        var messages = await status.GetStatusMessagesAsync();
        Assert.DoesNotContain(messages,
            m => m.Source == "Downloads" && m.Message.Contains("critical", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Downloads_OrphanedAttempt_ReportsNotice()
    {
        using var factory = new SqliteInMemoryFactory();
        await using (var db = factory.CreateDbContext())
        {
            var server = new MediaServer
            {
                Name = "Radarr", ServiceType = ServiceType.Radarr, Host = "localhost", Port = 7878
            };
            db.MediaServers.Add(server);
            await db.SaveChangesAsync();

            db.DownloadAttempts.Add(new DownloadAttempt
            {
                ServerId = server.Id,
                DownloadId = "orphan-1",
                Title = "Orphan",
                ServiceType = ServiceType.Radarr,
                Status = AttemptStatus.Active
            });
            await db.SaveChangesAsync();
        }

        var status = CreateStatusService(factory);
        var routine = CreateRoutine(factory, status);

        await routine.ExecuteAsync(CancellationToken.None);

        var messages = await status.GetStatusMessagesAsync();
        Assert.Contains(messages,
            m => m.Source == "Downloads" && m.Level == StatusLevel.Notice && m.Message.Contains("no longer appear"));
    }

    #endregion

    #region Routine Health

    [Fact]
    public async Task Routine_Failed_ReportsWarning()
    {
        using var factory = new SqliteInMemoryFactory();
        await using (var db = factory.CreateDbContext())
        {
            db.Routines.Add(new RoutineDefinition
            {
                Key = "heartbeat",
                Name = "Server Heartbeat",
                Description = "Test",
                Enabled = true,
                LastRunSuccess = false,
                LastRun = DateTime.UtcNow.AddMinutes(-5)
            });
            await db.SaveChangesAsync();
        }

        var status = CreateStatusService(factory);
        var routine = CreateRoutine(factory, status);

        await routine.ExecuteAsync(CancellationToken.None);

        var messages = await status.GetStatusMessagesAsync();
        Assert.Contains(messages, m => m.Source == "Server Heartbeat" && m.Level == StatusLevel.Warning);
    }

    [Fact]
    public async Task Routine_SkipsSelf()
    {
        using var factory = new SqliteInMemoryFactory();
        await using (var db = factory.CreateDbContext())
        {
            db.Routines.Add(new RoutineDefinition
            {
                Key = "health_check",
                Name = "Health Check",
                Description = "Test",
                Enabled = true,
                LastRunSuccess = false,
                LastRun = DateTime.UtcNow.AddMinutes(-5)
            });
            await db.SaveChangesAsync();
        }

        var status = CreateStatusService(factory);
        var routine = CreateRoutine(factory, status);

        await routine.ExecuteAsync(CancellationToken.None);

        var messages = await status.GetStatusMessagesAsync();
        Assert.DoesNotContain(messages, m => m.Source == "Health Check");
    }

    [Fact]
    public async Task Routine_Disabled_Ignored()
    {
        using var factory = new SqliteInMemoryFactory();
        await using (var db = factory.CreateDbContext())
        {
            db.Routines.Add(new RoutineDefinition
            {
                Key = "heartbeat",
                Name = "Server Heartbeat",
                Description = "Test",
                Enabled = false,
                LastRunSuccess = false,
                LastRun = DateTime.UtcNow.AddMinutes(-5)
            });
            await db.SaveChangesAsync();
        }

        var status = CreateStatusService(factory);
        var routine = CreateRoutine(factory, status);

        await routine.ExecuteAsync(CancellationToken.None);

        var messages = await status.GetStatusMessagesAsync();
        Assert.DoesNotContain(messages, m => m.Source == "Server Heartbeat");
    }

    #endregion

    #region Summary String

    [Fact]
    public async Task Execute_AllHealthy_ReturnsHealthyMessage()
    {
        using var factory = new SqliteInMemoryFactory();
        var routine = CreateRoutine(factory);

        var result = (await routine.ExecuteAsync(CancellationToken.None)).Message;

        Assert.Equal("All systems healthy", result);
    }

    [Fact]
    public async Task Execute_WithIssues_ReturnsIssueCount()
    {
        using var factory = new SqliteInMemoryFactory();
        await using (var db = factory.CreateDbContext())
        {
            db.MediaServers.Add(new MediaServer
            {
                Name = "Offline",
                ServiceType = ServiceType.Radarr,
                Host = "localhost",
                Port = 7878,
                IsEnabled = true,
                IsOnline = false
            });
            await db.SaveChangesAsync();
        }

        var routine = CreateRoutine(factory, httpFactory: new FailingHttpClientFactory());

        var result = (await routine.ExecuteAsync(CancellationToken.None)).Message;

        Assert.Contains("issue(s) found", result);
    }

    #endregion

    #region SetHealthCheckMessages Integration

    [Fact]
    public async Task StatusService_ReturnsHealthCheckMessages_AfterRoutineRuns()
    {
        using var factory = new SqliteInMemoryFactory();
        var status = CreateStatusService(factory);

        var messagesBefore = await status.GetStatusMessagesAsync();
        Assert.Contains(messagesBefore, m => m.Source == "Configuration");

        var routine = CreateRoutine(factory, status);
        await routine.ExecuteAsync(CancellationToken.None);

        var messagesAfter = await status.GetStatusMessagesAsync();
        Assert.Contains(messagesAfter, m => m.Source == "Configuration");
    }

    [Fact]
    public async Task StatusService_Fallback_BeforeRoutineRuns()
    {
        using var factory = new SqliteInMemoryFactory();
        await using (var db = factory.CreateDbContext())
        {
            db.MediaServers.Add(new MediaServer
            {
                Name = "Offline",
                ServiceType = ServiceType.Radarr,
                Host = "localhost",
                Port = 7878,
                IsEnabled = true,
                IsOnline = false
            });
            await db.SaveChangesAsync();
        }

        var status = CreateStatusService(factory);

        var messages = await status.GetStatusMessagesAsync();
        Assert.Contains(messages, m => m.Source == "Offline" && m.Level == StatusLevel.Error);
    }

    #endregion

    #region HTTP Stubs

    private class SuccessHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new SuccessHandler());
    }

    private class FailingHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new FailingHandler());
    }


    private class SuccessHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
    }

    private class FailingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            throw new HttpRequestException("Network unreachable");
    }

    #endregion
}
