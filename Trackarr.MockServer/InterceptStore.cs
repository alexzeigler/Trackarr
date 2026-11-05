using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;

namespace Trackarr.MockServer;

public class MockCommand
{
    public int Id { get; set; }
    public MockMode Mode { get; set; }
    public string Name { get; set; } = "";
    public DateTime QueuedAt { get; set; }
    public JsonElement Body { get; set; }
    public List<int> TargetIds { get; set; } = [];
    public bool WillSucceed { get; set; }

    public string Status
    {
        get
        {
            var elapsed = (DateTime.UtcNow - QueuedAt).TotalSeconds;
            return elapsed < 2 ? "queued" : elapsed < 6 ? "started" : "completed";
        }
    }
}

/// <summary>A DELETE /api/v3/queue/{id} that Trackarr sent and we swallowed.</summary>
public class RemovalAttempt
{
    public MockMode Mode { get; set; }
    public int QueueId { get; set; }
    public string Title { get; set; } = "";
    public string? Media { get; set; }
    public string? Status { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime FirstAttempt { get; set; }
    public DateTime LastAttempt { get; set; }
    public int Attempts { get; set; }
    public bool Blocklist { get; set; }
    public bool RemoveFromClient { get; set; }

    /// <summary>When the item last showed up in a live queue response (null = never seen).</summary>
    public DateTime? LastSeenLive { get; set; }

    /// <summary>The live queue record at the time of the attempt.</summary>
    public string? Snapshot { get; set; }
}

/// <summary>Any other write (PUT movie, POST to an unknown endpoint, ...) that was logged instead of forwarded.</summary>
public record InterceptedWrite(DateTime Timestamp, MockMode Mode, string Method, string Path, string? Body);

/// <summary>
///     Holds everything the mock answers itself instead of forwarding: search commands (random outcome),
///     swallowed queue removals (hidden from later queue responses), and other logged writes.
/// </summary>
public class InterceptStore
{
    private const double BaseSearchSuccessRate = 0.85;
    private const double ProfileChangedSearchSuccessRate = 0.95;
    private const int MaxHistory = 200;

    private readonly List<MockCommand> _commands = [];
    private readonly object _lock = new();
    private readonly HashSet<(MockMode, int)> _profileChangedItems = [];
    private readonly Dictionary<(MockMode, int), JsonObject> _queueSnapshots = [];
    private readonly Dictionary<(MockMode, int), RemovalAttempt> _removals = [];
    private readonly Random _rng = new();
    private readonly List<InterceptedWrite> _writes = [];
    private int _nextCommandId = 1;

    /// <summary>Re-pointed at each server instance's logger, since this store outlives in-process restarts.</summary>
    public ILogger Logger { get; set; } = NullLogger.Instance;

    private ILogger logger => Logger;

    // ── Search commands ──

    public object CreateCommand(JsonElement body, MockMode mode)
    {
        var name = body.TryGetProperty("name", out var n) ? n.GetString() ?? "Unknown" : "Unknown";

        lock (_lock)
        {
            var cmd = new MockCommand
            {
                Id = _nextCommandId++,
                Mode = mode,
                Name = name,
                QueuedAt = DateTime.UtcNow,
                Body = body.Clone()
            };

            if (name is "MoviesSearch" && body.TryGetProperty("movieIds", out var mIds))
                cmd.TargetIds = mIds.EnumerateArray().Select(e => e.GetInt32()).ToList();
            else if (name is "EpisodeSearch" && body.TryGetProperty("episodeIds", out var eIds))
                cmd.TargetIds = eIds.EnumerateArray().Select(e => e.GetInt32()).ToList();
            else if (name is "SeriesSearch" && body.TryGetProperty("seriesId", out var sId))
                cmd.TargetIds = [sId.GetInt32()];

            if (cmd.TargetIds.Count > 0)
            {
                cmd.WillSucceed = cmd.TargetIds.Any(id => _rng.NextDouble() < GetSearchRate(mode, id));
                logger.LogInformation("[{Mode}] Search (not forwarded): {Name} [{Ids}] → {Result}", mode, name,
                    string.Join(",", cmd.TargetIds), cmd.WillSucceed ? "found" : "missed");
            }
            else
            {
                logger.LogInformation("[{Mode}] Command (not forwarded): {Name}", mode, name);
            }

            _commands.Add(cmd);
            if (_commands.Count > MaxHistory) _commands.RemoveRange(0, _commands.Count - MaxHistory);

            return SerializeCommand(cmd);
        }
    }

    private double GetSearchRate(MockMode mode, int itemId)
    {
        return _profileChangedItems.Contains((mode, itemId)) ? ProfileChangedSearchSuccessRate : BaseSearchSuccessRate;
    }

    public List<object> GetCommands(MockMode mode)
    {
        lock (_lock) return _commands.Where(c => c.Mode == mode).Select(SerializeCommand).ToList();
    }

    public object? GetCommand(MockMode mode, int id)
    {
        lock (_lock)
        {
            var cmd = _commands.FirstOrDefault(c => c.Mode == mode && c.Id == id);
            return cmd is null ? null : SerializeCommand(cmd);
        }
    }

    public List<object> GetCommandsForDashboard()
    {
        lock (_lock)
        {
            return _commands.AsEnumerable().Reverse().Select(c => (object)new
            {
                c.Id,
                Mode = c.Mode.ToString(),
                c.Name,
                c.TargetIds,
                c.Status,
                Queued = c.QueuedAt,
                Outcome = c.TargetIds.Count == 0 ? null : c.WillSucceed ? "found" : "missed"
            }).ToList();
        }
    }

    private static object SerializeCommand(MockCommand cmd)
    {
        return new
        {
            cmd.Id,
            cmd.Name,
            CommandName = cmd.Name switch
            {
                "MoviesSearch" => "Movies Search",
                "EpisodeSearch" => "Episode Search",
                "SeriesSearch" => "Series Search",
                _ => cmd.Name
            },
            cmd.Status,
            Queued = cmd.QueuedAt,
            Started = cmd.Status is "started" or "completed" ? cmd.QueuedAt.AddSeconds(2) : (DateTime?)null,
            Ended = cmd.Status is "completed" ? cmd.QueuedAt.AddSeconds(6) : (DateTime?)null,
            cmd.Body,
            // Real Radarr/Sonarr end a search with this message; Trackarr reads the count to log what was found.
            Message = cmd.Status is "completed" && cmd.TargetIds.Count > 0
                ?
                $"{cmd.Name switch { "MoviesSearch" => "Movie", "SeriesSearch" => "Series", _ => "Episode" }} search completed. {(cmd.WillSucceed ? 1 : 0)} reports downloaded."
                : cmd.Status is "completed"
                    ? "Completed"
                    : null,
            Priority = "normal",
            Trigger = "manual"
        };
    }

    // ── Queue ──

    /// <summary>
    ///     Called with every proxied queue page: remembers each record (so a later DELETE can show what
    ///     it targeted) and strips items Trackarr already "removed", so it sees the removal take effect.
    /// </summary>
    public string FilterQueueResponse(MockMode mode, string body)
    {
        JsonObject? root;
        try
        {
            root = JsonNode.Parse(body)?.AsObject();
        }
        catch
        {
            return body;
        }

        if (root?["records"] is not JsonArray records) return body;

        lock (_lock)
        {
            var hidden = 0;
            for (var i = records.Count - 1; i >= 0; i--)
            {
                if (records[i] is not JsonObject rec || rec["id"]?.GetValue<int>() is not { } id) continue;

                _queueSnapshots[(mode, id)] = (JsonObject)rec.DeepClone();
                if (!_removals.TryGetValue((mode, id), out var removal)) continue;

                removal.LastSeenLive = DateTime.UtcNow;
                records.RemoveAt(i);
                hidden++;
            }

            if (hidden > 0 && root["totalRecords"] is JsonValue total && total.TryGetValue(out int t))
                root["totalRecords"] = Math.Max(0, t - hidden);

            // Snapshots only need to cover items still around; cap so long runs don't grow forever.
            if (_queueSnapshots.Count > 5000) _queueSnapshots.Clear();
        }

        return root.ToJsonString();
    }

    public bool HasSnapshot(MockMode mode, int queueId)
    {
        lock (_lock) return _queueSnapshots.ContainsKey((mode, queueId));
    }

    public void AddSnapshots(MockMode mode, JsonArray records)
    {
        lock (_lock)
        {
            foreach (var rec in records.OfType<JsonObject>())
            {
                if (rec["id"]?.GetValue<int>() is { } id)
                    _queueSnapshots[(mode, id)] = (JsonObject)rec.DeepClone();
            }
        }
    }

    public void RecordRemoval(MockMode mode, int queueId, bool blocklist, bool removeFromClient)
    {
        lock (_lock)
        {
            var now = DateTime.UtcNow;
            if (!_removals.TryGetValue((mode, queueId), out var removal))
            {
                removal = new RemovalAttempt { Mode = mode, QueueId = queueId, FirstAttempt = now };
                _removals[(mode, queueId)] = removal;
            }

            removal.LastAttempt = now;
            removal.Attempts++;
            removal.Blocklist = blocklist;
            removal.RemoveFromClient = removeFromClient;

            if (_queueSnapshots.TryGetValue((mode, queueId), out var snap))
            {
                removal.Title = snap["title"]?.GetValue<string>() ?? $"Queue item {queueId}";
                removal.Media = DescribeMedia(snap);
                removal.Status = snap["trackedDownloadState"]?.GetValue<string>() ?? snap["status"]?.GetValue<string>();
                removal.ErrorMessage = snap["errorMessage"]?.GetValue<string>() ?? FirstStatusMessage(snap);
                removal.Snapshot = snap.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
                removal.LastSeenLive ??= now;
            }
            else if (removal.Title.Length == 0)
            {
                removal.Title = $"Queue item {queueId} (not found in live queue)";
            }

            logger.LogInformation(
                "[{Mode}] Queue removal (not forwarded): #{Id} {Title} blocklist={Blocklist} removeFromClient={Rfc}",
                mode, queueId, removal.Title, blocklist, removeFromClient);
        }
    }

    public List<RemovalAttempt> GetRemovals()
    {
        lock (_lock) return _removals.Values.OrderByDescending(r => r.LastAttempt).ToList();
    }

    public bool RestoreRemoval(MockMode mode, int queueId)
    {
        lock (_lock) return _removals.Remove((mode, queueId));
    }

    public void ClearRemovals()
    {
        lock (_lock) _removals.Clear();
    }

    private static string? DescribeMedia(JsonObject rec)
    {
        if (rec["movie"] is JsonObject movie)
            return $"{movie["title"]?.GetValue<string>()} ({movie["year"]?.GetValue<int>()})";

        if (rec["series"] is JsonObject series)
        {
            var title = series["title"]?.GetValue<string>();
            return rec["episode"] is JsonObject ep
                ? $"{title} S{ep["seasonNumber"]?.GetValue<int>() ?? 0:D2}E{ep["episodeNumber"]?.GetValue<int>() ?? 0:D2}"
                : title;
        }

        return null;
    }

    private static string? FirstStatusMessage(JsonObject rec)
    {
        return rec["statusMessages"] is JsonArray msgs
            ? msgs.OfType<JsonObject>().SelectMany(m => m["messages"] as JsonArray ?? [])
                .Select(m => m?.GetValue<string>()).FirstOrDefault(m => !string.IsNullOrEmpty(m))
            : null;
    }

    // ── Other writes ──

    public void RecordWrite(MockMode mode, string method, string path, string? body)
    {
        lock (_lock)
        {
            _writes.Add(new InterceptedWrite(DateTime.UtcNow, mode, method, path, body));
            if (_writes.Count > MaxHistory) _writes.RemoveRange(0, _writes.Count - MaxHistory);

            // A profile change makes the next search for that item more likely to "find" something.
            if (method == "PUT" && body is not null && TryGetPutTarget(path) is { } id)
                _profileChangedItems.Add((mode, id));
        }

        logger.LogInformation("[{Mode}] Write (not forwarded): {Method} {Path}", mode, method, path);
    }

    public List<InterceptedWrite> GetWrites()
    {
        lock (_lock) return _writes.AsEnumerable().Reverse().ToList();
    }

    private static int? TryGetPutTarget(string path)
    {
        var parts = path.Trim('/').Split('/');
        return parts.Length == 4 && parts[2] is "movie" or "series" && int.TryParse(parts[3], out var id) ? id : null;
    }

    public (int Searches, int Found, int Missed, int Removals, int Writes) GetCounts()
    {
        lock (_lock)
        {
            var searches = _commands.Where(c => c.TargetIds.Count > 0).ToList();
            return (searches.Count, searches.Count(c => c.WillSucceed), searches.Count(c => !c.WillSucceed),
                _removals.Count, _writes.Count);
        }
    }
}
