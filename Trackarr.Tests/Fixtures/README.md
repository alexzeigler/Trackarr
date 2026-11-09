# API fixtures

Small Radarr and Sonarr responses, written by hand in the shape of their v3 API, with made-up titles. Tests replay them
through [`FixtureHandler`](../FixtureHandler.cs) instead of calling a real server.

```
Fixtures/
  radarr/api_v3_queue.json
  radarr/api_v3_wanted_missing.json
  sonarr/api_v3_queue.json
  ...
```

They only need the fields Trackarr reads. When a test needs an edge case (a season pack shown as several queue rows, a
queued download with no size yet), add it to the file or write a new one. Field names and shapes come from the
official API docs: [Radarr](https://radarr.video/docs/api/) and [Sonarr](https://sonarr.tv/docs/api/).

We don't commit responses recorded from real servers: they would publish library titles, folder paths and indexer
names, and the refactors these fixtures protect only need the old and new code to read the same input the same way.

## Naming

A request path maps to a file name like this: drop everything before `/api/` (the URL base), turn number segments
into `id`, and join the segments with `_`. So `/radarr/api/v3/movie/42?includeImages=true` becomes
`api_v3_movie_id.json`. The query string isn't part of the name, so one file serves every page and filter of the same
endpoint.

## Using them in a test

```csharp
var radarr = new FixtureHandler(FixtureHandler.Radarr);
var client = new ArrClientFactory(radarr.AsFactory()).Radarr(server, TimeSpan.FromSeconds(10));

var queue = await client.GetQueuePageAsync(1, 100);

Assert.Contains("api_v3_queue.json", radarr.Requested);
```

Only GETs are replayed. To test what Trackarr sends (removals, searches, profile changes), use a handler that records
the requests instead, like the one in `ArrClientTests`.
