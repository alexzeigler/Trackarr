namespace Trackarr.Integrations.Media.Arr.Dtos;

public record HistoryResponse(List<HistoryRecord> Records);

public record HistoryRecord(int Id, string? DownloadId, string EventType);
