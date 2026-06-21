using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using Trackarr.Core.Routine;
using Trackarr.Data;
using static Trackarr.Utils;

namespace Trackarr.Components.Pages.System;

public partial class Routines : ComponentBase
{
    private CancellationTokenSource? _cts;
    private List<RoutineDefinition> _routines = [];
    private List<RoutineRun> _runs = [];
    private string _search = string.Empty;
    private string _statusFilter = string.Empty;
    [Inject] private IDbContextFactory<AppDbContext> DbFactory { get; set; } = null!;
    private bool IsLoading { get; set; }

    private int EnabledCount => _routines.Count(x => x.Enabled);
    private int RunningCount => _routines.Count(x => x.IsRunning);
    private int PausedCount => _routines.Count(x => x.IsPaused);
    private int FailedCount => _routines.Count(x => x.LastRun != null && !x.LastRunSuccess);

    private IEnumerable<RoutineDefinition> FilteredRoutines =>
        _routines.Where(MatchesSearch).Where(MatchesStatus).OrderByDescending(x => x.IsRunning)
            .ThenByDescending(x => x.Enabled).ThenBy(x => x.Name);

    private IEnumerable<RoutineRun> RecentRuns => _runs.OrderByDescending(x => x.StartTime).Take(10);

    protected override async Task OnInitializedAsync()
    {
        _cts = new CancellationTokenSource();
        await Refresh();
        _ = RunRefreshLoopAsync(_cts.Token);
    }

    private async Task RunRefreshLoopAsync(CancellationToken token)
    {
        PeriodicTimer timer = new(TimeSpan.FromSeconds(2));
        while (await timer.WaitForNextTickAsync(token))
        {
            await Refresh();
            await InvokeAsync(StateHasChanged);
        }
    }

    private async Task Refresh()
    {
        IsLoading = true;

        await using var db = await DbFactory.CreateDbContextAsync();

        _routines = await db.Routines.AsNoTracking().OrderBy(x => x.Name).ToListAsync();

        _runs = await db.RoutineRuns.AsNoTracking().OrderByDescending(x => x.StartTime).Take(25).ToListAsync();

        IsLoading = false;
    }

    private bool MatchesSearch(RoutineDefinition routine)
    {
        if (string.IsNullOrWhiteSpace(_search)) return true;

        return Contains(routine.Name, _search) || Contains(routine.Key, _search) ||
               Contains(routine.Description, _search);
    }

    private bool MatchesStatus(RoutineDefinition routine)
    {
        if (string.IsNullOrWhiteSpace(_statusFilter)) return true;

        return RoutineStatusText(routine) == _statusFilter;
    }

    private static bool Contains(string? value, string search)
    {
        return value?.Contains(search, StringComparison.OrdinalIgnoreCase) == true;
    }

    private async Task TogglePaused(RoutineDefinition routine)
    {
        await using var db = await DbFactory.CreateDbContextAsync();

        var entity = await db.Routines.FirstOrDefaultAsync(x => x.Id == routine.Id);

        if (entity == null) return;

        entity.IsPaused = !entity.IsPaused;
        await db.SaveChangesAsync();

        await Refresh();
    }

    private async Task ToggleEnabled(RoutineDefinition routine)
    {
        await using var db = await DbFactory.CreateDbContextAsync();

        var entity = await db.Routines.FirstOrDefaultAsync(x => x.Id == routine.Id);

        if (entity == null) return;

        entity.Enabled = !entity.Enabled;

        if (!entity.Enabled)
        {
            entity.IsPaused = false;
            entity.IsRunning = false;
        }

        await db.SaveChangesAsync();

        await Refresh();
    }

    private async Task RunRoutine(string key)
    {
        var routine = _routines.FirstOrDefault(r => r.Key == key);
        if (routine is null) return;

        var lastRun = routine.LastRun;
        routine.IsRunning = true;
        RoutineRunner.RunAsync(key);
        await InvokeAsync(StateHasChanged);

        for (var i = 0; i < 20; i++)
        {
            await Task.Delay(300);
            await Refresh();
            await InvokeAsync(StateHasChanged);
            if (_routines.FirstOrDefault(r => r.Key == key)?.LastRun != lastRun)
                break;
        }
    }
}