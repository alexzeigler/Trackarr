using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Logging.Abstractions;
using Trackarr.Core.Media.Movies;
using Trackarr.Core.Queue;
using Trackarr.Core.Server;
using Trackarr.Data;
using Trackarr.Services.Media;
using Trackarr.Services.Settings;
using Trackarr.Services.UI;

namespace Trackarr.Tests;

/// <summary>
///     Runs the real migrations against an on-disk SQLite file: migrate to just before ResetHealthHistory,
///     seed health data, then apply it and check only the health tables were cleared.
/// </summary>
public class ResetHealthHistoryMigrationTests : IDisposable
{
    private const string PreviousMigration = "20260630100828_AddServerHealthMonitoring";

    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"trackarr-reset-{Guid.NewGuid():N}.db");
    private readonly DbContextOptions<AppDbContext> _options;

    public ResetHealthHistoryMigrationTests()
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _dbPath, Pooling = false, ForeignKeys = true
        }.ToString();
        _options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connectionString).Options;
    }

    [Fact]
    public async Task ResetHealthHistory_ClearsHealthTablesAndKeepsEverythingElse()
    {
        // Seed on the latest schema (so the current model matches the tables), then roll back to just before
        // ResetHealthHistory. The later migrations' Down steps keep the rows, and migrating up again runs the reset.
        await using (var db = new AppDbContext(_options))
            await db.Database.MigrateAsync();

        int serverId;
        await using (var db = new AppDbContext(_options))
        {
            var server = new MediaServer
            {
                Name = "Radarr", ServiceType = ServiceType.Radarr, Host = "localhost", Port = 7878
            };
            db.MediaServers.Add(server);
            await db.SaveChangesAsync();
            serverId = server.Id;

            db.Movies.Add(new Movie { RemoteId = 900, Title = "Some Movie", ServerId = serverId });
            await db.SaveChangesAsync();
        }

        var factory = new OptionsFactory(_options);
        var service = new HealthService(factory,
            new SettingsService(factory, NullLogger<SettingsService>.Instance, new DataChangeBus()),
            new DataChangeBus());

        var item = new DownloadItem
        {
            ServerId = serverId, RemoteId = 55, MovieId = 900, DownloadId = "dl-1", Title = "Some Movie",
            Size = 2000, SizeLeft = 1000, Protocol = "torrent"
        };
        await service.EvaluateAsync(item);
        item.SizeLeft = 500;
        await service.EvaluateAsync(item);

        await using (var db = new AppDbContext(_options))
        {
            Assert.True(await db.DownloadAttempts.AnyAsync());
            Assert.True(await db.HealthSnapshots.AnyAsync());
            Assert.True(await db.HealthEvents.AnyAsync());
        }

        await using (var db = new AppDbContext(_options))
            await db.GetService<IMigrator>().MigrateAsync(PreviousMigration);

        await using (var db = new AppDbContext(_options))
        {
            // Raw counts: the model has columns the rolled-back schema doesn't.
            var attempts = await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM DownloadAttempts")
                .SingleAsync();
            Assert.True(attempts > 0);
        }

        await using (var db = new AppDbContext(_options))
            await db.Database.MigrateAsync();

        await using (var db = new AppDbContext(_options))
        {
            Assert.Empty(await db.Database.GetPendingMigrationsAsync());

            Assert.Equal(0, await db.HealthEvents.CountAsync());
            Assert.Equal(0, await db.HealthSnapshots.CountAsync());
            Assert.Equal(0, await db.DownloadAttempts.CountAsync());
            Assert.Equal(0, await db.MediaHealthSummaries.CountAsync());
            Assert.Equal(0, await db.SourceHealthSummaries.CountAsync());

            Assert.Equal(1, await db.MediaServers.CountAsync());
            Assert.Equal(1, await db.Movies.CountAsync());
        }
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try
        {
            File.Delete(_dbPath);
        }
        catch
        {
            // best effort
        }
    }

    private sealed class OptionsFactory(DbContextOptions<AppDbContext> options) : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext()
        {
            return new AppDbContext(options);
        }
    }
}
