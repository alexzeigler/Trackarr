using Microsoft.Extensions.Logging;

namespace Trackarr.Core.Event;

public class LogEvent {
    public int Id { get; set; }
    public string? Message { get; set; }
    public string? Exception { get; set; }
    public string? Source { get; set; }
    public LogLevel LogLevel { get; set; }
    public DateTime TimeStamp { get; set; }
}