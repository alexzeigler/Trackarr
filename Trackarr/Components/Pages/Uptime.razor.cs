using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Trackarr.Components.Modals;
using Trackarr.Core.Exception;
using Trackarr.Core.Server;
using Trackarr.Services.Media;
using Trackarr.Services.Routines;
using static Trackarr.Utils;

namespace Trackarr.Components.Pages;

public partial class Uptime : ComponentBase, IAsyncDisposable
{
    private const int PageSize = 25;

    #region Parameters

    [SupplyParameterFromQuery(Name = "serverId")]
    public int? InitialServerId { get; set; }

    #endregion

    #region Injected Services

    [Inject] private IJSRuntime Js { get; set; } = null!;
    [Inject] private RoutineRunner RoutineRunner { get; set; } = null!;
    [Inject] private NavigationManager Nav { get; set; } = null!;

    #endregion

    #region State

    private List<MediaServer> _servers = [];
    private bool _hasServers = true;
    private int _selectedServerId;
    private List<ServerStatusSample> _samples = [];
    private List<ServerHealthEvent> _events = [];
    private List<RemoteHealthMessage> _remoteHealth = [];
    private int _periodHours = 24;
    private int _currentPage = 1;
    private bool _chartsPending;
    private DotNetObjectReference<Uptime>? _ref;

    private string? _actionError;
    private AddServerModal _serverModal = new();
    private ConfirmationDialog? _confirmModal;
    private string? _confirmText = "Are you sure you want to remove this server?";

    private MediaServer? SelectedServer => _servers.FirstOrDefault(s => s.Id == _selectedServerId);

    private List<ServerHealthEvent> PagedEvents =>
        _events.Skip((_currentPage - 1) * PageSize).Take(PageSize).ToList();

    private int TotalPages => Math.Max(1, (int)Math.Ceiling(_events.Count / (double)PageSize));

    #endregion

    #region Lifecycle

    protected override async Task OnInitializedAsync()
    {
        _servers = (await MediaServerService.GetAllServersAsync())
            .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ToList();
        _hasServers = _servers.Count > 0;
        if (!_hasServers) return;

        _selectedServerId = InitialServerId is { } id && _servers.Any(s => s.Id == id)
            ? id
            : _servers.FirstOrDefault()?.Id ?? 0;

        await LoadAsync();
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            await Js.InvokeVoidAsync("tomSelectHelper.init", "uptimeServerSelect",
                _ref ??= DotNetObjectReference.Create(this), nameof(OnServerSelected));
        }

        if (!_chartsPending) return;
        _chartsPending = false;

        try
        {
            await Js.InvokeVoidAsync("trackarrUptimeChart.render", "uptime-chart", ChartPayload());
        }
        catch (JSException)
        {
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await Js.InvokeVoidAsync("tomSelectHelper.destroy", "uptimeServerSelect");
            await Js.InvokeVoidAsync("trackarrUptimeChart.destroy");
        }
        catch (JSDisconnectedException)
        {
        }

        _ref?.Dispose();
    }

    #endregion

    #region Data Loading

    private async Task LoadAsync()
    {
        if (_selectedServerId == 0)
        {
            _samples = [];
            _events = [];
            _remoteHealth = [];
            return;
        }

        var since = DateTime.UtcNow.AddHours(-_periodHours);
        _samples = await MediaServerService.GetStatusSamplesAsync(_selectedServerId, since);
        _events = await MediaServerService.GetRecentHealthEventsAsync(_selectedServerId, 500);
        _remoteHealth = SelectedServer is { } server ? await MediaServerService.GetRemoteHealthAsync(server) : [];
        _currentPage = 1;
        _chartsPending = true;
    }

    #endregion

    #region Event Handlers

    [JSInvokable]
    public async Task OnServerSelected(string? value)
    {
        if (!int.TryParse(value, out var id)) return;
        _selectedServerId = id;
        await LoadAsync();
        await InvokeAsync(StateHasChanged);
    }

    private async Task OnPeriodChanged(ChangeEventArgs args)
    {
        if (!int.TryParse(args.Value?.ToString(), out var hours)) return;
        _periodHours = hours;
        await LoadAsync();
    }

    private void PreviousPage()
    {
        if (_currentPage > 1) _currentPage--;
    }

    private void NextPage()
    {
        if (_currentPage < TotalPages) _currentPage++;
    }

    private async Task EditServer()
    {
        if (SelectedServer is null) return;
        await _serverModal.OpenForEdit(SelectedServer);
    }

    private async Task ToggleEnabled()
    {
        if (SelectedServer is not { } server) return;

        server.IsEnabled = !server.IsEnabled;
        server.DisabledByHeartbeat = false;
        _actionError = null;

        try
        {
            await MediaServerService.UpdateServerAsync(server);
        }
        catch (MediaServerException ex)
        {
            _actionError = ex.Message;
        }

        await RefreshAsync();
    }

    private async Task SyncServer()
    {
        if (SelectedServer is not { } server) return;

        _actionError = null;

        try
        {
            await MediaServerService.SyncServerAsync(server);
        }
        catch (MediaServerException ex)
        {
            _actionError = ex.Message;
        }

        await RefreshAsync();
    }

    private async Task TestConnection()
    {
        if (SelectedServer is not { } server) return;

        _actionError = null;

        try
        {
            await MediaServerService.TestConnectionAsync(server);
        }
        catch (MediaServerException ex)
        {
            _actionError = ex.Message;
        }

        await RefreshAsync();
    }

    private async Task RemoveServer()
    {
        if (SelectedServer is null) return;
        _confirmText = $"Are you sure you want to remove '{SelectedServer.Name}'?";
        await Js.InvokeVoidAsync("modalHelper.show", "confirmationModal");
    }

    private async void SendDeleteToDb(int id)
    {
        await MediaServerService.DeleteServerAsync(id);
        await Js.InvokeVoidAsync("modalHelper.hide", "confirmationModal");
        Nav.NavigateTo("/servers");
    }

    private async Task RefreshAsync()
    {
        _servers = (await MediaServerService.GetAllServersAsync())
            .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ToList();
        _hasServers = _servers.Count > 0;
        if (_hasServers) await LoadAsync();
        await InvokeAsync(StateHasChanged);
    }

    #endregion

    #region Display Helpers

    private object ChartPayload()
    {
        // A real datetime axis (not a category axis) with an explicit min/max spanning the full
        // selected period — that's what makes a couple hours of data visibly "scrunch" into a sliver
        // of a 7-day or 30-day window instead of stretching to fill the whole chart width regardless
        // of how much of the period it actually covers.
        var periodStart = ToUnixMs(DateTime.UtcNow.AddHours(-_periodHours));
        var periodEnd = ToUnixMs(DateTime.UtcNow);

        var points = _samples.Select(s => new object?[] { ToUnixMs(s.SampledAt), s.PingMs }).ToArray();

        return new { points, periodStart, periodEnd };
    }

    private static long ToUnixMs(DateTime value)
    {
        return new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc)).ToUnixTimeMilliseconds();
    }

    private double UptimePercent()
    {
        return _samples.Count == 0 ? 100 : Math.Round(_samples.Count(s => s.Success) / (double)_samples.Count * 100, 1);
    }

    private string CheckIntervalText()
    {
        var seconds = RoutineRunner.GetDefinition("heartbeat")?.IntervalSeconds ?? 300;

        if (seconds % 60 != 0)
            return $"Checked every {seconds} seconds";

        var minutes = seconds / 60;
        return $"Checked every {minutes} minute{(minutes == 1 ? "" : "s")}";
    }

    private string CurrentStreakLabel()
    {
        return SelectedServer is { IsEnabled: true, IsOnline: true } ? "Currently Up For" : "Currently Down For";
    }

    private string CurrentStreakValue()
    {
        if (SelectedServer is not { } server) return "—";

        var since = _events.FirstOrDefault()?.OccurredAt ?? server.CreatedAt;
        return FormatDuration(DateTime.UtcNow - since);
    }

    private static string FormatDuration(TimeSpan span)
    {
        if (span.TotalSeconds < 1) return "0s";

        var parts = new List<string>();
        if (span.Days > 0) parts.Add($"{span.Days}d");
        if (span.Hours > 0) parts.Add($"{span.Hours}h");
        if (span.Minutes > 0) parts.Add($"{span.Minutes}m");
        if (parts.Count == 0) parts.Add($"{span.Seconds}s");

        return string.Join(" ", parts.Take(3));
    }

    private static string StatusIndicatorClass(MediaServer server)
    {
        var up = server.IsEnabled && server.IsOnline;
        return $"status-indicator status-{(up ? "green" : "red")} status-indicator-animated";
    }

    private static string StatusTextClass(MediaServer server)
    {
        return !server.IsEnabled || !server.IsOnline ? "text-red" : "text-green";
    }

    private static string StatusPillClass(MediaServer server)
    {
        return !server.IsEnabled || !server.IsOnline ? "badge bg-danger-lt text-danger" : "badge bg-success-lt text-success";
    }

    private static string StatusPillText(MediaServer server)
    {
        return !server.IsEnabled ? "Disabled" : !server.IsOnline ? "Down" : "Up";
    }

    private static string RemoteHealthAlertClass(string? type)
    {
        return type?.ToLowerInvariant() switch
        {
            "error" => "alert-danger",
            "warning" => "alert-warning",
            _ => "alert-info"
        };
    }

    private static string RemoteHealthIcon(string? type)
    {
        return type?.ToLowerInvariant() switch
        {
            "error" => "ti ti-alert-octagon",
            "warning" => "ti ti-alert-triangle",
            _ => "ti ti-info-circle"
        };
    }

    private static string EventIcon(ServerHealthEventType type)
    {
        return type switch
        {
            ServerHealthEventType.Down => "ti ti-circle-x text-danger",
            ServerHealthEventType.Resolved => "ti ti-circle-check text-success",
            _ => "ti ti-info-circle text-info"
        };
    }

    #endregion
}
