using Microsoft.EntityFrameworkCore;
using Trackarr.Core.Event;
using Trackarr.Core.Media.Movies;
using Trackarr.Core.Media.Shows;
using Trackarr.Core.Queue;
using Trackarr.Core.Routine;
using Trackarr.Core.Server;

namespace Trackarr.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options)
    : DbContext(options) {
    public DbSet<MediaServer> MediaServers { get; set; }
    public DbSet<ServerMetadata> ServerMetadata { get; set; }
    public DbSet<DownloadItem> DownloadQueue { get; set; }
    public DbSet<RoutineDefinition> Routines { get; set; }
    public DbSet<RoutineRun> RoutineRuns { get; set; }
    public DbSet<LogEvent> LogEvents { get; set; }
    public DbSet<Movie> Movies { get; set; }
    public DbSet<Series> Series { get; set; }
    public DbSet<Episode> Episodes { get; set; }
    public DbSet<DownloadStrike> DownloadStrikes { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder) {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<MediaServer>(entity => {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired();
            entity.Property(e => e.ServiceType).IsRequired();
            entity.Property(e => e.Host).IsRequired();
            entity.Property(e => e.Port).IsRequired();
            entity.HasOne(e => e.Metadata).WithOne().HasForeignKey<ServerMetadata>(m => m.ServerId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<DownloadItem>(entity => { entity.HasKey(e => e.Id); });

        modelBuilder.Entity<ServerMetadata>(entity => {
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

        modelBuilder.Entity<RoutineDefinition>(entity => {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Key).IsRequired().HasMaxLength(100);
            entity.HasIndex(e => e.Key).IsUnique();
            entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Description).HasMaxLength(500);
        });
        modelBuilder.Entity<RoutineRun>(entity => {
            entity.HasKey(e => e.Id);
            entity.HasOne(e => e.Routine).WithMany(e => e.Runs).HasForeignKey(e => e.RoutineDefinitionId).OnDelete(DeleteBehavior.Cascade);
            entity.Property(e => e.State).HasConversion<string>().HasMaxLength(50);
        });

        modelBuilder.Entity<LogEvent>(entity => {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Message).IsRequired().HasMaxLength(500);
            entity.Property(e => e.Source).IsRequired().HasMaxLength(50);
            entity.Property(e => e.LogLevel).IsRequired();
            entity.Property(e => e.TimeStamp).IsRequired();
        });

        modelBuilder.Entity<Movie>(entity => {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.RemoteId).IsRequired();
            entity.HasIndex(e => new { e.ServerId, e.RemoteId }).IsUnique();
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
            entity.HasOne(e => e.ParentServer).WithMany().HasForeignKey(e => e.ServerId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<Series>(entity => {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.RemoteId).IsRequired();
            entity.HasIndex(e => new { e.ServerId, e.RemoteId }).IsUnique();
            entity.Property(e => e.Title).HasMaxLength(500);
            entity.Property(e => e.SortTitle).HasMaxLength(500);
            entity.Property(e => e.Status).HasMaxLength(50);
            entity.Property(e => e.Overview);
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
            entity.Property(e => e.AlternateTitlesJson);
            entity.Property(e => e.ImagesJson);
            entity.Property(e => e.OriginalLanguageJson);
            entity.Property(e => e.SeasonsJson);
            entity.Property(e => e.GenresJson);
            entity.Property(e => e.TagsJson);
            entity.Property(e => e.RatingsJson);
            entity.Property(e => e.StatisticsJson);
            entity.HasOne(e => e.ParentServer).WithMany().HasForeignKey(e => e.ServerId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Episode>(entity => {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.RemoteId).IsRequired();
            entity.Property(e => e.RemoteSeriesId).IsRequired();
            entity.HasIndex(e => new { e.ServerId, e.RemoteId }).IsUnique();
            entity.HasIndex(e => new { e.ServerId, e.RemoteSeriesId });
            entity.HasIndex(e => new { e.SeriesDbId, e.SeasonNumber, e.EpisodeNumber });
            entity.Property(e => e.Title).HasMaxLength(500);
            entity.Property(e => e.AirDate).HasMaxLength(50);
            entity.Property(e => e.Overview);
            entity.Property(e => e.ImagesJson);
            entity.Property(e => e.EpisodeFileJson);
            entity.Property(e => e.RatingsJson);
            entity.Property(e => e.LanguagesJson);
            entity.HasOne(e => e.Series).WithMany().HasForeignKey(e => e.SeriesDbId).OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(e => e.ParentServer).WithMany().HasForeignKey(e => e.ServerId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<DownloadItem>(entity => {
            entity.HasMany(e => e.Strikes).WithOne(e => e.DownloadItem).HasForeignKey(e => e.DownloadItemId).OnDelete(DeleteBehavior.Cascade);
            entity.Property(e => e.TrackingState).HasConversion<string>();
            entity.HasIndex(e => e.AttemptKey);
            entity.HasIndex(e => new {
                e.ServerId,
                e.AttemptKey
            });
        });

        modelBuilder.Entity<DownloadStrike>(entity => {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Reason).HasConversion<string>();
            entity.Property(e => e.Severity).HasConversion<string>();
            entity.Property(e => e.Message).HasMaxLength(1000);
            entity.Property(e => e.Fingerprint).HasMaxLength(300);
            entity.Property(e => e.Resolution).HasMaxLength(1000);
            entity.HasIndex(e => e.DownloadItemId);
            entity.HasIndex(e => new {
                e.DownloadItemId,
                e.Reason,
                e.Fingerprint,
                e.ResolvedAtUtc
            });
        });
    }
}