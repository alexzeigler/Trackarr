using System.Text.Json;

namespace Trackarr.Core.Media.Movies;

public class MovieRecord
{
    public int Id { get; set; }
    public string? Title { get; set; }
    public string? OriginalTitle { get; set; }
    public string? SortTitle { get; set; }
    public string? CleanTitle { get; set; }
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

    public JsonElement? OriginalLanguage { get; set; }
    public JsonElement? AlternateTitles { get; set; }
    public JsonElement? Images { get; set; }
    public JsonElement? Genres { get; set; }
    public JsonElement? Keywords { get; set; }
    public JsonElement? Tags { get; set; }
    public JsonElement? Ratings { get; set; }
    public JsonElement? Collection { get; set; }
    public JsonElement? Statistics { get; set; }
}