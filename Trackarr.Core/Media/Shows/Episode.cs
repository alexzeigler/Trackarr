using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using Trackarr.Core.Server;

namespace Trackarr.Core.Media.Shows;

public class Episode {
    [JsonIgnore] public int Id { get; set; }

    public int RemoteId { get; set; }
    public int RemoteSeriesId { get; set; }
    public int TvdbId { get; set; }
    public int EpisodeFileId { get; set; }

    public int SeasonNumber { get; set; }
    public int EpisodeNumber { get; set; }
    public int AbsoluteEpisodeNumber { get; set; }

    public string? Title { get; set; }
    public string? AirDate { get; set; }
    public DateTime? AirDateUtc { get; set; }
    public string? Overview { get; set; }

    public bool HasFile { get; set; }
    public bool Monitored { get; set; }
    public bool UnverifiedSceneNumbering { get; set; }

    public DateTime? LastSearchTime { get; set; }

    public string? ImagesJson { get; set; }
    public string? EpisodeFileJson { get; set; }
    public string? RatingsJson { get; set; }
    public string? LanguagesJson { get; set; }

    public int? SeriesDbId { get; set; }

    [ForeignKey(nameof(SeriesDbId))]
    public Series? Series { get; set; }

    public int ServerId { get; set; }

    [ForeignKey(nameof(ServerId))]
    public MediaServer? ParentServer { get; set; }
}