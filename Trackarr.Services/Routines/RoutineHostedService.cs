using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Trackarr.Core.Routine;
using Trackarr.Services.Settings;

namespace Trackarr.Services.Routines;

public sealed class RoutineHostedService(
    RoutineRunner runner,
    IEnumerable<IRoutine> routines,
    RoutineService routineService,
    SettingsService settings,
    ILogger<RoutineHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        foreach (var routine in routines) await routineService.RegisterRoutineInDbAsync(routine, stoppingToken);

        await routineService.ResetAllRunningAsync(stoppingToken);

        var definitions = await routineService.GetAllAsync(stoppingToken);
        runner.Load(definitions);

        // The loop always runs so manual runs (Run buttons, queue refresh, "run cleanup now") work. Auto Start only
        // gates scheduled runs, and is read every tick so changing it applies without a restart.
        using PeriodicTimer timer = new(TimeSpan.FromMilliseconds(500));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                var runScheduled = settings.GetBool("General", "AutoStartRoutines", true);
                await runner.TickAsync(stoppingToken, runScheduled);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Routine tick failed");
            }
        }
    }
}
