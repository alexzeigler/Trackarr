using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Trackarr.Core.Server;
using Trackarr.Data;
using Trackarr.Services.Media;
using Trackarr.Services.Settings;
using Trackarr.Services.UI;

namespace Trackarr.Tests;

/// <summary>API keys and the proxy password are encrypted in the database (AUDIT S2).</summary>
public sealed class SecretsTests : IDisposable
{
    private const string Key = "0123456789abcdef0123456789abcdef";

    private readonly SecretProtector _secrets = NewProtector();
    private readonly SqliteFileFactory _factory;

    public SecretsTests()
    {
        _factory = new SqliteFileFactory(_secrets);
    }

    public void Dispose()
    {
        _factory.Dispose();
    }

    // Each EphemeralDataProtectionProvider has its own keys, like a fresh /config/keys folder.
    private static SecretProtector NewProtector()
    {
        return new SecretProtector(new EphemeralDataProtectionProvider());
    }

    #region SecretProtector

    [Fact]
    public void Protect_RoundTrips_AndMarksTheValue()
    {
        var stored = _secrets.Protect(Key);

        Assert.StartsWith(SecretProtector.Prefix, stored);
        Assert.DoesNotContain(Key, stored);
        Assert.Equal(Key, _secrets.Unprotect(stored));
    }

    [Fact]
    public void Protect_LeavesEmptyAndAlreadyProtectedValuesAlone()
    {
        var stored = _secrets.Protect(Key);

        Assert.Equal("", _secrets.Protect(""));
        Assert.Equal(stored, _secrets.Protect(stored));
    }

    [Fact]
    public void Unprotect_PlainValueFromBeforeEncryption_ReadsAsIs()
    {
        Assert.Equal(Key, _secrets.Unprotect(Key));
    }

    [Fact]
    public void Unprotect_WithOtherKeys_ReadsAsEmptyInsteadOfThrowing()
    {
        var stored = NewProtector().Protect(Key);

        Assert.Equal("", _secrets.Unprotect(stored));
        Assert.False(_secrets.TryUnprotect(stored, out _));
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("short", "••••")]
    [InlineData(Key, "••••cdef")]
    public void Mask_ShowsAtMostTheLastFourCharacters(string? value, string expected)
    {
        Assert.Equal(expected, SecretProtector.Mask(value));
    }

    #endregion

    #region Database

    [Fact]
    public async Task ApiKey_IsStoredEncrypted_AndReadsBackPlain()
    {
        var id = await AddServerAsync(Key);

        Assert.StartsWith(SecretProtector.Prefix, await StoredApiKeyAsync(id));

        await using var db = _factory.CreateDbContext();
        Assert.Equal(Key, (await db.MediaServers.SingleAsync()).ApiKey);
    }

    [Fact]
    public async Task ContextsWithDifferentProtectors_DontShareAModel()
    {
        // EF Core caches the model, converter included. Without the cache key per protector, a context without one
        // would reuse this test's converter, or the other way round.
        var id = await AddServerAsync(Key);

        await using var raw = new AppDbContext(_factory.Options);
        Assert.StartsWith(SecretProtector.Prefix, (await raw.MediaServers.SingleAsync(s => s.Id == id)).ApiKey);

        await using var other = new AppDbContext(_factory.Options, NewProtector());
        Assert.Equal("", (await other.MediaServers.SingleAsync(s => s.Id == id)).ApiKey);
    }

    [Fact]
    public async Task Upgrade_EncryptsPlainKeys_AndCountsUnreadableOnes()
    {
        await using (var raw = new AppDbContext(_factory.Options))
        {
            raw.MediaServers.AddRange(Server("plain", Key), Server("empty", ""),
                Server("other install", NewProtector().Protect(Key)));
            await raw.SaveChangesAsync();
        }

        var already = await AddServerAsync(Key);
        var before = await StoredApiKeyAsync(already);

        await using var db = _factory.CreateDbContext();
        var (encrypted, unreadable) = await SecretUpgrade.ProtectExistingAsync(db, _secrets);

        Assert.Equal(1, encrypted);
        Assert.Equal(1, unreadable);
        Assert.Equal(before, await StoredApiKeyAsync(already)); // already encrypted: untouched

        var keys = await db.MediaServers.AsNoTracking().ToDictionaryAsync(s => s.Name, s => s.ApiKey);
        Assert.Equal(Key, keys["plain"]);
        Assert.Equal("", keys["empty"]);
        Assert.Equal("", keys["other install"]);

        Assert.Equal((0, 1), await SecretUpgrade.ProtectExistingAsync(db, _secrets)); // nothing left to do
        Assert.False(DatabaseFilesContain(Key), "the plain-text key is still somewhere in the database file");
    }

    [Fact]
    public async Task UpdateServerStats_DoesNotRewriteTheApiKey()
    {
        var id = await AddServerAsync(Key);
        var before = await StoredApiKeyAsync(id);
        var settings = new SettingsService(_factory, NullLogger<SettingsService>.Instance, new DataChangeBus());
        var health = new HealthService(_factory, settings, new DataChangeBus());
        var service = new TestServices(_factory, new NoHttp(), health: health).Servers;

        await service.UpdateStatusAsync(id, s => s.LastPingMs = 42);

        // Protect uses a fresh IV every time, so a rewrite would show up as a different stored value.
        Assert.Equal(before, await StoredApiKeyAsync(id));
        await using var db = _factory.CreateDbContext();
        Assert.Equal(42L, (await db.MediaServers.SingleAsync(s => s.Id == id)).LastPingMs);
    }

    #endregion

    #region Proxy password

    [Fact]
    public async Task ProxyPassword_IsStoredEncrypted_AndReadsBackPlain()
    {
        var settings = await NewSettingsAsync();

        await settings.SaveCategoryAsync("Network",
            new Dictionary<string, string> { ["ProxyUsername"] = "me", ["ProxyPassword"] = "hunter22" });

        Assert.StartsWith(SecretProtector.Prefix, await StoredSettingAsync("ProxyPassword"));
        Assert.Equal("me", await StoredSettingAsync("ProxyUsername"));
        Assert.Equal("hunter22", settings.GetString("Network", "ProxyPassword"));
        Assert.Equal("hunter22", (await NewSettingsAsync()).GetString("Network", "ProxyPassword"));
    }

    [Fact]
    public async Task CredentialsInTheProxyUrl_MoveIntoTheirOwnSettingsAtStartup()
    {
        var settings = await NewSettingsAsync();
        await settings.SaveCategoryAsync("Network",
            new Dictionary<string, string> { ["ProxyUrl"] = "http://me:p%40ss@proxy.lan:3128" });

        var restarted = await NewSettingsAsync();

        Assert.Equal("http://proxy.lan:3128", restarted.GetString("Network", "ProxyUrl"));
        Assert.Equal("me", restarted.GetString("Network", "ProxyUsername"));
        Assert.Equal("p@ss", restarted.GetString("Network", "ProxyPassword"));
        Assert.StartsWith(SecretProtector.Prefix, await StoredSettingAsync("ProxyPassword"));
        Assert.Equal("http://proxy.lan:3128", await StoredSettingAsync("ProxyUrl"));
        Assert.False(DatabaseFilesContain("p%40ss"), "the old proxy URL is still somewhere in the database file");
    }

    [Theory]
    [InlineData("http://user:pass@proxy:3128", true, "http://proxy:3128", "user", "pass")]
    [InlineData("socks5://user@proxy:1080", true, "socks5://proxy:1080", "user", "")]
    [InlineData("http://u%3Ax:p%3Ay@[fd00::1]:3128/path", true, "http://[fd00::1]:3128/path", "u:x", "p:y")]
    [InlineData("http://proxy:3128", false, "http://proxy:3128", "", "")]
    [InlineData("not a url", false, "not a url", "", "")]
    [InlineData(null, false, "", "", "")]
    public void TrySplitProxyCredentials_SeparatesUserAndPassword(string? value, bool split, string url, string user,
        string password)
    {
        Assert.Equal(split, SettingsService.TrySplitProxyCredentials(value, out var u, out var n, out var p));
        Assert.Equal((url, user, password), (u, n, p));
    }

    #endregion

    private async Task<SettingsService> NewSettingsAsync()
    {
        var settings = new SettingsService(_factory, NullLogger<SettingsService>.Instance, new DataChangeBus(),
            _secrets);
        await settings.InitializeAsync();
        return settings;
    }

    private static MediaServer Server(string name, string apiKey)
    {
        return new MediaServer
        {
            Name = name, ServiceType = ServiceType.Radarr, Host = "radarr.test", Port = 7878, ApiKey = apiKey,
            IsEnabled = true, CreatedAt = DateTime.UtcNow
        };
    }

    private async Task<int> AddServerAsync(string apiKey)
    {
        await using var db = _factory.CreateDbContext();
        var server = Server("Radarr", apiKey);
        db.MediaServers.Add(server);
        await db.SaveChangesAsync();
        return server.Id;
    }

    // What's actually in the column, below the converter.
    private async Task<string> StoredApiKeyAsync(int id)
    {
        await using var raw = new AppDbContext(_factory.Options);
        return await raw.Database
            .SqlQuery<string>($"""SELECT "ApiKey" AS "Value" FROM "MediaServers" WHERE "Id" = {id}""")
            .SingleAsync();
    }

    // Searches the raw bytes, free pages and journals included, the way someone with a copy of the file could.
    private bool DatabaseFilesContain(string text)
    {
        var needle = System.Text.Encoding.UTF8.GetBytes(text);
        foreach (var suffix in new[] { "", "-wal", "-journal" })
        {
            var path = _factory.DbPath + suffix;
            if (!File.Exists(path)) continue;

            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using var copy = new MemoryStream();
            stream.CopyTo(copy);
            if (copy.ToArray().AsSpan().IndexOf(needle) >= 0) return true;
        }

        return false;
    }

    private sealed class NoHttp : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            return new HttpClient();
        }
    }

    private async Task<string> StoredSettingAsync(string key)
    {
        await using var raw = new AppDbContext(_factory.Options);
        return await raw.Database
            .SqlQuery<string>(
                $"""SELECT "Value" FROM "AppSettings" WHERE "Category" = 'Network' AND "Key" = {key}""")
            .SingleAsync();
    }
}
