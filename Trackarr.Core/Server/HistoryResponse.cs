namespace Trackarr.Core.Server;

public record HistoryResponse(List<HistoryRecord> Records);

public record HistoryRecord(int Id, string? DownloadId, string EventType);
