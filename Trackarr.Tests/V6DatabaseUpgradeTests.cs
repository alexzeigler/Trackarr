using System.Globalization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Trackarr.Data;

namespace Trackarr.Tests;

/// <summary>
///     The v6 database snapshot test from the roadmap, without committing anyone's real database: build the schema
///     exactly as v6.0.0 shipped it from the migrations, put a row in every table, upgrade to the current schema, and
///     check nothing was lost. Every migration added from v7 on runs against v6 data here.
/// </summary>
public sealed class V6DatabaseUpgradeTests : IDisposable
{
    /// <summary>The last migration in the v6.0.0-alpha release.</summary>
    private const string V6Migration = "20261105081350_AddServerUrlBaseAndTlsOption";

    /// <summary>
    ///     Tables a later migration empties on purpose. Add one here, with the reason, only when a migration is meant
    ///     to drop its data.
    /// </summary>
    private static readonly HashSet<string> ClearedOnPurpose = [];

    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"trackarr-v6-{Guid.NewGuid():N}.db");
    private readonly string _connectionString;
    private readonly DbContextOptions<AppDbContext> _options;

    public V6DatabaseUpgradeTests()
    {
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _dbPath, Pooling = false, ForeignKeys = true
        }.ToString();
        _options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connectionString).Options;
    }

    public void Dispose()
    {
        foreach (var file in new[] { _dbPath, _dbPath + "-wal", _dbPath + "-shm" })
            if (File.Exists(file))
                File.Delete(file);
    }

    [Fact]
    public async Task V6Database_UpgradesToTheCurrentSchemaWithoutLosingRows()
    {
        await using (var db = new AppDbContext(_options))
            await db.GetService<IMigrator>().MigrateAsync(V6Migration);

        var seeded = await SeedEveryTableAsync();
        Assert.Contains("MediaServers", seeded);

        await using (var db = new AppDbContext(_options))
        {
            await db.Database.MigrateAsync();
            Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        }

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var tables = await TableNamesAsync(connection);
        foreach (var table in seeded)
        {
            Assert.Contains(table, tables);
            var expected = ClearedOnPurpose.Contains(table) ? 0L : 1L;
            Assert.True(expected == await ScalarAsync<long>(connection, $"SELECT COUNT(*) FROM \"{table}\""),
                $"{table} should have {expected} row(s) after the upgrade");
        }

        Assert.Equal("v6 Radarr", await ScalarAsync<string>(connection, "SELECT Name FROM MediaServers"));
        Assert.Equal("ok", await ScalarAsync<string>(connection, "PRAGMA integrity_check"));
        Assert.Null(await ScalarOrNullAsync(connection, "PRAGMA foreign_key_check"));
    }

    [Fact]
    public async Task V6Database_ApiKeysAreEncryptedAfterTheUpgrade()
    {
        // v6 stored API keys in plain text. Startup runs SecretUpgrade right after the migrations.
        await using (var db = new AppDbContext(_options))
            await db.GetService<IMigrator>().MigrateAsync(V6Migration);
        await SeedEveryTableAsync(); // ApiKey is "x"

        var secrets = new SecretProtector(new EphemeralDataProtectionProvider());
        await using (var db = new AppDbContext(_options, secrets))
        {
            await db.Database.MigrateAsync();
            Assert.Equal((1, 0), await SecretUpgrade.ProtectExistingAsync(db, secrets));
            Assert.Equal("x", (await db.MediaServers.SingleAsync()).ApiKey);
        }

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        Assert.StartsWith(SecretProtector.Prefix, await ScalarAsync<string>(connection, "SELECT ApiKey FROM MediaServers"));
    }

    [Fact]
    public async Task V6Migration_IsPartOfTheMigrationHistory()
    {
        // If this fails, V6Migration was renamed or removed and the upgrade test no longer starts from v6.
        await using var db = new AppDbContext(_options);
        Assert.Contains(V6Migration, db.Database.GetMigrations());
    }

    /// <summary>
    ///     Inserts one row into every table. Every integer column, including every key and foreign key, is 1, so all
    ///     references point at the row with Id 1 in the referenced table. Returns the tables it filled.
    /// </summary>
    private async Task<List<string>> SeedEveryTableAsync()
    {
        var seedConnection = new SqliteConnectionStringBuilder(_connectionString) { ForeignKeys = false }.ToString();
        await using var connection = new SqliteConnection(seedConnection);
        await connection.OpenAsync();

        var tables = await TableNamesAsync(connection);
        foreach (var table in tables)
        {
            var columns = new List<(string Name, string Type)>();
            await using (var info = connection.CreateCommand())
            {
                info.CommandText = $"PRAGMA table_info(\"{table}\")";
                await using var reader = await info.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                    columns.Add((reader.GetString(1), reader.GetString(2)));
            }

            await using var insert = connection.CreateCommand();
            insert.CommandText =
                $"INSERT INTO \"{table}\" ({string.Join(", ", columns.Select(c => $"\"{c.Name}\""))}) " +
                $"VALUES ({string.Join(", ", columns.Select((_, i) => $"$p{i}"))})";
            for (var i = 0; i < columns.Count; i++)
                insert.Parameters.AddWithValue($"$p{i}", SeedValue(table, columns[i].Name, columns[i].Type));
            await insert.ExecuteNonQueryAsync();
        }

        // The seed itself must be consistent, or a failure later would be the seed's fault.
        Assert.Null(await ScalarOrNullAsync(connection, "PRAGMA foreign_key_check"));
        return tables;
    }

    private static object SeedValue(string table, string column, string type)
    {
        switch (table, column)
        {
            case ("MediaServers", "Name"): return "v6 Radarr";
            case ("MediaServers", "ServiceType"): return "Radarr";
        }

        type = type.ToUpperInvariant();
        if (type.Contains("INT", StringComparison.Ordinal)) return 1;
        if (type.Contains("REAL", StringComparison.Ordinal) || type.Contains("FLOA", StringComparison.Ordinal) ||
            type.Contains("DOUB", StringComparison.Ordinal)) return 1.0;
        if (type.Contains("BLOB", StringComparison.Ordinal)) return new byte[] { 0 };

        // Dates are TEXT in SQLite. A date string is also a valid value for any other text column.
        return column.EndsWith("At", StringComparison.Ordinal)
            ? new DateTime(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc).ToString("yyyy-MM-dd HH:mm:ss",
                CultureInfo.InvariantCulture)
            : "x";
    }

    private static async Task<List<string>> TableNamesAsync(SqliteConnection connection)
    {
        var names = new List<string>();
        await using var command = connection.CreateCommand();
        // EF's own tables stay as they are. A row in __EFMigrationsLock means "a migration is running", and the
        // upgrade would wait for it forever.
        command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%' " +
                              "AND name NOT IN ('__EFMigrationsHistory', '__EFMigrationsLock') ORDER BY name";
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) names.Add(reader.GetString(0));
        return names;
    }

    private static async Task<T> ScalarAsync<T>(SqliteConnection connection, string sql)
    {
        return (T)(await ScalarOrNullAsync(connection, sql))!;
    }

    private static async Task<object?> ScalarOrNullAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return await command.ExecuteScalarAsync();
    }
}
