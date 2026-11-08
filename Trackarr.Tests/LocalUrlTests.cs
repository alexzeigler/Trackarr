using Trackarr.Services.Auth;

namespace Trackarr.Tests;

public class LocalUrlTests
{
    [Theory]
    [InlineData("/")]
    [InlineData("/queue")]
    [InlineData("/trackarr/missing?page=2")]
    [InlineData("/settings/security#password")]
    public void IsLocal_AcceptsPathsOnThisSite(string url)
    {
        Assert.True(LocalUrl.IsLocal(url));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("queue")]
    [InlineData("https://evil.example/")]
    [InlineData("//evil.example/")]
    [InlineData("/\\evil.example/")]
    [InlineData("/queue\r\nLocation: https://evil.example/")]
    [InlineData("javascript:alert(1)")]
    public void IsLocal_RejectsAnythingThatCouldLeaveTheSite(string? url)
    {
        Assert.False(LocalUrl.IsLocal(url));
    }
}
