using System.Text.Json;
using Trackarr.Core.Media.Movies;
using Trackarr.Core.Media.Shows;
using Trackarr.Core.Queue;
using Trackarr.Integrations.Media.Arr.Dtos;

namespace Trackarr.Services.Media;

/// <summary>
///     Maps the JSON DTOs returned by the Radarr/Sonarr APIs onto the persisted entities.
///     Pure, stateless projections — extracted out of <see cref="MediaServerService" /> so the
///     mapping lives in exactly one place per type instead of being copy-pasted per call site.
/// </summary>
internal static class MediaRecordMappers
{
    #region Mappers

    public static Movie ToEntity(this MovieRecord x, int serverId)
    {
        return new Movie
        {
            RemoteId = x.Id,
            Title = x.Title,
            OriginalTitle = x.OriginalTitle,
            SortTitle = x.SortTitle,
            Status = x.Status,
            Overview = x.Overview,
            Website = x.Website,
            Year = x.Year,
            YouTubeTrailerId = x.YouTubeTrailerId,
            Studio = x.Studio,
            Path = x.Path,
            QualityProfileId = x.QualityProfileId,
            HasFile = x.HasFile,
            MovieFileId = x.MovieFileId,
            Monitored = x.Monitored,
            MinimumAvailability = x.MinimumAvailability,
            IsAvailable = x.IsAvailable,
            FolderName = x.FolderName,
            Runtime = x.Runtime,
            CleanTitle = x.CleanTitle,
            ImdbId = x.ImdbId,
            TmdbId = x.TmdbId,
            TitleSlug = x.TitleSlug,
            Certification = x.Certification,
            SizeOnDisk = x.SizeOnDisk,
            InCinemas = x.InCinemas,
            PhysicalRelease = x.PhysicalRelease,
            DigitalRelease = x.DigitalRelease,
            ReleaseDate = x.ReleaseDate,
            Added = x.Added,
            LastSearchTime = x.LastSearchTime,
            Popularity = x.Popularity,
            ServerId = serverId,
            OriginalLanguageJson = JsonSerializer.Serialize(x.OriginalLanguage),
            AlternateTitlesJson = JsonSerializer.Serialize(x.AlternateTitles),
            ImagesJson = JsonSerializer.Serialize(x.Images),
            GenresJson = JsonSerializer.Serialize(x.Genres),
            KeywordsJson = JsonSerializer.Serialize(x.Keywords),
            TagsJson = JsonSerializer.Serialize(x.Tags),
            RatingsJson = JsonSerializer.Serialize(x.Ratings),
            CollectionJson = JsonSerializer.Serialize(x.Collection),
            StatisticsJson = JsonSerializer.Serialize(x.Statistics)
        };
    }

    public static Episode ToEntity(this MissingEpisodeRecord x, int serverId)
    {
        return new Episode
        {
            RemoteId = x.Id,
            RemoteSeriesId = x.SeriesId,
            TvdbId = x.TvdbId,
            EpisodeFileId = x.EpisodeFileId,
            SeasonNumber = x.SeasonNumber,
            EpisodeNumber = x.EpisodeNumber,
            AbsoluteEpisodeNumber = x.AbsoluteEpisodeNumber,
            SceneAbsoluteEpisodeNumber = x.SceneAbsoluteEpisodeNumber,
            SceneEpisodeNumber = x.SceneEpisodeNumber,
            SceneSeasonNumber = x.SceneSeasonNumber,
            Title = x.Title,
            AirDate = x.AirDate,
            AirDateUtc = x.AirDateUtc,
            EndTime = x.EndTime,
            GrabDate = x.GrabDate,
            Runtime = x.Runtime,
            FinaleType = x.FinaleType,
            Overview = x.Overview,
            HasFile = x.HasFile,
            Monitored = x.Monitored,
            UnverifiedSceneNumbering = x.UnverifiedSceneNumbering,
            LastSearchTime = x.LastSearchTime,
            ServerId = serverId,
            ImagesJson = JsonSerializer.Serialize(x.Images),
            EpisodeFileJson = JsonSerializer.Serialize(x.EpisodeFile),
            SeriesJson = JsonSerializer.Serialize(x.Series)
        };
    }

    public static Episode ToEntity(this EpisodeRecord x, int serverId)
    {
        return new Episode
        {
            RemoteId = x.Id,
            RemoteSeriesId = x.SeriesId,
            TvdbId = x.TvdbId,
            EpisodeFileId = x.EpisodeFileId,
            SeasonNumber = x.SeasonNumber,
            EpisodeNumber = x.EpisodeNumber,
            AbsoluteEpisodeNumber = x.AbsoluteEpisodeNumber,
            Title = x.Title,
            AirDate = x.AirDate,
            AirDateUtc = x.AirDateUtc,
            Overview = x.Overview,
            HasFile = x.HasFile,
            Monitored = x.Monitored,
            UnverifiedSceneNumbering = x.UnverifiedSceneNumbering,
            LastSearchTime = x.LastSearchTime,
            ServerId = serverId,
            ImagesJson = JsonSerializer.Serialize(x.Images),
            EpisodeFileJson = JsonSerializer.Serialize(x.EpisodeFile)
        };
    }

    public static Series ToEntity(this SeriesRecord x, int serverId)
    {
        return new Series
        {
            RemoteId = x.Id,
            Title = x.Title,
            SortTitle = x.SortTitle,
            Status = x.Status,
            Ended = x.Ended,
            Overview = x.Overview,
            Network = x.Network,
            AirTime = x.AirTime,
            Path = x.Path,
            QualityProfileId = x.QualityProfileId,
            LanguageProfileId = x.LanguageProfileId,
            SeasonFolder = x.SeasonFolder,
            Monitored = x.Monitored,
            MonitorNewItems = x.MonitorNewItems,
            UseSceneNumbering = x.UseSceneNumbering,
            Runtime = x.Runtime,
            TvdbId = x.TvdbId,
            TvRageId = x.TvRageId,
            TvMazeId = x.TvMazeId,
            TmdbId = x.TmdbId,
            ImdbId = x.ImdbId,
            TitleSlug = x.TitleSlug,
            RootFolderPath = x.RootFolderPath,
            Certification = x.Certification,
            SeriesType = x.SeriesType,
            CleanTitle = x.CleanTitle,
            Year = x.Year,
            FirstAired = x.FirstAired,
            LastAired = x.LastAired,
            NextAiring = x.NextAiring,
            PreviousAiring = x.PreviousAiring,
            Added = x.Added,
            ServerId = serverId,
            AlternateTitlesJson = JsonSerializer.Serialize(x.AlternateTitles),
            ImagesJson = JsonSerializer.Serialize(x.Images),
            OriginalLanguageJson = JsonSerializer.Serialize(x.OriginalLanguage),
            SeasonsJson = JsonSerializer.Serialize(x.Seasons),
            GenresJson = JsonSerializer.Serialize(x.Genres),
            TagsJson = JsonSerializer.Serialize(x.Tags),
            RatingsJson = JsonSerializer.Serialize(x.Ratings),
            StatisticsJson = JsonSerializer.Serialize(x.Statistics)
        };
    }

    /// <summary>A queue record as a download. The embedded movie, series and episode are stored separately.</summary>
    public static DownloadItem ToEntity(this QueueRecord x, int serverId)
    {
        return new DownloadItem
        {
            RemoteId = x.Id,
            Title = x.Title,
            Size = x.Size,
            SizeLeft = x.SizeLeft,
            TimeLeft = x.TimeLeft,
            Status = x.Status,
            TrackedDownloadStatus = x.TrackedDownloadStatus,
            TrackedDownloadState = x.TrackedDownloadState,
            ErrorMessage = x.ErrorMessage,
            DownloadId = x.DownloadId,
            Protocol = x.Protocol,
            DownloadClient = x.DownloadClient,
            DownloadClientHasPostImportCategory = x.DownloadClientHasPostImportCategory,
            Indexer = x.Indexer,
            OutputPath = x.OutputPath,
            EstimatedCompletionTime = x.EstimatedCompletionTime,
            Added = x.Added,
            MovieId = x.MovieId,
            SeriesId = x.SeriesId,
            EpisodeId = x.EpisodeId,
            ServerId = serverId,
            State = ToQueueState(x.TrackedDownloadState),
            LanguagesJson = SerializeJsonElement(x.Languages, "[]"),
            QualityJson = SerializeJsonElement(x.Quality, "null"),
            CustomFormatsJson = SerializeJsonElement(x.CustomFormats, "[]"),
            StatusMessagesJson = SerializeJsonElement(x.StatusMessages, "[]")
        };
    }

    #endregion

    #region Helpers

    private static readonly JsonSerializerOptions CompactJsonOptions = new() { WriteIndented = false };

    internal static QueueState ToQueueState(string? trackedDownloadState)
    {
        return trackedDownloadState?.ToLowerInvariant() switch
        {
            "downloading" => QueueState.Downloading,
            "importpending" => QueueState.PendingImport,
            "importing" => QueueState.Importing,
            "failed" => QueueState.Failed,
            _ => QueueState.Unknown
        };
    }

    private static string SerializeJsonElement(JsonElement value, string fallback)
    {
        return value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null
            ? fallback
            : JsonSerializer.Serialize(value, CompactJsonOptions);
    }

    #endregion
}
