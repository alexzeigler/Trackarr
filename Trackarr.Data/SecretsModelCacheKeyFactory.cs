using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Trackarr.Data;

/// <summary>
///     Keeps one EF Core model per <see cref="SecretProtector" />, because the model holds the protector's converter.
///     The app has one protector, so one model. Tests that use several get one each.
/// </summary>
internal sealed class SecretsModelCacheKeyFactory : IModelCacheKeyFactory
{
    public object Create(DbContext context, bool designTime)
    {
        return (context.GetType(), (context as AppDbContext)?.Secrets, designTime);
    }
}
