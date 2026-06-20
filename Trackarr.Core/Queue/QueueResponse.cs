namespace Trackarr.Core.Queue;

public class QueueResponse
{
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalRecords { get; set; }
    public List<QueueRecord> Records { get; set; } = [];
}