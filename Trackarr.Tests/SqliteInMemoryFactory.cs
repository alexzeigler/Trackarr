using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Trackarr.Data;

namespace Trackarr.Tests;

/// <summary>
/// An <see cref="IDbContextFactory{TContext}" /> backed by a single open in-memory SQLite connection,
/// so every context the services create shares the same throwaway database for the test's lifetime.
/// </summary>
internal sealed class SqliteInMemoryFactory : IDbContextFactory<AppDbContext>, IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;

    public SqliteInMemoryFactory()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;

        using var db = new AppDbContext(_options);
        db.Database.EnsureCreated();
    }

    public AppDbContext CreateDbContext() => new(_options);

    public Task<AppDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(CreateDbContext());

    public void Dispose() => _connection.Dispose();
}
