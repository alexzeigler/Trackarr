using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Trackarr.Components.Modals;
using Trackarr.Core.Server;
using Trackarr.Services.UI;

namespace Trackarr.Components.Pages;

public partial class Servers : ComponentBase, IAsyncDisposable
{
    private readonly CancellationTokenSource _refreshCancellation = new();
    private ConfirmationDialog? _confirmModal;
    private string? _confirmText = "Are you sure you want to delete this server?";

    private string _searchTerm = string.Empty;
    private int _selectedServerId;
    private AddServerModal _serverModal = new();
    private List<MediaServer> _servers = [];
    private string _sortColumn = "name";
    private bool _sortDescending;
    private string? _statusFilter;
    private string? _typeFilter;
    [Inject] private AppStateService AppState { get; set; } = null!;

    private bool IsRefreshing { get; set; }

    private bool HasActiveFilters =>
        !string.IsNullOrWhiteSpace(_searchTerm) || _typeFilter != null || _statusFilter != null;

    private List<string> AvailableTypes =>
        _servers.Select(s => s.ServiceType.ToString()).Distinct().OrderBy(t => t).ToList();

    private List<MediaServer> FilteredServers => BuildFilteredServers();

    private List<MediaServer> BuildFilteredServers()
    {
        var rows = _servers.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(_searchTerm))
        {
            var search = _searchTerm.Trim();
            rows = rows.Where(s => s.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                                   s.BaseUrl.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                                   s.ServiceType.ToString().Contains(search, StringComparison.OrdinalIgnoreCase) ||
                                   (s.Metadata?.Version?.Contains(search, StringComparison.OrdinalIgnoreCase) ??
                                    false));
        }

        if (_typeFilter != null)
        {
            rows = rows.Where(s =>
                string.Equals(s.ServiceType.ToString(), _typeFilter, StringComparison.OrdinalIgnoreCase));
        }

        if (_statusFilter != null)
        {
            rows = _statusFilter switch
            {
                "online" => rows.Where(s => s.IsOnline && s.IsAuthenticated),
                "offline" => rows.Where(s => !s.IsOnline),
                "error" => rows.Where(s => (s.IsOnline && !s.IsAuthenticated) || s.LastError != null),
                _ => rows
            };
        }

        rows = _sortColumn switch
        {
            "type" => _sortDescending
                ? rows.OrderByDescending(s => s.ServiceType.ToString())
                : rows.OrderBy(s => s.ServiceType.ToString()),
            "status" => _sortDescending
                ? rows.OrderByDescending(s => GetServerStatusSort(s))
                : rows.OrderBy(s => GetServerStatusSort(s)),
            "queue" => _sortDescending
                ? rows.OrderByDescending(s => s.LastQueueCount ?? 0)
                : rows.OrderBy(s => s.LastQueueCount ?? 0),
            "missing" => _sortDescending
                ? rows.OrderByDescending(s => s.LastMissingCount ?? 0)
                : rows.OrderBy(s => s.LastMissingCount ?? 0),
            "ping" => _sortDescending
                ? rows.OrderByDescending(s => s.LastPingMs ?? long.MaxValue)
                : rows.OrderBy(s => s.LastPingMs ?? long.MaxValue),
            "sync" => _sortDescending
                ? rows.OrderByDescending(s => s.LastQueueFetchAt)
                : rows.OrderBy(s => s.LastQueueFetchAt),
            "version" => _sortDescending
                ? rows.OrderByDescending(s => s.Metadata?.Version ?? "")
                : rows.OrderBy(s => s.Metadata?.Version ?? ""),
            _ => _sortDescending ? rows.OrderByDescending(s => s.Name) : rows.OrderBy(s => s.Name)
        };

        return rows.ToList();
    }

    #region Lifecycle

    public async ValueTask DisposeAsync()
    {
        AppState.OnRefreshRequested -= RefreshAsync;
        await _refreshCancellation.CancelAsync();
        _refreshCancellation.Dispose();
    }

    protected override async Task OnInitializedAsync()
    {
        AppState.OnRefreshRequested += RefreshAsync;
        _servers = await MediaServerService.GetAllServersAsync();
        _ = RefreshLoopAsync();
    }

    private async Task RefreshLoopAsync()
    {
        try
        {
            using PeriodicTimer timer = new(TimeSpan.FromSeconds(30));
            while (await timer.WaitForNextTickAsync(_refreshCancellation.Token))
                await RefreshAsync();
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task RefreshAsync()
    {
        IsRefreshing = true;
        await InvokeAsync(StateHasChanged);
        _servers = await MediaServerService.GetAllServersAsync();
        IsRefreshing = false;
        await InvokeAsync(StateHasChanged);
    }

    #endregion

    #region Filters & Sort

    private void OnSearchChanged(ChangeEventArgs args)
    {
        _searchTerm = args.Value?.ToString() ?? "";
    }

    private void OnTypeFilterChanged(ChangeEventArgs args)
    {
        _typeFilter = string.IsNullOrWhiteSpace(args.Value?.ToString()) ? null : args.Value.ToString();
    }

    private void OnStatusFilterChanged(ChangeEventArgs args)
    {
        _statusFilter = string.IsNullOrWhiteSpace(args.Value?.ToString()) ? null : args.Value.ToString();
    }

    private void ClearFilters()
    {
        _searchTerm = string.Empty;
        _typeFilter = null;
        _statusFilter = null;
    }

    private void SortBy(string column)
    {
        if (_sortColumn == column)
            _sortDescending = !_sortDescending;
        else
        {
            _sortColumn = column;
            _sortDescending = column is "queue" or "missing" or "sync" or "ping";
        }
    }

    private string SortClass(string column)
    {
        return _sortColumn != column ? "table-sort" : _sortDescending ? "table-sort desc" : "table-sort asc";
    }

    #endregion

    #region Server Actions

    private async Task AddServer()
    {
        await _serverModal.OpenForAdd();
    }

    private async Task SyncAll()
    {
        IsRefreshing = true;
        await InvokeAsync(StateHasChanged);
        foreach (var server in _servers)
            await SyncServer(server);
        IsRefreshing = false;
        await InvokeAsync(StateHasChanged);
    }

    private async Task TestConnection(MediaServer row)
    {
        await MediaServerService.TestConnectionAsync(row);
    }

    private async Task SyncServer(MediaServer row)
    {
        await MediaServerService.UpdateServerAsync(row);
        await MediaServerService.GetQueueAsync(row, true);
        if (row.ServiceType == ServiceType.Sonarr)
            await MediaServerService.GetMissingEpisodes(row, true);
        else
            await MediaServerService.GetMissingMovies(row, true);
    }

    private async Task EditServer(MediaServer row)
    {
        await _serverModal.OpenForEdit(row);
    }

    private async void SendDeleteToDb(int id)
    {
        await MediaServerService.DeleteServerAsync(id);
        await Js.InvokeVoidAsync("modalHelper.hide", "confirmationModal");
    }

    private async Task RemoveServer(MediaServer row)
    {
        _selectedServerId = row.Id;
        _confirmText = $"Are you sure you want to remove '{row.Name}'?";
        await Js.InvokeVoidAsync("modalHelper.show", "confirmationModal");
    }

    #endregion

    #region Display Helpers

    private static string GetServerStatusText(MediaServer server)
    {
        if (!server.IsEnabled) return "Disabled";
        if (!server.IsOnline) return "Offline";
        if (!server.IsAuthenticated) return "Auth Failed";
        if (server.LastError != null) return "Error";
        return "Online";
    }

    private static string GetServerStatusBadge(MediaServer server)
    {
        return GetServerStatusText(server) switch
        {
            "Online" => "badge bg-success-lt text-success",
            "Offline" => "badge bg-danger-lt text-danger",
            "Auth Failed" => "badge bg-warning-lt text-warning",
            "Error" => "badge bg-orange-lt text-orange",
            "Disabled" => "badge bg-secondary-lt text-secondary",
            _ => "badge bg-secondary-lt text-secondary"
        };
    }

    private static int GetServerStatusSort(MediaServer server)
    {
        return GetServerStatusText(server) switch
        {
            "Online" => 0,
            "Error" => 1,
            "Auth Failed" => 2,
            "Offline" => 3,
            "Disabled" => 4,
            _ => 5
        };
    }

    #endregion
}
