namespace Trackarr.Core.Server;

public record CommandRecord(int Id, string Name, string Status, CommandBody? Body);

public record CommandBody(List<int>? MovieIds, int? SeriesId, List<int>? EpisodeIds);
