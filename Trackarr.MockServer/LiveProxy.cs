using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.WebUtilities;

namespace Trackarr.MockServer;

public enum MockMode
{
    Radarr,
    Sonarr
}

public record LiveSource(string Url, string? ApiKey);

public record ProxyResult(int StatusCode, string? ContentType, string Body);

/// <summary>Forwards read-only requests to the real Radarr/Sonarr configured as the mock's sources.</summary>
public class LiveProxy(IHttpClientFactory httpFactory, IConfiguration config, ILogger<LiveProxy> logger)
{
    public const string HttpClientName = "live";

    public int RadarrPort { get; } = config.GetValue("MockServer:RadarrPort", 9877);
    public int SonarrPort { get; } = config.GetValue("MockServer:SonarrPort", 9878);

    public MockMode GetMode(HttpContext ctx)
    {
        return ctx.Connection.LocalPort == SonarrPort ? MockMode.Sonarr : MockMode.Radarr;
    }

    public LiveSource? GetSource(MockMode mode)
    {
        var prefix = mode == MockMode.Sonarr ? "Sonarr" : "Radarr";
        var url = config[$"MockServer:{prefix}SourceUrl"];
        return string.IsNullOrWhiteSpace(url)
            ? null
            : new LiveSource(url.TrimEnd('/'), config[$"MockServer:{prefix}SourceApiKey"]);
    }

    /// <summary>
    ///     GETs <paramref name="path" /> on the live server. The caller's apikey query param is dropped
    ///     (it is the mock's key, not the live one) and the live key is sent as a header instead.
    /// </summary>
    public async Task<ProxyResult> GetAsync(MockMode mode, string path, QueryString query, CancellationToken ct)
    {
        var source = GetSource(mode);
        if (source is null)
        {
            return new ProxyResult(502, "application/json",
                $$"""{"message":"MockServer has no {{mode}}SourceUrl configured"}""");
        }

        var filtered = QueryString.Create(QueryHelpers.ParseQuery(query.Value)
            .Where(kv => !kv.Key.Equals("apikey", StringComparison.OrdinalIgnoreCase))
            .SelectMany(kv => kv.Value.Select(v => new KeyValuePair<string, string?>(kv.Key, v))));

        var client = httpFactory.CreateClient(HttpClientName);
        using var request = new HttpRequestMessage(HttpMethod.Get, source.Url + path + filtered);
        if (!string.IsNullOrWhiteSpace(source.ApiKey))
            request.Headers.TryAddWithoutValidation("X-Api-Key", source.ApiKey);

        try
        {
            using var response = await client.SendAsync(request, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            return new ProxyResult((int)response.StatusCode, response.Content.Headers.ContentType?.ToString(), body);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning("[{Mode}] Live request failed: GET {Path} — {Msg}", mode, path, ex.Message);
            var message = JsonSerializer.Serialize(new { message = $"Live {mode} unreachable: {ex.Message}" });
            return new ProxyResult(502, "application/json", message);
        }
    }

    /// <summary>Fetches the full live queue (used to snapshot an item we never saw in a proxied page).</summary>
    public async Task<JsonArray?> GetLiveQueueRecordsAsync(MockMode mode, CancellationToken ct)
    {
        var query = new QueryString(
            "?page=1&pageSize=1000&includeUnknownMovieItems=true&includeMovie=true&includeSeries=true&includeEpisode=true");
        var result = await GetAsync(mode, "/api/v3/queue", query, ct);
        if (result.StatusCode is < 200 or >= 300) return null;
        try
        {
            return JsonNode.Parse(result.Body)?["records"]?.AsArray();
        }
        catch
        {
            return null;
        }
    }
}
