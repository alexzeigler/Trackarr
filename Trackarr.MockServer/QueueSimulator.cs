namespace Trackarr.MockServer;

public class QueueSimulator(MockDataStore store, IConfiguration config) : BackgroundService
{
    private readonly int _tickMs = config.GetValue("MockServer:QueueTickMs", 2000);

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(_tickMs));
        while (await timer.WaitForNextTickAsync(ct))
        {
            store.TickQueue();
        }
    }
}
