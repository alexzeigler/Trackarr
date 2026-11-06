using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Trackarr.Data;

namespace Trackarr.Tests;

public sealed class DatabaseBackupTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"trackarr-backup-{Guid.NewGuid():N}");
    private readonly string _dbPath;
    private readonly string _backupDir;
    private readonly DbContextOptions<AppDbContext> _options;

    public DatabaseBackupTests()
    {
        Directory.CreateDirectory(_root);
        _dbPath = Path.Combine(_root, "trackarr.db");
        _backupDir = Path.Combine(_root, "backups");

        var connectionString = new SqliteConnectionStringBuilder { DataSource = _dbPath, Pooling = false }.ToString();
        _options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connectionString).Options;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public async Task NewDatabase_IsNotBackedUp()
    {
        await using var db = new AppDbContext(_options);

        var backup = await DatabaseBackup.BackupBeforeMigrateAsync(db, _backupDir, "7.0.0");

        Assert.Null(backup);
        Assert.False(Directory.Exists(_backupDir));
    }

    [Fact]
    public async Task UpToDateDatabase_IsNotBackedUp()
    {
        await using (var db = new AppDbContext(_options))
            await db.Database.MigrateAsync();

        await using (var db = new AppDbContext(_options))
            Assert.Null(await DatabaseBackup.BackupBeforeMigrateAsync(db, _backupDir, "7.0.0"));
    }

    [Fact]
    public async Task PendingMigrations_WritesACopyOfTheDatabaseAsItWas()
    {
        string previous;
        await using (var db = new AppDbContext(_options))
        {
            previous = db.Database.GetMigrations().SkipLast(1).Last();
            await db.GetService<IMigrator>().MigrateAsync(previous);
            await db.Database.ExecuteSqlRawAsync("CREATE TABLE BackupProbe (Value TEXT); " +
                                                 "INSERT INTO BackupProbe VALUES ('kept');");
        }

        string? backup;
        await using (var db = new AppDbContext(_options))
            backup = await DatabaseBackup.BackupBeforeMigrateAsync(db, _backupDir, "7.0.0/test");

        Assert.NotNull(backup);
        Assert.True(File.Exists(backup));
        // "/" can't go in a file name, so the backup still lands in the backup folder.
        Assert.Equal(Path.GetFullPath(_backupDir), Path.GetDirectoryName(Path.GetFullPath(backup)));
        Assert.StartsWith(DatabaseBackup.FilePrefix + "7.0.0-test_", Path.GetFileName(backup), StringComparison.Ordinal);

        await using var copy = new SqliteConnection(
            new SqliteConnectionStringBuilder { DataSource = backup, Pooling = false, Mode = SqliteOpenMode.ReadOnly }
                .ToString());
        await copy.OpenAsync();
        Assert.Equal("kept", await ScalarAsync<string>(copy, "SELECT Value FROM BackupProbe"));
        // The copy is from before the upgrade: its last applied migration is the one we stopped at.
        Assert.Equal(previous,
            await ScalarAsync<string>(copy,
                "SELECT MigrationId FROM __EFMigrationsHistory ORDER BY MigrationId DESC LIMIT 1"));
    }

    [Fact]
    public void Prune_KeepsTheNewestBackupsAndLeavesOtherFilesAlone()
    {
        Directory.CreateDirectory(_backupDir);
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        for (var i = 0; i < 7; i++)
        {
            var file = Path.Combine(_backupDir, $"{DatabaseBackup.FilePrefix}v{i}.db");
            File.WriteAllText(file, "x");
            File.SetLastWriteTimeUtc(file, start.AddDays(i));
        }

        var other = Path.Combine(_backupDir, "notes.txt");
        File.WriteAllText(other, "keep me");

        DatabaseBackup.Prune(_backupDir, 5);

        var left = Directory.GetFiles(_backupDir, $"{DatabaseBackup.FilePrefix}*.db").Select(Path.GetFileName)
            .Order(StringComparer.Ordinal).ToList();
        Assert.Equal(new[]
        {
            "trackarr_pre_v2.db", "trackarr_pre_v3.db", "trackarr_pre_v4.db", "trackarr_pre_v5.db",
            "trackarr_pre_v6.db"
        }, left);
        Assert.True(File.Exists(other));
    }

    private static async Task<T> ScalarAsync<T>(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (T)(await command.ExecuteScalarAsync())!;
    }
}
