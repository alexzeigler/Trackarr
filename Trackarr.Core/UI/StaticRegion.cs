using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace Trackarr.Core.UI;

public class StaticRegion : ComponentBase
{
    private bool _rendered;
    [Parameter] public RenderFragment? ChildContent { get; set; }

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
        builder.AddContent(0, ChildContent);
    }
}
