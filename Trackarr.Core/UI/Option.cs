namespace Trackarr.Core.UI;

public class Option {
    public string? DisplayName { get; set; }
    public string? Icon { get; set; }
    public Func<Task>? Command { get; set; }
}