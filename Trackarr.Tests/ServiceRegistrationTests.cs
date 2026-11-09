using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trackarr.Core.Routine;
using Trackarr.Data;
using Trackarr.Integrations.Media.Arr;
using Trackarr.Routines;
using Trackarr.Services;

namespace Trackarr.Tests;

/// <summary>
///     Builds the services and routines the way Program.cs does, so one whose dependency isn't registered fails here
///     instead of when the app starts.
/// </summary>
public class ServiceRegistrationTests
{
    [Fact]
    public void EveryServiceAndRoutine_CanBeCreated()
    {
        using var db = new SqliteInMemoryFactory();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IDbContextFactory<AppDbContext>>(db);
        services.AddHttpClient();
        services.AddArrHttpClients((_, _) => new HttpClientHandler());
        services.AddTrackarrServices();
        services.AddTrackarrRoutines();

        // ValidateOnBuild checks that every registration's constructor can be satisfied.
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true, ValidateScopes = true
        });

        Assert.Equal(6, provider.GetServices<IRoutine>().Count());
    }
}
