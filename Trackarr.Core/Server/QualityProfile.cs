namespace Trackarr.Core.Server;

/// <summary>A Radarr/Sonarr quality profile, as stored. Read from the API as QualityProfileDto (Integrations).</summary>
public class QualityProfile
{
    public int Id { get; set; }

    /// <summary>The profile's ID on the Radarr/Sonarr server.</summary>
    public int RemoteId { get; set; }

    public string? Name { get; set; }
    public int? ServerId { get; set; }
    public MediaServer? ParentServer { get; set; }
}
