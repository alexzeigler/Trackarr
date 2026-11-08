using Microsoft.Extensions.Logging;
using Trackarr.Services.Settings;

namespace Trackarr.Services.UI;

public record BreadcrumbSegment(string Label, string? Href);

public sealed class AppStateService : IAsyncDisposable
{
    private static readonly List<RouteEntry> Routes =
    [
        new("/", "Queue", []),
        new("/missing", "Missing", []),
        new("/servers", "Servers", []),
        new("/history", "History", []),
        new("/system/routines", "Routines", [new BreadcrumbSegment("System", "/system/routines")]),
        new("/system/events", "Events", [new BreadcrumbSegment("System", "/system/events")]),
        new("/system/actions", "Actions", [new BreadcrumbSegment("System", "/system/actions")]),
        new("/system/tasks", "Tasks", [new BreadcrumbSegment("System", "/system/tasks")]),
        new("/system/status", "Status", [new BreadcrumbSegment("System", "/system/status")]),
        new("/settings", "Settings", []),
        new("/settings/general", "General", [new BreadcrumbSegment("Settings", "/settings/general")]),
        new("/settings/display", "Display", [new BreadcrumbSegment("Settings", "/settings/display")]),
        new("/settings/health", "Health Policy", [new BreadcrumbSegment("Settings", "/settings/health")]),
        new("/settings/missing", "Missing Policy", [new BreadcrumbSegment("Settings", "/settings/missing")]),
        new("/settings/blacklist", "Blacklist", [new BreadcrumbSegment("Settings", "/settings/blacklist")]),
        new("/settings/services", "Servers", [new BreadcrumbSegment("Settings", "/settings/services")]),
        new("/settings/maintenance", "Maintenance", [new BreadcrumbSegment("Settings", "/settings/maintenance")]),
        new("/settings/network", "Network", [new BreadcrumbSegment("Settings", "/settings/network")]),
        new("/settings/security", "Security", [new BreadcrumbSegment("Settings", "/settings/security")])
    ];

    private readonly DataChangeBus _bus;
    private readonly ILogger<AppStateService>? _logger;
    private readonly SettingsService _settings;
    private readonly CancellationTokenSource _timerCts = new();
    private Task? _timerTask;

    public AppStateService(SettingsService settings, DataChangeBus bus, ILogger<AppStateService>? logger = null)
    {
        _settings = settings;
        _bus = bus;
        _logger = logger;
        _bus.OnChanged += OnBusChanged;
    }

    public string PageTitle { get; private set; } = "";
    public List<BreadcrumbSegment> Breadcrumbs { get; private set; } = [];

    public async ValueTask DisposeAsync()
    {
        _bus.OnChanged -= OnBusChanged;
        await _timerCts.CancelAsync();
        _timerCts.Dispose();
        if (_timerTask != null)
            await _timerTask;
    }

    public event Action? OnStateChanged;
    public event Func<Task>? OnRefreshRequested;

    public void StartAutoRefresh()
    {
        _timerTask ??= RunAutoRefreshAsync(_timerCts.Token);
    }

    public void UpdateRoute(string uri)
    {
        var path = new Uri(uri).AbsolutePath.TrimEnd('/');
        if (string.IsNullOrEmpty(path)) path = "/";

        var match = Routes
            .Where(r => path.Equals(r.Path, StringComparison.OrdinalIgnoreCase) ||
                        (r.Path != "/" && path.StartsWith(r.Path, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(r => r.Path.Length).FirstOrDefault();

        if (match is not null)
        {
            PageTitle = match.Title;
            Breadcrumbs = match.ParentCrumbs.Count > 0
                ? [..match.ParentCrumbs, new BreadcrumbSegment(match.Title, null)]
                : [new BreadcrumbSegment(match.Title, null)];
        }
        else
        {
            PageTitle = "Trackarr";
            Breadcrumbs = [new BreadcrumbSegment("Trackarr", null)];
        }

        OnStateChanged?.Invoke();
    }

    public async Task RequestRefreshAsync()
    {
        await FireRefreshAsync();
    }

    // Routines fire a change per item (a 50-item evaluation is 50 changes). Coalesce a burst into one refresh
    // shortly after it starts instead of 50 full page refreshes per open tab.
    private static readonly TimeSpan BusDebounce = TimeSpan.FromSeconds(1);
    private int _busRefreshPending;

    private void OnBusChanged()
    {
        if (Interlocked.Exchange(ref _busRefreshPending, 1) == 1) return;
        _ = DebouncedRefreshAsync();
    }

    private async Task DebouncedRefreshAsync()
    {
        try
        {
            await Task.Delay(BusDebounce, _timerCts.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (ObjectDisposedException)
        {
            return;
        }

        // Changes that arrive from here on schedule the next refresh.
        Interlocked.Exchange(ref _busRefreshPending, 0);
        await FireRefreshAsync();
    }

    private async Task RunAutoRefreshAsync(CancellationToken ct)
    {
        try
        {
            var interval = _settings.GetInt("Display", "QueueRefreshIntervalSeconds", 10);
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Clamp(interval, 5, 300)));
            while (await timer.WaitForNextTickAsync(ct))
                await FireRefreshAsync();
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task FireRefreshAsync()
    {
        if (OnRefreshRequested is not { } handler) return;

        foreach (var d in handler.GetInvocationList().Cast<Func<Task>>())
        {
            try
            {
                await d();
            }
            catch (Exception ex)
            {
                // One page's failed refresh mustn't stop the others; it'll try again next time.
                _logger?.LogDebug(ex, "Auto-refresh handler failed");
            }
        }
    }

    private record RouteEntry(string Path, string Title, List<BreadcrumbSegment> ParentCrumbs);
}
