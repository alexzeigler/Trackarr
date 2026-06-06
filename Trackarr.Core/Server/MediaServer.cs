using System.ComponentModel.DataAnnotations.Schema;
using Trackarr.Core.Media;
using Trackarr.Core.Queue;

namespace Trackarr.Core.Server;

public enum ServiceType {
    Radarr,
    Sonarr
}

public class MediaServer {
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public ServiceType ServiceType { get; set; }

    public string Host { get; set; } = string.Empty;
    public int Port { get; set; }
    public bool UseHttps { get; set; }

    public string? ApiKey { get; set; }
    public string? FallbackProfile { get; set; }
    public bool IsDefault { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastAuthenticated { get; set; }
    public long? LastPingTime { get; set; }
    public bool? LastPingSuccessful { get; set; }

    public bool IsEnabled { get; set; } = true;
    public bool IsAuthenticated { get; set; }

    public int DownloadsProcessed { get; set; }

    public string BaseUrl => $"{(UseHttps ? "https" : "http")}://{Host}:{Port}";


    [NotMapped] public ServerMetadata? Metadata { get; set; }

    [NotMapped] public List<QualityProfile>? Profiles { get; set; }
    [NotMapped] public List<DownloadItem>? Queue { get; set; }

    public MediaServer Clone() {
        return (MediaServer)MemberwiseClone();
    }
}