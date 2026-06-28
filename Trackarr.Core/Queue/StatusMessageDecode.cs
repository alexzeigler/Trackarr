using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Components;

namespace Trackarr.Core.Queue;

public class StatusMessageDecode
{
    #region Dependencies

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    #endregion

    #region Public API

    public static MarkupString FormatStatusMessages(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new MarkupString("—");

        List<StatusMessageGroup>? groups;

        try
        {
            groups = JsonSerializer.Deserialize<List<StatusMessageGroup>>(json, JsonOptions);
        }
        catch
        {
            return new MarkupString(WebUtility.HtmlEncode(json));
        }

        if (groups == null || groups.Count == 0) return new MarkupString("—");

        var html = new StringBuilder();

        html.Append("<ul class=\"mb-0 ps-3\">");

        foreach (var group in groups)
        {
            var title = WebUtility.HtmlEncode(group.Title);

            html.Append("<li>");
            html.Append($"<span class=\"fw-semibold\">{title}</span>");

            if (group.Messages.Count > 0)
            {
                html.Append("<ul class=\"mt-1 ps-3\">");

                foreach (var message in group.Messages) html.Append($"<li>{WebUtility.HtmlEncode(message)}</li>");

                html.Append("</ul>");
            }

            html.Append("</li>");
        }

        html.Append("</ul>");

        return new MarkupString(html.ToString());
    }

    #endregion

    #region Helpers

    private sealed class StatusMessageGroup
    {
        public string Title { get; } = string.Empty;
        public List<string> Messages { get; } = [];
    }

    #endregion
}
