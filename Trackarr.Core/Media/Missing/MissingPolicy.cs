namespace Trackarr.Core.Media.Missing;

public class MissingPolicy
{
    public TimeSpan ScanInterval { get; set; } = TimeSpan.FromMinutes(15);
    public TimeSpan SearchCooldown { get; set; } = TimeSpan.FromMinutes(10);
    public int MaxFailedSearches { get; set; } = 3;
}