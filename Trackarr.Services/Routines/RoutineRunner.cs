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
                        $"Task '{routine.Name}' completed in {routine.LastRunTime}ms. {result.Message}", routine.Key,
                        LogLevel.Information, null, cancellationToken);
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

    private readonly SemaphoreSlim _lock = new(1, 1);

    private readonly Queue<string> _manualQueue = new();

    // Swapped as a whole (never changed in place) so readers on other threads never see a half-built list.
    private volatile List<RoutineDefinition> _routines = [];

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
        _routines = [.. definitions];
    }

    /// <summary>
    ///     Runs <paramref name="wipe" /> (the database reset) once no routine is running, then re-creates the
    ///     routine rows and reloads them. Without this the runner keeps the old in-memory definitions, whose rows
    ///     are gone, and every later run fails to save until the app restarts.
    /// </summary>
    public async Task ResetAsync(Func<Task> wipe, CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            _manualQueue.Clear();
            await wipe();

            foreach (var handler in handlers)
                await routineService.RegisterRoutineInDbAsync(handler, cancellationToken);
            Load(await routineService.GetAllAsync(cancellationToken));
        }
        finally
        {
            _lock.Release();
        }
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

    /// <summary>All loaded routines, for status displays. The list is replaced, never changed, so it's safe to read.</summary>
    public IReadOnlyList<RoutineDefinition> Definitions => _routines;

    public const int MinIntervalSeconds = 60;
    public const int MaxIntervalSeconds = 7 * 86400;

    /// <summary>
    ///     Changes how often a routine runs. The value is clamped to [<see cref="MinIntervalSeconds" />,
    ///     <see cref="MaxIntervalSeconds" />] and the next run is rescheduled from the last run, so the change
    ///     applies right away and survives restarts. Returns the interval that was saved, or null for an unknown key.
    /// </summary>
    public async Task<int?> SetIntervalAsync(string key, int seconds, CancellationToken cancellationToken = default)
    {
        var routine = _routines.FirstOrDefault(x => x.Key == key);
        if (routine is null) return null;

        seconds = Math.Clamp(seconds, MinIntervalSeconds, MaxIntervalSeconds);
        if (routine.IntervalSeconds == seconds) return seconds;

        routine.IntervalSeconds = seconds;
        if (routine.LastRun is { } lastRun) routine.NextRun = lastRun.AddSeconds(seconds);
        await routineService.UpdateDefinitionAsync(routine, cancellationToken);
        return seconds;
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
