using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Trackarr.Core.SystemInfo;

namespace Trackarr.Services.SystemInfo;

public sealed class GitHubReleaseService(IHttpClientFactory httpClientFactory)
{
    private const string RepoApiUrl = "https://api.github.com/repos/alexzeigler/Trackarr/releases";
    private List<GitHubRelease>? _cache;
    private DateTime _cacheExpiry;

    public async Task<List<GitHubRelease>> GetReleasesAsync()
    {
        if (_cache is not null && DateTime.UtcNow < _cacheExpiry)
            return _cache;

        try
        {
            var client = httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Trackarr");
            client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            client.Timeout = TimeSpan.FromSeconds(10);

            var response = await client.GetFromJsonAsync<List<GitHubReleaseDto>>(RepoApiUrl);

            _cache = response?.Select(r => new GitHubRelease
            {
                TagName = r.TagName ?? string.Empty,
                Name = r.Name ?? r.TagName ?? string.Empty,
                Body = r.Body ?? string.Empty,
                PublishedAt = r.PublishedAt,
                IsPrerelease = r.Prerelease,
                HtmlUrl = r.HtmlUrl ?? string.Empty
            }).ToList() ?? [];

            _cacheExpiry = DateTime.UtcNow.AddMinutes(30);
        }
        catch
        {
            _cache ??= [];
        }

        return _cache;
    }

    /// <summary>
    ///     The newest release when it is newer than <paramref name="currentVersion" /> (e.g. "5.2.0-alpha"), else
    ///     null. Only the numeric part is compared, so a local dev build never reads as out of date by its suffix.
    /// </summary>
    public async Task<GitHubRelease?> GetNewerReleaseAsync(string currentVersion)
    {
        if (ParseVersion(currentVersion) is not { } current) return null;

        var newest = (await GetReleasesAsync()).Select(r => (Release: r, Version: ParseVersion(r.TagName)))
            .Where(r => r.Version is not null).MaxBy(r => r.Version);

        return newest.Version is { } latest && latest > current ? newest.Release : null;
    }

    private static Version? ParseVersion(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var core = value.Trim().TrimStart('v', 'V').Split('-', '+')[0];
        if (!Version.TryParse(core, out var v)) return null;
        // "5.2" and "5.2.0" must compare equal; Version treats a missing part as lower than 0.
        return new Version(v.Major, v.Minor, Math.Max(v.Build, 0), Math.Max(v.Revision, 0));
    }

    private sealed class GitHubReleaseDto
    {
        [JsonPropertyName("tag_name")] public string? TagName { get; set; }
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("body")] public string? Body { get; set; }
        [JsonPropertyName("published_at")] public DateTime PublishedAt { get; set; }
        [JsonPropertyName("prerelease")] public bool Prerelease { get; set; }
        [JsonPropertyName("html_url")] public string? HtmlUrl { get; set; }
    }
}
