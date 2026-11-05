using Trackarr.Core.Routine;
using Trackarr.Services.Routines;

namespace Trackarr.Tests;

/// <summary>
///     Minimal stub implementing <see cref="IRoutine" /> for registration tests.
/// </summary>
internal sealed class StubRoutine : IRoutine
{
    public int Id { get; set; }
    public required string Key { get; init; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool Enabled { get; set; }
    public int LastRunTime { get; set; }
    public int IntervalSeconds { get; set; }
    public DateTime LastRun { get; set; }
    public DateTimeOffset NextRun { get; set; }
    public bool IsRunning { get; set; }
    public bool IsPaused { get; set; }
    public IEnumerable<RoutineRun>? Runs { get; set; }

    public Task<RoutineResult> ExecuteAsync(CancellationToken cancellationToken) =>
        Task.FromResult<RoutineResult>("done");
}

public class RoutineServiceTests
{
    private static RoutineService CreateService(SqliteInMemoryFactory factory) => new(factory);

    #region GetAllAsync

    [Fact]
    public async Task GetAllAsync_ReturnsAllRoutines()
    {
        using var factory = new SqliteInMemoryFactory();
        var service = CreateService(factory);

        await service.RegisterRoutineInDbAsync(new StubRoutine
        {
            Key = "a",
            Name = "A",
            Description = "A desc",
            IntervalSeconds = 10,
            NextRun = DateTimeOffset.UtcNow
        });
        await service.RegisterRoutineInDbAsync(new StubRoutine
        {
            Key = "b",
            Name = "B",
            Description = "B desc",
            IntervalSeconds = 20,
            NextRun = DateTimeOffset.UtcNow
        });

        var results = await service.GetAllAsync();

        Assert.Equal(2, results.Count);
        Assert.Contains(results, r => r.Key == "a");
        Assert.Contains(results, r => r.Key == "b");
    }

    #endregion

    #region GetByKeyAsync

    [Fact]
    public async Task GetByKeyAsync_ReturnsNullForUnknownKey()
    {
        using var factory = new SqliteInMemoryFactory();
        var service = CreateService(factory);

        var result = await service.GetByKeyAsync("nonexistent");

        Assert.Null(result);
    }

    #endregion

    #region SaveRunAsync

    [Fact]
    public async Task SaveRunAsync_PersistsRun()
    {
        using var factory = new SqliteInMemoryFactory();
        var service = CreateService(factory);

        // Seed a parent definition first.
        await service.RegisterRoutineInDbAsync(new StubRoutine
        {
            Key = "run-test",
            Name = "Run Test",
            Description = "desc",
            IntervalSeconds = 10,
            NextRun = DateTimeOffset.UtcNow
        });

        var definition = await service.GetByKeyAsync("run-test");
        Assert.NotNull(definition);

        var run = new RoutineRun
        {
            RoutineDefinitionId = definition.Id,
            StartTime = DateTime.UtcNow.AddSeconds(-5),
            EndTime = DateTime.UtcNow,
            State = RoutineState.Success
        };

        await service.SaveRunAsync(run);

        await using var db = factory.CreateDbContext();
        var saved = db.RoutineRuns.Single(r => r.RoutineDefinitionId == definition.Id);
        Assert.Equal(RoutineState.Success, saved.State);
        Assert.NotNull(saved.EndTime);
    }

    #endregion

    #region ResetAllRunningAsync

    [Fact]
    public async Task ResetAllRunningAsync_ClearsRunningFlags()
    {
        using var factory = new SqliteInMemoryFactory();
        var service = CreateService(factory);

        // Seed two routines: one running, one not.
        await using (var db = factory.CreateDbContext())
        {
            db.Routines.AddRange(
                new RoutineDefinition { Key = "running", Name = "Running", Description = "d", IsRunning = true },
                new RoutineDefinition { Key = "idle", Name = "Idle", Description = "d", IsRunning = false });
            await db.SaveChangesAsync();
        }

        await service.ResetAllRunningAsync();

        await using var verify = factory.CreateDbContext();
        Assert.False(verify.Routines.Single(r => r.Key == "running").IsRunning);
        Assert.False(verify.Routines.Single(r => r.Key == "idle").IsRunning);
    }

    #endregion

    #region RegisterRoutineInDbAsync

    [Fact]
    public async Task RegisterRoutineInDbAsync_InsertsNewRoutine()
    {
        using var factory = new SqliteInMemoryFactory();
        var service = CreateService(factory);

        var stub = new StubRoutine
        {
            Key = "test-routine",
            Name = "Test Routine",
            Description = "A test routine",
            Enabled = true,
            IntervalSeconds = 60,
            NextRun = DateTimeOffset.UtcNow.AddMinutes(1)
        };

        await service.RegisterRoutineInDbAsync(stub);

        await using var db = factory.CreateDbContext();
        var saved = db.Routines.Single(r => r.Key == "test-routine");
        Assert.Equal("Test Routine", saved.Name);
        Assert.Equal("A test routine", saved.Description);
        Assert.True(saved.Enabled);
        Assert.Equal(60, saved.IntervalSeconds);
    }

    [Fact]
    public async Task RegisterRoutineInDbAsync_UpdatesExistingRoutine()
    {
        using var factory = new SqliteInMemoryFactory();
        var service = CreateService(factory);

        var stub = new StubRoutine
        {
            Key = "dup-key",
            Name = "Original",
            Description = "First version",
            Enabled = false,
            IntervalSeconds = 30,
            NextRun = DateTimeOffset.UtcNow
        };

        await service.RegisterRoutineInDbAsync(stub);

        // Mutate and re-register with the same key.
        stub.Name = "Updated";
        stub.Description = "Second version";
        stub.Enabled = true;
        stub.IntervalSeconds = 120;

        await service.RegisterRoutineInDbAsync(stub);

        await using var db = factory.CreateDbContext();
        var all = db.Routines.Where(r => r.Key == "dup-key").ToList();
        Assert.Single(all);

        var saved = all[0];
        Assert.Equal("Updated", saved.Name);
        Assert.Equal("Second version", saved.Description);

        // Regression guard for H1: Enabled and Interval are the user's once the row exists, so code
        // defaults on re-registration (every startup) must not overwrite them.
        Assert.False(saved.Enabled);
        Assert.Equal(30, saved.IntervalSeconds);
    }

    [Fact]
    public async Task RegisterRoutineInDbAsync_KeepsUserChangesAcrossRestart()
    {
        using var factory = new SqliteInMemoryFactory();
        var service = CreateService(factory);
        var stub = new StubRoutine
        {
            Key = "restart-key", Name = "Routine", Description = "", Enabled = true, IntervalSeconds = 60,
            NextRun = DateTimeOffset.UtcNow
        };
        await service.RegisterRoutineInDbAsync(stub);

        // The user disables it and changes the interval on the Routines page.
        var definition = (await service.GetByKeyAsync("restart-key"))!;
        definition.Enabled = false;
        definition.IntervalSeconds = 3600;
        await service.UpdateDefinitionAsync(definition);

        // Restart: the same code defaults are registered again.
        await service.RegisterRoutineInDbAsync(stub);

        var after = (await service.GetByKeyAsync("restart-key"))!;
        Assert.False(after.Enabled);
        Assert.Equal(3600, after.IntervalSeconds);
    }

    #endregion
}
