using Trackarr.Core.Server;
using Trackarr.Integrations.Media.Arr.Dtos;

namespace Trackarr.Integrations.Media;

/// <summary>
///     Reads from a Radarr or Sonarr server (ROADMAP 4.5). Stateless: no database, no caching. Every failure is a
///     <see cref="Core.Exception.MediaServerException" />: <see cref="Core.Exception.ConnectionException" /> when the
///     server can't be reached, <see cref="Core.Exception.ApiException" /> when it answers with an error status.
/// </summary>
public interface IMediaServerReader
{
    MediaServer Server { get; }

    /// <summary>A light call to /system/status that only reads the headers: is it up, and is the API key accepted?</summary>
    Task<ConnectionCheck> CheckConnectionAsync(CancellationToken ct = default);

    Task<ServerMetadata> GetSystemStatusAsync(CancellationToken ct = default);
    Task<IReadOnlyList<RemoteHealthMessage>> GetHealthAsync(CancellationToken ct = default);
    Task<IReadOnlyList<QualityProfile>> GetQualityProfilesAsync(CancellationToken ct = default);

    /// <summary>One page of the queue, with the movie, series and episode included.</summary>
    Task<QueueResponse> GetQueuePageAsync(int page, int pageSize, CancellationToken ct = default);

    /// <summary>Every command the server knows about: queued, running and recently finished.</summary>
    Task<IReadOnlyList<CommandRecord>> GetCommandsAsync(CancellationToken ct = default);

    Task<CommandRecord?> GetCommandAsync(int id, CancellationToken ct = default);

    /// <summary>True when the history has an import for this download ID.</summary>
    Task<bool> HasImportedAsync(string downloadId, CancellationToken ct = default);

    /// <summary>False when the movie or series is gone (404). Other errors throw.</summary>
    Task<bool> MediaExistsAsync(int remoteId, CancellationToken ct = default);
}

/// <param name="Elapsed">How long the server took to answer.</param>
/// <param name="ReasonPhrase">The status line's text, such as "OK".</param>
public sealed record ConnectionCheck(TimeSpan Elapsed, string? ReasonPhrase);
