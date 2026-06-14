using Trackarr.Core.Queue;

namespace Trackarr.Core.Health;

public interface IHealthService {
    Task<HealthDecision> EvaluateAsync(DownloadItem item, CancellationToken cancellationToken = default);
    Task<DownloadAttempt?> GetAttemptAsync(int attemptId, CancellationToken cancellationToken = default);
    Task<HealthSnapshot?> GetSnapshotAsync(int attemptId, CancellationToken cancellationToken = default);
    Task<List<DownloadHealthEvent>> GetEventsAsync(int attemptId, int take = 100, CancellationToken cancellationToken = default);
    Task<List<DownloadAttempt>> GetHistoryAsync(int take = 100, CancellationToken cancellationToken = default);
    Task<List<DownloadAttempt>> GetActiveAttemptsAsync(CancellationToken cancellationToken = default);
    Task MarkImportedAsync(int attemptId, string? message = null, CancellationToken cancellationToken = default);
    Task MarkRemovedByTrackarrAsync(int attemptId, string? message = null, CancellationToken cancellationToken = default);
    Task MarkFailedAsync(int attemptId, string reasonKey, string message, CancellationToken cancellationToken = default);
    Task<List<DownloadAttempt>> ResolveMissingAttemptsAsync(int serverId, IEnumerable<string> activeDownloadIds, CancellationToken cancellationToken = default);
}