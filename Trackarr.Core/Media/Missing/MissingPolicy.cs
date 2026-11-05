namespace Trackarr.Core.Media.Missing;

public class MissingPolicy
{
    /// <summary>Cooldown after the first search. Doubles with each failed search, up to <see cref="MaxCooldown" />.</summary>
    public TimeSpan SearchCooldown { get; set; } = TimeSpan.FromHours(6);

    public TimeSpan MaxCooldown { get; set; } = TimeSpan.FromDays(7);
    public int MaxFailedSearches { get; set; } = 3;

    /// <summary>Series with more missing episodes than this keep searching at series level instead of per episode.</summary>
    public int MaxEpisodeCascade { get; set; } = 3;

    public int AbandonAfter { get; set; } = 5;

    /// <summary>Most items searched per server in one run. Items over the cap wait for a later run.</summary>
    public int MaxSearchesPerRun { get; set; } = 10;

    /// <summary>Abandoned items get a fresh set of searches after this long. Zero keeps them abandoned.</summary>
    public TimeSpan RetryAbandonedAfter { get; set; } = TimeSpan.FromDays(30);

    /// <summary>How long to wait after a search before counting it as failed and searching again.</summary>
    public TimeSpan GetCooldown(int failedSearchCount)
    {
        var doublings = Math.Clamp(failedSearchCount, 0, 16);
        var ticks = SearchCooldown.Ticks * (1L << doublings);
        return ticks <= 0 || ticks > MaxCooldown.Ticks ? MaxCooldown : TimeSpan.FromTicks(ticks);
    }
}
