namespace Trackarr.Core.SystemInfo;

public sealed class SystemStats
{
    public int Servers { get; init; }
    public int Movies { get; init; }
    public int Series { get; init; }
    public int Episodes { get; init; }
    public int Downloads { get; init; }
    public int HealthEvents { get; init; }
}
