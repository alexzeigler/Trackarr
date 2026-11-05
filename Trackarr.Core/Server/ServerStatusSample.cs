namespace Trackarr.Core.Server;

public class ServerStatusSample
{
    public int Id { get; set; }

    public int ServerId { get; set; }
    public DateTime SampledAt { get; set; } = DateTime.UtcNow;
    public long? PingMs { get; set; }
    public bool Success { get; set; }
}
