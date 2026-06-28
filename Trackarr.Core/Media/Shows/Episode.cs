using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;
using System.Text.Json.Serialization;
using Trackarr.Core.Server;

namespace Trackarr.Core.Media.Shows;

public class Episode
{
    #region Identity

    [JsonIgnore] public int Id { get; set; }

    [JsonPropertyName("id")] public int RemoteId { get; set; }

    [JsonPropertyName("seriesId")] public int RemoteSeriesId { get; set; }

    #endregion

    #region Properties

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
    public DateTime? AirDateUtc { get; set; }
    public DateTime? EndTime { get; set; }
    public DateTime? GrabDate { get; set; }

    public int? Runtime { get; set; }
    public string? FinaleType { get; set; }
    public string? Overview { get; set; }

    public bool HasFile { get; set; }
    public bool Monitored { get; set; }
    public bool UnverifiedSceneNumbering { get; set; }

    public DateTime? LastSearchTime { get; set; }

    [JsonIgnore] public string? ImagesJson { get; set; }
    [JsonIgnore] public string? EpisodeFileJson { get; set; }
    [JsonIgnore] public string? SeriesJson { get; set; }

    #endregion

    #region Navigation

    public int? SeriesDbId { get; set; }

    [JsonIgnore]
    [ForeignKey(nameof(SeriesDbId))]
    public Series? Series { get; set; }

    public int? ServerId { get; set; }

    [JsonIgnore]
    [ForeignKey(nameof(ServerId))]
    public MediaServer? ParentServer { get; set; }

    #endregion

    #region Computed

    [NotMapped]
    [JsonPropertyName("images")]
    public List<MediaImage>? ImageResources
    {
        get => JsonHelper.Deserialize<List<MediaImage>>(ImagesJson);
        set => ImagesJson = JsonHelper.Serialize(value);
    }

    [NotMapped]
    [JsonPropertyName("episodeFile")]
    public JsonElement? EpisodeFile
    {
        get => JsonHelper.Deserialize<JsonElement>(EpisodeFileJson);
        set => EpisodeFileJson = JsonHelper.Serialize(value);
    }

    [NotMapped]
    [JsonPropertyName("series")]
    public JsonElement? SeriesResource
    {
        get => JsonHelper.Deserialize<JsonElement>(SeriesJson);
        set => SeriesJson = JsonHelper.Serialize(value);
    }

    [NotMapped] [JsonIgnore] public IReadOnlyList<MediaImage> Images => ImageResources ?? [];

    [NotMapped] [JsonIgnore] public string? PosterUrl => Images.FirstOrDefault(i => i.CoverType == "poster")?.RemoteUrl;

    [NotMapped] [JsonIgnore] public string? FanartUrl => Images.FirstOrDefault(i => i.CoverType == "fanart")?.RemoteUrl;

    [NotMapped] [JsonIgnore] public string? BannerUrl => Images.FirstOrDefault(i => i.CoverType == "banner")?.RemoteUrl;

    #endregion
}
