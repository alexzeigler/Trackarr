using Microsoft.EntityFrameworkCore;

namespace Trackarr.Data;

/// <summary>
///     The app's <see cref="IDbContextFactory{TContext}" />: every context it makes encrypts secrets with the app's
///     <see cref="SecretProtector" />. EF Core's built-in factory can only pass the options to the constructor.
/// </summary>
public sealed class ProtectedDbContextFactory(DbContextOptions<AppDbContext> options, SecretProtector secrets)
    : IDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext()
    {
        return new AppDbContext(options, secrets);
    }
}
