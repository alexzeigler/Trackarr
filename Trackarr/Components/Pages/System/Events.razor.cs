using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Trackarr.Core.Event;
using Trackarr.Services.Settings;

namespace Trackarr.Components.Pages.System;

public partial class Events : ComponentBase
{
    #region Injected Services

    [Inject] private SettingsService Settings { get; set; } = null!;

    #endregion

    #region Lifecycle

    protected override async Task OnInitializedAsync()
    {
        _pageSize = Settings.GetInt("Display", "EventsPageSize", 50);
        await LoadLogsAsync();
    }

    #endregion

    #region Data Loading

    private async Task LoadLogsAsync()
    {
        _isRefreshing = true;
        try
        {
            _logs = await EventService.GetAllAsync();
            _currentPage = 1;
        }
        finally
        {
            _isRefreshing = false;
        }
    }

    #endregion

    #region State

    private int _currentPage = 1;
    private string _filterText = string.Empty;
    private bool _isRefreshing;
    private List<LogEvent> _logs = [];
    private int _pageSize;
    private string _selectedLevel = string.Empty;
    private LogEvent? _selectedLog;
    private string _sortColumn = "time";
    private bool _sortDescending = true;

    private string TableDensityClass =>
        Settings.GetString("Display", "TableDensity", "normal") switch
        {
            "compact" => "table-density-compact",
            "comfortable" => "table-density-comfortable",
            _ => ""
        };

    private bool HasActiveFilters =>
        !string.IsNullOrWhiteSpace(_filterText) || !string.IsNullOrWhiteSpace(_selectedLevel);

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

    #endregion

    #region Event Handlers

    private async Task DeleteAllLogs()
    {
        await EventService.ClearAllAsync();
        _logs.Clear();
        _selectedLog = null;
        _currentPage = 1;
        await LoadLogsAsync();
    }

    private async Task ShowLog(LogEvent log)
    {
        _selectedLog = log;
        await Js.InvokeVoidAsync("modalHelper.show", "logDetailsModal");
    }

    private void OnSearchChanged(ChangeEventArgs args)
    {
        _filterText = args.Value?.ToString() ?? "";
        _currentPage = 1;
    }

    private void OnLevelChanged(ChangeEventArgs args)
    {
        _selectedLevel = args.Value?.ToString() ?? "";
        _currentPage = 1;
    }

    private void OnPageSizeChanged(ChangeEventArgs args)
    {
        if (int.TryParse(args.Value?.ToString(), out var size))
            _pageSize = Math.Clamp(size, 10, 250);
        _currentPage = 1;
    }

    private void ClearFilters()
    {
        _filterText = string.Empty;
        _selectedLevel = string.Empty;
        _currentPage = 1;
    }

    private void SortBy(string column)
    {
        if (_sortColumn == column)
            _sortDescending = !_sortDescending;
        else
        {
            _sortColumn = column;
            _sortDescending = column == "time";
        }

        _currentPage = 1;
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

    #endregion

    #region Display Helpers

    private string SortClass(string column)
    {
        return _sortColumn != column ? "table-sort" : _sortDescending ? "table-sort desc" : "table-sort asc";
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
            LogLevel.Trace => "badge bg-secondary-lt text-secondary",
            LogLevel.Debug => "badge bg-info-lt text-info",
            LogLevel.Information => "badge bg-blue-lt text-blue",
            LogLevel.Warning => "badge bg-warning-lt text-warning",
            LogLevel.Error => "badge bg-danger-lt text-danger",
            LogLevel.Critical => "badge bg-red-lt text-red",
            _ => "badge bg-secondary-lt text-secondary"
        };
    }

    #endregion
}
