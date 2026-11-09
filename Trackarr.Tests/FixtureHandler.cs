using System.Net;
using System.Text;
using System.Text.Json;

namespace Trackarr.Tests;

/// <summary>
///     Answers HTTP requests from the hand-written Radarr/Sonarr responses in <c>Fixtures/&lt;app&gt;/</c> instead of
///     the network (see <c>Fixtures/README.md</c>). Only GETs are served: Trackarr's writes (searches, removals) have
///     nothing to replay.
/// </summary>
internal sealed class FixtureHandler(string app, string? root = null) : HttpMessageHandler
{
    public const string Radarr = "radarr";
    public const string Sonarr = "sonarr";

    /// <summary>Where the fixtures are copied next to the test assembly.</summary>
    public static string DefaultRoot => Path.Combine(AppContext.BaseDirectory, "Fixtures");

    private string Folder => Path.Combine(root ?? DefaultRoot, app);

    /// <summary>The fixture file names requested so far, in order.</summary>
    public List<string> Requested { get; } = [];

    /// <summary>
    ///     The fixture file a request path maps to, or null for paths outside /api. Everything before "/api/" (a
    ///     URL base) is dropped, number segments become "id", and the segments are joined with "_":
    ///     <c>/radarr/api/v3/movie/42</c> → <c>api_v3_movie_id.json</c>. The query string isn't part of the name.
    /// </summary>
    public static string? FileNameFor(string path)
    {
        var start = path.IndexOf("/api/", StringComparison.OrdinalIgnoreCase);
        if (start < 0) return null;

        var segments = path[(start + 1)..].Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.All(char.IsAsciiDigit) ? "id" : Clean(s));
        return string.Join('_', segments) + ".json";

        static string Clean(string segment)
        {
            var sb = new StringBuilder(segment.Length);
            foreach (var c in segment.ToLowerInvariant())
                sb.Append(char.IsAsciiLetterOrDigit(c) || c == '-' ? c : '-');
            return sb.ToString();
        }
    }

    /// <summary>True when a fixture exists for <paramref name="path" />.</summary>
    public bool Has(string path)
    {
        return FileNameFor(path) is { } name && File.Exists(Path.Combine(Folder, name));
    }

    /// <summary>An <see cref="IHttpClientFactory" /> whose clients all go through this handler.</summary>
    public IHttpClientFactory AsFactory()
    {
        return new Factory(this);
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var path = request.RequestUri?.AbsolutePath ?? "/";

        if (request.Method != HttpMethod.Get)
            return Json(HttpStatusCode.MethodNotAllowed, $"Fixture replay only serves GET, not {request.Method} {path}");

        if (FileNameFor(path) is not { } name)
            return Json(HttpStatusCode.NotFound, $"{path} isn't an API path");

        Requested.Add(name);
        var file = Path.Combine(Folder, name);
        if (!File.Exists(file))
        {
            return Json(HttpStatusCode.NotFound,
                $"No fixture {app}/{name}. Add one (see Trackarr.Tests/Fixtures/README.md).");
        }

        var body = await File.ReadAllTextAsync(file, cancellationToken);
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"), RequestMessage = request
        };
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string message)
    {
        return new HttpResponseMessage(status)
        {
            Content = new StringContent(JsonSerializer.Serialize(new { message }), Encoding.UTF8, "application/json")
        };
    }

    private sealed class Factory(FixtureHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            // The handler outlives each client, so Requested keeps the whole conversation.
            return new HttpClient(handler, false);
        }
    }
}
