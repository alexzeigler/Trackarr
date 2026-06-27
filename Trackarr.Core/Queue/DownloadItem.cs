using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace Trackarr.Core.Queue;

public record QualityModel(QualityDetail Quality);

public record QualityDetail(int Id, string Name, string Source, int Resolution);

public record StatusMessage(string Title, List<string> Messages);

public class DownloadItem
{
    #region Identity

    [JsonIgnore] public int Id { get; set; }
    [JsonPropertyName("id")] public int RemoteId { get; set; }

    #endregion

    #region Properties

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
    public int MovieId { get; set; }
    public int SeriesId { get; set; }
    public int EpisodeId { get; set; }
    public int ServerId { get; set; }
    public string? LanguagesJson { get; set; }
    public string? QualityJson { get; set; }
    public string? CustomFormatsJson { get; set; }
    public string? StatusMessagesJson { get; set; }
    public QueueState State { get; set; }

    #endregion

    #region Computed

    public int DownloadPercent => Size > 0 ? (int)((double)(Size - SizeLeft) / Size * 100) : 0;

    [NotMapped] public QualityModel? Quality => JsonHelper.Deserialize<QualityModel>(QualityJson);

    [NotMapped]
    public IReadOnlyList<StatusMessage> StatusMessages =>
        JsonHelper.Deserialize<List<StatusMessage>>(StatusMessagesJson) ?? [];

    [NotMapped] public int SeasonNumber { get; set; }
    [NotMapped] public int EpisodeNumber { get; set; }

    #endregion
}
