namespace Trackarr.Core.Media.Missing;

public class ActiveSearches
{
    public HashSet<int> MovieIds { get; } = [];
    public HashSet<int> SeriesIds { get; } = [];
    public HashSet<int> EpisodeIds { get; } = [];
}
