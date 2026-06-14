using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Trackarr.Core.Queue;

public record QualityModel(QualityDetail Quality);

public record QualityDetail(int Id, string Name, string Source, int Resolution);

public record StatusMessage(string Title, List<string> Messages);

public class DownloadItem {
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    [JsonIgnore] public int Id { get; set; }
    [JsonPropertyName("id")] public int RemoteId { get; set; }

    public string? Title { get; set; }
    public long Size { get; set; }

    public long SizeLeft { get; set; }

    public string? TimeLeft { get; set; }
    public string? Status { get; set; }
    public string? TrackedDownloadStatus { get; set; }
    public string? TrackedDownloadState { get; set; }
    public string? ErrorMessage { get; set; }
    public string? DownloadId { get; set; }
    public string? Protocol { get; set; }
    public string? DownloadClient { get; set; }
    public bool DownloadClientHasPostImportCategory { get; set; }
    public string? Indexer { get; set; }
    public string? OutputPath { get; set; }
    public DateTime? EstimatedCompletionTime { get; set; }
    public DateTime? Added { get; set; }

    public int CustomFormatScore { get; set; }
    public int MovieId { get; set; }
    public int SeriesId { get; set; }
    public int EpisodeId { get; set; }

    public int ServerId { get; set; }

    public string? LanguagesJson { get; set; }
    public string? QualityJson { get; set; }
    public string? CustomFormatsJson { get; set; }
    public string? StatusMessagesJson { get; set; }

    public int DownloadPercent => Size > 0 ? (int)((double)(Size - SizeLeft) / Size * 100) : 0;

    public QueueState State { get; set; }
    public QueueStatus QueueStatus { get; set; }
    public string? AttemptKey { get; set; }

    public DownloadItemTrackingState TrackingState { get; set; } = DownloadItemTrackingState.Active;

    public int ActiveStrikeCount { get; set; }
    public int TotalStrikeCount { get; set; }

    public DateTime? FirstSeenAtUtc { get; set; }
    public DateTime? LastSeenAtUtc { get; set; }
    public DateTime? LastProgressAtUtc { get; set; }
    public DateTime? FailedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }

    public decimal? LastObservedProgress { get; set; }
    public long? LastObservedSizeLeft { get; set; }

    [NotMapped]
    public QualityModel? Quality => Deserialize<QualityModel>(QualityJson);

    [NotMapped]
    public IReadOnlyList<StatusMessage> StatusMessages => Deserialize<List<StatusMessage>>(StatusMessagesJson) ?? [];

    [NotMapped]
    public int SeasonNumber { get; set; }

    [NotMapped]
    public int EpisodeNumber { get; set; }

    private static T? Deserialize<T>(string? json) {
        if (string.IsNullOrEmpty(json)) return default;
        try { return JsonSerializer.Deserialize<T>(json, JsonOptions); }
        catch { return default; }
    }
}