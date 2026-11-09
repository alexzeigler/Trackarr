namespace Trackarr.Integrations.Media.Arr.Dtos;

/// <param name="Message">The server's summary, such as "Completed. 0 reports downloaded.", when it has one.</param>
public record CommandRecord(int Id, string Name, string Status, CommandBody? Body, string? Message = null);

public record CommandBody(List<int>? MovieIds, int? SeriesId, List<int>? EpisodeIds);
