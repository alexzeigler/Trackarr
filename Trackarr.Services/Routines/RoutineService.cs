using Microsoft.EntityFrameworkCore;
using Trackarr.Core.Routine;
using Trackarr.Data;

namespace Trackarr.Services.Routines;

public sealed class RoutineService(IDbContextFactory<AppDbContext> dbContextFactory) {
    public async Task<List<RoutineDefinition>> GetAllAsync(CancellationToken cancellationToken = default) {
        await using AppDbContext db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await db.Routines.ToListAsync(cancellationToken);
    }

    public async Task<RoutineDefinition?> GetByKeyAsync(string key, CancellationToken cancellationToken = default) {
        await using AppDbContext db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await db.Routines.FirstOrDefaultAsync(r => r.Key == key, cancellationToken);
    }

    public async Task RegisterRoutineInDbAsync(IRoutine routine, CancellationToken cancellationToken = default) {
        await using AppDbContext db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        RoutineDefinition? existing = await db.Routines.FirstOrDefaultAsync(r => r.Key == routine.Key, cancellationToken);

        if (existing is null) {
            db.Routines.Add(new RoutineDefinition {
                Key = routine.Key,
                Name = routine.Name,
                Description = routine.Description,
                Enabled = routine.Enabled,
                IntervalSeconds = routine.IntervalSeconds,
                NextRun = routine.NextRun.UtcDateTime
            });
        }
        else {
            existing.Name = routine.Name;
            existing.Description = routine.Description;
            existing.Enabled = routine.Enabled;
            existing.IntervalSeconds = routine.IntervalSeconds;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(RoutineDefinition routine, CancellationToken cancellationToken = default) {
        await using AppDbContext db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        db.Routines.Update(routine);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task SaveRunAsync(RoutineRun run, CancellationToken cancellationToken = default) {
        await using AppDbContext db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        run.Routine = null;
        db.RoutineRuns.Add(run);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateDefinitionAsync(RoutineDefinition routine, CancellationToken cancellationToken = default) {
        await using AppDbContext db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        db.Routines.Update(routine);
        await db.SaveChangesAsync(cancellationToken);
    }
}