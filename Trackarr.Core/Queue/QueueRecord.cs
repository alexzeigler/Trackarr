using System.Text.Json;

namespace Trackarr.Core.Queue;

public class QueueRecord
{
    public int Id { get; set; }
    public int MovieId { get; set; }
    public int SeriesId { get; set; }
    public int EpisodeId { get; set; }
    public long Size { get; set; }
    public long SizeLeft { get; set; }

    public string? TimeLeft { get; set; }
    public string? Title { get; set; }
    public DateTime? Added { get; set; }
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
    public int CustomFormatScore { get; set; }

    public JsonElement Languages { get; set; }
    public JsonElement Quality { get; set; }
    public JsonElement CustomFormats { get; set; }
    public JsonElement StatusMessages { get; set; }
}
