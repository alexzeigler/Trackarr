# v5.2.0-alpha

## Before You Upgrade

- **Download health history is reset once.** Earlier versions could link a download on one server to a movie or series on another server of the same type, and those records can't be repaired. All download health history (attempts, scores, events, summaries) is cleared on first start. Missing-media records, search history, servers and settings are kept.
- **Deleting a server now deletes its data.** Its movies, series, episodes, quality profiles and download history go with it. Leftovers from servers deleted in earlier versions are cleaned up on first start.
- **Docker now runs as `PUID`/`PGID` instead of root** (default `99:100`, Unraid's nobody:users). Ownership of `/config` is fixed automatically on start. Set `PUID`/`PGID` if you use different IDs.
- **Logs and crash reports move to `/config/logs` and `/config/crashes`** (next to the database). Before, they were written inside the container and lost on every update.
- **Set `TZ`** (e.g. `America/New_York`) so times show in your time zone. The Unraid template has a new Time Zone field. Without it, times show in UTC.
- **Request Timeout is now actually used.** The default is 30 seconds, so an unreachable server takes longer to detect than before. Lower it under Settings > Servers if you want faster detection.

## New Features

### Server Health Monitoring
- Servers that fail a heartbeat are now automatically disabled. The heartbeat continues probing them in the background and re-enables them once they respond again, triggering a full sync on recovery.
- Each heartbeat tick records a ping sample (response time + success/fail). Uptime percentage is computed from the full sample history and displayed in the Servers table with color-coded indicators (green / blue / yellow / red).
- New `/servers/uptime` page per server: shows current status, uptime % for the selected time window (24h / 7d / 30d), response time chart, and a paginated health events log.
- Remote health messages from Radarr/Sonarr (`/api/v3/health`) are surfaced on the uptime page.
- Status badge on the Servers table now shows a tooltip with the most recent health event.

### Protect Downloads
- New **Protect / Unprotect** action on queue rows and in the details modal. A protected download is still scored but is never removed automatically.

### Evaluate Is Now a Preview
- **Evaluate** and **Evaluate All** show the score, state and whether the download would be removed (and if not, why), without recording anything. Clicking them repeatedly can no longer push a download toward removal.

### Quality Profiles Are Restored After Import
- When Trackarr lowers a movie's or series' quality profile to the fallback, it remembers the original and puts it back once the item imports or is unmonitored. A profile you changed by hand in the meantime is left alone.

### Docker
- New `/health` endpoint and a Docker `HEALTHCHECK`, so Docker and Unraid can show whether the app is up.
- `PUID`, `PGID` and `TZ` settings (see Before You Upgrade).

## Improvements

### Missing Media Search
- Searches are capped per server per run (new **Searches Per Run** setting, default 10). Items over the cap wait for the next run; they aren't counted as failures.
- All movies due in a run are searched with one Radarr command, and all episodes with one Sonarr command, instead of one command each.
- **Search Cooldown** is now set in hours (default 6) and doubles after each failed search, up to 7 days. With the defaults the quality profile is lowered after about 1.5 days and an item is abandoned after about 8 days (was 45 and 75 minutes).
- Abandoned items get a fresh round of searches after **Retry Abandoned After** days (default 30, 0 turns it off). Items abandoned before this version are retried right away.
- **Episode Cascade** now works: series with more missing episodes than the limit keep using one series search instead of per-episode searches.
- Passive Mode now also stops missing searches and quality profile changes.

### Queue Health
- Downloads that are queued, paused, held by a delay profile, or finished and waiting on import are no longer scored, so they can't be removed and blocklisted for "no progress".
- If a server's download client is unavailable, that server is skipped for the run instead of its whole queue scoring as stalled.
- Sonarr downloads are tracked per episode, so one bad episode no longer puts a failure on every other episode of the series. The series summary still shows the total.
- A download has to be missing from the queue on two scans in a row before it's marked Disappeared.
- A download only shows as "Remove" when it will actually be removed. Passive Mode and Auto Remove being off are now listed as the reason it won't be.
- Removing a download yourself no longer counts as a Trackarr removal or toward the stuck flag.
- Recovery protection now counts scans, as the setting says (it was using minutes).
- Bad Scan Decay now holds for as long as the good scan stays in the window.
- The rolling window counts scans, not stored events.

### Settings That Now Do What They Say
- **Request Timeout** (global and per server) is used for every API call.
- **Monitor Queue = off** also stops evaluation and removals on that server.
- **Auto Remove = off** also keeps blacklist matches in the queue.
- **Cleanup Interval** applies as soon as you save, without a restart.
- Routine **Enabled** and **Interval** changes now survive a restart.
- **Run routines on schedule** (General, was "Auto-start routines on launch") applies immediately, and turning it off no longer blocks Run buttons and other manual runs.
- Speed and ETA rewards were never used by the scorer and have been removed from the Health Policy page.

### Queue Refresh
- New queue items are now health-evaluated immediately on refresh instead of waiting for the next standalone health pass.

### Performance
- Queue refreshes use the movie/series/episode data already embedded in the Radarr/Sonarr queue response instead of one API call per item.
- Missing episodes sync uses embedded series data from the wanted/missing response, and the per-series episode sync after it has been removed.
- Missing service series availability check reads from the local database instead of the live API.
- The Queue page loads health data for the whole page in one batch instead of three database calls per download, and no longer writes to the database while reading.
- Missing-media and episode syncs look up existing rows in one query instead of one per row.
- Open tabs refresh at most once per burst of changes instead of once per evaluated item.
- Database cleanup only checks downloads with new events since the last run.

### Servers Page
- Added Uptime column (sortable) to the server table.
- "Edit" moved into the actions dropdown; primary button is now "Uptime" (links to the uptime page).
- Action errors (test connection, toggle enabled, delete) are now shown inline as an alert instead of being silently swallowed.
- "Sync All" replaced by a unified `SyncServerAsync` path used by both the UI and heartbeat recovery.

### Database Cleanup
- Cleanup routine now prunes old `ServerHealthEvent` and `ServerStatusSample` records.
- New retention settings: `ServerHealthEventRetentionDays` (default 90) and `ServerStatusSampleRetentionDays` (default 14).
- Old log files are pruned using the log retention setting, and crash reports after 90 days.

## Bug Fixes

- **Sidebar badges ran the missing-media search** every minute for each open tab. Badges now read stored counts.
- **Two servers of the same type cross-linked media** (e.g. a 1080p and a 4K Radarr), putting failures and penalties on the wrong titles.
- **One offline server stopped queue refresh and evaluation for every server.** Each server, and each removal, now fails on its own.
- **Routine runs with errors showed as successful** on the Routines page. Partial failures now show as Failed.
- **Servers page crashed** when deleting a server failed. Unexpected page errors now show inline instead of ending the session.
- **A rejected API key still showed as authenticated** after Test Connection.
- **Saving a server rolled back fresh status** (ping, queue counts, errors) written since the page loaded.
- **Overlapping queue refreshes left duplicate rows**, and a read during a refresh could see an empty queue.
- **Queue page merged or hid items without a download ID** (e.g. pending releases). Same in the details modal.
- **History page crashed** on leftover rows from deleted servers.
- **Duplicate movie/series missing records** could be created.
- **A failure partway through a missing run** repeated searches and profile changes on the next run.
- **The Routines page refresh loop never stopped**, adding a database poll every 2 seconds per visit.
- **Settings reset (`PRAGMA foreign_keys = OFF`)** — the connection was implicitly closed and reopened between statements, causing `PRAGMA foreign_keys = OFF` to expire before the DELETE statements ran. The connection is now held open for the duration of the reset to keep the pragma in effect.
- **Server update/add failure on unreachable host** — metadata fetch after saving a server's config no longer throws and rolls back the save; the error is logged as a warning and the save succeeds.
- **Settings pages not marking dirty on load** — `HealthPolicySettings` and `MaintenanceSettings` now call `Context.NotifyChanged()` on init so the parent layout correctly reflects unsaved state.
- **Health policy threshold inputs** — threshold fields now render with colored borders matching their severity band (green / blue / yellow / red) for easier visual alignment.
- **An invalid blacklist regex was silently skipped.** It's now logged as a warning.

## Housekeeping

- Removed 178 MB of unused front-end files from the image, the empty `Trackarr.Client` project and the unused Blazor.Bootstrap package.
- CDN versions are pinned (Tabler Icons 3.50.0, ApexCharts 8.7.0) so an upstream release can't change the UI.
- Package versions are managed in one place (`Directory.Packages.props`).
- SQLite no longer uses shared cache, which could cause lock errors with WAL.

## Database

New migrations, applied automatically on start:
- `AddServerHealthMonitoring` — adds `ServerHealthEvents` and `ServerStatusSamples` tables, and `DisabledByHeartbeat` / `UptimePercent` columns on `MediaServers`.
- `ResetHealthHistory` — one-time reset of download health history (see Before You Upgrade).
- `AddMissingOriginalProfile` — stores a quality profile's original value before it's lowered.
- `AddAttemptMissedScans` — counts missed scans before a download is marked Disappeared.
- `AddScanCountAndMissingIndex` — scan counts on health events; removes duplicate missing records and adds a unique index.
- `CascadeServerDelete` — deleting a server deletes its data; cleans up leftovers from past deletions.
- `RelabelMovieMissingTier` — movie missing records get a "Movie" tier instead of "Series".
