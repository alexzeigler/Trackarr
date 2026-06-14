using System.Text.Json;

namespace Trackarr.Core.Media.Shows;

public class MissingEpisodeRecord {
    public int Id { get; set; }
    public int SeriesId { get; set; }
    public int TvdbId { get; set; }
    public int EpisodeFileId { get; set; }

    public int SeasonNumber { get; set; }
    public int EpisodeNumber { get; set; }
    public int? AbsoluteEpisodeNumber { get; set; }

    public int? SceneAbsoluteEpisodeNumber { get; set; }
    public int? SceneEpisodeNumber { get; set; }
    public int? SceneSeasonNumber { get; set; }

    public string? Title { get; set; }
    public string? AirDate { get; set; }
    public DateTime AirDateUtc { get; set; }
    public DateTime EndTime { get; set; }
    public DateTime GrabDate { get; set; }

    public int? Runtime { get; set; }
    public string? FinaleType { get; set; }
    public string? Overview { get; set; }

    public bool HasFile { get; set; }
    public bool Monitored { get; set; }
    public bool UnverifiedSceneNumbering { get; set; }

    public DateTime? LastSearchTime { get; set; }

    public JsonElement? Series { get; set; }
    public JsonElement? Images { get; set; }
    public JsonElement? EpisodeFile { get; set; }
    public JsonElement? Ratings { get; set; }
    public JsonElement? Languages { get; set; }
}