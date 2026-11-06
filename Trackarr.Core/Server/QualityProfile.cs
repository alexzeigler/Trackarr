using System.Text.Json.Serialization;

namespace Trackarr.Core.Server;

public class QualityProfile
{
    [JsonIgnore] public int Id { get; set; }
    [JsonPropertyName("id")] public int RemoteId { get; set; }
    public string? Name { get; set; }
    public int? ServerId { get; set; }
    public MediaServer? ParentServer { get; set; }
}
