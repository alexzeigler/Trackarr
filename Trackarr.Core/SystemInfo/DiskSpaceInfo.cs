namespace Trackarr.Core.SystemInfo;

public sealed class DiskSpaceInfo
{
    public required string Location { get; init; }
    public long FreeBytes { get; init; }
    public long TotalBytes { get; init; }

    public double UsedPercent => TotalBytes > 0 ? (double)(TotalBytes - FreeBytes) / TotalBytes * 100 : 0;
}
