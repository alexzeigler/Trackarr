using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Components;
using Trackarr.Core.SystemInfo;
using Trackarr.Services.SystemInfo;

namespace Trackarr.Components.Pages;

public partial class Releases : ComponentBase
{
    private readonly HashSet<string> _expanded = [];
    private bool _error;
    private bool _loading = true;

    private List<GitHubRelease> _releases = [];
    [Inject] private GitHubReleaseService ReleaseService { get; set; } = null!;

    protected override async Task OnInitializedAsync()
    {
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        _loading = true;
        _error = false;

        try
        {
            _releases = await ReleaseService.GetReleasesAsync();

            if (_releases.Count > 0 && _expanded.Count == 0)
                _expanded.Add(_releases[0].TagName);
        }
        catch
        {
            _error = true;
        }
        finally
        {
            _loading = false;
        }
    }

    private void Toggle(string tag)
    {
        if (!_expanded.Remove(tag))
            _expanded.Add(tag);
    }

    internal static MarkupString RenderMarkdown(string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
            return new MarkupString("<p class=\"text-muted\">No release notes.</p>");

        var sb = new StringBuilder();
        var lines = markdown.Split('\n');
        var inList = false;

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd('\r');

            if (line.StartsWith("### "))
            {
                if (inList)
                {
                    sb.Append("</ul>");
                    inList = false;
                }

                sb.Append($"<h5 class=\"mt-3 mb-2\">{Encode(line[4..])}</h5>");
            }
            else if (line.StartsWith("## "))
            {
                if (inList)
                {
                    sb.Append("</ul>");
                    inList = false;
                }

                sb.Append($"<h4 class=\"mt-3 mb-2\">{Encode(line[3..])}</h4>");
            }
            else if (line.StartsWith("- ") || line.StartsWith("* "))
            {
                if (!inList)
                {
                    sb.Append("<ul class=\"mb-2\">");
                    inList = true;
                }

                sb.Append($"<li>{FormatInline(line[2..])}</li>");
            }
            else if (string.IsNullOrWhiteSpace(line))
            {
                if (inList)
                {
                    sb.Append("</ul>");
                    inList = false;
                }
            }
            else
            {
                if (inList)
                {
                    sb.Append("</ul>");
                    inList = false;
                }

                sb.Append($"<p class=\"mb-1\">{FormatInline(line)}</p>");
            }
        }

        if (inList) sb.Append("</ul>");

        return new MarkupString(sb.ToString());
    }

    private static string FormatInline(string text)
    {
        var encoded = Encode(text);
        encoded = Regex.Replace(encoded, @"\*\*(.+?)\*\*", "<strong>$1</strong>");
        encoded = Regex.Replace(encoded, @"`(.+?)`", "<code>$1</code>");
        encoded = Regex.Replace(encoded, @"\[(.+?)\]\((.+?)\)",
            "<a href=\"$2\" target=\"_blank\" rel=\"noopener\">$1</a>");
        return encoded;
    }

    private static string Encode(string text)
    {
        return WebUtility.HtmlEncode(text);
    }
}
