namespace Trackarr.Services.UI;

public sealed class DataChangeBus
{
    public event Action? OnChanged;

    public void NotifyChanged()
    {
        OnChanged?.Invoke();
    }
}
