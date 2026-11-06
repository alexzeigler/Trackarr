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

    /// <summary>Path the server is served under, e.g. "/radarr" behind a reverse proxy. "" or null for none.</summary>
    public string? UrlBase { get; set; }

    /// <summary>
    ///     Accept any TLS certificate, for self-signed certificates (e.g. Radarr/Sonarr's own SSL port). Only use
    ///     on a network you trust: it turns off the check that you're talking to the real server.
    /// </summary>
    public bool SkipCertificateValidation { get; set; }

    public string? ApiKey { get; set; }

    public int? FallBackProfileId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public bool IsEnabled { get; set; } = true;
    public bool DisabledByHeartbeat { get; set; }
    public double UptimePercent { get; set; } = 100;

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

    public string BaseUrl => $"{(UseHttps ? "https" : "http")}://{Host}:{Port}{NormalizePath(UrlBase)}";

    /// <summary>
    ///     Cleans up the address fields. If a full URL was pasted into Host ("https://example.com/radarr"), it's
    ///     split into scheme, host, port and URL base. Trailing slashes are dropped from the URL base.
    /// </summary>
    public void NormalizeAddress()
    {
        var host = (Host ?? "").Trim();

        if (host.Contains("://") && Uri.TryCreate(host, UriKind.Absolute, out var uri) &&
            uri.Scheme is "http" or "https")
        {
            UseHttps = uri.Scheme == "https";
            Host = uri.Host;
            Port = uri.Port; // 80 / 443 when the URL has no port
            var path = uri.AbsolutePath.Trim('/');
            if (path.Length > 0) UrlBase = path;
        }
        else
        {
            Host = host.TrimEnd('/');
        }

        UrlBase = NormalizePath(UrlBase);
    }

    private static string NormalizePath(string? path)
    {
        var trimmed = (path ?? "").Trim().Trim('/');
        return trimmed.Length == 0 ? "" : "/" + trimmed;
    }

    // Relationships, configured in AppDbContext.
    public ServerMetadata? Metadata { get; set; }
    public QualityProfile? FallbackProfile { get; set; }

    // Held in memory only. AppDbContext ignores them.
    public List<QualityProfile>? Profiles { get; set; }
    public List<DownloadItem>? Queue { get; set; }

    #endregion
}
