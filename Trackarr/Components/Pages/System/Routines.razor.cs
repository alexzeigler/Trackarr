using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using Trackarr.Core.Routine;
using Trackarr.Data;

namespace Trackarr.Components.Pages.System;

public partial class Routines : ComponentBase
{
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

    private IEnumerable<RoutineDefinition> FilteredRoutines => _routines
        .Where(MatchesSearch)
        .Where(MatchesStatus)
        .OrderByDescending(x => x.IsRunning)
        .ThenByDescending(x => x.Enabled)
        .ThenBy(x => x.Name);

    private IEnumerable<RoutineRun> RecentRuns => _runs
        .OrderByDescending(x => x.StartTime).Take(10);

    protected override async Task OnInitializedAsync()
    {
        await Refresh();
    }

    private async Task Refresh()
    {
        IsLoading = true;

        await using var db = await DbFactory.CreateDbContextAsync();

        _routines = await db.Routines.AsNoTracking().OrderBy(x => x.Name).ToListAsync();

        _runs = await db.RoutineRuns
            .AsNoTracking()
            .OrderByDescending(x => x.StartTime)
            .Take(25)
            .ToListAsync();

        IsLoading = false;
    }

    private bool MatchesSearch(RoutineDefinition routine)
    {
        if (string.IsNullOrWhiteSpace(_search)) return true;

        return Contains(routine.Name, _search)
               || Contains(routine.Key, _search)
               || Contains(routine.Description, _search);
    }

    private bool MatchesStatus(RoutineDefinition routine)
    {
        if (string.IsNullOrWhiteSpace(_statusFilter)) return true;

        return StatusText(routine) == _statusFilter;
    }

    private static bool Contains(string? value, string search)
    {
        return value?.Contains(search, StringComparison.OrdinalIgnoreCase) == true;
    }

    private async Task TogglePaused(RoutineDefinition routine)
    {
        await using var db = await DbFactory.CreateDbContextAsync();

        var entity = await db.Routines
            .FirstOrDefaultAsync(x => x.Id == routine.Id);

        if (entity == null) return;

        entity.IsPaused = !entity.IsPaused;
        await db.SaveChangesAsync();

        await Refresh();
    }

    private async Task ToggleEnabled(RoutineDefinition routine)
    {
        await using var db = await DbFactory.CreateDbContextAsync();

        var entity = await db.Routines
            .FirstOrDefaultAsync(x => x.Id == routine.Id);

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

    private static string StatusText(RoutineDefinition routine)
    {
        if (routine.IsRunning) return "Running";

        if (!routine.Enabled) return "Disabled";

        if (routine.IsPaused) return "Paused";

        if (routine.LastRun != null && !routine.LastRunSuccess) return "Failed";

        return "Enabled";
    }

    private static string StatusBadge(RoutineDefinition routine)
    {
        return StatusText(routine) switch
        {
            "Running" => "bg-primary-lt",
            "Paused" => "bg-warning-lt",
            "Disabled" => "bg-secondary-lt",
            "Failed" => "bg-danger-lt",
            _ => "bg-success-lt"
        };
    }

    private static string ResultText(RoutineDefinition routine)
    {
        if (routine.LastRun == null) return "Never ran";

        return routine.LastRunSuccess ? "Success" : "Failed";
    }

    private static string ResultClass(RoutineDefinition routine)
    {
        if (routine.LastRun == null) return "text-muted";

        return routine.LastRunSuccess ? "text-success" : "text-danger";
    }

    private static string IntervalText(int seconds)
    {
        if (seconds <= 0) return "Manual";

        if (seconds < 60) return $"{seconds}s";

        if (seconds < 3600) return $"{seconds / 60}m";

        if (seconds < 86400) return $"{seconds / 3600}h";

        return $"{seconds / 86400}d";
    }

    private static string DateText(DateTime? value)
    {
        if (value == null) return "—";

        return TimeAgo(value.Value);
    }

    private static string DateText(DateTime value)
    {
        return TimeAgo(value);
    }

    private static string DisplayText(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "—" : value;
    }

    private static string TimeAgo(DateTime value)
    {
        var utc = value.Kind == DateTimeKind.Utc
            ? value
            : value.ToUniversalTime();

        var diff = DateTime.UtcNow - utc;

        if (diff.TotalSeconds < 0)
        {
            diff = utc - DateTime.UtcNow;

            return diff switch
            {
                { TotalMinutes: < 1 } => "soon",
                { TotalHours: < 1 } => $"in {(int)diff.TotalMinutes}m",
                { TotalDays: < 1 } => $"in {(int)diff.TotalHours}h",
                { TotalDays: < 7 } => $"in {(int)diff.TotalDays}d",
                { TotalDays: < 30 } => $"in {(int)(diff.TotalDays / 7)}w",
                _ => $"in {(int)(diff.TotalDays / 30)}mo"
            };
        }

        return diff switch
        {
            { TotalMinutes: < 1 } => "just now",
            { TotalHours: < 1 } => $"{(int)diff.TotalMinutes}m ago",
            { TotalDays: < 1 } => $"{(int)diff.TotalHours}h ago",
            { TotalDays: < 7 } => $"{(int)diff.TotalDays}d ago",
            { TotalDays: < 30 } => $"{(int)(diff.TotalDays / 7)}w ago",
            _ => $"{(int)(diff.TotalDays / 30)}mo ago"
        };
    }
}