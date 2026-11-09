using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Trackarr.Core.Server;
using Trackarr.Data;

namespace Trackarr.Components.Modals;

public partial class AddServerModal : ComponentBase
{
    #region Lifecycle

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender) await Js.InvokeVoidAsync("initTooltips");
    }

    #endregion

    #region Parameters

    [Parameter] public ServiceType ServiceType { get; set; }
    [Parameter] public EventCallback OnSaved { get; set; }

    #endregion

    #region State

    private string _autoRemove = "";
    private int _fallbackSelectKey;
    private string _monitorQueue = "";
    private DotNetObjectReference<AddServerModal>? _ref;
    private string _searchMissing = "";
    private bool _showApiKey;

    // What's typed into the API key field. The saved key stays on the server: an empty field keeps it.
    private string _apiKeyInput = "";
    private string _savedApiKey = "";

    private string ApiKeyPlaceholder =>
        _savedApiKey.Length > 0 ? $"Saved key {SecretProtector.Mask(_savedApiKey)}" : "";

    private string ApiKeyHint => (IsEditing, _savedApiKey.Length > 0) switch
    {
        (true, true) => "Leave empty to keep the saved key, or paste a new one.",
        (true, false) => "Paste the API key again: the saved one can't be read (was the keys folder replaced?).",
        _ => $"Your {Model.ServiceType} API key (Settings > General)."
    };

    private bool HasProfiles { get; set; }

    private MediaServer Model { get; set; } = new();
    private bool IsEditing => Model.Id != 0;
    private bool IsTesting { get; set; }
    private bool IsSaving { get; set; }
    private bool IsBusy => IsTesting || IsSaving;

    private bool IsValid =>
        !string.IsNullOrWhiteSpace(Model.Name) && !string.IsNullOrWhiteSpace(Model.Host) && Model.Port > 0 &&
        !string.IsNullOrWhiteSpace(Model.ApiKey);

    private bool CanSave => IsValid && !IsBusy;
    private List<QualityProfile>? Profiles { get; set; } = [];

    private (bool, long ElapsedMilliseconds, string? ReasonPhrase)? TestResult { get; set; }

    #endregion

    #region Event Handlers

    private void ToggleApiKey()
    {
        _showApiKey = !_showApiKey;
    }

    private void ApplyApiKey()
    {
        Model.ApiKey = string.IsNullOrWhiteSpace(_apiKeyInput) ? _savedApiKey : _apiKeyInput.Trim();
    }

    private void ResetApiKeyField(string? saved)
    {
        _savedApiKey = saved ?? "";
        _apiKeyInput = "";
        _showApiKey = false;
    }

    public async Task OpenForAdd(ServiceType? type = null)
    {
        type ??= Enum.TryParse<ServiceType>(Settings.GetString("General", "DefaultServerType", "Radarr"),
            out var parsed)
            ? parsed
            : ServiceType.Radarr;
        ServiceType = type.Value;

        await ResetFallbackSelect();
        Model = new MediaServer
        {
            IsEnabled = true,
            ServiceType = type.Value,
            Port = type.Value switch
            {
                ServiceType.Radarr => 7878,
                ServiceType.Sonarr => 8989,
                _ => 80
            },
            CreatedAt = DateTime.UtcNow
        };

        TestResult = null;
        ResetApiKeyField(null);
        HasProfiles = false;
        SyncBehaviorFromModel();

        await InvokeAsync(StateHasChanged);

        await Js.InvokeVoidAsync("modalHelper.show", "serverModal");
    }

    public async Task OpenForEdit(MediaServer server)
    {
        ServiceType = server.ServiceType;
        await ResetFallbackSelect();
        Model = server.Clone();
        ResetApiKeyField(server.ApiKey);
        SyncBehaviorFromModel();
        if (server.FallBackProfileId != null)
        {
            Profiles = await QualityProfiles.GetProfilesAsync(server);
            var selected = Profiles?.FirstOrDefault(p =>
                string.Equals(p.Name, Model.FallbackProfile?.Name, StringComparison.OrdinalIgnoreCase));
            if (selected == null)
            {
                TestResult = new ValueTuple<bool, long, string?>(false, 0,
                    $"Profile does not exist on the server: \"{Model.FallbackProfile}\".");
            }

            HasProfiles = Profiles?.Count > 0;
        }
        else
        {
            Profiles?.Clear();
            TestResult = null;
        }

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
            await ResetFallbackSelect(); // the profile options are about to change, on success or failure
            TestResult = await ServerConnection.TestConnectionAsync(Model);

            if (TestResult is { Item1: true })
            {
                Model.LastPingMs = TestResult.Value.ElapsedMilliseconds;
                Model.IsOnline = true;
                TestResult = TestResult.Value with
                {
                    ReasonPhrase =
                    $"{TestResult.Value.ReasonPhrase} Last ping time: {TestResult.Value.ElapsedMilliseconds}ms"
                };
                Profiles = await QualityProfiles.GetProfilesAsync(Model);
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

    // A pasted full URL ("https://example.com/radarr") is split into SSL, host, port and URL base.
    private void ParseAddress()
    {
        if (Model.Host?.Contains("://") == true) Model.NormalizeAddress();
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

    private async Task Save()
    {
        SyncBehaviorToModel();
        IsSaving = true;
        var isNew = !IsEditing;
        if (IsEditing)
            await ServerRepository.UpdateAsync(Model);
        else
            await ServerRepository.AddAsync(Model);

        // The address or key may have changed. A server that's down still saves; its metadata waits for the next sync.
        await ServerConnection.TryRefreshMetadataAsync(Model);

        await ResetFallbackSelect();
        await Js.InvokeVoidAsync("modalHelper.hide", "serverModal");

        IsSaving = false;
        await OnSaved.InvokeAsync();
        await InvokeAsync(StateHasChanged);

        if (isNew)
            ServerSync.SyncInBackground(Model);
    }

    private async Task Close()
    {
        await InvokeAsync(StateHasChanged);
        await Task.Delay(300);
        TestResult = null;
        await InvokeAsync(StateHasChanged);
        await Js.InvokeVoidAsync("modalHelper.hide", "serverModal");
    }

    #endregion

    #region Display Helpers

    /// <summary>
    ///     Tom Select rewrites the select's option nodes (and rebuilds them on destroy), so Blazor's references to
    ///     those nodes go stale and its next diff of the options fails with "removeChild of null". Before the
    ///     options change, tear Tom Select down and bump the @key so Blazor renders a brand-new select element.
    /// </summary>
    private async Task ResetFallbackSelect()
    {
        await Js.InvokeVoidAsync("tomSelectHelper.destroy", "fallbackProfileSelect");
        _fallbackSelectKey++;
    }

    private async Task InitFallbackSelect()
    {
        await InvokeAsync(StateHasChanged);
        if (HasProfiles)
        {
            await Js.InvokeVoidAsync("disposeTooltip", "#fallbackTooltip");
            await Js.InvokeVoidAsync("tomSelectHelper.init", "fallbackProfileSelect",
                _ref ??= DotNetObjectReference.Create(this), nameof(OnFallbackChanged));
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

    private static string GetStatusDescription(string? reasonPhrase)
    {
        return reasonPhrase switch
        {
            not null when reasonPhrase.Contains("401") => "401: Unauthorized — check your API key.",
            not null when reasonPhrase.Contains("403") => "403: Forbidden — insufficient permissions.",
            not null when reasonPhrase.Contains("404") =>
                "404: Not Found — check the host, port and URL base (e.g. /radarr behind a reverse proxy).",
            not null when reasonPhrase.Contains("408") => "408: Request Timeout — server took too long to respond.",
            not null when reasonPhrase.Contains("500") =>
                "500: Internal Server Error — something went wrong on the server.",
            not null when reasonPhrase.Contains("502") => "502: Bad Gateway — server may be behind a proxy.",
            not null when reasonPhrase.Contains("503") => "503: Service Unavailable — server may be starting up.",
            _ => reasonPhrase ?? "Unknown error."
        };
    }

    #endregion
}
