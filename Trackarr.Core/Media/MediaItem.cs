using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;
using System.Text.Json.Serialization;
using Trackarr.Core.Server;

namespace Trackarr.Core.Media;

public record MediaImage(string CoverType, string Url, string RemoteUrl);

public record MediaLanguage(int Id, string Name);

public abstract class MediaItem
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    [JsonIgnore] public int Id { get; set; }
    [JsonPropertyName("id")] public int RemoteId { get; set; }
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

    public int ServerId { get; set; }
    [ForeignKey(nameof(ServerId))] public MediaServer? ParentServer { get; set; }


    [NotMapped] public int SeasonNumber { get; set; }

    [NotMapped] public int EpisodeNumber { get; set; }

    [NotMapped] public IReadOnlyList<MediaImage> Images => Deserialize<List<MediaImage>>(ImagesJson) ?? [];

    [NotMapped] public IReadOnlyList<string> Genres => Deserialize<List<string>>(GenresJson) ?? [];

    [NotMapped] public IReadOnlyList<int> Tags => Deserialize<List<int>>(TagsJson) ?? [];

    [NotMapped] public MediaLanguage? OriginalLanguage => Deserialize<MediaLanguage>(OriginalLanguageJson);

    [NotMapped] public string? PosterUrl => Images.FirstOrDefault(i => i.CoverType == "poster")?.RemoteUrl;

    [NotMapped] public string? FanartUrl => Images.FirstOrDefault(i => i.CoverType == "fanart")?.RemoteUrl;

    [NotMapped] public string? BannerUrl => Images.FirstOrDefault(i => i.CoverType == "banner")?.RemoteUrl;

    private static T? Deserialize<T>(string? json)
    {
        if (string.IsNullOrEmpty(json)) return default;
        try
        {
            return JsonSerializer.Deserialize<T>(json, JsonOptions);
        }
        catch
        {
            return default;
        }
    }
}