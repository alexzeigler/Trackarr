# Recorded API fixtures

Real Radarr and Sonarr responses, recorded once and replayed in tests by
[`FixtureHandler`](../FixtureHandler.cs). They prove Trackarr still reads the APIs correctly when the HTTP code moves
(roadmap Phase 2) and catch changes in new Radarr or Sonarr versions (AUDIT T3).

```
Fixtures/
  radarr/api_v3_queue.json
  radarr/api_v3_wanted_missing.json
  sonarr/api_v3_queue.json
  ...
```

## Recording

The recordings come from the Trackarr MockServer's record mode (a separate, private repository):

1. Start the mock in record mode, pointed at this folder:

   ```powershell
   ./run-mockserver.ps1 -Record ..\Trackarr\Trackarr.Tests\Fixtures
   ```

2. Point a development copy of Trackarr at the mock (Radarr `127.0.0.1:9877`, Sonarr `127.0.0.1:9878`, with the
   mock's API key).
3. Let every routine run once, and open the Queue, Missing and Servers pages. Each response lands here.
4. **Review every file before committing.** The mock trims lists to 25 items and replaces API keys, passwords and
   tokens with `(redacted)`, but the files still contain:
   - titles from your library
   - folder paths (`path`, `rootFolderPath`, `outputPath`)
   - download client and indexer names
   - your servers' versions, operating system and start-up paths (`system/status`)

   This repository is public. Edit or replace anything you don't want published.

Re-record after a Radarr or Sonarr upgrade, and check the diff to see what changed in their API.

## Naming

A request path maps to a file name like this: drop everything before `/api/` (the URL base), turn number segments
into `id`, and join the segments with `_`. So `/radarr/api/v3/movie/42?includeImages=true` becomes
`api_v3_movie_id.json`. The query string isn't part of the name, so one recording serves every page and filter of
the same endpoint.

The MockServer's `FixtureRecorder.FileNameFor` and `FixtureHandler.FileNameFor` here must stay the same.

## Hand-written samples

`samples/{radarr,sonarr}/` holds small responses written by hand in the shape of the v3 API, with made-up titles. They
let `ArrClientTests` test the clients before any recordings exist. They are not recordings: they only contain the fields
Trackarr reads, so they can't catch a change in a real Radarr or Sonarr. Recordings go in `radarr/` and `sonarr/`
directly, never in `samples/`.

## Using them in a test

```csharp
var radarr = new FixtureHandler(FixtureHandler.Radarr);
var service = new MediaServerService(dbFactory, radarr.AsFactory(), NullLogger<MediaServerService>.Instance,
    new DataChangeBus(), healthService);

var queue = await service.GetQueueAsync(server, forceRefresh: true);

Assert.Contains("api_v3_queue.json", radarr.Requested);
Assert.NotEmpty(queue);
```

A request with no recording gets a 404 that names the file to record, and anything but `GET` gets a 405.
