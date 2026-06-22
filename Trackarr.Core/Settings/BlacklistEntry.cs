namespace Trackarr.Core.Settings;

public class BlacklistEntry
{
    public int Id { get; set; }
    public string Pattern { get; set; } = string.Empty;
    public BlacklistType Type { get; set; }
    public bool IsRegex { get; set; }
    public string? Note { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public enum BlacklistType
{
    Indexer,
    ReleaseGroup,
    Keyword
}
