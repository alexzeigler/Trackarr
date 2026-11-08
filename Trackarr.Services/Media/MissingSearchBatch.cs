using Trackarr.Core.Media.Missing;
using Trackarr.Services.Actions;

namespace Trackarr.Services.Media;

/// <summary>
///     Searches collected during one missing-media run for one server, sent at the end as as few commands as
///     possible (one MoviesSearch, one EpisodeSearch, one SeriesSearch per series). Holds at most
///     <c>capacity</c> items so a large library can't flood the indexers in a single run. It keeps the records too,
///     so each can be marked "search pushed" only if its command was really sent.
/// </summary>
public sealed class MissingSearchBatch(int capacity)
{
    private readonly List<Entry> _movies = [];
    private readonly List<Entry> _series = [];
    private readonly List<Entry> _episodes = [];

    public IReadOnlyList<int> MovieIds => Ids(_movies);
    public IReadOnlyList<int> SeriesIds => Ids(_series);
    public IReadOnlyList<int> EpisodeIds => Ids(_episodes);

    public int Count => MovieIds.Count + SeriesIds.Count + EpisodeIds.Count;
    public bool IsFull => Count >= capacity;

    /// <summary>
    ///     Adds the record's search if there's room. Returns false when the batch is full. <paramref name="label" />
    ///     names it in the action log, such as "Dune (2021)" or "Severance S02E03".
    /// </summary>
    public bool TryAdd(bool isRadarr, MissingMediaRecord record, string? label = null)
    {
        if (IsFull) return false;

        if (isRadarr)
            _movies.Add(new Entry(record.RemoteId, record, label));
        else if (record.Tier == MissingSearchTier.Series || record.EpisodeId is null)
            _series.Add(new Entry(record.RemoteId, record, label));
        else
            _episodes.Add(new Entry(record.EpisodeId.Value, record, label));

        return true;
    }

    /// <summary>The commands to send, each with the records it searches for.</summary>
    public IEnumerable<SearchCommand> Commands()
    {
        if (_movies.Count > 0) yield return Command(SearchKind.Movies, _movies);
        if (_episodes.Count > 0) yield return Command(SearchKind.Episodes, _episodes);

        foreach (var group in _series.GroupBy(e => e.Id))
            yield return Command(SearchKind.Series, group.ToList());
    }

    private static SearchCommand Command(SearchKind kind, List<Entry> entries)
    {
        return new SearchCommand(kind, Ids(entries), entries.Select(e => e.Record).ToList(), Describe(kind, entries));
    }

    /// <summary>Up to five names, then "and N more".</summary>
    private static string Describe(SearchKind kind, List<Entry> entries)
    {
        var noun = kind switch
        {
            SearchKind.Movies => "movie",
            SearchKind.Episodes => "episode",
            _ => "series"
        };
        var names = entries.Select(e => e.Label ?? $"{noun} {e.Id}").Distinct().ToList();
        return names.Count <= 5
            ? string.Join(", ", names)
            : $"{string.Join(", ", names.Take(5))} and {names.Count - 5} more";
    }

    private static List<int> Ids(List<Entry> entries)
    {
        return entries.Select(e => e.Id).Distinct().ToList();
    }

    private sealed record Entry(int Id, MissingMediaRecord Record, string? Label);
}

/// <param name="Kind">Which search command.</param>
/// <param name="Ids">Movie, episode or series ids for it.</param>
/// <param name="Records">The records it searches for.</param>
/// <param name="Description">The names, for the action log.</param>
public sealed record SearchCommand(
    SearchKind Kind,
    IReadOnlyList<int> Ids,
    IReadOnlyList<MissingMediaRecord> Records,
    string Description);
