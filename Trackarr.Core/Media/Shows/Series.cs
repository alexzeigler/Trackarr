using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using Trackarr.Core.Server;

namespace Trackarr.Core.Media.Shows;

public class Series {
    [JsonIgnore] public int Id { get; set; }
    [JsonPropertyName("id")] public int RemoteId { get; set; }

    public string? Title { get; set; }
    public string? SortTitle { get; set; }
    public string? Status { get; set; }
    public bool Ended { get; set; }
    public string? Overview { get; set; }
    public string? Network { get; set; }
    public string? AirTime { get; set; }
    public string? Path { get; set; }
    public int QualityProfileId { get; set; }
    public int? LanguageProfileId { get; set; }
    public bool SeasonFolder { get; set; }
    public bool Monitored { get; set; }
    public string? MonitorNewItems { get; set; }
    public bool UseSceneNumbering { get; set; }
    public int Runtime { get; set; }
    public int TvdbId { get; set; }
    public int TvRageId { get; set; }
    public int TvMazeId { get; set; }
    public int TmdbId { get; set; }
    public string? ImdbId { get; set; }
    public string? TitleSlug { get; set; }
    public string? RootFolderPath { get; set; }
    public string? Certification { get; set; }
    public string? SeriesType { get; set; }
    public string? CleanTitle { get; set; }
    public int Year { get; set; }
    public DateTime? FirstAired { get; set; }
    public DateTime? LastAired { get; set; }
    public DateTime? NextAiring { get; set; }
    public DateTime? PreviousAiring { get; set; }
    public DateTime? Added { get; set; }

    public string? AlternateTitlesJson { get; set; }
    public string? ImagesJson { get; set; }
    public string? OriginalLanguageJson { get; set; }
    public string? SeasonsJson { get; set; }
    public string? GenresJson { get; set; }
    public string? TagsJson { get; set; }
    public string? RatingsJson { get; set; }
    public string? StatisticsJson { get; set; }

    public int ServerId { get; set; }

    [ForeignKey(nameof(ServerId))] public MediaServer? ParentServer { get; set; }
}