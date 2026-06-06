namespace Trackarr.Core.Media;

public class MissingMovieResponse {
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalRecords { get; set; }
    public List<MissingMovieRecord> Records { get; set; } = [];
}