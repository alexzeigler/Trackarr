using Microsoft.AspNetCore.Components;
using Trackarr.Core.Event;

namespace Trackarr.Components.Pages.System;

public partial class Events : ComponentBase
{
    private int _currentPage = 1;

    private string _filterText = string.Empty;
    private bool _isRefreshing;
    private List<LogEvent> _logs = [];
    private int _pageSize = 50;
    private string _selectedLevel = string.Empty;
    private LogEvent? _selectedLog;

    private string _sortColumn = "time";
    private bool _sortDescending = true;

    private string FilterText
    {
        get => _filterText;
        set
        {
            _filterText = value;
            _currentPage = 1;
        }
    }

    private string SelectedLevel
    {
        get => _selectedLevel;
        set
        {
            _selectedLevel = value;
            _currentPage = 1;
        }
    }

    private int PageSize
    {
        get => _pageSize;
        set
        {
            _pageSize = Math.Clamp(value, 10, 250);
            _currentPage = 1;
        }
    }

    private int WarningCount => _logs.Count(x => x.LogLevel >= LogLevel.Warning && x.LogLevel != LogLevel.None);
    private int ErrorCount => _logs.Count(x => x.LogLevel is LogLevel.Error or LogLevel.Critical);
    private int RecentCount => _logs.Count(x => x.TimeStamp.ToUniversalTime() >= DateTime.UtcNow.AddMinutes(-15));

    private List<LogEvent> FilteredLogs
    {
        get
        {
            IEnumerable<LogEvent> query = _logs;

            if (!string.IsNullOrWhiteSpace(_selectedLevel) && Enum.TryParse(_selectedLevel, out LogLevel level))
                query = query.Where(x => x.LogLevel == level);

            if (!string.IsNullOrWhiteSpace(_filterText))
                query = query.Where(x =>
                    Contains(x.Source, _filterText) || Contains(x.Message, _filterText) ||
                    Contains(x.Exception, _filterText) || Contains(x.LogLevel.ToString(), _filterText));

            query = (_sortColumn, _sortDescending) switch
            {
                ("time", true) => query.OrderByDescending(x => x.TimeStamp),
                ("time", false) => query.OrderBy(x => x.TimeStamp),
                ("level", true) => query.OrderByDescending(x => x.LogLevel),
                ("level", false) => query.OrderBy(x => x.LogLevel),
                ("source", true) => query.OrderByDescending(x => x.Source),
                ("source", false) => query.OrderBy(x => x.Source),
                _ => query.OrderByDescending(x => x.TimeStamp)
            };

            return query.ToList();
        }
    }

    private List<LogEvent> PagedLogs => FilteredLogs.Skip((_currentPage - 1) * _pageSize).Take(_pageSize).ToList();

    private int TotalPages => Math.Max(1, (int)Math.Ceiling(FilteredLogs.Count / (double)_pageSize));
    private int PageStart => FilteredLogs.Count == 0 ? 0 : (_currentPage - 1) * _pageSize + 1;
    private int PageEnd => Math.Min(_currentPage * _pageSize, FilteredLogs.Count);

    protected override async Task OnInitializedAsync()
    {
        await LoadLogsAsync();
    }

    private async Task LoadLogsAsync()
    {
        Logger.LogInformation("Refreshing logs...");
        _isRefreshing = true;

        try
        {
            _logs = await EventService.GetAllAsync();
            _currentPage = 1;
        }
        finally
        {
            _isRefreshing = false;
            Logger.LogInformation("Loaded {LogsCount} logs", _logs.Count);
        }
    }

    private async Task DeleteAllLogs()
    {
        await EventService.ClearAllAsync();
        _logs.Clear();
        _selectedLog = null;
        _currentPage = 1;
        await LoadLogsAsync();
    }

    private void SelectLog(LogEvent log)
    {
        _selectedLog = log;
    }

    private void SortBy(string column)
    {
        if (_sortColumn == column)
        {
            _sortDescending = !_sortDescending;
        }
        else
        {
            _sortColumn = column;
            _sortDescending = column == "time";
        }

        _currentPage = 1;
    }

    private string SortIcon(string column)
    {
        if (_sortColumn != column) return "ti ti-selector";

        return _sortDescending ? "ti ti-chevron-down" : "ti ti-chevron-up";
    }

    private void PreviousPage()
    {
        if (_currentPage > 1) _currentPage--;
    }

    private void NextPage()
    {
        if (_currentPage < TotalPages) _currentPage++;
    }

    private void FirstPage()
    {
        _currentPage = 1;
    }

    private void LastPage()
    {
        _currentPage = TotalPages;
    }

    private static bool Contains(string? value, string filter)
    {
        return !string.IsNullOrWhiteSpace(value) && value.Contains(filter, StringComparison.OrdinalIgnoreCase);
    }

    private static string LevelIcon(LogLevel level)
    {
        return level switch
        {
            LogLevel.Trace => "ti ti-circle-dotted text-muted",
            LogLevel.Debug => "ti ti-bug text-info",
            LogLevel.Information => "ti ti-info-circle text-info",
            LogLevel.Warning => "ti ti-alert-circle text-warning",
            LogLevel.Error => "ti ti-circle-x text-danger",
            LogLevel.Critical => "ti ti-alert-octagon text-danger",
            _ => "ti ti-minus text-muted"
        };
    }

    private static string LevelBadgeClass(LogLevel level)
    {
        return level switch
        {
            LogLevel.Trace => "bg-secondary-lt",
            LogLevel.Debug => "bg-info-lt",
            LogLevel.Information => "bg-blue-lt",
            LogLevel.Warning => "bg-warning-lt",
            LogLevel.Error => "bg-danger-lt",
            LogLevel.Critical => "bg-red-lt",
            _ => "bg-secondary-lt"
        };
    }
}