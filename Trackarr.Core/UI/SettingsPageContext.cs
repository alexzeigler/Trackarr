namespace Trackarr.Core.UI;

public class SettingsPageContext
{
    public Func<Task>? SaveAsync { get; set; }
    public Func<Task>? DiscardAsync { get; set; }
    public bool HasChanges { get; set; }
    public string? SuccessMessage { get; set; }
    public event Action? OnStateChanged;

    public void NotifyChanged()
    {
        OnStateChanged?.Invoke();
    }
}
