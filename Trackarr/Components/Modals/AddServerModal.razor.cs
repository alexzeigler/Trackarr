using Microsoft.AspNetCore.Components;

namespace Trackarr.Components.Modals;

public partial class AddServerModal : ComponentBase {
    public string? SelectedService { get; set; } = "Radarr";
}