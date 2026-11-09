using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Trackarr.Core.Exception;
using Trackarr.Core.Server;
using Trackarr.Integrations.Media.Arr.Dtos;

namespace Trackarr.Integrations.Media.Arr;

/// <summary>
///     What Radarr and Sonarr share: their v3 API for status, health, quality profiles, the queue, commands and
///     history. Each subclass adds its own media endpoints. One instance per server and use: the factory gives it an
///     <see cref="HttpClient" /> with the server's timeout, TLS and proxy settings.
/// </summary>
public abstract class ArrClientBase(HttpClient http, MediaServer server) : IMediaServerReader, IMediaServerWriter
{
    internal static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public MediaServer Server { get; } = server;

    /// <summary>"movie" or "series": where a quality profile lives.</summary>
    protected abstract string MediaResource { get; }

    /// <summary>The searches this app has.</summary>
    protected abstract IReadOnlySet<ArrSearch> Searches { get; }

    #region IMediaServerReader

    public async Task<ConnectionCheck> CheckConnectionAsync(CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        using var response = await SendAsync(HttpMethod.Get, "/api/v3/system/status", null, ct);
        return new ConnectionCheck(sw.Elapsed, response.ReasonPhrase);
    }

    public async Task<ServerMetadata> GetSystemStatusAsync(CancellationToken ct = default)
    {
        return await GetAsync<ServerMetadata>("/api/v3/system/status", ct) ?? throw new MetadataException(Server.Name);
    }

    public async Task<IReadOnlyList<RemoteHealthMessage>> GetHealthAsync(CancellationToken ct = default)
    {
        return await GetAsync<List<RemoteHealthMessage>>("/api/v3/health", ct) ?? [];
    }

    public async Task<IReadOnlyList<QualityProfileDto>> GetQualityProfilesAsync(CancellationToken ct = default)
    {
        return await GetAsync<List<QualityProfileDto>>("/api/v3/qualityprofile", ct) ?? [];
    }

    public async Task<QueueResponse> GetQueuePageAsync(int page, int pageSize, CancellationToken ct = default)
    {
        return await GetAsync<QueueResponse>(Invariant(
                   $"/api/v3/queue?page={page}&pageSize={pageSize}&includeUnknownMovieItems=true&includeMovie=true&includeSeries=true&includeEpisode=true"),
               ct) ?? new QueueResponse { Page = page, PageSize = pageSize };
    }

    public async Task<IReadOnlyList<CommandRecord>> GetCommandsAsync(CancellationToken ct = default)
    {
        return await GetAsync<List<CommandRecord>>("/api/v3/command", ct) ?? [];
    }

    public Task<CommandRecord?> GetCommandAsync(int id, CancellationToken ct = default)
    {
        return GetAsync<CommandRecord>(Invariant($"/api/v3/command/{id}"), ct);
    }

    public async Task<bool> HasImportedAsync(string downloadId, CancellationToken ct = default)
    {
        // eventType 3 is DownloadFolderImported.
        var history = await GetAsync<HistoryResponse>(
            $"/api/v3/history?downloadId={Uri.EscapeDataString(downloadId)}&eventType=3", ct);
        return history?.Records.Count > 0;
    }

    public async Task<bool> ExistsAsync(ArrSearch kind, int id, CancellationToken ct = default)
    {
        var resource = kind switch
        {
            ArrSearch.Movies => "movie",
            ArrSearch.Episodes => "episode",
            _ => "series"
        };
        using var response = await SendAsync(HttpMethod.Get, Invariant($"/api/v3/{resource}/{id}"), null, ct,
            allowNotFound: true);
        return response.StatusCode != HttpStatusCode.NotFound;
    }

    #endregion

    #region IMediaServerWriter

    public async Task RemoveFromQueueAsync(int queueId, bool removeFromClient, bool blocklist,
        CancellationToken ct = default)
    {
        using var _ = await SendAsync(HttpMethod.Delete, Invariant(
            $"/api/v3/queue/{queueId}?removeFromClient={Bool(removeFromClient)}&blocklist={Bool(blocklist)}&skipRedownload=false"),
            null, ct);
    }

    public async Task<ProfileChange> SetQualityProfileAsync(int remoteId, int profileId, int? onlyIfCurrent = null,
        CancellationToken ct = default)
    {
        var path = Invariant($"/api/v3/{MediaResource}/{remoteId}");

        // The whole resource goes back on PUT, so read it as a JSON tree and change only the profile.
        JsonNode node;
        using (var response = await SendAsync(HttpMethod.Get, path, null, ct))
            node = await ReadAsync<JsonNode>(response, path, ct) ??
                   throw new MediaServerException($"{Server.Name}: Empty {MediaResource} body.");

        var current = node["qualityProfileId"]?.GetValue<int>();
        if (current == profileId) return new ProfileChange(current, ProfileChangeOutcome.AlreadySet);
        if (onlyIfCurrent is { } expected && current != expected)
            return new ProfileChange(current, ProfileChangeOutcome.LeftAlone);

        node["qualityProfileId"] = profileId;
        using var content = new StringContent(node.ToJsonString(), Encoding.UTF8, "application/json");
        using var _ = await SendAsync(HttpMethod.Put, path, content, ct);
        return new ProfileChange(current, ProfileChangeOutcome.Changed);
    }

    public async Task<IReadOnlyList<int>> SearchAsync(ArrSearch kind, IReadOnlyCollection<int> ids,
        CancellationToken ct = default)
    {
        if (!Searches.Contains(kind))
            throw new MediaServerException($"{Server.Name}: {Server.ServiceType} has no {kind} search.");
        if (ids.Count == 0) return [];

        List<int> started = [];
        switch (kind)
        {
            case ArrSearch.Movies:
                AddId(await StartCommandAsync("MoviesSearch", "movieIds", ids.ToArray(), ct));
                break;
            case ArrSearch.Episodes:
                AddId(await StartCommandAsync("EpisodeSearch", "episodeIds", ids.ToArray(), ct));
                break;
            case ArrSearch.Series:
                // SeriesSearch takes a single seriesId.
                foreach (var id in ids)
                    AddId(await StartCommandAsync("SeriesSearch", "seriesId", id, ct));
                break;
        }

        return started;

        void AddId(int? id)
        {
            if (id is { } value) started.Add(value);
        }
    }

    private async Task<int?> StartCommandAsync(string name, string parameter, object value, CancellationToken ct)
    {
        var payload = JsonSerializer.Serialize(new Dictionary<string, object> { ["name"] = name, [parameter] = value });
        using var content = new StringContent(payload, Encoding.UTF8, "application/json");
        using var response = await SendAsync(HttpMethod.Post, "/api/v3/command", content, ct);

        // The command ID is only used to follow the search up, so an unreadable reply isn't an error.
        try
        {
            return (await response.Content.ReadFromJsonOrDefaultAsync<CommandRecord>(JsonOptions, ct))?.Id;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    #endregion

    #region HTTP

    /// <summary>GET a path under the server's URL base and read the JSON body.</summary>
    protected async Task<T?> GetAsync<T>(string pathAndQuery, CancellationToken ct)
    {
        using var response = await SendAsync(HttpMethod.Get, pathAndQuery, null, ct);
        return await ReadAsync<T>(response, PathOf(pathAndQuery), ct);
    }

    /// <summary>
    ///     Sends one request with the API key. A non-success status becomes an <see cref="ApiException" /> (404 is
    ///     returned instead when <paramref name="allowNotFound" />), and a connection failure or timeout a
    ///     <see cref="ConnectionException" />. Cancelling <paramref name="ct" /> still throws as a cancellation.
    /// </summary>
    protected async Task<HttpResponseMessage> SendAsync(HttpMethod method, string pathAndQuery, HttpContent? content,
        CancellationToken ct, bool allowNotFound = false)
    {
        if (string.IsNullOrWhiteSpace(Server.ApiKey))
            throw new MediaServerException($"{Server.Name}: API key is required.");

        HttpResponseMessage response;
        try
        {
            using var request = new HttpRequestMessage(method, Server.BaseUrl + pathAndQuery) { Content = content };
            request.Headers.TryAddWithoutValidation("X-Api-Key", Server.ApiKey);
            response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new ConnectionException(Server.Name, ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            // The HttpClient's own timeout, not the caller cancelling.
            throw new ConnectionException(Server.Name, ex);
        }

        if (response.IsSuccessStatusCode || (allowNotFound && response.StatusCode == HttpStatusCode.NotFound))
            return response;

        var status = (int)response.StatusCode;
        response.Dispose();
        throw new ApiException(Server.Name, PathOf(pathAndQuery), status);
    }

    private async Task<T?> ReadAsync<T>(HttpResponseMessage response, string path, CancellationToken ct)
    {
        try
        {
            return await response.Content.ReadFromJsonOrDefaultAsync<T>(JsonOptions, ct);
        }
        catch (JsonException ex)
        {
            throw new MediaServerException($"{Server.Name}: Unexpected response from {path}.", ex);
        }
        catch (IOException ex)
        {
            throw new ConnectionException(Server.Name, ex);
        }
    }

    private static string PathOf(string pathAndQuery)
    {
        var query = pathAndQuery.IndexOf('?', StringComparison.Ordinal);
        return query < 0 ? pathAndQuery : pathAndQuery[..query];
    }

    private static string Bool(bool value)
    {
        return value ? "true" : "false";
    }

    private static string Invariant(FormattableString value)
    {
        return FormattableString.Invariant(value);
    }

    #endregion
}

internal static class HttpContentJson
{
    /// <summary>Reads JSON, and gives default for an empty body instead of throwing.</summary>
    public static async Task<T?> ReadFromJsonOrDefaultAsync<T>(this HttpContent content, JsonSerializerOptions options,
        CancellationToken ct)
    {
        await using var stream = await content.ReadAsStreamAsync(ct);
        if (stream.CanSeek && stream.Length == 0) return default;

        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, ct);
        if (buffer.Length == 0) return default;

        buffer.Position = 0;
        return await JsonSerializer.DeserializeAsync<T>(buffer, options, ct);
    }
}
