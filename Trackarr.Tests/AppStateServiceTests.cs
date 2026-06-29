using Microsoft.Extensions.Logging.Abstractions;
using Trackarr.Services.Settings;
using Trackarr.Services.UI;

namespace Trackarr.Tests;

public class AppStateServiceTests : IDisposable
{
    private readonly SqliteInMemoryFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    private AppStateService Create() =>
        new(new SettingsService(_factory, NullLogger<SettingsService>.Instance, new DataChangeBus()),
            new DataChangeBus());

    #region Trailing Slash Handling

    [Fact]
    public void UpdateRoute_TrailingSlash_StillMatchesRoute()
    {
        var sut = Create();

        sut.UpdateRoute("http://localhost/servers/");

        Assert.Equal("Servers", sut.PageTitle);
    }

    #endregion

    #region UpdateRoute — PageTitle

    [Fact]
    public void UpdateRoute_Root_SetsPageTitleToQueue()
    {
        var sut = Create();

        sut.UpdateRoute("http://localhost/");

        Assert.Equal("Queue", sut.PageTitle);
    }

    [Fact]
    public void UpdateRoute_Servers_SetsPageTitleToServers()
    {
        var sut = Create();

        sut.UpdateRoute("http://localhost/servers");

        Assert.Equal("Servers", sut.PageTitle);
    }

    [Fact]
    public void UpdateRoute_SettingsGeneral_SetsPageTitleToGeneral()
    {
        var sut = Create();

        sut.UpdateRoute("http://localhost/settings/general");

        Assert.Equal("General", sut.PageTitle);
    }

    [Fact]
    public void UpdateRoute_SystemRoutines_SetsPageTitleToRoutines()
    {
        var sut = Create();

        sut.UpdateRoute("http://localhost/system/routines");

        Assert.Equal("Routines", sut.PageTitle);
    }

    [Fact]
    public void UpdateRoute_UnknownRoute_SetsPageTitleToTrackarr()
    {
        var sut = Create();

        sut.UpdateRoute("http://localhost/unknown/page");

        Assert.Equal("Trackarr", sut.PageTitle);
    }

    #endregion

    #region UpdateRoute — Breadcrumbs

    [Fact]
    public void UpdateRoute_Root_HasSingleBreadcrumb()
    {
        var sut = Create();

        sut.UpdateRoute("http://localhost/");

        var crumb = Assert.Single(sut.Breadcrumbs);
        Assert.Equal("Queue", crumb.Label);
        Assert.Null(crumb.Href);
    }

    [Fact]
    public void UpdateRoute_Servers_HasSingleBreadcrumb()
    {
        var sut = Create();

        sut.UpdateRoute("http://localhost/servers");

        var crumb = Assert.Single(sut.Breadcrumbs);
        Assert.Equal("Servers", crumb.Label);
        Assert.Null(crumb.Href);
    }

    [Fact]
    public void UpdateRoute_SettingsGeneral_HasParentBreadcrumbSettings()
    {
        var sut = Create();

        sut.UpdateRoute("http://localhost/settings/general");

        Assert.Equal(2, sut.Breadcrumbs.Count);
        Assert.Equal("Settings", sut.Breadcrumbs[0].Label);
        Assert.Equal("/settings/general", sut.Breadcrumbs[0].Href);
        Assert.Equal("General", sut.Breadcrumbs[1].Label);
        Assert.Null(sut.Breadcrumbs[1].Href);
    }

    [Fact]
    public void UpdateRoute_SystemRoutines_HasParentBreadcrumbSystem()
    {
        var sut = Create();

        sut.UpdateRoute("http://localhost/system/routines");

        Assert.Equal(2, sut.Breadcrumbs.Count);
        Assert.Equal("System", sut.Breadcrumbs[0].Label);
        Assert.Equal("/system/routines", sut.Breadcrumbs[0].Href);
        Assert.Equal("Routines", sut.Breadcrumbs[1].Label);
        Assert.Null(sut.Breadcrumbs[1].Href);
    }

    [Fact]
    public void UpdateRoute_UnknownRoute_HasFallbackBreadcrumb()
    {
        var sut = Create();

        sut.UpdateRoute("http://localhost/does-not-exist");

        var crumb = Assert.Single(sut.Breadcrumbs);
        Assert.Equal("Trackarr", crumb.Label);
        Assert.Null(crumb.Href);
    }

    #endregion

    #region UpdateRoute — Case Insensitivity

    [Theory]
    [InlineData("http://localhost/SERVERS")]
    [InlineData("http://localhost/Servers")]
    [InlineData("http://localhost/sErVeRs")]
    public void UpdateRoute_IsCaseInsensitive(string uri)
    {
        var sut = Create();

        sut.UpdateRoute(uri);

        Assert.Equal("Servers", sut.PageTitle);
    }

    [Fact]
    public void UpdateRoute_SettingsGeneral_CaseInsensitive()
    {
        var sut = Create();

        sut.UpdateRoute("http://localhost/SETTINGS/GENERAL");

        Assert.Equal("General", sut.PageTitle);
        Assert.Equal(2, sut.Breadcrumbs.Count);
        Assert.Equal("Settings", sut.Breadcrumbs[0].Label);
    }

    #endregion

    #region OnStateChanged Event

    [Fact]
    public void UpdateRoute_FiresOnStateChanged()
    {
        var sut = Create();
        var fired = false;
        sut.OnStateChanged += () => fired = true;

        sut.UpdateRoute("http://localhost/servers");

        Assert.True(fired);
    }

    [Fact]
    public void UpdateRoute_FiresOnStateChanged_ForUnknownRoute()
    {
        var sut = Create();
        var fired = false;
        sut.OnStateChanged += () => fired = true;

        sut.UpdateRoute("http://localhost/nope");

        Assert.True(fired);
    }

    #endregion

    #region RequestRefreshAsync

    [Fact]
    public async Task RequestRefreshAsync_InvokesHandler()
    {
        var sut = Create();
        var invoked = false;
        sut.OnRefreshRequested += () =>
        {
            invoked = true;
            return Task.CompletedTask;
        };

        await sut.RequestRefreshAsync();

        Assert.True(invoked);
    }

    [Fact]
    public async Task RequestRefreshAsync_NoHandler_DoesNotThrow()
    {
        var sut = Create();

        var ex = await Record.ExceptionAsync(() => sut.RequestRefreshAsync());

        Assert.Null(ex);
    }

    #endregion
}
