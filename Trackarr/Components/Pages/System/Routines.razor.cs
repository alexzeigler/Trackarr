using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.EntityFrameworkCore;
using Microsoft.JSInterop;
using Trackarr.Core.Routine;
using Trackarr.Data;
using Trackarr.Services.Routines;
using Trackarr.Services.Settings;
using static Trackarr.Utils;

namespace Trackarr.Components.Pages.System;

public partial class Routines : ComponentBase, IDisposable
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
    [Inject] private SettingsService SettingsService { get; set; } = null!;
    [Inject] private ILogger<Routines> Logger { get; set; } = null!;
    [Inject] private IJSRuntime Js { get; set; } = null!;

    #endregion

    #region State

    private CancellationTokenSource? _cts;
    private List<RoutineDefinition> _routines = [];
    private List<RoutineRun> _runs = [];
    private string _search = string.Empty;
    private string _sortColumn = "name";
    private bool _sortDescending;
    private string _statusFilter = string.Empty;
    private string? _editingKey;
    private int _editValue;
    private int _editUnit = 60;
    private string _editingName = string.Empty;
    private int _editingCurrentSeconds;

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

    // Database cleanup's interval is also a Maintenance setting (whole hours); keep the two in sync.
    private const string CleanupKey = "database-cleanup";

    private const string IntervalModalId = "intervalModal";

    private static readonly (string Label, int Seconds)[] IntervalPresets =
    [
        ("1 min", 60), ("5 min", 300), ("15 min", 900), ("1 hour", 3600), ("6 hours", 21600), ("1 day", 86400)
    ];

    private bool IsCleanupEdit => _editingKey == CleanupKey;

    /// <summary>What the form adds up to, before the runner clamps it to 1 minute–7 days.</summary>
    private long EditSeconds => (long)_editValue * _editUnit;

    private bool EditIsValid =>
        _editValue >= 1 && EditSeconds >= RoutineRunner.MinIntervalSeconds &&
        EditSeconds <= RoutineRunner.MaxIntervalSeconds;

    /// <summary>"every 1 hour 30 minutes": the full interval, which the short table form (1h) rounds off.</summary>
    private static string EveryText(long seconds)
    {
        if (seconds <= 0) return "manually only";

        var span = TimeSpan.FromSeconds(seconds);
        var parts = new List<string>();
        if (span.Days > 0) parts.Add(span.Days == 1 ? "1 day" : $"{span.Days} days");
        if (span.Hours > 0) parts.Add(span.Hours == 1 ? "1 hour" : $"{span.Hours} hours");
        if (span.Minutes > 0) parts.Add(span.Minutes == 1 ? "1 minute" : $"{span.Minutes} minutes");
        if (parts.Count == 0) parts.Add($"{span.Seconds} seconds");
        return "every " + string.Join(" ", parts);
    }

    private async Task BeginEditInterval(RoutineDefinition routine)
    {
        _editingKey = routine.Key;
        _editingName = routine.Name;
        _editingCurrentSeconds = routine.IntervalSeconds;
        var useHours = routine.Key == CleanupKey || routine.IntervalSeconds % 3600 == 0;
        _editUnit = useHours ? 3600 : 60;
        _editValue = Math.Max(1, routine.IntervalSeconds / _editUnit);
        StateHasChanged();
        await Js.InvokeVoidAsync("modalHelper.show", IntervalModalId);
    }

    private void ApplyPreset(int seconds)
    {
        var useHours = seconds % 3600 == 0;
        _editUnit = useHours ? 3600 : 60;
        _editValue = seconds / _editUnit;
    }

    private async Task CancelEditInterval()
    {
        _editingKey = null;
        await Js.InvokeVoidAsync("modalHelper.hide", IntervalModalId);
    }

    private async Task OnIntervalKeyDown(KeyboardEventArgs e)
    {
        if (e.Key == "Enter") await SaveInterval();
    }

    private async Task SaveInterval()
    {
        if (_editingKey is not { } key || !EditIsValid) return;

        var seconds = (int)Math.Min(EditSeconds, int.MaxValue);
        var saved = await RoutineRunner.SetIntervalAsync(key, seconds);

        if (saved is { } applied && key == CleanupKey)
            await SettingsService.SaveCategoryAsync("Maintenance",
                new Dictionary<string, string> { ["CleanupIntervalHours"] = Math.Max(1, applied / 3600).ToString() });

        _editingKey = null;
        await Js.InvokeVoidAsync("modalHelper.hide", IntervalModalId);
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
        using PeriodicTimer timer = new(TimeSpan.FromSeconds(2));
        try
        {
            while (await timer.WaitForNextTickAsync(token))
            {
                try
                {
                    await Refresh();
                    await InvokeAsync(StateHasChanged);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // A failed refresh shouldn't kill the loop; the next tick tries again.
                    Logger.LogWarning(ex, "Routines page refresh failed");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Page closed.
        }
    }

    // Without this the loop outlived the page: every visit added another 2 second database poll for the
    // rest of the browser session.
    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
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
