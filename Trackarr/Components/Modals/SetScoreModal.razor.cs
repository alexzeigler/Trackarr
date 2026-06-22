using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Trackarr.Core.Queue;

namespace Trackarr.Components.Modals;

public partial class SetScoreModal : ComponentBase
{
    private int _confidence;
    private string? _errorMessage;
    private bool _isSaving;
    private DownloadItem? _item;
    private string _itemTitle = "";
    private int _score = 70;

    public async Task Open(DownloadItem item, string title, int? currentScore = null, int? currentConfidence = null)
    {
        _item = item;
        _itemTitle = title;
        _score = currentScore ?? 70;
        _confidence = currentConfidence ?? 0;
        _isSaving = false;
        _errorMessage = null;

        await InvokeAsync(StateHasChanged);
        await Js.InvokeVoidAsync("modalHelper.show", "setScoreModal");
    }

    private async Task SaveAsync()
    {
        if (_item == null) return;

        _isSaving = true;
        _errorMessage = null;
        StateHasChanged();

        try
        {
            await HealthService.SetScoreAsync(_item, _score, _confidence);
            await Js.InvokeVoidAsync("modalHelper.hide", "setScoreModal");
            await OnSaved.InvokeAsync();
        }
        catch (Exception ex)
        {
            _errorMessage = ex.Message;
        }
        finally
        {
            _isSaving = false;
            StateHasChanged();
        }
    }
}
