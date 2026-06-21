namespace Trackarr.Core.Media.Missing;

public class MissingPolicy
{
    public TimeSpan SearchCooldown { get; set; } = TimeSpan.FromMinutes(10);
    public int MaxFailedSearches { get; set; } = 3;
    public int MaxEpisodeCascade { get; set; } = 3;
    public int AbandonAfter { get; set; } = 5;
}