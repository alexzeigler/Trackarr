using System.Collections.Concurrent;
using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Trackarr.Core.Health;
using Trackarr.Core.Media.Missing;
using Trackarr.Core.Server;
using Trackarr.Core.Settings;
using Trackarr.Data;
using Trackarr.Services.UI;

namespace Trackarr.Services.Settings;

// The only disposable fields are SemaphoreSlims, which need no disposal unless their wait handle is used, and this
// singleton lives as long as the app.
#pragma warning disable CA1001
public class SettingsService(
#pragma warning restore CA1001
    IDbContextFactory<AppDbContext> dbFactory,
    ILogger<SettingsService> logger,
    DataChangeBus changeBus)
{
    #region Defaults Registry

    private static readonly Dictionary<(string Category, string Key), (string Value, string Type)> Defaults = new()
    {
        // General
        [("General", "AppName")] = ("Trackarr", "string"),
        [("General", "DisabledServerBehavior")] = ("hidden", "string"),
        [("General", "AutoStartRoutines")] = ("true", "bool"),
        [("General", "DefaultServerType")] = ("Radarr", "string"),
        [("General", "PassiveMode")] = ("false", "bool"),
        [("General", "ConsoleLogLevel")] = ("Information", "string"),

        // Servers (global defaults)
        [("Servers", "MonitorQueue")] = ("true", "bool"),
        [("Servers", "AutoRemoveDownloads")] = ("true", "bool"),
        [("Servers", "SearchMissing")] = ("true", "bool"),
        [("Servers", "RequestTimeoutSeconds")] = ("30", "int"),

        // Network. Host settings (port, bind address, URL base, reverse proxy) are read at startup, so changes
        // apply after a restart. Outgoing proxy settings apply within a couple of minutes.
        [("Network", "Port")] = ("8585", "int"),
        [("Network", "BindAddress")] = ("*", "string"),
        [("Network", "UrlBase")] = ("", "string"),
        [("Network", "ReverseProxy")] = ("false", "bool"),
        [("Network", "TrustedProxies")] = ("", "string"),
        [("Network", "ProxyUrl")] = ("", "string"),
        [("Network", "ProxyBypassLocal")] = ("true", "bool"),

        // Security. The login itself is in the Users table. These keys are LocalAccess's constants.
        [("Security", "LocalAccess")] = ("false", "bool"),
        [("Security", "LocalNetworks")] = ("", "string"),

        // Display
        [("Display", "DateFormat")] = ("MMM d, yyyy h:mm tt", "string"),
        [("Display", "UseTimeAgo")] = ("true", "bool"),
        [("Display", "TimeAgoThresholdHours")] = ("24", "int"),
        [("Display", "QueuePageSize")] = ("10", "int"),
        [("Display", "MissingPageSize")] = ("10", "int"),
        [("Display", "HistoryPageSize")] = ("25", "int"),
        [("Display", "EventsPageSize")] = ("50", "int"),
        [("Display", "ShowQueueCharts")] = ("true", "bool"),
        [("Display", "ShowMissingCharts")] = ("true", "bool"),
        [("Display", "ShowHistoryCharts")] = ("true", "bool"),
        [("Display", "ShowPosterImages")] = ("true", "bool"),
        [("Display", "DefaultQueueGrouping")] = ("mediaTitle", "string"),
        [("Display", "ExpandQueueGroupsByDefault")] = ("true", "bool"),
        [("Display", "TableDensity")] = ("normal", "string"),
        [("Display", "QueueRefreshIntervalSeconds")] = ("10", "int"),

        // Health Policy
        [("HealthPolicy", "StartingScore")] = ("70", "int"),
        [("HealthPolicy", "MaximumScore")] = ("100", "int"),
        [("HealthPolicy", "MinimumVisibleScore")] = ("0", "int"),
        [("HealthPolicy", "RemovalConfidenceThreshold")] = ("70", "int"),
        [("HealthPolicy", "StartupGracePeriodMinutes")] = ("10", "int"),
        [("HealthPolicy", "MinimumBadScansBeforeRemoval")] = ("8", "int"),
        [("HealthPolicy", "RollingWindowSize")] = ("10", "int"),
        [("HealthPolicy", "RequiredBadScansInWindow")] = ("8", "int"),
        [("HealthPolicy", "RecentRecoveryProtectionScans")] = ("3", "int"),
        [("HealthPolicy", "RecoveryCreditMax")] = ("25", "int"),
        [("HealthPolicy", "PenaltyStreakEscalation")] = ("1.5", "decimal"),
        [("HealthPolicy", "PenaltyStreakCap")] = ("8.5", "decimal"),
        [("HealthPolicy", "RewardStreakEscalation")] = ("1.5", "decimal"),
        [("HealthPolicy", "RewardStreakCap")] = ("8.5", "decimal"),
        [("HealthPolicy", "HealthyThreshold")] = ("85", "int"),
        [("HealthPolicy", "StableThreshold")] = ("65", "int"),
        [("HealthPolicy", "WatchingThreshold")] = ("40", "int"),
        [("HealthPolicy", "DegradingThreshold")] = ("15", "int"),
        [("HealthPolicy", "ProgressIncreaseReward")] = ("3", "int"),
        [("HealthPolicy", "SizeLeftDecreaseReward")] = ("3", "int"),
        [("HealthPolicy", "SpeedPositiveReward")] = ("2", "int"),
        [("HealthPolicy", "EtaImprovedReward")] = ("1", "int"),
        [("HealthPolicy", "ErrorClearedReward")] = ("8", "int"),
        [("HealthPolicy", "RecoveryReward")] = ("10", "int"),
        [("HealthPolicy", "SustainedProgressReward")] = ("5", "int"),
        [("HealthPolicy", "NoProgressPenalty")] = ("-2", "int"),
        [("HealthPolicy", "WarningPenalty")] = ("-8", "int"),
        [("HealthPolicy", "ErrorPenalty")] = ("-15", "int"),
        [("HealthPolicy", "StalledPenalty")] = ("-18", "int"),
        [("HealthPolicy", "StalledNoConnectionsPenalty")] = ("-20", "int"),
        [("HealthPolicy", "RepeatedErrorPenalty")] = ("-10", "int"),
        [("HealthPolicy", "PreviousMediaFailurePenalty")] = ("-15", "int"),
        [("HealthPolicy", "QualityDowngradedStillFailingPenalty")] = ("-25", "int"),
        [("HealthPolicy", "ByteProgressProtectionMinutes")] = ("10", "int"),
        [("HealthPolicy", "FetchingMetadataPenalty")] = ("-20", "decimal"),
        [("HealthPolicy", "GoodObservationBadScanDecay")] = ("2", "int"),

        // Missing Policy
        [("MissingPolicy", "SearchCooldownHours")] = ("6", "int"),
        [("MissingPolicy", "MaxSearchesPerRun")] = ("10", "int"),
        [("MissingPolicy", "RetryAbandonedAfterDays")] = ("30", "int"),
        [("MissingPolicy", "MaxFailedSearches")] = ("3", "int"),
        [("MissingPolicy", "MaxEpisodeCascade")] = ("3", "int"),
        [("MissingPolicy", "AbandonAfter")] = ("5", "int"),

        // Maintenance
        [("Maintenance", "HealthEventRetentionDays")] = ("90", "int"),
        [("Maintenance", "RoutineRunRetentionDays")] = ("30", "int"),
        [("Maintenance", "LogEventRetentionDays")] = ("14", "int"),
        [("Maintenance", "SourceSummaryRetentionDays")] = ("90", "int"),
        [("Maintenance", "VacuumAfterCleanup")] = ("true", "bool"),
        [("Maintenance", "CleanupIntervalHours")] = ("24", "int")
    };

    #endregion

    private readonly ConcurrentDictionary<string, AppSetting> _cache = new();
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public event Action? OnSettingsChanged;

    public async Task InitializeAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var existing = await db.AppSettings.AsNoTracking().ToListAsync();

        foreach (var setting in existing)
            _cache[$"{setting.Category}:{setting.Key}"] = setting;

        var inserted = 0;
        foreach (var (key, def) in Defaults)
        {
            var cacheKey = $"{key.Category}:{key.Key}";
            if (_cache.ContainsKey(cacheKey)) continue;

            var entity = new AppSetting { Category = key.Category, Key = key.Key, Value = def.Value, Type = def.Type };

            db.AppSettings.Add(entity);
            _cache[cacheKey] = entity;
            inserted++;
        }

        if (inserted > 0)
            await db.SaveChangesAsync();

        logger.LogInformation("Settings initialized: {Loaded} loaded, {Inserted} defaults seeded", existing.Count,
            inserted);
    }

    public async Task SaveCategoryAsync(string category, Dictionary<string, string> values)
    {
        await _writeLock.WaitAsync();
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync();
            var dbSettings = await db.AppSettings.Where(s => s.Category == category).ToListAsync();
            var lookup = dbSettings.ToDictionary(s => s.Key);

            foreach (var (key, value) in values)
            {
                if (lookup.TryGetValue(key, out var existing))
                {
                    existing.Value = value;
                }
                else
                {
                    var type = Defaults.TryGetValue((category, key), out var def) ? def.Type : "string";
                    var entity = new AppSetting { Category = category, Key = key, Value = value, Type = type };
                    db.AppSettings.Add(entity);
                }

                _cache[$"{category}:{key}"] = new AppSetting
                {
                    Category = category,
                    Key = key,
                    Value = value,
                    Type = Defaults.TryGetValue((category, key), out var d) ? d.Type : "string"
                };
            }

            await db.SaveChangesAsync();
        }
        finally
        {
            _writeLock.Release();
        }

        OnSettingsChanged?.Invoke();
        changeBus.NotifyChanged();
    }

    #region General Behavior

    public bool IsPassiveMode() => GetBool("General", "PassiveMode", false);

    #endregion

    #region Typed Accessors

    public string GetString(string category, string key, string defaultValue = "")
    {
        return _cache.TryGetValue($"{category}:{key}", out var s) ? s.Value : defaultValue;
    }

    public int GetInt(string category, string key, int defaultValue = 0)
    {
        return _cache.TryGetValue($"{category}:{key}", out var s) && int.TryParse(s.Value, out var v)
            ? v
            : defaultValue;
    }

    public bool GetBool(string category, string key, bool defaultValue = false)
    {
        return _cache.TryGetValue($"{category}:{key}", out var s) && bool.TryParse(s.Value, out var v)
            ? v
            : defaultValue;
    }

    public decimal GetDecimal(string category, string key, decimal defaultValue = 0m)
    {
        return _cache.TryGetValue($"{category}:{key}", out var s) && decimal.TryParse(s.Value, out var v)
            ? v
            : defaultValue;
    }

    #endregion

    #region Server Behavior Resolution

    public bool ShouldMonitorQueue(MediaServer server)
    {
        return server.MonitorQueue ?? GetBool("Servers", "MonitorQueue", true);
    }

    public bool ShouldAutoRemove(MediaServer server)
    {
        return server.AutoRemoveDownloads ?? GetBool("Servers", "AutoRemoveDownloads", true);
    }

    public bool ShouldSearchMissing(MediaServer server)
    {
        return server.SearchMissing ?? GetBool("Servers", "SearchMissing", true);
    }

    public int GetRequestTimeout(MediaServer server)
    {
        return server.RequestTimeoutSeconds ?? GetInt("Servers", "RequestTimeoutSeconds", 30);
    }

    #endregion

    #region Database Reset

    public async Task ResetDatabaseAsync()
    {
        await _writeLock.WaitAsync();
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync();

            // PRAGMA foreign_keys is per-connection, and the connection string enables
            // "Foreign Keys=True" — Microsoft.Data.Sqlite re-applies that on every connection
            // open, including the implicit open/close ExecuteSqlRawAsync does around each
            // statement by default. Without holding the connection open ourselves, the OFF
            // pragma below only lasts for that one statement, so later deletes run with FK
            // enforcement back on and can fail (or be skipped) depending on table order.
            await db.Database.OpenConnectionAsync();
            try
            {
                // Disable FK checks so we can delete in any order
                await db.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = OFF");

                // Table names come from sqlite_master, not from users, and SQL can't take a table name as a
                // parameter.
#pragma warning disable EF1002
                foreach (var table in await GetAllTableNamesAsync(db))
                    await db.Database.ExecuteSqlRawAsync($"DELETE FROM \"{table}\"");
#pragma warning restore EF1002

                await db.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = ON");

                // Re-seed default settings
                _cache.Clear();
                foreach (var (key, def) in Defaults)
                {
                    var entity = new AppSetting
                    {
                        Category = key.Category, Key = key.Key, Value = def.Value, Type = def.Type
                    };
                    db.AppSettings.Add(entity);
                    _cache[$"{key.Category}:{key.Key}"] = entity;
                }

                await db.SaveChangesAsync();

                try
                {
                    await db.Database.ExecuteSqlRawAsync("VACUUM");
                }
                catch (Exception ex)
                {
                    // VACUUM may fail inside a transaction in some SQLite modes; the reset itself worked.
                    logger.LogDebug(ex, "VACUUM after database reset failed");
                }
            }
            finally
            {
                await db.Database.CloseConnectionAsync();
            }
        }
        finally
        {
            _writeLock.Release();
        }

        OnSettingsChanged?.Invoke();
        changeBus.NotifyChanged();
        logger.LogWarning("Database reset: all data has been wiped and defaults re-seeded.");
    }

    private static async Task<List<string>> GetAllTableNamesAsync(AppDbContext db)
    {
        var tables = new List<string>();
        var conn = db.Database.GetDbConnection();
        var wasOpen = conn.State == ConnectionState.Open;
        if (!wasOpen) await conn.OpenAsync();
        try
        {
            using var cmd = conn.CreateCommand();
            // Users stays: clearing the login would leave the setup page open to anyone who can reach Trackarr.
            cmd.CommandText =
                "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' AND name NOT LIKE '__EF%' " +
                "AND name <> 'Users'";
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                tables.Add(reader.GetString(0));
        }
        finally
        {
            if (!wasOpen) await conn.CloseAsync();
        }

        return tables;
    }

    public async Task ResetSettingsAsync()
    {
        await _writeLock.WaitAsync();
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync();

            await db.Database.ExecuteSqlRawAsync("DELETE FROM AppSettings");

            _cache.Clear();

            foreach (var (key, def) in Defaults)
            {
                var entity = new AppSetting
                {
                    Category = key.Category, Key = key.Key, Value = def.Value, Type = def.Type
                };
                db.AppSettings.Add(entity);
                _cache[$"{key.Category}:{key.Key}"] = entity;
            }

            await db.SaveChangesAsync();
        }
        finally
        {
            _writeLock.Release();
        }

        OnSettingsChanged?.Invoke();
        changeBus.NotifyChanged();
        logger.LogWarning("Settings reset: all settings restored to defaults.");
    }

    #endregion

    #region Policy Materialization

    public HealthPolicy GetHealthPolicy()
    {
        return new HealthPolicy
        {
            StartingScore = GetInt("HealthPolicy", "StartingScore", 70),
            MaximumScore = GetInt("HealthPolicy", "MaximumScore", 100),
            MinimumVisibleScore = GetInt("HealthPolicy", "MinimumVisibleScore"),
            RemovalConfidenceThreshold = GetInt("HealthPolicy", "RemovalConfidenceThreshold", 70),
            StartupGracePeriodMinutes = GetInt("HealthPolicy", "StartupGracePeriodMinutes", 10),
            MinimumBadScansBeforeRemoval = GetInt("HealthPolicy", "MinimumBadScansBeforeRemoval", 8),
            RollingWindowSize = GetInt("HealthPolicy", "RollingWindowSize", 10),
            RequiredBadScansInWindow = GetInt("HealthPolicy", "RequiredBadScansInWindow", 8),
            RecentRecoveryProtectionScans = GetInt("HealthPolicy", "RecentRecoveryProtectionScans", 3),
            RecoveryCreditMax = GetInt("HealthPolicy", "RecoveryCreditMax", 25),
            PenaltyStreakEscalation = GetDecimal("HealthPolicy", "PenaltyStreakEscalation", 1.5m),
            PenaltyStreakCap = GetDecimal("HealthPolicy", "PenaltyStreakCap", 8.5m),
            RewardStreakEscalation = GetDecimal("HealthPolicy", "RewardStreakEscalation", 1.5m),
            RewardStreakCap = GetDecimal("HealthPolicy", "RewardStreakCap", 8.5m),
            HealthyThreshold = GetInt("HealthPolicy", "HealthyThreshold", 85),
            StableThreshold = GetInt("HealthPolicy", "StableThreshold", 65),
            WatchingThreshold = GetInt("HealthPolicy", "WatchingThreshold", 40),
            DegradingThreshold = GetInt("HealthPolicy", "DegradingThreshold", 15),
            ProgressIncreaseReward = GetInt("HealthPolicy", "ProgressIncreaseReward", 3),
            SizeLeftDecreaseReward = GetInt("HealthPolicy", "SizeLeftDecreaseReward", 3),
            SpeedPositiveReward = GetInt("HealthPolicy", "SpeedPositiveReward", 2),
            EtaImprovedReward = GetInt("HealthPolicy", "EtaImprovedReward", 1),
            ErrorClearedReward = GetInt("HealthPolicy", "ErrorClearedReward", 8),
            RecoveryReward = GetInt("HealthPolicy", "RecoveryReward", 10),
            SustainedProgressReward = GetInt("HealthPolicy", "SustainedProgressReward", 5),
            NoProgressPenalty = GetInt("HealthPolicy", "NoProgressPenalty", -2),
            WarningPenalty = GetInt("HealthPolicy", "WarningPenalty", -8),
            ErrorPenalty = GetInt("HealthPolicy", "ErrorPenalty", -15),
            StalledPenalty = GetInt("HealthPolicy", "StalledPenalty", -18),
            StalledNoConnectionsPenalty = GetInt("HealthPolicy", "StalledNoConnectionsPenalty", -20),
            RepeatedErrorPenalty = GetInt("HealthPolicy", "RepeatedErrorPenalty", -10),
            PreviousMediaFailurePenalty = GetInt("HealthPolicy", "PreviousMediaFailurePenalty", -15),
            QualityDowngradedStillFailingPenalty =
                GetInt("HealthPolicy", "QualityDowngradedStillFailingPenalty", -25),
            ByteProgressProtectionMinutes = GetInt("HealthPolicy", "ByteProgressProtectionMinutes", 10),
            FetchingMetadataPenalty = GetDecimal("HealthPolicy", "FetchingMetadataPenalty", -20m),
            GoodObservationBadScanDecay = GetInt("HealthPolicy", "GoodObservationBadScanDecay", 2)
        };
    }

    public MissingPolicy GetMissingPolicy()
    {
        return new MissingPolicy
        {
            SearchCooldown = TimeSpan.FromHours(Math.Max(1, GetInt("MissingPolicy", "SearchCooldownHours", 6))),
            MaxFailedSearches = GetInt("MissingPolicy", "MaxFailedSearches", 3),
            MaxEpisodeCascade = GetInt("MissingPolicy", "MaxEpisodeCascade", 3),
            AbandonAfter = GetInt("MissingPolicy", "AbandonAfter", 5),
            MaxSearchesPerRun = Math.Max(1, GetInt("MissingPolicy", "MaxSearchesPerRun", 10)),
            RetryAbandonedAfter = TimeSpan.FromDays(Math.Max(0, GetInt("MissingPolicy", "RetryAbandonedAfterDays", 30)))
        };
    }

    #endregion
}
