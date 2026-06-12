using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using Trackarr.Core.Server;

namespace Trackarr.Core.Media.Movies;

public class Movie {
    [JsonIgnore] public int Id { get; set; }
    [JsonPropertyName("id")] public int RemoteId { get; set; }

    public string? Title { get; set; }
    public string? OriginalTitle { get; set; }
    public string? SortTitle { get; set; }
    public string? Status { get; set; }
    public string? Overview { get; set; }
    public string? Website { get; set; }
    public int Year { get; set; }
    public string? YouTubeTrailerId { get; set; }
    public string? Studio { get; set; }
    public string? Path { get; set; }
    public int QualityProfileId { get; set; }
    public bool HasFile { get; set; }
    public int MovieFileId { get; set; }
    public bool Monitored { get; set; }
    public string? MinimumAvailability { get; set; }
    public bool IsAvailable { get; set; }
    public string? FolderName { get; set; }
    public int Runtime { get; set; }
    public string? CleanTitle { get; set; }
    public string? ImdbId { get; set; }
    public int TmdbId { get; set; }
    public string? TitleSlug { get; set; }
    public string? Certification { get; set; }
    public long SizeOnDisk { get; set; }
    public DateTime? InCinemas { get; set; }
    public DateTime? PhysicalRelease { get; set; }
    public DateTime? DigitalRelease { get; set; }
    public DateTime? ReleaseDate { get; set; }
    public DateTime? Added { get; set; }
    public DateTime? LastSearchTime { get; set; }
    public double Popularity { get; set; }

    public string? OriginalLanguageJson { get; set; }
    public string? AlternateTitlesJson { get; set; }
    public string? ImagesJson { get; set; }
    public string? GenresJson { get; set; }
    public string? KeywordsJson { get; set; }
    public string? TagsJson { get; set; }
    public string? RatingsJson { get; set; }
    public string? CollectionJson { get; set; }
    public string? StatisticsJson { get; set; }

    public int ServerId { get; set; }

    [ForeignKey(nameof(ServerId))] public MediaServer? ParentServer { get; set; }
}