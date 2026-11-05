using Microsoft.EntityFrameworkCore;
using Trackarr.Core.Routine;
using Trackarr.Data;

namespace Trackarr.Services.Routines;

public sealed class RoutineService(IDbContextFactory<AppDbContext> dbContextFactory)
{
    #region Queries

    public async Task<List<RoutineDefinition>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await db.Routines.ToListAsync(cancellationToken);
    }

    public async Task<RoutineDefinition?> GetByKeyAsync(string key, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await db.Routines.FirstOrDefaultAsync(r => r.Key == key, cancellationToken);
    }

    #endregion

    #region Lifecycle

    public async Task RegisterRoutineInDbAsync(IRoutine routine, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var existing = await db.Routines.FirstOrDefaultAsync(r => r.Key == routine.Key, cancellationToken);

        if (existing is null)
        {
            db.Routines.Add(new RoutineDefinition
            {
                Key = routine.Key,
                Name = routine.Name,
                Description = routine.Description,
                Enabled = routine.Enabled,
                IntervalSeconds = routine.IntervalSeconds,
                NextRun = routine.NextRun.UtcDateTime
            });
        }
        else
        {
            // Enabled and IntervalSeconds belong to the user once the row exists (Routines page, Maintenance
            // settings). Only the code-owned text is refreshed, so a restart doesn't undo their changes.
            existing.Name = routine.Name;
            existing.Description = routine.Description;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(RoutineDefinition routine, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        db.Routines.Update(routine);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task SaveRunAsync(RoutineRun run, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        run.Routine = null;
        db.RoutineRuns.Add(run);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateDefinitionAsync(RoutineDefinition routine, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        db.Routines.Update(routine);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task ResetAllRunningAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await db.Routines.Where(r => r.IsRunning)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.IsRunning, false), cancellationToken);
    }

    #endregion
}
