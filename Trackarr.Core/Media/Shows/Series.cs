namespace Trackarr.Core.Media.Shows;

public class Series : MediaItem
{
    public bool Ended { get; set; }
    public string? Network { get; set; }
    public string? AirTime { get; set; }
    public int? LanguageProfileId { get; set; }
    public bool SeasonFolder { get; set; }
    public string? MonitorNewItems { get; set; }
    public bool UseSceneNumbering { get; set; }
    public int TvdbId { get; set; }
    public int TvRageId { get; set; }
    public int TvMazeId { get; set; }
    public string? RootFolderPath { get; set; }
    public string? SeriesType { get; set; }
    public DateTime? FirstAired { get; set; }
    public DateTime? LastAired { get; set; }
    public DateTime? NextAiring { get; set; }
    public DateTime? PreviousAiring { get; set; }

    public string? AlternateTitlesJson { get; set; }
    public string? SeasonsJson { get; set; }
}