using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Trackarr.Core.Event;
using Trackarr.Services.Diagnostics;
using Trackarr.Services.UI;

namespace Trackarr.Tests;

public class EventServiceTests
{
    private static EventService Create(SqliteInMemoryFactory factory)
    {
        return new EventService(factory, NullLogger<EventService>.Instance, new DataChangeBus());
    }

    #region LogAsync

    [Fact]
    public async Task LogAsync_PersistsEventToDatabase()
    {
        using var factory = new SqliteInMemoryFactory();
        var service = Create(factory);

        await service.LogAsync("test message", "TestSource", LogLevel.Warning, null);

        await using var db = factory.CreateDbContext();
        var events = await db.LogEvents.ToListAsync();
        Assert.Single(events);

        var evt = events[0];
        Assert.Equal("test message", evt.Message);
        Assert.Equal("TestSource", evt.Source);
        Assert.Equal(LogLevel.Warning, evt.LogLevel);
        Assert.Null(evt.Exception);
        Assert.True(evt.TimeStamp <= DateTime.UtcNow);
    }

    [Fact]
    public async Task LogAsync_PersistsExceptionString()
    {
        using var factory = new SqliteInMemoryFactory();
        var service = Create(factory);

        await service.LogAsync("crash", "App", LogLevel.Error, "NullReferenceException: x was null");

        await using var db = factory.CreateDbContext();
        var evt = await db.LogEvents.SingleAsync();
        Assert.Equal("NullReferenceException: x was null", evt.Exception);
    }

    #endregion

    #region GetAllAsync

    [Fact]
    public async Task GetAllAsync_ReturnsEventsInDescendingTimestampOrder()
    {
        using var factory = new SqliteInMemoryFactory();

        await using (var db = factory.CreateDbContext())
        {
            db.LogEvents.AddRange(
                new LogEvent
                {
                    Message = "oldest",
                    Source = "A",
                    LogLevel = LogLevel.Information,
                    TimeStamp = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc)
                },
                new LogEvent
                {
                    Message = "newest",
                    Source = "B",
                    LogLevel = LogLevel.Warning,
                    TimeStamp = new DateTime(2025, 3, 1, 0, 0, 0, DateTimeKind.Utc)
                },
                new LogEvent
                {
                    Message = "middle",
                    Source = "C",
                    LogLevel = LogLevel.Error,
                    TimeStamp = new DateTime(2025, 2, 1, 0, 0, 0, DateTimeKind.Utc)
                });
            await db.SaveChangesAsync();
        }

        var service = Create(factory);
        var result = await service.GetAllAsync();

        Assert.Equal(3, result.Count);
        Assert.Equal("newest", result[0].Message);
        Assert.Equal("middle", result[1].Message);
        Assert.Equal("oldest", result[2].Message);
    }

    [Fact]
    public async Task GetAllAsync_ReturnsEmptyListWhenNoEvents()
    {
        using var factory = new SqliteInMemoryFactory();
        var service = Create(factory);

        var result = await service.GetAllAsync();

        Assert.Empty(result);
    }

    #endregion

    #region GetByLevelAsync

    [Fact]
    public async Task GetByLevelAsync_FiltersCorrectly()
    {
        using var factory = new SqliteInMemoryFactory();

        await using (var db = factory.CreateDbContext())
        {
            db.LogEvents.AddRange(
                new LogEvent
                {
                    Message = "info1",
                    Source = "A",
                    LogLevel = LogLevel.Information,
                    TimeStamp = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc)
                },
                new LogEvent
                {
                    Message = "error1",
                    Source = "B",
                    LogLevel = LogLevel.Error,
                    TimeStamp = new DateTime(2025, 2, 1, 0, 0, 0, DateTimeKind.Utc)
                },
                new LogEvent
                {
                    Message = "info2",
                    Source = "C",
                    LogLevel = LogLevel.Information,
                    TimeStamp = new DateTime(2025, 3, 1, 0, 0, 0, DateTimeKind.Utc)
                });
            await db.SaveChangesAsync();
        }

        var service = Create(factory);
        var result = await service.GetByLevelAsync(LogLevel.Information);

        Assert.Equal(2, result.Count);
        Assert.All(result, e => Assert.Equal(LogLevel.Information, e.LogLevel));
    }

    [Fact]
    public async Task GetByLevelAsync_ReturnsDescendingOrder()
    {
        using var factory = new SqliteInMemoryFactory();

        await using (var db = factory.CreateDbContext())
        {
            db.LogEvents.AddRange(
                new LogEvent
                {
                    Message = "early",
                    Source = "A",
                    LogLevel = LogLevel.Error,
                    TimeStamp = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc)
                },
                new LogEvent
                {
                    Message = "late",
                    Source = "B",
                    LogLevel = LogLevel.Error,
                    TimeStamp = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc)
                });
            await db.SaveChangesAsync();
        }

        var service = Create(factory);
        var result = await service.GetByLevelAsync(LogLevel.Error);

        Assert.Equal("late", result[0].Message);
        Assert.Equal("early", result[1].Message);
    }

    [Fact]
    public async Task GetByLevelAsync_ReturnsEmptyWhenNoMatch()
    {
        using var factory = new SqliteInMemoryFactory();

        await using (var db = factory.CreateDbContext())
        {
            db.LogEvents.Add(new LogEvent
            {
                Message = "info", Source = "A", LogLevel = LogLevel.Information, TimeStamp = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var service = Create(factory);
        var result = await service.GetByLevelAsync(LogLevel.Critical);

        Assert.Empty(result);
    }

    #endregion

    #region DeleteAsync

    [Fact]
    public async Task DeleteAsync_RemovesCorrectEvent()
    {
        using var factory = new SqliteInMemoryFactory();

        int targetId;
        await using (var db = factory.CreateDbContext())
        {
            var keep = new LogEvent
            {
                Message = "keep", Source = "A", LogLevel = LogLevel.Information, TimeStamp = DateTime.UtcNow
            };
            var remove = new LogEvent
            {
                Message = "remove", Source = "B", LogLevel = LogLevel.Error, TimeStamp = DateTime.UtcNow
            };
            db.LogEvents.AddRange(keep, remove);
            await db.SaveChangesAsync();
            targetId = remove.Id;
        }

        var service = Create(factory);
        await service.DeleteAsync(targetId);

        await using var verifyDb = factory.CreateDbContext();
        var remaining = await verifyDb.LogEvents.ToListAsync();
        Assert.Single(remaining);
        Assert.Equal("keep", remaining[0].Message);
    }

    [Fact]
    public async Task DeleteAsync_NonExistentId_DoesNotThrow()
    {
        using var factory = new SqliteInMemoryFactory();
        var service = Create(factory);

        await service.DeleteAsync(999);

        var result = await service.GetAllAsync();
        Assert.Empty(result);
    }

    #endregion

    #region ClearAllAsync

    [Fact]
    public async Task ClearAllAsync_EmptiesTable()
    {
        using var factory = new SqliteInMemoryFactory();

        await using (var db = factory.CreateDbContext())
        {
            db.LogEvents.AddRange(
                new LogEvent
                {
                    Message = "a", Source = "X", LogLevel = LogLevel.Information, TimeStamp = DateTime.UtcNow
                },
                new LogEvent { Message = "b", Source = "Y", LogLevel = LogLevel.Error, TimeStamp = DateTime.UtcNow },
                new LogEvent { Message = "c", Source = "Z", LogLevel = LogLevel.Warning, TimeStamp = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }

        var service = Create(factory);
        await service.ClearAllAsync();

        await using var verifyDb = factory.CreateDbContext();
        var count = await verifyDb.LogEvents.CountAsync();
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task ClearAllAsync_OnEmptyTable_DoesNotThrow()
    {
        using var factory = new SqliteInMemoryFactory();
        var service = Create(factory);

        await service.ClearAllAsync();

        var result = await service.GetAllAsync();
        Assert.Empty(result);
    }

    #endregion
}
