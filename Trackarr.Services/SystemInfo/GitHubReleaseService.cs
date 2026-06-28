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
