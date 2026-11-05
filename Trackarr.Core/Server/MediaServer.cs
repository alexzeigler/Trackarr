using System.ComponentModel.DataAnnotations.Schema;
using Trackarr.Core.Queue;

namespace Trackarr.Core.Server;

public enum ServiceType
{
    Radarr,
    Sonarr
}

public class MediaServer
{
    #region Identity

    public int Id { get; set; }

    #endregion

    #region Methods

    public MediaServer Clone()
    {
        return (MediaServer)MemberwiseClone();
    }

    #endregion

    #region Properties

    public string Name { get; set; } = string.Empty;
    public ServiceType ServiceType { get; set; }

    public string Host { get; set; } = string.Empty;
    public int Port { get; set; }
    public bool UseHttps { get; set; }

    public string? ApiKey { get; set; }

    public int? FallBackProfileId { get; set; }
    public bool IsDefault { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public bool IsEnabled { get; set; } = true;
    public bool DisabledByHeartbeat { get; set; }
    public double UptimePercent { get; set; } = 100;
    public int DownloadsProcessed { get; set; }

    public bool? MonitorQueue { get; set; }
    public bool? AutoRemoveDownloads { get; set; }
    public bool? SearchMissing { get; set; }
    public int? RequestTimeoutSeconds { get; set; }
    public int Priority { get; set; }

    public bool IsOnline { get; set; }
    public DateTime? LastPingedAt { get; set; }
    public long? LastPingMs { get; set; }

    public bool IsAuthenticated { get; set; }
    public DateTime LastAuthenticatedAt { get; set; }

    public DateTime LastQueueFetchAt { get; set; }
    public int? LastQueueCount { get; set; }

    public DateTime? LastMissingFetchAt { get; set; }
    public int? LastMissingCount { get; set; }

    public DateTime? LastErrorAt { get; set; }
    public string? LastError { get; set; }

    #endregion

    #region Computed

    public string BaseUrl => $"{(UseHttps ? "https" : "http")}://{Host}:{Port}";

    [NotMapped] public ServerMetadata? Metadata { get; set; }
    [NotMapped] public List<QualityProfile>? Profiles { get; set; }
    [NotMapped] public List<DownloadItem>? Queue { get; set; }
    [NotMapped] public QualityProfile? FallbackProfile { get; set; }

    #endregion
}
