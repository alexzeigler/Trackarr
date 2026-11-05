using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Trackarr.Data;

namespace Trackarr.Tests;

/// <summary>
///     An <see cref="IDbContextFactory{TContext}" /> backed by a real on-disk SQLite file using the same
///     connection-string options Program.cs uses in production (notably "Foreign Keys=True" and pooling).
///     Unlike <see cref="SqliteInMemoryFactory" />, which hands EF Core a single already-open connection,
///     this lets EF Core open/close pooled connections implicitly per command — the exact conditions needed
///     to reproduce bugs where a PRAGMA set on one connection doesn't survive to the next.
/// </summary>
internal sealed class SqliteFileFactory : IDbContextFactory<AppDbContext>, IDisposable
{
    private readonly DbContextOptions<AppDbContext> _options;

    public string DbPath { get; }

    public SqliteFileFactory()
    {
        DbPath = Path.Combine(Path.GetTempPath(), $"trackarr-test-{Guid.NewGuid():N}.db");

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = DbPath, Cache = SqliteCacheMode.Shared, Pooling = true, ForeignKeys = true
        }.ToString();

        _options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connectionString).Options;

        using var db = new AppDbContext(_options);
        db.Database.EnsureCreated();
    }

    public AppDbContext CreateDbContext()
    {
        return new AppDbContext(_options);
    }

    public Task<AppDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(CreateDbContext());
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();

        try
        {
            foreach (var suffix in new[] { "", "-wal", "-shm", ".wipe-pending" })
                File.Delete(DbPath + suffix);
        }
        catch (IOException)
        {
            /* best-effort cleanup */
        }
    }
}
