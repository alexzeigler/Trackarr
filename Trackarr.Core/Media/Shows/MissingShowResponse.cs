namespace Trackarr.Core.Media.Shows;

public class MissingShowResponse
{
    public int Page { get; set; }
    public int PageSize { get; set; }
    public string? SortKey { get; set; }
    public string? SortDirection { get; set; }
    public int TotalRecords { get; set; }
    public List<MissingEpisodeRecord> Records { get; set; } = [];
}
