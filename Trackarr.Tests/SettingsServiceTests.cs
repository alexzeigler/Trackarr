using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Trackarr.Core.Server;
using Trackarr.Core.Settings;
using Trackarr.Services.Settings;

namespace Trackarr.Tests;

public class SettingsServiceTests
{
    private static SettingsService Create(SqliteInMemoryFactory factory) =>
        new(factory, NullLogger<SettingsService>.Instance);

    #region InitializeAsync

    [Fact]
    public async Task Initialize_SeedsDefaults()
    {
        using var factory = new SqliteInMemoryFactory();
        var service = Create(factory);

        await service.InitializeAsync();

        Assert.Equal(70, service.GetInt("HealthPolicy", "StartingScore"));
        Assert.Equal("Trackarr", service.GetString("General", "AppName"));
        Assert.True(service.GetBool("General", "AutoStartRoutines"));
    }

    [Fact]
    public async Task Initialize_DoesNotOverwriteExistingValues()
    {
        using var factory = new SqliteInMemoryFactory();
        await using (var db = factory.CreateDbContext())
        {
            db.AppSettings.Add(new AppSetting
            {
                Category = "General", Key = "AppName", Value = "MyCustomName", Type = "string"
            });
            await db.SaveChangesAsync();
        }

        var service = Create(factory);
        await service.InitializeAsync();

        Assert.Equal("MyCustomName", service.GetString("General", "AppName"));
    }

    [Fact]
    public async Task Initialize_PersistsToDatabase()
    {
        using var factory = new SqliteInMemoryFactory();
        var service = Create(factory);
        await service.InitializeAsync();

        await using var db = factory.CreateDbContext();
        var count = await db.AppSettings.CountAsync();
        Assert.True(count > 0);
    }

    #endregion

    #region Typed Accessors

    [Fact]
    public async Task GetString_ReturnsCachedValue()
    {
        using var factory = new SqliteInMemoryFactory();
        var service = Create(factory);
        await service.InitializeAsync();

        Assert.Equal("Trackarr", service.GetString("General", "AppName"));
    }

    [Fact]
    public void GetString_MissingKey_ReturnsDefault()
    {
        using var factory = new SqliteInMemoryFactory();
        var service = Create(factory);

        Assert.Equal("fallback", service.GetString("Missing", "Key", "fallback"));
    }

    [Fact]
    public void GetString_MissingKey_NoDefault_ReturnsEmpty()
    {
        using var factory = new SqliteInMemoryFactory();
        var service = Create(factory);

        Assert.Equal("", service.GetString("Missing", "Key"));
    }

    [Fact]
    public async Task GetInt_ParsesValue()
    {
        using var factory = new SqliteInMemoryFactory();
        var service = Create(factory);
        await service.InitializeAsync();

        Assert.Equal(70, service.GetInt("HealthPolicy", "StartingScore"));
    }

    [Fact]
    public void GetInt_MissingKey_ReturnsDefault()
    {
        using var factory = new SqliteInMemoryFactory();
        var service = Create(factory);

        Assert.Equal(42, service.GetInt("Missing", "Key", 42));
    }

    [Fact]
    public async Task GetInt_NonNumericValue_ReturnsDefault()
    {
        using var factory = new SqliteInMemoryFactory();
        await using (var db = factory.CreateDbContext())
        {
            db.AppSettings.Add(new AppSetting
            {
                Category = "Test", Key = "BadInt", Value = "not-a-number", Type = "int"
            });
            await db.SaveChangesAsync();
        }

        var service = Create(factory);
        await service.InitializeAsync();

        Assert.Equal(0, service.GetInt("Test", "BadInt"));
    }

    [Fact]
    public async Task GetBool_ParsesValue()
    {
        using var factory = new SqliteInMemoryFactory();
        var service = Create(factory);
        await service.InitializeAsync();

        Assert.True(service.GetBool("General", "AutoStartRoutines"));
    }

    [Fact]
    public void GetBool_MissingKey_ReturnsDefault()
    {
        using var factory = new SqliteInMemoryFactory();
        var service = Create(factory);

        Assert.True(service.GetBool("Missing", "Key", true));
        Assert.False(service.GetBool("Missing", "Key", false));
    }

    [Fact]
    public async Task GetBool_NonBoolValue_ReturnsDefault()
    {
        using var factory = new SqliteInMemoryFactory();
        await using (var db = factory.CreateDbContext())
        {
            db.AppSettings.Add(new AppSetting { Category = "Test", Key = "BadBool", Value = "maybe", Type = "bool" });
            await db.SaveChangesAsync();
        }

        var service = Create(factory);
        await service.InitializeAsync();

        Assert.False(service.GetBool("Test", "BadBool"));
    }

    [Fact]
    public async Task GetDecimal_ParsesValue()
    {
        using var factory = new SqliteInMemoryFactory();
        var service = Create(factory);
        await service.InitializeAsync();

        Assert.Equal(1.5m, service.GetDecimal("HealthPolicy", "PenaltyStreakEscalation"));
    }

    [Fact]
    public void GetDecimal_MissingKey_ReturnsDefault()
    {
        using var factory = new SqliteInMemoryFactory();
        var service = Create(factory);

        Assert.Equal(3.14m, service.GetDecimal("Missing", "Key", 3.14m));
    }

    #endregion

    #region SaveCategoryAsync

    [Fact]
    public async Task SaveCategory_UpdatesExistingValues()
    {
        using var factory = new SqliteInMemoryFactory();
        var service = Create(factory);
        await service.InitializeAsync();

        await service.SaveCategoryAsync("General", new Dictionary<string, string> { ["AppName"] = "MyApp" });

        Assert.Equal("MyApp", service.GetString("General", "AppName"));

        await using var db = factory.CreateDbContext();
        var setting = await db.AppSettings.FirstAsync(s => s.Category == "General" && s.Key == "AppName");
        Assert.Equal("MyApp", setting.Value);
    }

    [Fact]
    public async Task SaveCategory_InsertsNewValues()
    {
        using var factory = new SqliteInMemoryFactory();
        var service = Create(factory);
        await service.InitializeAsync();

        await service.SaveCategoryAsync("Custom", new Dictionary<string, string> { ["NewKey"] = "NewValue" });

        Assert.Equal("NewValue", service.GetString("Custom", "NewKey"));
    }

    [Fact]
    public async Task SaveCategory_FiresChangedEvent()
    {
        using var factory = new SqliteInMemoryFactory();
        var service = Create(factory);
        await service.InitializeAsync();

        var fired = false;
        service.OnSettingsChanged += () => fired = true;

        await service.SaveCategoryAsync("General", new Dictionary<string, string> { ["AppName"] = "X" });

        Assert.True(fired);
    }

    #endregion

    #region Server Behavior Resolution

    [Fact]
    public async Task ShouldMonitorQueue_ServerOverrideWins()
    {
        using var factory = new SqliteInMemoryFactory();
        var service = Create(factory);
        await service.InitializeAsync();

        var server = new MediaServer { MonitorQueue = false };
        Assert.False(service.ShouldMonitorQueue(server));
    }

    [Fact]
    public async Task ShouldMonitorQueue_FallsBackToGlobalDefault()
    {
        using var factory = new SqliteInMemoryFactory();
        var service = Create(factory);
        await service.InitializeAsync();

        var server = new MediaServer { MonitorQueue = null };
        Assert.True(service.ShouldMonitorQueue(server));
    }

    [Fact]
    public async Task ShouldAutoRemove_ServerOverrideWins()
    {
        using var factory = new SqliteInMemoryFactory();
        var service = Create(factory);
        await service.InitializeAsync();

        var server = new MediaServer { AutoRemoveDownloads = false };
        Assert.False(service.ShouldAutoRemove(server));
    }

    [Fact]
    public async Task ShouldSearchMissing_ServerOverrideWins()
    {
        using var factory = new SqliteInMemoryFactory();
        var service = Create(factory);
        await service.InitializeAsync();

        var server = new MediaServer { SearchMissing = false };
        Assert.False(service.ShouldSearchMissing(server));
    }

    [Fact]
    public async Task GetRequestTimeout_ServerOverrideWins()
    {
        using var factory = new SqliteInMemoryFactory();
        var service = Create(factory);
        await service.InitializeAsync();

        var server = new MediaServer { RequestTimeoutSeconds = 60 };
        Assert.Equal(60, service.GetRequestTimeout(server));
    }

    [Fact]
    public async Task GetRequestTimeout_FallsBackToGlobalDefault()
    {
        using var factory = new SqliteInMemoryFactory();
        var service = Create(factory);
        await service.InitializeAsync();

        var server = new MediaServer { RequestTimeoutSeconds = null };
        Assert.Equal(30, service.GetRequestTimeout(server));
    }

    #endregion

    #region Policy Materialization

    [Fact]
    public async Task GetHealthPolicy_ReturnsPopulatedPolicy()
    {
        using var factory = new SqliteInMemoryFactory();
        var service = Create(factory);
        await service.InitializeAsync();

        var policy = service.GetHealthPolicy();

        Assert.Equal(70, policy.StartingScore);
        Assert.Equal(100, policy.MaximumScore);
        Assert.Equal(-20, policy.StalledNoConnectionsPenalty);
        Assert.Equal(10, policy.StartupGracePeriodMinutes);
    }

    [Fact]
    public async Task GetHealthPolicy_ReflectsCustomSettings()
    {
        using var factory = new SqliteInMemoryFactory();
        var service = Create(factory);
        await service.InitializeAsync();
        await service.SaveCategoryAsync("HealthPolicy", new Dictionary<string, string> { ["StartingScore"] = "50" });

        var policy = service.GetHealthPolicy();

        Assert.Equal(50, policy.StartingScore);
    }

    [Fact]
    public async Task GetMissingPolicy_ReturnsPopulatedPolicy()
    {
        using var factory = new SqliteInMemoryFactory();
        var service = Create(factory);
        await service.InitializeAsync();

        var policy = service.GetMissingPolicy();

        Assert.Equal(TimeSpan.FromMinutes(10), policy.SearchCooldown);
        Assert.Equal(3, policy.MaxFailedSearches);
        Assert.Equal(3, policy.MaxEpisodeCascade);
        Assert.Equal(5, policy.AbandonAfter);
    }

    #endregion
}
