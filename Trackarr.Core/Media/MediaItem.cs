using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using Trackarr.Core.Server;

namespace Trackarr.Core.Media;

public record MediaImage(string CoverType, string Url, string RemoteUrl);

public record MediaLanguage(int Id, string Name);

public abstract class MediaItem
{
    #region Identity

    [JsonIgnore] public int Id { get; set; }
    [JsonPropertyName("id")] public int RemoteId { get; set; }

    #endregion

    #region Properties

    public string? Title { get; set; }
    public string? SortTitle { get; set; }
    public string? Status { get; set; }
    public string? Overview { get; set; }
    public string? Path { get; set; }
    public int QualityProfileId { get; set; }
    public bool Monitored { get; set; }
    public int Runtime { get; set; }
    public int TmdbId { get; set; }
    public string? ImdbId { get; set; }
    public string? TitleSlug { get; set; }
    public string? Certification { get; set; }
    public string? CleanTitle { get; set; }
    public int Year { get; set; }
    public DateTime? Added { get; set; }

    public string? ImagesJson { get; set; }
    public string? GenresJson { get; set; }
    public string? TagsJson { get; set; }
    public string? RatingsJson { get; set; }
    public string? StatisticsJson { get; set; }
    public string? OriginalLanguageJson { get; set; }

    #endregion

    #region Navigation

    public int ServerId { get; set; }
    [ForeignKey(nameof(ServerId))] public MediaServer? ParentServer { get; set; }

    #endregion

    #region Computed

    [NotMapped] public IReadOnlyList<MediaImage> Images => JsonHelper.Deserialize<List<MediaImage>>(ImagesJson) ?? [];

    [NotMapped] public IReadOnlyList<string> Genres => JsonHelper.Deserialize<List<string>>(GenresJson) ?? [];

    [NotMapped] public IReadOnlyList<int> Tags => JsonHelper.Deserialize<List<int>>(TagsJson) ?? [];

    [NotMapped] public MediaLanguage? OriginalLanguage => JsonHelper.Deserialize<MediaLanguage>(OriginalLanguageJson);

    [NotMapped] public string? PosterUrl => Images.FirstOrDefault(i => i.CoverType == "poster")?.RemoteUrl;

    [NotMapped] public string? FanartUrl => Images.FirstOrDefault(i => i.CoverType == "fanart")?.RemoteUrl;

    [NotMapped] public string? BannerUrl => Images.FirstOrDefault(i => i.CoverType == "banner")?.RemoteUrl;

    #endregion
}
