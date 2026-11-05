using Microsoft.Extensions.Logging;
using Trackarr.Core.Routine;
using Trackarr.Services.Diagnostics;

namespace Trackarr.Services.Routines;

public sealed class RoutineRunner(
    IEnumerable<IRoutine> handlers,
    RoutineService routineService,
    EventService eventService,
    ILogger<RoutineRunner> logger)
{
    #region Execution

    private async Task ExecuteRoutineAsync(RoutineDefinition routine, CancellationToken cancellationToken)
    {
        var handler = handlers.FirstOrDefault(x => x.Key == routine.Key);

        RoutineRun run = new()
        {
            Routine = routine,
            RoutineDefinitionId = routine.Id,
            StartTime = DateTime.UtcNow,
            State = RoutineState.Running
        };

        _history.Add(run);

        try
        {
            routine.IsRunning = true;
            await routineService.UpdateDefinitionAsync(routine, cancellationToken);
            await eventService.LogAsync($"Task '{routine.Name}' started.", routine.Key, LogLevel.Information, null,
                cancellationToken);
            if (handler != null)
            {
                var result = await handler.ExecuteAsync(cancellationToken);

                var finishedAt = DateTime.UtcNow;

                routine.LastRun = finishedAt;
                routine.LastRunTime = (int)(finishedAt - run.StartTime).TotalMilliseconds;
                routine.NextRun = finishedAt.AddSeconds(routine.IntervalSeconds);
                routine.LastRunSuccess = result.Success;

                run.EndTime = finishedAt;

                if (result.Success)
                {
                    run.State = RoutineState.Success;
                    await eventService.LogAsync(
                        $"Task '{routine.Name}' completed in {routine.LastRunTime}ms. {result.Message}",
                        routine.Key, LogLevel.Information, null, cancellationToken);
                }
                else
                {
                    // Finished, but part of the work failed (say one server was unreachable).
                    run.State = RoutineState.Failed;
                    run.ErrorMessage = result.Message;
                    await eventService.LogAsync(
                        $"Task '{routine.Name}' finished with errors in {routine.LastRunTime}ms. {result.Message}",
                        routine.Key, LogLevel.Warning, null, cancellationToken);
                }
            }
        }
        catch (Exception ex)
        {
            var failedAt = DateTime.UtcNow;

            routine.LastRun = failedAt;
            routine.LastRunTime = (int)(failedAt - run.StartTime).TotalMilliseconds;
            routine.NextRun = failedAt.AddSeconds(routine.IntervalSeconds);
            routine.LastRunSuccess = false;

            run.EndTime = failedAt;
            run.State = RoutineState.Failed;
            run.ErrorMessage = ex.Message;

            await eventService.LogAsync($"Routine '{routine.Name}' failed: {ex.Message}", routine.Key, LogLevel.Error,
                ex.ToString(), cancellationToken);
        }
        finally
        {
            routine.IsRunning = false;

            try
            {
                await routineService.SaveRunAsync(run);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to save run record for routine '{Key}'", routine.Key);
            }

            try
            {
                await routineService.UpdateDefinitionAsync(routine);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to update definition for routine '{Key}'", routine.Key);
            }
        }
    }

    #endregion

    #region Dependencies

    private readonly List<RoutineRun> _history = [];
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly Queue<string> _manualQueue = new();
    private readonly List<RoutineDefinition> _routines = [];

    public IReadOnlyList<RoutineRun> History => _history.AsReadOnly();
    public bool IsRunning => _lock.CurrentCount == 0;

    #endregion

    #region Public API

    private void Enqueue(string key)
    {
        if (!_manualQueue.Contains(key))
            _manualQueue.Enqueue(key);
    }

    public void Load(List<RoutineDefinition> definitions)
    {
        _routines.AddRange(definitions);
    }

    /// <summary>
    ///     Runs at most one routine: the next manually queued one, otherwise (when <paramref name="runScheduled" />)
    ///     the most overdue enabled one. Manual runs go through even when scheduled runs are off.
    /// </summary>
    public async Task TickAsync(CancellationToken cancellationToken, bool runScheduled = true)
    {
        if (!await _lock.WaitAsync(0, cancellationToken)) return;

        try
        {
            var now = DateTime.UtcNow;

            var routine = _manualQueue.Count > 0
                ? _routines.FirstOrDefault(x => x.Key == _manualQueue.Peek() && !x.IsRunning)
                : runScheduled
                    ? _routines.Where(x => x.Enabled).Where(x => !x.IsPaused).Where(x => !x.IsRunning)
                        .Where(x => x.NextRun <= now).OrderBy(x => x.NextRun).FirstOrDefault()
                    : null;

            if (routine is null) return;

            if (_manualQueue.Count > 0 && _manualQueue.Peek() == routine.Key) _manualQueue.Dequeue();

            await ExecuteRoutineAsync(routine, cancellationToken);
        }
        finally
        {
            _lock.Release();
        }
    }

    public void RunAsync(string key)
    {
        var routine = _routines.FirstOrDefault(x => x.Key == key);
        if (routine is null)
            throw new InvalidOperationException($"No routine found for key '{key}'.");
        Enqueue(key);
    }

    public bool IsQueued(string key)
    {
        return _manualQueue.Contains(key);
    }

    public RoutineDefinition? GetDefinition(string key)
    {
        return _routines.FirstOrDefault(x => x.Key == key);
    }

    public async Task ForceResetAsync(string key)
    {
        var routine = _routines.FirstOrDefault(x => x.Key == key);
        if (routine is null) return;

        routine.IsRunning = false;
        await routineService.UpdateDefinitionAsync(routine);
    }

    #endregion
}
