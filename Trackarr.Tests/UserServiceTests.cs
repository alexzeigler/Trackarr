using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Trackarr.Services.Auth;
using Trackarr.Services.Settings;
using Trackarr.Services.UI;

namespace Trackarr.Tests;

public sealed class UserServiceTests : IDisposable
{
    private const string Password = "correct horse";

    private readonly SqliteFileFactory _factory = new();
    private readonly UserService _users;

    public UserServiceTests()
    {
        _users = NewService();
    }

    public void Dispose()
    {
        _factory.Dispose();
    }

    private UserService NewService()
    {
        return new UserService(_factory, NullLogger<UserService>.Instance);
    }

    [Fact]
    public async Task HasUser_IsFalseUntilTheFirstLoginIsCreated()
    {
        Assert.False(await _users.HasUserAsync());

        await _users.CreateFirstUserAsync("admin", Password);

        Assert.True(await _users.HasUserAsync());
        Assert.True(await NewService().HasUserAsync());
    }

    [Fact]
    public async Task CreateFirstUser_OnlyWorksOnce()
    {
        await _users.CreateFirstUserAsync("admin", Password);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _users.CreateFirstUserAsync("other", Password));
        await using var db = await _factory.CreateDbContextAsync();
        Assert.Equal(1, await db.Users.CountAsync());
    }

    [Theory]
    [InlineData("", Password)]
    [InlineData("   ", Password)]
    [InlineData("admin", "short")]
    public async Task CreateFirstUser_RejectsBadInput(string username, string password)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _users.CreateFirstUserAsync(username, password));
        Assert.False(await _users.HasUserAsync());
    }

    [Fact]
    public async Task CreateFirstUser_StoresAHashNotThePassword()
    {
        var user = await _users.CreateFirstUserAsync("  admin  ", Password);

        Assert.Equal("admin", user.Username);
        Assert.NotEqual(Password, user.PasswordHash);
        Assert.DoesNotContain(Password, user.PasswordHash, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ValidateCredentials_AcceptsTheRightPasswordWithAnyUsernameCase()
    {
        await _users.CreateFirstUserAsync("Admin", Password);

        var user = await _users.ValidateCredentialsAsync(" admin ", Password);

        Assert.NotNull(user);
        Assert.Equal("Admin", user.Username);
        Assert.NotNull(user.LastLoginAt);
    }

    [Theory]
    [InlineData("admin", "wrong password")]
    [InlineData("nobody", Password)]
    [InlineData("admin", "")]
    [InlineData(null, null)]
    public async Task ValidateCredentials_RejectsWrongDetails(string? username, string? password)
    {
        await _users.CreateFirstUserAsync("admin", Password);

        Assert.Null(await _users.ValidateCredentialsAsync(username, password));
    }

    [Fact]
    public async Task Cookie_IsCurrentUntilThePasswordChanges()
    {
        var user = await _users.CreateFirstUserAsync("admin", Password);
        var cookie = UserService.CreatePrincipal(user, "test");
        Assert.True(await _users.IsCurrentAsync(cookie));

        Assert.Null(await _users.ChangePasswordAsync(user.Id, Password, "a new password"));

        Assert.False(await _users.IsCurrentAsync(cookie));
        // A fresh service (as after a restart) reads the stamp from the database and agrees.
        Assert.False(await NewService().IsCurrentAsync(cookie));
        Assert.Null(await _users.ValidateCredentialsAsync("admin", Password));
        Assert.NotNull(await _users.ValidateCredentialsAsync("admin", "a new password"));
    }

    [Fact]
    public async Task ChangePassword_NeedsTheCurrentPasswordAndAValidNewOne()
    {
        var user = await _users.CreateFirstUserAsync("admin", Password);

        Assert.Equal("The current password is wrong.",
            await _users.ChangePasswordAsync(user.Id, "not it", "a new password"));
        Assert.NotNull(await _users.ChangePasswordAsync(user.Id, Password, "short"));
        Assert.NotNull(await _users.ChangePasswordAsync(user.Id + 1, Password, "a new password"));

        Assert.NotNull(await _users.ValidateCredentialsAsync("admin", Password));
    }

    [Fact]
    public async Task IsCurrent_RejectsTamperedOrForeignCookies()
    {
        var user = await _users.CreateFirstUserAsync("admin", Password);

        var tampered = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new Claim(UserService.StampClaim, "not-the-stamp")
        ], "test"));
        var unknownUser = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, "999"), new Claim(UserService.StampClaim, user.SecurityStamp)
        ], "test"));

        Assert.False(await _users.IsCurrentAsync(tampered));
        Assert.False(await _users.IsCurrentAsync(unknownUser));
        Assert.False(await _users.IsCurrentAsync(new ClaimsPrincipal(new ClaimsIdentity())));
        Assert.False(await _users.IsCurrentAsync((ClaimsPrincipal?)null));
    }

    [Fact]
    public async Task Reset_RemovesTheLoginAndInvalidatesItsCookies()
    {
        var user = await _users.CreateFirstUserAsync("admin", Password);
        var cookie = UserService.CreatePrincipal(user, "test");

        Assert.Equal(1, await _users.ResetAsync());

        Assert.False(await _users.HasUserAsync());
        Assert.False(await _users.IsCurrentAsync(cookie));
        // Setup works again.
        await _users.CreateFirstUserAsync("new admin", Password);
    }

    [Fact]
    public async Task ResetData_KeepsTheLogin()
    {
        await _users.CreateFirstUserAsync("admin", Password);
        var settings = new SettingsService(_factory, NullLogger<SettingsService>.Instance, new DataChangeBus());

        await settings.ResetSettingsAsync();

        Assert.NotNull(await NewService().ValidateCredentialsAsync("admin", Password));
    }
}
