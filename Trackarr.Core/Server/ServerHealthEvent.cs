namespace Trackarr.Core.Server;

public enum ServerHealthEventType
{
    Down,
    Resolved
}

public class ServerHealthEvent
{
    public int Id { get; set; }

    public int ServerId { get; set; }
    public ServerHealthEventType EventType { get; set; }
    public string Message { get; set; } = "";
    public string? Source { get; set; }

    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
}
