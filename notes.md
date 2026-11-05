# v5.2.0-alpha

## New Features

### Server Health Monitoring
- Servers that fail a heartbeat are now automatically disabled. The heartbeat continues probing them in the background and re-enables them once they respond again, triggering a full sync on recovery.
- Each heartbeat tick records a ping sample (response time + success/fail). Uptime percentage is computed from the full sample history and displayed in the Servers table with color-coded indicators (green / blue / yellow / red).
- New `/servers/uptime` page per server: shows current status, uptime % for the selected time window (24h / 7d / 30d), response time chart, and a paginated health events log.
- Remote health messages from Radarr/Sonarr (`/api/v3/health`) are surfaced on the uptime page.
- Status badge on the Servers table now shows a tooltip with the most recent health event.

## Improvements

### Queue Refresh
- New queue items are now health-evaluated immediately on refresh instead of waiting for the next standalone health pass.

### N+1 Query Elimination
- Queue refreshes now use the movie/series/episode data already embedded in the Radarr/Sonarr queue response instead of issuing a separate API fetch per item.
- Missing episodes sync uses embedded series data from the wanted/missing response instead of fetching each series by ID. The follow-up series-episodes sync per series has also been removed.
- Missing service series availability check now reads from the local DB instead of hitting the live API.

### Servers Page
- Added Uptime column (sortable) to the server table.
- "Edit" moved into the actions dropdown; primary button is now "Uptime" (links to the uptime page).
- Action errors (test connection, toggle enabled) are now shown inline as an alert instead of being silently swallowed.
- "Sync All" replaced by a unified `SyncServerAsync` path used by both the UI and heartbeat recovery.

### Database Cleanup
- Cleanup routine now prunes old `ServerHealthEvent` and `ServerStatusSample` records.
- New retention settings: `ServerHealthEventRetentionDays` (default 90) and `ServerStatusSampleRetentionDays` (default 14).

## Bug Fixes

- **Settings reset (`PRAGMA foreign_keys = OFF`)** — the connection was implicitly closed and reopened between statements, causing `PRAGMA foreign_keys = OFF` to expire before the DELETE statements ran. The connection is now held open for the duration of the reset to keep the pragma in effect.
- **Server update/add failure on unreachable host** — metadata fetch after saving a server's config no longer throws and rolls back the save; the error is logged as a warning and the save succeeds.
- **Settings pages not marking dirty on load** — `HealthPolicySettings` and `MaintenanceSettings` now call `Context.NotifyChanged()` on init so the parent layout correctly reflects unsaved state.
- **Health policy threshold inputs** — threshold fields now render with colored borders matching their severity band (green / blue / yellow / red) for easier visual alignment.

## Database
- New migration: `AddServerHealthMonitoring` — adds `ServerHealthEvents` and `ServerStatusSamples` tables, and `DisabledByHeartbeat` / `UptimePercent` columns on `MediaServers`.
