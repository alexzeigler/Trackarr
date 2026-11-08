namespace Trackarr.Integrations.Media.Arr.Dtos;

public class MissingMovieResponse
{
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalRecords { get; set; }
    public List<MovieRecord> Records { get; set; } = [];
}
