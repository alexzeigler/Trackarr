using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Trackarr.Core.Health;
using Trackarr.Core.Queue;
using Trackarr.Core.Server;
using Trackarr.Services.Media;
using Trackarr.Services.Settings;
using static Trackarr.Utils;

namespace Trackarr.Components.Pages;

public partial class History : ComponentBase
{
    #region Data Loading

    private async Task LoadAsync()
    {
        IsLoading = true;
        await InvokeAsync(StateHasChanged);

        var filter = new HistoryFilter
        {
            From = _fromDate, To = _toDate, ServerId = _serverFilter, MediaType = _typeFilter
        };

        _data = await HistoryService.GetHistoryAsync(filter);
        _servers = _data.Servers;
        _mediaTypes = _data.Rows.Select(row => row.MediaType.ToString())
            .Where(value =>
                !string.Equals(value, TrackedMediaType.Unknown.ToString(), StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(value => value).ToList();

        _statuses = _data.Rows.Select(row => row.Status).Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(value => value).ToList();

        IsLoading = false;
        _chartsPending = true;

        await InvokeAsync(StateHasChanged);
    }

    #endregion

    private record SeriesLegend(string Label, string Color, int Total);

    #region Injected Services

    [Inject] private SettingsService Settings { get; set; } = null!;
    [Inject] private HistoryService HistoryService { get; set; } = null!;
    [Inject] private IJSRuntime Js { get; set; } = null!;

    #endregion

    #region State

    private bool _hasServers = true;
    private HistoryTab _activeTab = HistoryTab.DownloadEvents;
    private bool _chartsPending;

    private HistoryPageData _data = new();

    private DateTime _fromDate = DateTime.Today.AddDays(-7);
    private List<string> _mediaTypes = [];
    private int _page = 1;
    private string _search = "";

    private int? _serverFilter;
    private List<MediaServer> _servers = [];
    private string _sortColumn = "date";
    private bool _sortDescending = true;
    private List<string> _statuses = [];
    private string? _statusFilter;

    private bool _tableInitialized;
    private DateTime _toDate = DateTime.Today;
    private string? _typeFilter;

    private int PageSize => Settings.GetInt("Display", "HistoryPageSize", 25);

    private string TableDensityClass =>
        Settings.GetString("Display", "TableDensity", "normal") switch
        {
            "compact" => "table-density-compact",
            "comfortable" => "table-density-comfortable",
            _ => ""
        };

    private IEnumerable<HistoryRow> TabRows =>
        _data.Rows.Where(r => r.Tab == _activeTab)
            .Where(r => string.IsNullOrWhiteSpace(_statusFilter) ||
                        string.Equals(r.Status, _statusFilter, StringComparison.OrdinalIgnoreCase)).Where(r =>
                r.Item != null && (string.IsNullOrWhiteSpace(_search) ||
                                   r.Item.Contains(_search, StringComparison.OrdinalIgnoreCase) ||
                                   r.Event.Contains(_search, StringComparison.OrdinalIgnoreCase) ||
                                   r.Server.Contains(_search, StringComparison.OrdinalIgnoreCase) ||
                                   (r.Details?.Contains(_search, StringComparison.OrdinalIgnoreCase) ?? false)));

    private List<HistoryRow> SortedRows
    {
        get
        {
            var rows = _sortColumn switch
            {
                "event" => Order(TabRows, r => r.Event),
                "item" => Order(TabRows, r => r.Item),
                "type" => Order(TabRows, r => r.MediaType.ToString()),
                "server" => Order(TabRows, r => r.Server),
                "status" => Order(TabRows, r => r.Status),
                _ => Order(TabRows, r => r.OccurredAt)
            };
            return rows.ToList();
        }
    }

    private List<SeriesLegend> ActivitySeries =>
    [
        new("Completed", "#2fb344", _data.Activity.Sum(p => p.Completed)),
        new("Failed", "#d63939", _data.Activity.Sum(p => p.Failed)),
        new("Removed", "#f76707", _data.Activity.Sum(p => p.Removed)),
        new("Added", "#4299e1", _data.Activity.Sum(p => p.Added))
    ];

    private List<SeriesLegend> SearchSeries =>
    [
        new("Searches Pushed", "#7738e9", _data.Activity.Sum(p => p.SearchesPushed)),
        new("Cascaded", "#4299e1", _data.Activity.Sum(p => p.Cascaded)),
        new("Abandoned", "#d63939", _data.Activity.Sum(p => p.Abandoned)),
        new("Resolved", "#2fb344", _data.Activity.Sum(p => p.Resolved))
    ];

    private IEnumerable<HistoryRow> PagedRows => SortedRows.Skip((_page - 1) * PageSize).Take(PageSize);
    private int TotalPages => Math.Max(1, (int)Math.Ceiling(SortedRows.Count / (double)PageSize));

    private bool IsLoading { get; set; }

    private IEnumerable<HistoryRow> FilteredRows =>
        _data.Rows.Where(row => row.Tab == _activeTab).Where(row =>
            string.IsNullOrWhiteSpace(_statusFilter) ||
            string.Equals(row.Status, _statusFilter, StringComparison.OrdinalIgnoreCase));

    #endregion

    #region Lifecycle

    protected override async Task OnInitializedAsync()
    {
        _hasServers = (await MediaServerService.GetAllServersAsync()).Count > 0;
        if (!_hasServers) return;

        await LoadAsync();
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!_chartsPending) return;
        _chartsPending = false;
        try
        {
            await Js.InvokeVoidAsync("trackarrHistoryCharts.renderSearchActivity", "history-search-chart",
                SearchActivityChartPayload());
            await Js.InvokeVoidAsync("trackarrHistoryCharts.renderActivity", "history-activity-chart",
                ActivityChartPayload());
            await Js.InvokeVoidAsync("trackarrHistoryCharts.renderBreakdown", "history-breakdown-chart",
                BreakdownChartPayload());

            await Js.InvokeVoidAsync("trackarrHistoryCharts.renderSpark", "spark-searches", "bar",
                _data.Activity.Select(p => p.SearchesPushed).ToArray(), "#7738e9");
            await Js.InvokeVoidAsync("trackarrHistoryCharts.renderSpark", "spark-cascaded", "line",
                _data.Activity.Select(p => p.Cascaded).ToArray(), "#4299e1");
            await Js.InvokeVoidAsync("trackarrHistoryCharts.renderSpark", "spark-abandoned", "line",
                _data.Activity.Select(p => p.Abandoned).ToArray(), "#d63939");
            await Js.InvokeVoidAsync("trackarrHistoryCharts.renderSpark", "spark-resolved", "area",
                _data.Activity.Select(p => p.Resolved).ToArray(), "#2fb344");
        }
        catch (JSException)
        {
        }
    }

    #endregion

    #region Event Handlers

    private void SortBy(string column)
    {
        if (_sortColumn == column)
        {
            _sortDescending = !_sortDescending;
        }
        else
        {
            _sortColumn = column;
            _sortDescending = true;
        }

        _page = 1;
    }

    private void OnSearchChanged(ChangeEventArgs args)
    {
        _search = args.Value?.ToString() ?? "";
        _page = 1;
    }

    private void Prev()
    {
        if (_page > 1) _page--;
    }

    private void Next()
    {
        if (_page < TotalPages) _page++;
    }

    private async Task OnFromDateChanged(ChangeEventArgs args)
    {
        if (DateTime.TryParse(args.Value?.ToString(), out var value))
        {
            _fromDate = value.Date;
            await LoadAsync();
        }
    }

    private async Task OnToDateChanged(ChangeEventArgs args)
    {
        if (DateTime.TryParse(args.Value?.ToString(), out var value))
        {
            _toDate = value.Date;
            await LoadAsync();
        }
    }

    private async Task OnServerChanged(ChangeEventArgs args)
    {
        _serverFilter = int.TryParse(args.Value?.ToString(), out var value) ? value : null;
        await LoadAsync();
    }

    private async Task OnTypeChanged(ChangeEventArgs args)
    {
        _typeFilter = string.IsNullOrWhiteSpace(args.Value?.ToString()) ? null : args.Value.ToString();
        await LoadAsync();
    }

    private void SetTab(HistoryTab tab)
    {
        _activeTab = tab;
        _page = 1;
    }

    private Task OnStatusChanged(ChangeEventArgs args)
    {
        _statusFilter = string.IsNullOrWhiteSpace(args.Value?.ToString()) ? null : args.Value.ToString();
        _page = 1;
        return InvokeAsync(StateHasChanged);
    }

    #endregion

    #region Display Helpers

    private IEnumerable<HistoryRow> Order<T>(IEnumerable<HistoryRow> rows, Func<HistoryRow, T> key)
    {
        return _sortDescending ? rows.OrderByDescending(key) : rows.OrderBy(key);
    }

    private string SortClass(string column)
    {
        return _sortColumn != column ? "table-sort" : _sortDescending ? "table-sort desc" : "table-sort asc";
    }

    private string TabClass(HistoryTab tab)
    {
        return _activeTab == tab ? "nav-link active" : "nav-link";
    }

    private object SearchActivityChartPayload()
    {
        return new
        {
            labels = _data.Activity.Select(p => p.Date.ToString("MMM d", CultureInfo.InvariantCulture)).ToArray(),
            searches = _data.Activity.Select(p => p.SearchesPushed).ToArray(),
            cascaded = _data.Activity.Select(p => p.Cascaded).ToArray(),
            abandoned = _data.Activity.Select(p => p.Abandoned).ToArray(),
            resolved = _data.Activity.Select(p => p.Resolved).ToArray()
        };
    }

    private object ActivityChartPayload()
    {
        return new
        {
            labels =
                _data.Activity.Select(point => point.Date.ToString("MMM d", CultureInfo.InvariantCulture))
                    .ToArray(),
            completed = _data.Activity.Select(point => point.Completed).ToArray(),
            failed = _data.Activity.Select(point => point.Failed).ToArray(),
            removed = _data.Activity.Select(point => point.Removed).ToArray(),
            added = _data.Activity.Select(point => point.Added).ToArray()
        };
    }

    private object BreakdownChartPayload()
    {
        return new
        {
            labels = _data.Breakdown.Select(item => item.Label).ToArray(),
            series = _data.Breakdown.Select(item => item.Count).ToArray()
        };
    }

    private static string FormatDate(DateTime value)
    {
        return Utils.ToLocal(value).ToString("MMM d, yyyy h:mm tt", CultureInfo.InvariantCulture);
    }

    private static string FormatNumber(int value)
    {
        return value.ToString("N0", CultureInfo.InvariantCulture);
    }

    private static string FormatPercent(decimal value)
    {
        return value <= 0 ? "0%" : $"{value:0.0}%";
    }

    private static string FormatDuration(TimeSpan? value)
    {
        if (value == null || value.Value.TotalSeconds <= 0) return "—";

        if (value.Value.TotalDays >= 1) return $"{(int)value.Value.TotalDays}d {value.Value.Hours}h";

        if (value.Value.TotalHours >= 1) return $"{(int)value.Value.TotalHours}h {value.Value.Minutes}m";

        return $"{Math.Max(1, value.Value.Minutes)}m";
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB", "PB"];
        var size = (double)bytes;
        var unit = 0;

        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }

        return $"{size:0.##} {units[unit]}";
    }

    private static string DisplayType(TrackedMediaType type)
    {
        return type == TrackedMediaType.Unknown ? "System" : type.ToString();
    }

    private static string TypeBadgeClass(TrackedMediaType type)
    {
        return MediaTypeBadgeClass(type.ToString());
    }

    private static string StatusBadgeClass(string status)
    {
        return status.ToLowerInvariant() switch
        {
            "completed" => "badge bg-success-lt text-success",
            "success" => "badge bg-success-lt text-success",
            "failed" => "badge bg-danger-lt text-danger",
            "removed" => "badge bg-orange-lt text-orange",
            "warning" => "badge bg-warning-lt text-warning",
            "added" => "badge bg-blue-lt text-blue",
            "active" => "badge bg-blue-lt text-blue",
            _ => "badge bg-secondary-lt text-secondary"
        };
    }

    private static string EventIcon(HistoryRow row)
    {
        return row.Status.ToLowerInvariant() switch
        {
            "completed" => "ti ti-circle-check",
            "success" => "ti ti-circle-check",
            "failed" => "ti ti-circle-x",
            "removed" => "ti ti-trash",
            "warning" => "ti ti-alert-triangle",
            "added" => "ti ti-plus",
            _ => "ti ti-info-circle"
        };
    }

    private static string EventIconClass(HistoryRow row)
    {
        return row.Status.ToLowerInvariant() switch
        {
            "completed" => "text-success",
            "success" => "text-success",
            "failed" => "text-danger",
            "removed" => "text-orange",
            "warning" => "text-warning",
            "added" => "text-blue",
            _ => "text-secondary"
        };
    }

    private static string BreakdownDotClass(string label)
    {
        return label.ToLowerInvariant() switch
        {
            "completed" => "bg-success",
            "failed" => "bg-danger",
            "removed" => "bg-orange",
            "added" => "bg-blue",
            _ => "bg-secondary"
        };
    }

    #endregion
}
