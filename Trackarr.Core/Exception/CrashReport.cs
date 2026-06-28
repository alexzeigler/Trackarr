namespace Trackarr.Core.Exception;

public class CrashReport
{
    public DateTime TimeStamp { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? ExceptionType { get; set; }
    public string? StackTrace { get; set; }
    public string Source { get; set; } = "Unhandled";
}
