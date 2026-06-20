using System.Text.Json;

namespace Trackarr.Core.Media.Shows;

public class SeriesRecord
{
    public int Id { get; set; }
    public string? Title { get; set; }
    public JsonElement? AlternateTitles { get; set; }
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

    public JsonElement? Images { get; set; }
    public JsonElement? OriginalLanguage { get; set; }
    public JsonElement? Seasons { get; set; }
    public JsonElement? Genres { get; set; }
    public JsonElement? Tags { get; set; }
    public JsonElement? Ratings { get; set; }
    public JsonElement? Statistics { get; set; }
}