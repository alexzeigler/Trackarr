using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Trackarr.Core.Event;
using Trackarr.Data;
using Trackarr.Services.UI;

namespace Trackarr.Services.Diagnostics;

public sealed class EventService(
    IDbContextFactory<AppDbContext> dbFactory,
    ILogger<EventService> logger,
    DataChangeBus changeBus)
{
    private readonly LogFileWriter _fileWriter = new();

    public async Task IngestCrashReportsAsync(CancellationToken cancellationToken = default)
    {
        foreach (var (path, report) in CrashReporter.ReadAll())
        {
            await LogAsync(report.Message, report.Source, LogLevel.Critical,
                report.StackTrace is not null ? $"{report.ExceptionType}: {report.StackTrace}" : report.ExceptionType,
                cancellationToken);
            CrashReporter.Delete(path);
        }
    }

    public async Task<List<LogEvent>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.LogEvents.OrderByDescending(e => e.TimeStamp).ToListAsync(cancellationToken);
    }

    public async Task<List<LogEvent>> GetByLevelAsync(LogLevel level, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.LogEvents.Where(e => e.LogLevel == level).OrderByDescending(e => e.TimeStamp)
            .ToListAsync(cancellationToken);
    }

    public async Task LogAsync(string message, string source, LogLevel level, string? exception,
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        LogEvent evt = new()
        {
            Message = message,
            Source = source,
            LogLevel = level,
            Exception = exception,
            TimeStamp = DateTime.UtcNow
        };

        db.LogEvents.Add(evt);
        await db.SaveChangesAsync(cancellationToken);

        _fileWriter.Write(evt);
        if (_fileWriter.LastError is not null)
            logger.LogError("LogFileWriter failed: {Error}", _fileWriter.LastError);
        logger.Log(level, "[{source}] {message}", source, message);
    }

    public async Task DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var logEvent = await db.LogEvents.FindAsync([id], cancellationToken);
        if (logEvent is null)
            return;
        db.LogEvents.Remove(logEvent);
        await db.SaveChangesAsync(cancellationToken);
        changeBus.NotifyChanged();
    }

    public async Task ClearAllAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await db.Database.ExecuteSqlRawAsync("DELETE FROM LogEvents", cancellationToken);
        logger.LogInformation("Cleared all log events.");
        changeBus.NotifyChanged();
    }
}
