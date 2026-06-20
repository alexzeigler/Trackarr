using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Trackarr.Components.Modals;
using Trackarr.Core.Server;

namespace Trackarr.Components.Pages;

public partial class Servers : ComponentBase
{
    private readonly int _pageSize = 10;

    private readonly CancellationTokenSource _refreshCancellation = new();
    private ConfirmationDialog? _confirmModal;
    private string? _confirmText = "Are you sure you want to delete this server?";
    private int _selectedServerId;
    private AddServerModal _serverModal = new();
    private List<MediaServer> _servers = [];
    private bool IsRefreshing { get; set; }

    public async ValueTask DisposeAsync()
    {
        await ValueTask.CompletedTask;
    }

    protected override async void OnInitialized()
    {
        _servers = await MediaServerService.GetAllServersAsync();
        _ = RefreshLoopAsync();
    }

    private async Task RefreshLoopAsync()
    {
        using PeriodicTimer timer = new(TimeSpan.FromSeconds(30));

        while (await timer.WaitForNextTickAsync(_refreshCancellation.Token))
        {
            IsRefreshing = true;
            await InvokeAsync(StateHasChanged);
            await Task.Delay(1000);
            _servers = await MediaServerService.GetAllServersAsync();
            IsRefreshing = false;
            await InvokeAsync(StateHasChanged);
        }
    }


    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender) return;

        await Js.InvokeVoidAsync("trackarrTable.init",
            "servers-table",
            "servers-table-search",
            "servers-page-count",
            _pageSize,
            new[]
            {
                "sort-name",
                "sort-type",
                "sort-url",
                "sort-status",
                "sort-sync",
                "sort-last-sync",
                "sort-version"
            });
    }

    private async Task AddServer()
    {
        await _serverModal.OpenForAdd();
    }

    private async Task SyncAll()
    {
        IsRefreshing = true;
        await InvokeAsync(StateHasChanged);
        foreach (var server in _servers)
        {
            await Task.Delay(1000);
            await SyncServer(server);
        }

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
}