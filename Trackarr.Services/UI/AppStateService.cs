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
        new("/system/tasks", "Tasks", [new BreadcrumbSegment("System", "/system/tasks")]),
        new("/system/status", "Status", [new BreadcrumbSegment("System", "/system/status")]),
        new("/settings", "Settings", []),
        new("/settings/general", "General", [new BreadcrumbSegment("Settings", "/settings/general")]),
        new("/settings/display", "Display", [new BreadcrumbSegment("Settings", "/settings/display")]),
        new("/settings/health", "Health Policy", [new BreadcrumbSegment("Settings", "/settings/health")]),
        new("/settings/missing", "Missing Policy", [new BreadcrumbSegment("Settings", "/settings/missing")]),
        new("/settings/blacklist", "Blacklist", [new BreadcrumbSegment("Settings", "/settings/blacklist")]),
        new("/settings/services", "Servers", [new BreadcrumbSegment("Settings", "/settings/services")]),
        new("/settings/maintenance", "Maintenance", [new BreadcrumbSegment("Settings", "/settings/maintenance")])
    ];

    private readonly DataChangeBus _bus;
    private readonly SettingsService _settings;
    private readonly CancellationTokenSource _timerCts = new();
    private Task? _timerTask;

    public AppStateService(SettingsService settings, DataChangeBus bus)
    {
        _settings = settings;
        _bus = bus;
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

    private void OnBusChanged()
    {
        _ = FireRefreshAsync();
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
            catch
            {
            }
        }
    }

    private record RouteEntry(string Path, string Title, List<BreadcrumbSegment> ParentCrumbs);
}
