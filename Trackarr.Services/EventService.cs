using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Trackarr.Core.Event;
using Trackarr.Data;

namespace Trackarr.Services;

public class EventService(IDbContextFactory<AppDbContext> dbFactory, ILogger<EventService> logger) {
    public async Task<List<LogEvent>> GetAllAsync(CancellationToken cancellationToken = default) {
        await using AppDbContext db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.LogEvents.OrderByDescending(e => e.TimeStamp).ToListAsync(cancellationToken);
    }

    public async Task<List<LogEvent>> GetByLevelAsync(LogLevel level, CancellationToken cancellationToken = default) {
        await using AppDbContext db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.LogEvents.Where(e => e.LogLevel == level).OrderByDescending(e => e.TimeStamp).ToListAsync(cancellationToken);
    }

    public async Task LogAsync(string message, string source, LogLevel level, CancellationToken cancellationToken = default) {
        await using AppDbContext db = await dbFactory.CreateDbContextAsync(cancellationToken);

        db.LogEvents.Add(new LogEvent { Message = message, Source = source, LogLevel = level, TimeStamp = DateTime.UtcNow });

        await db.SaveChangesAsync(cancellationToken);
        logger.Log(level, "[{source}] {message}", source, message);
    }

    public async Task DeleteAsync(int id, CancellationToken cancellationToken = default) {
        await using AppDbContext db = await dbFactory.CreateDbContextAsync(cancellationToken);
        LogEvent? logEvent = await db.LogEvents.FindAsync([id], cancellationToken);
        if (logEvent is null) return;
        db.LogEvents.Remove(logEvent);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task ClearAllAsync(CancellationToken cancellationToken = default) {
        await using AppDbContext db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await db.Database.ExecuteSqlRawAsync("DELETE FROM LogEvents", cancellationToken);
        logger.LogInformation("Cleared all log events.");
    }
}