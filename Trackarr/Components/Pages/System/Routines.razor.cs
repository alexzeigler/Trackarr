using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using Trackarr.Core.Routine;
using Trackarr.Data;
using Trackarr.Services.Routines;
using static Trackarr.Utils;

namespace Trackarr.Components.Pages.System;

public partial class Routines : ComponentBase
{
    #region Lifecycle

    protected override async Task OnInitializedAsync()
    {
        _cts = new CancellationTokenSource();
        await Refresh();
        _ = RunRefreshLoopAsync(_cts.Token);
    }

    #endregion

    #region Injected Services

    [Inject] private IDbContextFactory<AppDbContext> DbFactory { get; set; } = null!;
    [Inject] private RoutineService RoutineService { get; set; } = null!;

    #endregion

    #region State

    private CancellationTokenSource? _cts;
    private List<RoutineDefinition> _routines = [];
    private List<RoutineRun> _runs = [];
    private string _search = string.Empty;
    private string _sortColumn = "name";
    private bool _sortDescending;
    private string _statusFilter = string.Empty;

    private bool IsLoading { get; set; }

    private bool HasActiveFilters => !string.IsNullOrWhiteSpace(_search) || !string.IsNullOrWhiteSpace(_statusFilter);

    private IEnumerable<RoutineDefinition> FilteredRoutines
    {
        get
        {
            var rows = _routines.Where(MatchesSearch).Where(MatchesStatus);

            rows = _sortColumn switch
            {
                "status" => _sortDescending
                    ? rows.OrderByDescending(r => RoutineStatusText(r, RoutineRunner.IsQueued(r.Key)))
                    : rows.OrderBy(r => RoutineStatusText(r, RoutineRunner.IsQueued(r.Key))),
                "interval" => _sortDescending
                    ? rows.OrderByDescending(r => r.IntervalSeconds)
                    : rows.OrderBy(r => r.IntervalSeconds),
                "lastRun" => _sortDescending
                    ? rows.OrderByDescending(r => GetSortTicks(r.LastRun))
                    : rows.OrderBy(r => GetSortTicks(r.LastRun)),
                "nextRun" => _sortDescending
                    ? rows.OrderByDescending(r => GetSortTicks(r.NextRun))
                    : rows.OrderBy(r => GetSortTicks(r.NextRun)),
                "result" => _sortDescending
                    ? rows.OrderByDescending(r => RoutineResultText(r))
                    : rows.OrderBy(r => RoutineResultText(r)),
                _ => _sortDescending ? rows.OrderByDescending(r => r.Name) : rows.OrderBy(r => r.Name)
            };

            return rows;
        }
    }

    private IEnumerable<RoutineRun> RecentRuns => _runs.OrderByDescending(x => x.StartTime).Take(10);

    #endregion

    #region Event Handlers

    private void OnSearchChanged(ChangeEventArgs args)
    {
        _search = args.Value?.ToString() ?? "";
    }

    private void OnStatusFilterChanged(ChangeEventArgs args)
    {
        _statusFilter = args.Value?.ToString() ?? "";
    }

    private void ClearFilters()
    {
        _search = string.Empty;
        _statusFilter = string.Empty;
    }

    private void SortBy(string column)
    {
        if (_sortColumn == column)
            _sortDescending = !_sortDescending;
        else
        {
            _sortColumn = column;
            _sortDescending = column is "lastRun" or "nextRun";
        }
    }

    private async Task TogglePaused(RoutineDefinition routine)
    {
        var definition = RoutineRunner.GetDefinition(routine.Key);
        if (definition is null) return;
        definition.IsPaused = !definition.IsPaused;
        await RoutineService.UpdateDefinitionAsync(definition);
        await Refresh();
    }

    private async Task ToggleEnabled(RoutineDefinition routine)
    {
        var definition = RoutineRunner.GetDefinition(routine.Key);
        if (definition is null) return;
        definition.Enabled = !definition.Enabled;
        if (!definition.Enabled)
        {
            definition.IsPaused = false;
            definition.IsRunning = false;
        }

        await RoutineService.UpdateDefinitionAsync(definition);
        await Refresh();
    }

    private async Task ForceReset(string key)
    {
        await RoutineRunner.ForceResetAsync(key);
        await Refresh();
    }

    private void RunAll()
    {
        foreach (var routine in _routines.Where(r => r.Enabled && !r.IsRunning && !r.IsPaused))
        {
            routine.IsRunning = true;
            RoutineRunner.RunAsync(routine.Key);
        }
    }

    private void RunRoutine(string key)
    {
        var routine = _routines.FirstOrDefault(r => r.Key == key);
        if (routine is null) return;
        routine.IsRunning = true;
        RoutineRunner.RunAsync(key);
    }

    #endregion

    #region Data Loading

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

    #endregion

    #region Display Helpers

    private string SortClass(string column)
    {
        return _sortColumn != column ? "table-sort" : _sortDescending ? "table-sort desc" : "table-sort asc";
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
        return RoutineStatusText(routine, RoutineRunner.IsQueued(routine.Key)) == _statusFilter;
    }

    private static bool Contains(string? value, string search)
    {
        return value?.Contains(search, StringComparison.OrdinalIgnoreCase) == true;
    }

    #endregion
}
