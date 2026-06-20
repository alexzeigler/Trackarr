using Microsoft.EntityFrameworkCore;
using Trackarr.Core.Event;
using Trackarr.Core.Health;
using Trackarr.Core.Media.Missing;
using Trackarr.Core.Media.Movies;
using Trackarr.Core.Media.Shows;
using Trackarr.Core.Queue;
using Trackarr.Core.Routine;
using Trackarr.Core.Server;

namespace Trackarr.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options)
    : DbContext(options)
{
    public DbSet<MediaServer> MediaServers { get; set; }
    public DbSet<ServerMetadata> ServerMetadata { get; set; }
    public DbSet<DownloadItem> DownloadQueue { get; set; }
    public DbSet<RoutineDefinition> Routines { get; set; }
    public DbSet<RoutineRun> RoutineRuns { get; set; }
    public DbSet<LogEvent> LogEvents { get; set; }
    public DbSet<Movie> Movies { get; set; }
    public DbSet<Series> Series { get; set; }
    public DbSet<Episode> Episodes { get; set; }

    public DbSet<DownloadAttempt> DownloadAttempts { get; set; }
    public DbSet<HealthSnapshot> HealthSnapshots { get; set; }
    public DbSet<DownloadHealthEvent> HealthEvents { get; set; }
    public DbSet<MediaHealthSummary> MediaHealthSummaries { get; set; }
    public DbSet<SourceHealthSummary> SourceHealthSummaries { get; set; }
    public DbSet<QualityProfile> QualityProfiles { get; set; }
    public DbSet<MissingMediaRecord> MissingMedia { get; set; }
    public DbSet<MissingSearchEvent> SearchEvents { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<MediaServer>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(255);
            entity.Property(e => e.ServiceType).IsRequired().HasConversion<string>().HasMaxLength(50);
            entity.Property(e => e.Host).IsRequired().HasMaxLength(1000);
            entity.Property(e => e.Port).IsRequired();
            entity.Property(e => e.ApiKey).HasMaxLength(1000);
            entity.Property(e => e.FallBackProfileId);

            entity.HasOne(e => e.Metadata).WithOne().HasForeignKey<ServerMetadata>(m => m.ServerId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.FallbackProfile).WithMany()
                .HasForeignKey(e => e.FallBackProfileId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ServerMetadata>(entity =>
        {
            entity.HasKey(e => e.ServerId);

            entity.Property(e => e.AppName).HasMaxLength(255);
            entity.Property(e => e.InstanceName).HasMaxLength(255);
            entity.Property(e => e.Version).HasMaxLength(50);
            entity.Property(e => e.OsName).HasMaxLength(255);
            entity.Property(e => e.OsVersion).HasMaxLength(255);
            entity.Property(e => e.DatabaseType).HasMaxLength(50);
            entity.Property(e => e.DatabaseVersion).HasMaxLength(50);
            entity.Property(e => e.Authentication).HasMaxLength(50);
            entity.Property(e => e.Mode).HasMaxLength(50);
            entity.Property(e => e.Branch).HasMaxLength(50);
            entity.Property(e => e.UrlBase).HasMaxLength(255);
            entity.Property(e => e.RuntimeVersion).HasMaxLength(50);
            entity.Property(e => e.RuntimeName).HasMaxLength(50);
            entity.Property(e => e.PackageVersion).HasMaxLength(50);
            entity.Property(e => e.PackageAuthor).HasMaxLength(255);
            entity.Property(e => e.PackageUpdateMechanism).HasMaxLength(50);
        });

        modelBuilder.Entity<DownloadItem>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Title).HasMaxLength(1000);
            entity.Property(e => e.Status).HasMaxLength(100);
            entity.Property(e => e.TrackedDownloadStatus).HasMaxLength(100);
            entity.Property(e => e.TrackedDownloadState).HasMaxLength(100);
            entity.Property(e => e.ErrorMessage).HasMaxLength(2000);
            entity.Property(e => e.DownloadId).HasMaxLength(255);
            entity.Property(e => e.Protocol).HasMaxLength(100);
            entity.Property(e => e.DownloadClient).HasMaxLength(255);
            entity.Property(e => e.Indexer).HasMaxLength(255);
            entity.Property(e => e.AttemptKey).HasMaxLength(500);
            entity.Property(e => e.TrackingState).HasConversion<string>().HasMaxLength(100);

            entity.HasIndex(e => e.AttemptKey);
            entity.HasIndex(e => new { e.ServerId, e.AttemptKey });
            entity.HasIndex(e => new { e.ServerId, e.DownloadId });
        });

        modelBuilder.Entity<RoutineDefinition>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Key).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Description).HasMaxLength(500);

            entity.HasIndex(e => e.Key).IsUnique();
        });

        modelBuilder.Entity<RoutineRun>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.State).HasConversion<string>().HasMaxLength(50);
            entity.Property(e => e.ErrorMessage).HasMaxLength(1000);
            entity.HasOne(e => e.Routine).WithMany(e => e.Runs).HasForeignKey(e => e.RoutineDefinitionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<LogEvent>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Message).IsRequired().HasMaxLength(500);
            entity.Property(e => e.Source).IsRequired().HasMaxLength(50);
            entity.Property(e => e.LogLevel).IsRequired();
            entity.Property(e => e.TimeStamp).IsRequired();

            entity.HasIndex(e => e.TimeStamp);
            entity.HasIndex(e => e.LogLevel);
            entity.HasIndex(e => e.Source);
        });

        modelBuilder.Entity<Movie>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.Property(e => e.RemoteId).IsRequired();
            entity.Property(e => e.Title).HasMaxLength(500);
            entity.Property(e => e.OriginalTitle).HasMaxLength(500);
            entity.Property(e => e.SortTitle).HasMaxLength(500);
            entity.Property(e => e.Status).HasMaxLength(50);
            entity.Property(e => e.Website).HasMaxLength(1000);
            entity.Property(e => e.YouTubeTrailerId).HasMaxLength(100);
            entity.Property(e => e.Studio).HasMaxLength(255);
            entity.Property(e => e.Path).HasMaxLength(1000);
            entity.Property(e => e.MinimumAvailability).HasMaxLength(50);
            entity.Property(e => e.FolderName).HasMaxLength(1000);
            entity.Property(e => e.CleanTitle).HasMaxLength(500);
            entity.Property(e => e.ImdbId).HasMaxLength(50);
            entity.Property(e => e.TitleSlug).HasMaxLength(255);
            entity.Property(e => e.Certification).HasMaxLength(50);

            entity.Property(e => e.Overview);
            entity.Property(e => e.OriginalLanguageJson);
            entity.Property(e => e.AlternateTitlesJson);
            entity.Property(e => e.ImagesJson);
            entity.Property(e => e.GenresJson);
            entity.Property(e => e.KeywordsJson);
            entity.Property(e => e.TagsJson);
            entity.Property(e => e.RatingsJson);
            entity.Property(e => e.CollectionJson);
            entity.Property(e => e.StatisticsJson);

            entity.HasIndex(e => new { e.ServerId, e.RemoteId }).IsUnique();
            entity.HasIndex(e => e.Title);
            entity.HasIndex(e => e.CleanTitle);

            entity.HasOne(e => e.ParentServer).WithMany().HasForeignKey(e => e.ServerId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Series>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.Property(e => e.RemoteId).IsRequired();
            entity.Property(e => e.Title).HasMaxLength(500);
            entity.Property(e => e.SortTitle).HasMaxLength(500);
            entity.Property(e => e.Status).HasMaxLength(50);
            entity.Property(e => e.Network).HasMaxLength(255);
            entity.Property(e => e.AirTime).HasMaxLength(50);
            entity.Property(e => e.Path).HasMaxLength(1000);
            entity.Property(e => e.MonitorNewItems).HasMaxLength(100);
            entity.Property(e => e.ImdbId).HasMaxLength(50);
            entity.Property(e => e.TitleSlug).HasMaxLength(255);
            entity.Property(e => e.RootFolderPath).HasMaxLength(1000);
            entity.Property(e => e.Certification).HasMaxLength(50);
            entity.Property(e => e.SeriesType).HasMaxLength(50);
            entity.Property(e => e.CleanTitle).HasMaxLength(500);

            entity.Property(e => e.Overview);
            entity.Property(e => e.AlternateTitlesJson);
            entity.Property(e => e.ImagesJson);
            entity.Property(e => e.OriginalLanguageJson);
            entity.Property(e => e.SeasonsJson);
            entity.Property(e => e.GenresJson);
            entity.Property(e => e.TagsJson);
            entity.Property(e => e.RatingsJson);
            entity.Property(e => e.StatisticsJson);

            entity.HasIndex(e => new { e.ServerId, e.RemoteId }).IsUnique();
            entity.HasIndex(e => e.Title);
            entity.HasIndex(e => e.CleanTitle);

            entity.HasOne(e => e.ParentServer).WithMany().HasForeignKey(e => e.ServerId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Episode>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.HasIndex(e => new { e.ServerId, e.RemoteId }).IsUnique();
            entity.HasIndex(e => new { e.ServerId, e.RemoteSeriesId });
            entity.HasIndex(e => new { e.ServerId, e.SeriesDbId });
            entity.HasIndex(e => new { e.ServerId, e.RemoteSeriesId, e.SeasonNumber, e.EpisodeNumber });

            entity.Property(e => e.Title).HasMaxLength(500);
            entity.Property(e => e.AirDate).HasMaxLength(50);
            entity.Property(e => e.FinaleType).HasMaxLength(100);

            entity.Property(e => e.Overview).HasColumnType("TEXT");
            entity.Property(e => e.ImagesJson).HasColumnType("TEXT");
            entity.Property(e => e.EpisodeFileJson).HasColumnType("TEXT");
            entity.Property(e => e.SeriesJson).HasColumnType("TEXT");

            entity.HasOne(e => e.Series).WithMany().HasForeignKey(e => e.SeriesDbId).OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.ParentServer).WithMany().HasForeignKey(e => e.ServerId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<DownloadAttempt>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.Property(e => e.ServiceType).HasConversion<string>().HasMaxLength(50);
            entity.Property(e => e.DownloadId).IsRequired().HasMaxLength(255);
            entity.Property(e => e.Title).IsRequired().HasMaxLength(1000);
            entity.Property(e => e.NormalizedTitle).HasMaxLength(1000);
            entity.Property(e => e.ReleaseKey).HasMaxLength(1000);
            entity.Property(e => e.DownloadClient).HasMaxLength(255);
            entity.Property(e => e.Indexer).HasMaxLength(255);
            entity.Property(e => e.Protocol).HasMaxLength(100);
            entity.Property(e => e.MediaType).HasConversion<string>().HasMaxLength(50);
            entity.Property(e => e.Status).HasConversion<string>().HasMaxLength(100);
            entity.Property(e => e.FinalReasonKey).HasMaxLength(255);
            entity.Property(e => e.FinalMessage).HasMaxLength(2000);

            entity.HasIndex(e => new { e.ServerId, e.DownloadId });
            entity.HasIndex(e => new { e.ServerId, e.DownloadId, e.Status });
            entity.HasIndex(e => new { e.ServerId, e.ReleaseKey });
            entity.HasIndex(e => new { e.MediaType, e.MovieId });
            entity.HasIndex(e => new { e.MediaType, e.SeriesId });
            entity.HasIndex(e => new { e.MediaType, e.EpisodeId });
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.FirstSeenAt);
            entity.HasIndex(e => e.LastSeenAt);

            entity.HasOne(e => e.Server).WithMany().HasForeignKey(e => e.ServerId).OnDelete(DeleteBehavior.Cascade);

            entity.HasOne<Movie>().WithMany().HasForeignKey(e => e.MovieId).OnDelete(DeleteBehavior.SetNull);

            entity.HasOne<Series>().WithMany().HasForeignKey(e => e.SeriesId).OnDelete(DeleteBehavior.SetNull);

            entity.HasOne<Episode>().WithMany().HasForeignKey(e => e.EpisodeId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<HealthSnapshot>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.Property(e => e.RawScore).HasPrecision(8, 2);
            entity.Property(e => e.State).HasConversion<string>().HasMaxLength(100);
            entity.Property(e => e.LastProgress).HasPrecision(8, 2);
            entity.Property(e => e.LastStatus).HasMaxLength(100);
            entity.Property(e => e.LastTrackedDownloadStatus).HasMaxLength(100);
            entity.Property(e => e.LastTrackedDownloadState).HasMaxLength(100);
            entity.Property(e => e.LastErrorKey).HasMaxLength(255);
            entity.Property(e => e.LastErrorMessage).HasMaxLength(2000);
            entity.Property(e => e.LastScoreChange).HasPrecision(8, 2);
            entity.Property(e => e.LastReasonKey).HasMaxLength(255);
            entity.Property(e => e.LastReasonMessage).HasMaxLength(2000);
            entity.Property(e => e.RemovalRestrictionReason).HasMaxLength(2000);

            entity.HasIndex(e => e.DownloadAttemptId).IsUnique();
            entity.HasIndex(e => e.State);
            entity.HasIndex(e => e.VisibleScore);
            entity.HasIndex(e => e.Confidence);
            entity.HasIndex(e => e.IsEligibleForRemoval);
            entity.HasIndex(e => e.LastObservationAt);
            entity.HasIndex(e => e.LastBadObservationAt);
            entity.HasIndex(e => e.LastMeaningfulRecoveryAt);

            entity.HasOne(e => e.DownloadAttempt).WithOne(e => e.HealthSnapshot)
                .HasForeignKey<HealthSnapshot>(e => e.DownloadAttemptId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<DownloadHealthEvent>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.Property(e => e.EventType).HasConversion<string>().HasMaxLength(100);
            entity.Property(e => e.Severity).HasConversion<string>().HasMaxLength(50);
            entity.Property(e => e.ReasonKey).IsRequired().HasMaxLength(255);
            entity.Property(e => e.Message).IsRequired().HasMaxLength(2000);
            entity.Property(e => e.ScoreBefore).HasPrecision(8, 2);
            entity.Property(e => e.ScoreDelta).HasPrecision(8, 2);
            entity.Property(e => e.ScoreAfter).HasPrecision(8, 2);
            entity.Property(e => e.Source).HasMaxLength(100);
            entity.Property(e => e.RawStatus).HasMaxLength(100);
            entity.Property(e => e.RawTrackedDownloadStatus).HasMaxLength(100);
            entity.Property(e => e.RawTrackedDownloadState).HasMaxLength(100);
            entity.Property(e => e.RawErrorMessage).HasMaxLength(2000);
            entity.Property(e => e.MetadataJson);

            entity.HasIndex(e => e.DownloadAttemptId);
            entity.HasIndex(e => e.OccurredAt);
            entity.HasIndex(e => e.EventType);
            entity.HasIndex(e => e.Severity);
            entity.HasIndex(e => e.ReasonKey);
            entity.HasIndex(e => e.IsBadObservation);
            entity.HasIndex(e => e.IsGoodObservation);
            entity.HasIndex(e => e.IsMeaningfulRecovery);
            entity.HasIndex(e => e.CountsTowardRemoval);

            entity.HasOne(e => e.DownloadAttempt).WithMany(e => e.HealthEvents).HasForeignKey(e => e.DownloadAttemptId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<MediaHealthSummary>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.Property(e => e.MediaType).HasConversion<string>().HasMaxLength(50);
            entity.Property(e => e.CurrentState).HasConversion<string>().HasMaxLength(100);
            entity.Property(e => e.LastQualityProfile).HasMaxLength(255);
            entity.Property(e => e.LowestQualityTried).HasMaxLength(255);
            entity.Property(e => e.StuckReasonKey).HasMaxLength(255);
            entity.Property(e => e.StuckReasonMessage).HasMaxLength(2000);

            entity.HasIndex(e => new { e.MediaType, e.MovieId }).IsUnique();
            entity.HasIndex(e => new { e.MediaType, e.SeriesId }).IsUnique();
            entity.HasIndex(e => new { e.MediaType, e.EpisodeId }).IsUnique();
            entity.HasIndex(e => e.CurrentState);
            entity.HasIndex(e => e.IsStuck);
            entity.HasIndex(e => e.LastAttemptAt);
            entity.HasIndex(e => e.LastFailureAt);
            entity.HasIndex(e => e.StuckSince);

            entity.HasOne<Movie>().WithMany().HasForeignKey(e => e.MovieId).OnDelete(DeleteBehavior.Cascade);

            entity.HasOne<Series>().WithMany().HasForeignKey(e => e.SeriesId).OnDelete(DeleteBehavior.Cascade);

            entity.HasOne<Episode>().WithMany().HasForeignKey(e => e.EpisodeId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SourceHealthSummary>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.Property(e => e.ServiceType).HasConversion<string>().HasMaxLength(50);
            entity.Property(e => e.Indexer).HasMaxLength(255);
            entity.Property(e => e.DownloadClient).HasMaxLength(255);
            entity.Property(e => e.Protocol).HasMaxLength(100);
            entity.Property(e => e.ReleaseKey).HasMaxLength(1000);
            entity.Property(e => e.ReasonKey).HasMaxLength(255);
            entity.Property(e => e.AverageScore).HasPrecision(8, 2);

            entity.HasIndex(e => new
            {
                e.ServerId,
                e.ServiceType,
                e.Indexer,
                e.DownloadClient,
                e.Protocol,
                e.ReleaseKey,
                e.ReasonKey
            });

            entity.HasIndex(e => e.ReputationScore);
            entity.HasIndex(e => e.LastSeenAt);
            entity.HasIndex(e => e.LastFailureAt);
            entity.HasIndex(e => e.LastSuccessAt);

            entity.HasOne(e => e.Server).WithMany().HasForeignKey(e => e.ServerId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<QualityProfile>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasOne(e => e.ParentServer).WithMany()
                .HasForeignKey(e => e.ServerId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<MissingMediaRecord>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Tier).HasConversion<string>().HasMaxLength(50);
            entity.Property(e => e.SearchState).HasConversion<string>().HasMaxLength(50);

            entity.HasIndex(e => new { e.ServerId, e.RemoteId, e.EpisodeId }).IsUnique();
            entity.HasIndex(e => e.ServerId);
        });

        modelBuilder.Entity<MissingSearchEvent>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.Property(e => e.EventType).HasConversion<string>().HasMaxLength(50);
            entity.Property(e => e.Tier).HasConversion<string>().HasMaxLength(50);
            entity.Property(e => e.Detail).HasMaxLength(2000);

            entity.HasIndex(e => new { e.ServerId, e.RemoteId, e.EpisodeId });
            entity.HasIndex(e => e.OccurredAt);
        });
    }
}