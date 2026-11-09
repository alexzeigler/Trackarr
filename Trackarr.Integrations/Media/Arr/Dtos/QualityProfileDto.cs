namespace Trackarr.Integrations.Media.Arr.Dtos;

/// <summary>A quality profile as /api/v3/qualityprofile returns it. Only the fields Trackarr keeps.</summary>
public sealed record QualityProfileDto(int Id, string? Name);
