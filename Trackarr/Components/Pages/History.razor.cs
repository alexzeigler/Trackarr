using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Trackarr.Core.Health;
using Trackarr.Core.Queue;
using Trackarr.Core.Server;
using Trackarr.Services.Media;

namespace Trackarr.Components.Pages;

public partial class History : ComponentBase
{
    private const int PageSize = 25;
    private HistoryTab _activeTab = HistoryTab.DownloadEvents;
    private bool _chartsPending;

    private HistoryPageData _data = new();

    private DateTime _fromDate = DateTime.Today.AddDays(-30);
    private List<string> _mediaTypes = [];

    private int? _serverFilter;
    private List<MediaServer> _servers = [];
    private List<string> _statuses = [];
    private string? _statusFilter;

    private bool _tableInitialized;
    private DateTime _toDate = DateTime.Today;
    private string? _typeFilter;

    [Inject] private HistoryService HistoryService { get; set; } = null!;
    [Inject] private IJSRuntime Js { get; set; } = null!;

    private bool IsLoading { get; set; }

    private IEnumerable<HistoryRow> FilteredRows => _data.Rows
        .Where(row => row.Tab == _activeTab)
        .Where(row => string.IsNullOrWhiteSpace(_statusFilter) ||
                      string.Equals(row.Status, _statusFilter, StringComparison.OrdinalIgnoreCase));

    protected override async Task OnInitializedAsync()
    {
        await LoadAsync();
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!_tableInitialized && FilteredRows.Any())
        {
            await Js.InvokeVoidAsync("trackarrTable.init", "history-table", "history-table-search",
                "history-page-count", PageSize, Array.Empty<string>());
            _tableInitialized = true;
        }
        else if (_tableInitialized)
        {
            await Js.InvokeVoidAsync("trackarrTable.reindex", "history-table");
        }

        if (_chartsPending)
            try
            {
                await Js.InvokeVoidAsync("trackarrHistoryCharts.renderActivity", "history-activity-chart",
                    ActivityChartPayload());
                await Js.InvokeVoidAsync("trackarrHistoryCharts.renderBreakdown", "history-breakdown-chart",
                    BreakdownChartPayload());
                _chartsPending = false;
            }
            catch (JSException)
            {
                _chartsPending = false;
            }
    }

    private async Task LoadAsync()
    {
        IsLoading = true;
        await InvokeAsync(StateHasChanged);

        var filter = new HistoryFilter
        {
            From = _fromDate,
            To = _toDate,
            ServerId = _serverFilter,
            MediaType = _typeFilter
        };

        _data = await HistoryService.GetHistoryAsync(filter);
        _servers = _data.Servers;
        _mediaTypes = _data.Rows
            .Select(row => row.MediaType.ToString())
            .Where(value =>
                !string.Equals(value, TrackedMediaType.Unknown.ToString(), StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value)
            .ToList();

        _statuses = _data.Rows
            .Select(row => row.Status)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value)
            .ToList();

        IsLoading = false;
        _chartsPending = true;

        await InvokeAsync(StateHasChanged);
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

    private Task OnStatusChanged(ChangeEventArgs args)
    {
        _statusFilter = string.IsNullOrWhiteSpace(args.Value?.ToString()) ? null : args.Value.ToString();
        return InvokeAsync(StateHasChanged);
    }

    private void SetTab(HistoryTab tab)
    {
        _activeTab = tab;
        _tableInitialized = false;
    }

    private string TabClass(HistoryTab tab)
    {
        return _activeTab == tab ? "nav-link active" : "nav-link";
    }

    private object ActivityChartPayload()
    {
        return new
        {
            labels = _data.Activity.Select(point => point.Date.ToString("MMM d", CultureInfo.InvariantCulture))
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
        return value.ToLocalTime().ToString("MMM d, yyyy h:mm tt", CultureInfo.InvariantCulture);
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
        return type.ToString() switch
        {
            "Movie" => "badge bg-purple-lt text-purple",
            "Episode" => "badge bg-blue-lt text-blue",
            "Series" => "badge bg-indigo-lt text-indigo",
            "Season" => "badge bg-indigo-lt text-indigo",
            _ => "badge bg-secondary-lt text-secondary"
        };
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
}