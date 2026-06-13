namespace Trackarr.Core.Media.Movies;

public class Movie : MediaItem {
    public string? OriginalTitle { get; set; }
    public string? Website { get; set; }
    public string? YouTubeTrailerId { get; set; }
    public string? Studio { get; set; }
    public bool HasFile { get; set; }
    public int MovieFileId { get; set; }
    public string? MinimumAvailability { get; set; }
    public bool IsAvailable { get; set; }
    public string? FolderName { get; set; }
    public long SizeOnDisk { get; set; }
    public DateTime? InCinemas { get; set; }
    public DateTime? PhysicalRelease { get; set; }
    public DateTime? DigitalRelease { get; set; }
    public DateTime? ReleaseDate { get; set; }
    public DateTime? LastSearchTime { get; set; }
    public double Popularity { get; set; }

    public string? AlternateTitlesJson { get; set; }
    public string? KeywordsJson { get; set; }
    public string? CollectionJson { get; set; }
}