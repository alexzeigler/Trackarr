namespace Trackarr.MockServer;

public record RequestEntry(
    DateTime Timestamp,
    string Method,
    string Path,
    string Query,
    string Mode,
    string Handling,
    int StatusCode,
    string? RequestBody,
    string? ResponseBody,
    string RemoteIp,
    string Host,
    string Scheme,
    string Protocol,
    string? UserAgent,
    long DurationMs,
    long? RequestContentLength,
    long ResponseContentLength,
    string? RequestContentType,
    string? ResponseContentType,
    Dictionary<string, string> RequestHeaders,
    Dictionary<string, string> ResponseHeaders);

public class RequestTracker
{
    private const int MaxEntries = 100;
    private readonly Queue<RequestEntry> _entries = new();
    private readonly object _lock = new();
    private int _rejectedCount;
    private int _totalCount;

    public int TotalCount
    {
        get
        {
            lock (_lock)
            {
                return _totalCount;
            }
        }
    }

    public int RejectedCount
    {
        get
        {
            lock (_lock)
            {
                return _rejectedCount;
            }
        }
    }

    public void Record(RequestEntry entry)
    {
        lock (_lock)
        {
            _entries.Enqueue(entry);
            if (_entries.Count > MaxEntries) _entries.Dequeue();
            _totalCount++;
            if (entry.Handling == "rejected") _rejectedCount++;
        }
    }

    public List<RequestEntry> GetRecent(int count = 100)
    {
        lock (_lock)
        {
            return _entries.TakeLast(count).Reverse().ToList();
        }
    }
}
