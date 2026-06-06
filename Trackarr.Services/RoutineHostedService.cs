using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Trackarr.Core.Routine;
using Trackarr.Data;

namespace Trackarr.Services;

public sealed class RoutineHostedService(RoutineRunner runner, IEnumerable<IRoutine> routines, IDbContextFactory<AppDbContext> dbContextFactory, RoutineService routineService) : BackgroundService {
    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        foreach (IRoutine routine in routines) {
            await routineService.RegisterRoutineInDbAsync(routine, stoppingToken);
        }

        List<RoutineDefinition> definitions = await routineService.GetAllAsync(stoppingToken);
        runner.Load(definitions);

        using PeriodicTimer timer = new(TimeSpan.FromSeconds(5));
        while (await timer.WaitForNextTickAsync(stoppingToken)) await runner.TickAsync(stoppingToken);
    }
}