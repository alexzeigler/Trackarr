using System.Net;

namespace Trackarr.Tests;

public sealed class FixtureHandlerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"trackarr-fixtures-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    [Theory]
    [InlineData("/api/v3/queue", "api_v3_queue.json")]
    [InlineData("/radarr/api/v3/movie/42", "api_v3_movie_id.json")]
    [InlineData("/api/v3/wanted/missing", "api_v3_wanted_missing.json")]
    [InlineData("/sonarr/api/v3/episode/7/file", "api_v3_episode_id_file.json")]
    [InlineData("/API/V3/System/Status", "api_v3_system_status.json")]
    public void FileNameFor_MatchesTheMockServerRule(string path, string expected)
    {
        Assert.Equal(expected, FixtureHandler.FileNameFor(path));
    }

    [Fact]
    public void FileNameFor_IgnoresPathsOutsideTheApi()
    {
        Assert.Null(FixtureHandler.FileNameFor("/mock/status"));
    }

    [Fact]
    public async Task Get_ServesTheRecordingAndIgnoresTheQueryAndUrlBase()
    {
        Directory.CreateDirectory(Path.Combine(_root, FixtureHandler.Radarr));
        await File.WriteAllTextAsync(Path.Combine(_root, FixtureHandler.Radarr, "api_v3_queue.json"),
            """{"records":[]}""");

        var handler = new FixtureHandler(FixtureHandler.Radarr, _root);
        using var client = handler.AsFactory().CreateClient("any");

        var response = await client.GetAsync("http://radarr.local:7878/radarr/api/v3/queue?page=1&pageSize=50");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("""{"records":[]}""", await response.Content.ReadAsStringAsync());
        Assert.Equal(new[] { "api_v3_queue.json" }, handler.Requested);
        Assert.True(handler.Has("/api/v3/queue"));
    }

    [Fact]
    public async Task Get_WithoutARecording_Returns404ThatSaysWhatToRecord()
    {
        var handler = new FixtureHandler(FixtureHandler.Sonarr, _root);
        using var client = handler.AsFactory().CreateClient("any");

        var response = await client.GetAsync("http://sonarr.local/api/v3/series");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("sonarr/api_v3_series.json", await response.Content.ReadAsStringAsync());
        Assert.False(handler.Has("/api/v3/series"));
    }

    [Fact]
    public async Task Writes_AreRefused()
    {
        var handler = new FixtureHandler(FixtureHandler.Radarr, _root);
        using var client = handler.AsFactory().CreateClient("any");

        var response = await client.DeleteAsync("http://radarr.local/api/v3/queue/5");

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        Assert.Empty(handler.Requested);
    }
}
