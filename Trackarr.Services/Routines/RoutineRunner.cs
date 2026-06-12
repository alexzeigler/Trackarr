using Microsoft.Extensions.Logging;
using Trackarr.Core.Routine;
using Trackarr.Services.Diagnostics;

namespace Trackarr.Services.Routines;

public sealed class RoutineRunner(IEnumerable<IRoutine> handlers, RoutineService routineService, EventService eventService) {
    private readonly List<RoutineRun> _history = [];
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly Queue<string> _manualQueue = new();
    private readonly List<RoutineDefinition> _routines = [];

    public IReadOnlyList<RoutineRun> History => _history.AsReadOnly();
    public bool IsRunning => _lock.CurrentCount == 0;

    private void Enqueue(string key) {
        if (!_manualQueue.Contains(key))
            _manualQueue.Enqueue(key);
    }

    public void Load(List<RoutineDefinition> definitions) {
        _routines.AddRange(definitions);
    }

    public async Task TickAsync(CancellationToken cancellationToken) {
        if (!await _lock.WaitAsync(0, cancellationToken)) {
            return;
        }

        try {
            DateTime now = DateTime.UtcNow;

            RoutineDefinition? routine = _manualQueue.Count > 0 ? _routines.FirstOrDefault(x => x.Key == _manualQueue.Peek() && !x.IsRunning) : _routines.Where(x => x.Enabled).Where(x => !x.IsPaused).Where(x => !x.IsRunning).Where(x => x.NextRun <= now).OrderBy(x => x.NextRun).FirstOrDefault();

            if (routine is null) {
                return;
            }

            if (_manualQueue.Count > 0 && _manualQueue.Peek() == routine.Key) {
                _manualQueue.Dequeue();
            }

            await ExecuteRoutineAsync(routine, cancellationToken);
        }
        finally {
            _lock.Release();
        }
    }

    public void RunAsync(string key) {
        RoutineDefinition? routine = _routines.FirstOrDefault(x => x.Key == key);
        if (routine is null)
            throw new InvalidOperationException($"No routine found for key '{key}'.");
        Enqueue(key);
    }

    private async Task ExecuteRoutineAsync(RoutineDefinition routine, CancellationToken cancellationToken) {
        IRoutine? handler = handlers.FirstOrDefault(x => x.Key == routine.Key);

        RoutineRun run = new() {
            Routine = routine,
            RoutineDefinitionId = routine.Id,
            StartTime = DateTime.UtcNow,
            State = RoutineState.Running
        };

        _history.Add(run);

        try {
            routine.IsRunning = true;
            await routineService.UpdateDefinitionAsync(routine, cancellationToken);
            await eventService.LogAsync($"Task '{routine.Name}' started.",
            routine.Key,
            LogLevel.Information,
            null,
            cancellationToken);
            if (handler != null) {
                string msg = await handler.ExecuteAsync(cancellationToken);

                DateTime finishedAt = DateTime.UtcNow;

                routine.LastRun = finishedAt;
                routine.LastRunTime = (int)(finishedAt - run.StartTime).TotalMilliseconds;
                routine.NextRun = finishedAt.AddSeconds(routine.IntervalSeconds);
                routine.LastRunSuccess = true;

                run.EndTime = finishedAt;
                run.State = RoutineState.Success;

                await eventService.LogAsync($"Task '{routine.Name}' completed in {routine.LastRunTime}ms. {msg}",
                routine.Key,
                LogLevel.Information,
                null,
                cancellationToken);
            }
        }
        catch (Exception ex) {
            DateTime failedAt = DateTime.UtcNow;

            routine.LastRun = failedAt;
            routine.LastRunTime = (int)(failedAt - run.StartTime).TotalMilliseconds;
            routine.NextRun = failedAt.AddSeconds(routine.IntervalSeconds);
            routine.LastRunSuccess = false;

            run.EndTime = failedAt;
            run.State = RoutineState.Failed;
            run.ErrorMessage = ex.Message;

            await eventService.LogAsync($"Routine '{routine.Name}' failed: {ex.Message}",
            routine.Key,
            LogLevel.Error,
            ex.ToString(),
            cancellationToken);
        }
        finally {
            routine.IsRunning = false;
            await routineService.SaveRunAsync(run, cancellationToken);
            await routineService.UpdateDefinitionAsync(routine, cancellationToken);
        }
    }
}