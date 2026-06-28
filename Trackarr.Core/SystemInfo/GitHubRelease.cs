namespace Trackarr.Core.SystemInfo;

public sealed class GitHubRelease
{
    public string TagName { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Body { get; init; } = string.Empty;
    public DateTime PublishedAt { get; init; }
    public bool IsPrerelease { get; init; }
    public string HtmlUrl { get; init; } = string.Empty;
}
