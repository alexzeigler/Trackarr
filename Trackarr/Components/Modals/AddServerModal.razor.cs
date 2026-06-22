using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Trackarr.Core.Server;

namespace Trackarr.Components.Modals;

public partial class AddServerModal : ComponentBase
{
    private string _autoRemove = "";
    private string _monitorQueue = "";
    private DotNetObjectReference<AddServerModal>? _ref;
    private string _searchMissing = "";
    private bool _showApiKey;
    [Parameter] public ServiceType ServiceType { get; set; }
    [Parameter] public EventCallback OnSaved { get; set; }

    private bool HasProfiles { get; set; }


    private MediaServer Model { get; set; } = new();
    private bool IsEditing => Model.Id != 0;
    private bool IsTesting { get; set; }
    private bool IsSaving { get; set; }
    private bool IsBusy => IsTesting || IsSaving;

    private bool IsValid =>
        !string.IsNullOrWhiteSpace(Model.Name) && !string.IsNullOrWhiteSpace(Model.Host) && Model.Port > 0 &&
        !string.IsNullOrWhiteSpace(Model.ApiKey);

    private bool CanSave => IsValid && TestResult is { Item1: true } && !IsBusy;
    private List<QualityProfile>? Profiles { get; set; } = [];

    private (bool, long ElapsedMilliseconds, string? ReasonPhrase)? TestResult { get; set; }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender) await Js.InvokeVoidAsync("initTooltips");
    }

    private void ToggleApiKey()
    {
        _showApiKey = !_showApiKey;
    }

    public async Task OpenForAdd(ServiceType type = ServiceType.Radarr)
    {
        ServiceType = type;

        Model = new MediaServer
        {
            IsEnabled = true,
            ServiceType = type,
            Port = type switch
            {
                ServiceType.Radarr => 7878,
                ServiceType.Sonarr => 8989,
                _ => 80
            },
            CreatedAt = DateTime.UtcNow
        };

        TestResult = null;
        _showApiKey = false;
        HasProfiles = false;
        SyncBehaviorFromModel();

        await InvokeAsync(StateHasChanged);

        await Js.InvokeVoidAsync("modalHelper.show", "serverModal");
    }

    public async Task OpenForEdit(MediaServer server)
    {
        ServiceType = server.ServiceType;
        Model = server.Clone();
        SyncBehaviorFromModel();
        if (server.FallBackProfileId != null)
        {
            Profiles = await ServerService.GetProfilesAsync(server);
            var selected = Profiles?.FirstOrDefault(p =>
                string.Equals(p.Name, Model.FallbackProfile?.Name, StringComparison.OrdinalIgnoreCase));
            if (selected == null)
                TestResult = new ValueTuple<bool, long, string?>(false, 0,
                    $"Profile does not exist on the server: \"{Model.FallbackProfile}\".");

            HasProfiles = Profiles?.Count > 0;
        }
        else
        {
            Profiles?.Clear();
            TestResult = null;
        }

        _showApiKey = false;
        await InvokeAsync(StateHasChanged);
        await InitFallbackSelect();
        await Js.InvokeVoidAsync("modalHelper.show", "serverModal");
    }

    private async Task TestConnection()
    {
        IsTesting = true;
        TestResult = null;
        try
        {
            TestResult = await ServerService.TestConnectionAsync(Model);

            if (TestResult is { Item1: true })
            {
                Model.LastPingMs = TestResult.Value.ElapsedMilliseconds;
                Model.IsOnline = true;
                TestResult = TestResult.Value with
                {
                    ReasonPhrase =
                    $"{TestResult.Value.ReasonPhrase} Last ping time: {TestResult.Value.ElapsedMilliseconds}ms"
                };
                Profiles = await ServerService.GetProfilesAsync(Model);
            }
            else
            {
                Profiles?.Clear();
            }

            HasProfiles = Profiles?.Count > 0;
            await RefreshTooltips();
            await InitFallbackSelect();
        }
        catch (Exception ex)
        {
            TestResult = (false, 0, GetStatusDescription(ex.Message));
            Profiles?.Clear();
        }
        finally
        {
            IsTesting = false;
        }
    }

    [JSInvokable]
    public void OnFallbackChanged(string? value)
    {
        Model.FallbackProfile = Profiles?.FirstOrDefault(p => p.Name == value);
        if (Model.FallbackProfile != null) Model.FallBackProfileId = Model.FallbackProfile.Id;
    }

    private async Task InitFallbackSelect()
    {
        await InvokeAsync(StateHasChanged);
        if (HasProfiles)
        {
            await Js.InvokeVoidAsync("disposeTooltip", "#fallbackTooltip");
            await Js.InvokeVoidAsync("tomSelectHelper.init", "fallbackProfileSelect",
                _ref ??= DotNetObjectReference.Create(this));
        }

        await Js.InvokeVoidAsync("initTooltips");
    }

    private async Task RefreshTooltips()
    {
        await InvokeAsync(StateHasChanged);
        await Js.InvokeVoidAsync("initTooltips");
    }

    private void SyncBehaviorFromModel()
    {
        _monitorQueue = Model.MonitorQueue?.ToString() ?? "";
        _autoRemove = Model.AutoRemoveDownloads?.ToString() ?? "";
        _searchMissing = Model.SearchMissing?.ToString() ?? "";
    }

    private void SyncBehaviorToModel()
    {
        Model.MonitorQueue = string.IsNullOrEmpty(_monitorQueue) ? null : bool.Parse(_monitorQueue);
        Model.AutoRemoveDownloads = string.IsNullOrEmpty(_autoRemove) ? null : bool.Parse(_autoRemove);
        Model.SearchMissing = string.IsNullOrEmpty(_searchMissing) ? null : bool.Parse(_searchMissing);
    }

    private async Task Save()
    {
        SyncBehaviorToModel();
        IsSaving = true;
        if (IsEditing)
            await ServerService.UpdateServerAsync(Model);
        else
            await ServerService.CreateServerAsync(Model);

        await Js.InvokeVoidAsync("tomSelectHelper.destroy", "fallbackProfileSelect");
        await Js.InvokeVoidAsync("modalHelper.hide", "serverModal");

        IsSaving = false;
        await OnSaved.InvokeAsync();
        await InvokeAsync(StateHasChanged);
    }

    private async Task Close()
    {
        await InvokeAsync(StateHasChanged);
        await Task.Delay(300);
        TestResult = null;
        await InvokeAsync(StateHasChanged);
        await Js.InvokeVoidAsync("modalHelper.hide", "serverModal");
    }

    private static string GetStatusDescription(string? reasonPhrase)
    {
        return reasonPhrase switch
        {
            not null when reasonPhrase.Contains("401") => "401: Unauthorized — check your API key.",
            not null when reasonPhrase.Contains("403") => "403: Forbidden — insufficient permissions.",
            not null when reasonPhrase.Contains("404") => "404: Not Found — check your host and API path.",
            not null when reasonPhrase.Contains("408") => "408: Request Timeout — server took too long to respond.",
            not null when reasonPhrase.Contains("500") =>
                "500: Internal Server Error — something went wrong on the server.",
            not null when reasonPhrase.Contains("502") => "502: Bad Gateway — server may be behind a proxy.",
            not null when reasonPhrase.Contains("503") => "503: Service Unavailable — server may be starting up.",
            _ => reasonPhrase ?? "Unknown error."
        };
    }

    private void UpdatePort()
    {
        Model.Port = (Model.ServiceType, Model.UseHttps) switch
        {
            (ServiceType.Radarr, false) => 7878,
            (ServiceType.Sonarr, false) => 8989,
            (ServiceType.Radarr, true) => 443,
            (ServiceType.Sonarr, true) => 443,
            _ => Model.Port
        };
    }
}
