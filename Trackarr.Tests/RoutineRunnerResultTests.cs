using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Trackarr.Core.Routine;
using Trackarr.Services.Diagnostics;
using Trackarr.Services.Routines;
using Trackarr.Services.UI;

namespace Trackarr.Tests;

/// <summary>
///     Regression guards for H4: a run that finishes without throwing but reports a partial failure must be
///     recorded as failed, not as a success.
/// </summary>
public class RoutineRunnerResultTests
{
    private sealed class ResultRoutine(RoutineResult result) : Routine
    {
        public override string Key { get; set; } = "result-routine";
        public override string Name { get; set; } = "Result Routine";
        public override Task<RoutineResult> ExecuteAsync(CancellationToken cancellationToken) => Task.FromResult(result);
    }

    [Theory]
    [InlineData(true, RoutineState.Success)]
    [InlineData(false, RoutineState.Failed)]
    public async Task Runner_RecordsTheRoutinesOwnSuccess(bool success, RoutineState expectedState)
    {
        using var factory = new SqliteInMemoryFactory();
        var routineService = new RoutineService(factory);
        var handler = new ResultRoutine(new RoutineResult("1 server(s) failed.", success));
        await routineService.RegisterRoutineInDbAsync(handler);

        var runner = new RoutineRunner([handler], routineService,
            new EventService(factory, NullLogger<EventService>.Instance, new DataChangeBus()),
            NullLogger<RoutineRunner>.Instance);
        runner.Load(await routineService.GetAllAsync());

        runner.RunAsync(handler.Key);
        await runner.TickAsync(CancellationToken.None);

        var definition = runner.GetDefinition(handler.Key)!;
        Assert.Equal(success, definition.LastRunSuccess);

        await using var db = factory.CreateDbContext();
        var run = Assert.Single(await db.RoutineRuns.ToListAsync());
        Assert.Equal(expectedState, run.State);

        Assert.Equal(success, (await db.Routines.SingleAsync(r => r.Key == handler.Key)).LastRunSuccess);
    }

    [Fact]
    public async Task ScheduledRunsOff_ManualRunStillRuns()
    {
        // Regression guard for H2: turning off Auto Start used to stop the tick loop, so Run buttons did nothing.
        var (runner, handler) = await CreateRunnerAsync();

        runner.RunAsync(handler.Key);
        await runner.TickAsync(CancellationToken.None, runScheduled: false);

        Assert.Equal(1, handler.Runs);
    }

    [Fact]
    public async Task ScheduledRunsOff_DueRoutineDoesNotRun()
    {
        var (runner, handler) = await CreateRunnerAsync();

        await runner.TickAsync(CancellationToken.None, runScheduled: false);

        Assert.Equal(0, handler.Runs);
    }

    [Fact]
    public async Task ScheduledRunsOn_DueRoutineRuns()
    {
        var (runner, handler) = await CreateRunnerAsync();

        await runner.TickAsync(CancellationToken.None);

        Assert.Equal(1, handler.Runs);
    }

    private sealed class CountingRoutine : Routine
    {
        public int Runs { get; private set; }
        public override string Key { get; set; } = "counting-routine";
        public override string Name { get; set; } = "Counting Routine";
        public override int IntervalSeconds { get; set; } = 60;
        public override DateTimeOffset NextRun { get; set; } = DateTimeOffset.UtcNow.AddMinutes(-1); // due now

        public override Task<RoutineResult> ExecuteAsync(CancellationToken cancellationToken)
        {
            Runs++;
            return Task.FromResult<RoutineResult>("ok");
        }
    }

    private static async Task<(RoutineRunner, CountingRoutine)> CreateRunnerAsync()
    {
        var factory = new SqliteInMemoryFactory();
        var routineService = new RoutineService(factory);
        var handler = new CountingRoutine();
        await routineService.RegisterRoutineInDbAsync(handler);

        var runner = new RoutineRunner([handler], routineService,
            new EventService(factory, NullLogger<EventService>.Instance, new DataChangeBus()),
            NullLogger<RoutineRunner>.Instance);
        runner.Load(await routineService.GetAllAsync());
        return (runner, handler);
    }

    [Fact]
    public void PlainString_IsASuccessfulResult()
    {
        RoutineResult result = "done";
        Assert.True(result.Success);
        Assert.Equal("done", result.Message);
    }
}
