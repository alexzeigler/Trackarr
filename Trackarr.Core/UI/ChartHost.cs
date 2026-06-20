using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace Trackarr.Core.UI;

public class ChartHost : ComponentBase
{
    private bool _rendered;
    [Parameter] public string Id { get; set; } = "";
    [Parameter] public string Class { get; set; } = "";

    protected override bool ShouldRender()
    {
        return !_rendered;
    }

    protected override void OnAfterRender(bool firstRender)
    {
        if (firstRender) _rendered = true;
    }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "div");
        builder.AddAttribute(1, "id", Id);
        builder.AddAttribute(2, "class", Class);
        builder.CloseElement();
    }
}