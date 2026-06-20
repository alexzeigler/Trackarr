namespace Trackarr.Core.Media.Missing;

public class ActiveSearches
{
    public HashSet<int> MovieIds { get; } = new();
    public HashSet<int> SeriesIds { get; } = new();
    public HashSet<int> EpisodeIds { get; } = new();
}