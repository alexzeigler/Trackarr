using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Trackarr.Core.Event;
using Trackarr.Core.Media.Movies;
using Trackarr.Core.Server;
using Trackarr.Core.Settings;
using Trackarr.Services.Settings;
using Trackarr.Services.UI;

namespace Trackarr.Tests;

public class SettingsServiceTests
{
    private static SettingsService Create(SqliteInMemoryFactory factory)
    {
        return new SettingsService(factory, NullLogger<SettingsService>.Instance, new DataChangeBus());
    }

    private static SettingsService Create(SqliteFileFactory factory)
    {
        return new SettingsService(factory, NullLogger<SettingsService>.Instance, new DataChangeBus());
    }

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
        Assert.False(service.GetBool("Missing", "Key"));
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

        Assert.Equal(TimeSpan.FromHours(6), policy.SearchCooldown);
        Assert.Equal(3, policy.MaxFailedSearches);
        Assert.Equal(3, policy.MaxEpisodeCascade);
        Assert.Equal(5, policy.AbandonAfter);
        Assert.Equal(10, policy.MaxSearchesPerRun);
        Assert.Equal(TimeSpan.FromDays(30), policy.RetryAbandonedAfter);
    }

    #endregion

    #region Passive Mode

    [Fact]
    public async Task IsPassiveMode_DefaultsFalse()
    {
        using var factory = new SqliteInMemoryFactory();
        var service = Create(factory);
        await service.InitializeAsync();

        Assert.False(service.IsPassiveMode());
    }

    [Fact]
    public async Task IsPassiveMode_ReflectsSettingChange()
    {
        using var factory = new SqliteInMemoryFactory();
        var service = Create(factory);
        await service.InitializeAsync();

        await service.SaveCategoryAsync("General", new Dictionary<string, string> { ["PassiveMode"] = "True" });

        Assert.True(service.IsPassiveMode());
    }

    #endregion

    #region Console Log Level

    [Fact]
    public async Task ConsoleLogLevel_DefaultsToInformation()
    {
        using var factory = new SqliteInMemoryFactory();
        var service = Create(factory);
        await service.InitializeAsync();

        Assert.Equal("Information", service.GetString("General", "ConsoleLogLevel"));
    }

    [Fact]
    public async Task ConsoleLogLevel_CanBeChanged()
    {
        using var factory = new SqliteInMemoryFactory();
        var service = Create(factory);
        await service.InitializeAsync();

        await service.SaveCategoryAsync("General", new Dictionary<string, string> { ["ConsoleLogLevel"] = "Debug" });

        Assert.Equal("Debug", service.GetString("General", "ConsoleLogLevel"));
    }

    #endregion

    #region ResetDatabaseAsync

    [Fact]
    public async Task ResetDatabase_ClearsAllDataTables()
    {
        using var factory = new SqliteInMemoryFactory();
        var service = Create(factory);
        await service.InitializeAsync();

        await using (var db = factory.CreateDbContext())
        {
            db.LogEvents.Add(new LogEvent
            {
                Message = "test log", Source = "test", LogLevel = LogLevel.Information, TimeStamp = DateTime.UtcNow
            });
            db.Movies.Add(new Movie { RemoteId = 1, Title = "Test Movie" });
            await db.SaveChangesAsync();
        }

        await service.ResetDatabaseAsync();

        await using var verifyDb = factory.CreateDbContext();
        Assert.Equal(0, await verifyDb.LogEvents.CountAsync());
        Assert.Equal(0, await verifyDb.Movies.CountAsync());
    }

    [Fact]
    public async Task ResetDatabase_ReseedsDefaultSettings()
    {
        using var factory = new SqliteInMemoryFactory();
        var service = Create(factory);
        await service.InitializeAsync();

        await service.SaveCategoryAsync("General", new Dictionary<string, string> { ["AppName"] = "MyCustomApp" });

        await service.ResetDatabaseAsync();

        Assert.Equal("Trackarr", service.GetString("General", "AppName"));
    }

    [Fact]
    public async Task ResetDatabase_DeletesServers()
    {
        using var factory = new SqliteInMemoryFactory();
        var service = Create(factory);
        await service.InitializeAsync();

        await using (var db = factory.CreateDbContext())
        {
            db.MediaServers.Add(new MediaServer
            {
                Name = "TestServer",
                Host = "localhost",
                Port = 7878,
                ServiceType = ServiceType.Radarr,
                ApiKey = "test-key"
            });
            await db.SaveChangesAsync();
        }

        await service.ResetDatabaseAsync();

        await using var verifyDb = factory.CreateDbContext();
        Assert.Equal(0, await verifyDb.MediaServers.CountAsync());
    }

    [Fact]
    public async Task ResetDatabase_FiresChangedEvent()
    {
        using var factory = new SqliteInMemoryFactory();
        var service = Create(factory);
        await service.InitializeAsync();

        var fired = false;
        service.OnSettingsChanged += () => fired = true;

        await service.ResetDatabaseAsync();

        Assert.True(fired);
    }

    /// <summary>
    ///     Regression test for a production-only bug: the real connection string sets "Foreign Keys=True",
    ///     which Microsoft.Data.Sqlite re-applies on every connection open — including the implicit
    ///     open/close ExecuteSqlRawAsync does per statement. That silently re-enabled FK enforcement
    ///     mid-reset, so deletes could throw (or be skipped) depending on table order. SqliteInMemoryFactory
    ///     doesn't reproduce this because it hands EF Core a single already-open connection whose connection
    ///     string never enables FK enforcement, so this test uses a real pooled file connection instead,
    ///     mirroring Program.cs, with rows wired together across FK relationships (including a Restrict FK
    ///     via FallBackProfileId) so an order-dependent FK violation would surface here.
    /// </summary>
    [Fact]
    public async Task ResetDatabase_ClearsAllDataTables_WithRealForeignKeyEnforcement()
    {
        using var factory = new SqliteFileFactory();
        var service = Create(factory);
        await service.InitializeAsync();

        await using (var db = factory.CreateDbContext())
        {
            var server = new MediaServer
            {
                Name = "TestServer", Host = "localhost", Port = 7878, ServiceType = ServiceType.Radarr,
                ApiKey = "test-key"
            };
            db.MediaServers.Add(server);
            await db.SaveChangesAsync();

            var profile = new QualityProfile { RemoteId = 1, Name = "HD", ServerId = server.Id };
            db.QualityProfiles.Add(profile);
            await db.SaveChangesAsync();

            server.FallBackProfileId = profile.Id;

            var movie = new Movie { RemoteId = 1, Title = "Test Movie", ServerId = server.Id };
            db.Movies.Add(movie);

            db.ServerHealthEvents.Add(new ServerHealthEvent
            {
                ServerId = server.Id, EventType = ServerHealthEventType.Down, Message = "down"
            });
            db.ServerStatusSamples.Add(new ServerStatusSample { ServerId = server.Id, Success = false });

            await db.SaveChangesAsync();
        }

        await service.ResetDatabaseAsync();

        await using var verifyDb = factory.CreateDbContext();
        Assert.Equal(0, await verifyDb.MediaServers.CountAsync());
        Assert.Equal(0, await verifyDb.QualityProfiles.CountAsync());
        Assert.Equal(0, await verifyDb.Movies.CountAsync());
        Assert.Equal(0, await verifyDb.ServerHealthEvents.CountAsync());
        Assert.Equal(0, await verifyDb.ServerStatusSamples.CountAsync());
    }

    #endregion

    #region ResetSettingsAsync

    [Fact]
    public async Task ResetSettings_RestoresDefaults()
    {
        using var factory = new SqliteInMemoryFactory();
        var service = Create(factory);
        await service.InitializeAsync();

        await service.SaveCategoryAsync("General", new Dictionary<string, string> { ["AppName"] = "CustomName" });
        Assert.Equal("CustomName", service.GetString("General", "AppName"));

        await service.ResetSettingsAsync();

        Assert.Equal("Trackarr", service.GetString("General", "AppName"));
    }

    [Fact]
    public async Task ResetSettings_PreservesData()
    {
        using var factory = new SqliteInMemoryFactory();
        var service = Create(factory);
        await service.InitializeAsync();

        await using (var db = factory.CreateDbContext())
        {
            db.MediaServers.Add(new MediaServer
            {
                Name = "KeepMe",
                Host = "localhost",
                Port = 7878,
                ServiceType = ServiceType.Radarr,
                ApiKey = "key"
            });
            await db.SaveChangesAsync();
        }

        await service.ResetSettingsAsync();

        await using var verifyDb = factory.CreateDbContext();
        Assert.Equal(1, await verifyDb.MediaServers.CountAsync());
    }

    [Fact]
    public async Task ResetSettings_FiresChangedEvent()
    {
        using var factory = new SqliteInMemoryFactory();
        var service = Create(factory);
        await service.InitializeAsync();

        var fired = false;
        service.OnSettingsChanged += () => fired = true;

        await service.ResetSettingsAsync();

        Assert.True(fired);
    }

    #endregion
}
