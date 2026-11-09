using Microsoft.Extensions.DependencyInjection;
using Trackarr.Core.Routine;
using Trackarr.Services.Routines;

namespace Trackarr.Routines;

public static class TrackarrRoutines
{
    /// <summary>The routines, and the hosted service that runs them on their schedules.</summary>
    public static IServiceCollection AddTrackarrRoutines(this IServiceCollection services)
    {
        services.AddHostedService<RoutineHostedService>();

        services.AddSingleton<IRoutine, HeartbeatRoutine>();
        services.AddSingleton<IRoutine, QueueRefreshRoutine>();
        services.AddSingleton<IRoutine, QueueEvaluationRoutine>();
        services.AddSingleton<IRoutine, MissingMediaFetchRoutine>();
        services.AddSingleton<IRoutine, DatabaseCleanupRoutine>();
        services.AddSingleton<IRoutine, HealthCheckRoutine>();

        return services;
    }
}
