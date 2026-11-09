using System.Collections.Concurrent;
using System.Net;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Trackarr.Core.Exception;
using Trackarr.Core.Server;
using Trackarr.Integrations.Media;
using Trackarr.Integrations.Media.Arr;

namespace Trackarr.Tests;

/// <summary>
///     The retries on the Radarr/Sonarr HttpClients, through the same registration Program.cs uses
///     (<see cref="ArrHttpClients" />), with no delay between tries.
/// </summary>
public sealed class ArrRetryTests : IDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private readonly ScriptedServer _server = new();
    private readonly ServiceProvider _services;
    private readonly ArrClientFactory _factory;

    public ArrRetryTests()
    {
        _services = new ServiceCollection().AddArrHttpClients((_, _) => _server, TimeSpan.Zero).BuildServiceProvider();
        _factory = new ArrClientFactory(_services.GetRequiredService<IHttpClientFactory>());
    }

    public void Dispose()
    {
        _services.Dispose();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)] // the insecure client too
    public async Task Get_ThatFailsTwiceWithA503_WorksOnTheThirdTry(bool skipCertificateValidation)
    {
        _server.Answer(HttpStatusCode.ServiceUnavailable, HttpStatusCode.ServiceUnavailable);

        Assert.Empty(await Radarr(skipCertificateValidation).GetHealthAsync());

        Assert.Equal(3, _server.Requests.Count);
    }

    [Fact]
    public async Task Get_ThatKeepsFailing_GivesUpAfterTwoRetries()
    {
        _server.Answer(HttpStatusCode.BadGateway, HttpStatusCode.BadGateway, HttpStatusCode.BadGateway);

        var error = await Assert.ThrowsAsync<ApiException>(() => Radarr().GetHealthAsync());

        Assert.Equal(502, error.StatusCode);
        Assert.Equal(3, _server.Requests.Count);
    }

    [Fact]
    public async Task Get_AfterAFailedConnection_IsRetried()
    {
        _server.Answer(new HttpRequestException("Connection refused"));

        Assert.Empty(await Radarr().GetHealthAsync());

        Assert.Equal(2, _server.Requests.Count);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.RequestTimeout)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task Get_IsRetriedOnOtherPassingErrors(HttpStatusCode status)
    {
        _server.Answer(status);

        Assert.Empty(await Radarr().GetHealthAsync());

        Assert.Equal(2, _server.Requests.Count);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task Get_IsNotRetriedOnA4xx(HttpStatusCode status)
    {
        _server.Answer(status);

        var error = await Assert.ThrowsAsync<ApiException>(() => Radarr().GetHealthAsync());

        Assert.Equal((int)status, error.StatusCode);
        Assert.Single(_server.Requests);
    }

    [Fact]
    public async Task Get_ThatTimesOut_IsNotRetried()
    {
        _server.Hang = true;
        var radarr = Radarr(timeout: TimeSpan.FromMilliseconds(200));

        await Assert.ThrowsAsync<ConnectionException>(() => radarr.GetHealthAsync());

        Assert.Single(_server.Requests);
    }

    [Fact]
    public async Task Search_IsNotRetried()
    {
        _server.Answer(HttpStatusCode.ServiceUnavailable);

        await Assert.ThrowsAsync<ApiException>(() => Radarr().SearchAsync(ArrSearch.Movies, [201]));

        Assert.Equal(["POST /api/v3/command"], _server.Requests);
    }

    [Fact]
    public async Task QueueRemoval_IsNotRetried()
    {
        _server.Answer(HttpStatusCode.ServiceUnavailable);

        await Assert.ThrowsAsync<ApiException>(() => Radarr().RemoveFromQueueAsync(1001, true, true));

        Assert.Equal(["DELETE /api/v3/queue/1001"], _server.Requests);
    }

    [Fact]
    public async Task ProfileChange_RetriesItsReadButNotItsWrite()
    {
        _server.Answer(HttpStatusCode.ServiceUnavailable, """{ "id": 201, "qualityProfileId": 4 }""",
            HttpStatusCode.ServiceUnavailable);

        await Assert.ThrowsAsync<ApiException>(() => Radarr().SetQualityProfileAsync(201, 6));

        Assert.Equal(["GET /api/v3/movie/201", "GET /api/v3/movie/201", "PUT /api/v3/movie/201"], _server.Requests);
    }

    private RadarrClient Radarr(bool skipCertificateValidation = false, TimeSpan? timeout = null)
    {
        var server = new MediaServer
        {
            Name = "Test Radarr", ServiceType = ServiceType.Radarr, Host = "radarr.test", Port = 7878,
            ApiKey = "test-key", SkipCertificateValidation = skipCertificateValidation
        };
        return _factory.Radarr(server, timeout ?? Timeout);
    }

    /// <summary>
    ///     Gives the queued answers in order, then 200 with an empty list. An answer is a status (with an empty list
    ///     body), a JSON body (with 200), or an exception to throw.
    /// </summary>
    private sealed class ScriptedServer : HttpMessageHandler
    {
        private readonly ConcurrentQueue<object> _answers = new();

        public ConcurrentQueue<string> Requests { get; } = new();

        /// <summary>Never answers: waits for the caller to give up.</summary>
        public bool Hang { get; set; }

        public void Answer(params object[] answers)
        {
            foreach (var answer in answers)
                _answers.Enqueue(answer);
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Enqueue($"{request.Method} {request.RequestUri!.AbsolutePath}");
            if (Hang) await Task.Delay(System.Threading.Timeout.Infinite, cancellationToken);

            if (!_answers.TryDequeue(out var answer)) return Json(HttpStatusCode.OK, "[]");
            return answer switch
            {
                HttpStatusCode status => Json(status, "[]"),
                string body => Json(HttpStatusCode.OK, body),
                Exception error => throw error,
                _ => throw new ArgumentException($"Not an answer: {answer}")
            };
        }

        private static HttpResponseMessage Json(HttpStatusCode status, string body)
        {
            return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }
}
