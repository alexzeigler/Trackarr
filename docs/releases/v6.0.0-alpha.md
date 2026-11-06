# v6.0.0-alpha

A big reliability release. Trackarr is now much more careful about what it removes and searches for, works properly
with more than one Radarr or Sonarr, handles HTTPS and reverse proxies, and is simpler to set up in Docker.

## Before You Upgrade

- **Your download health history starts fresh.** Older versions could mix up downloads between two servers of the same
  type (for example a 1080p and a 4K Radarr), and that history can't be fixed. It's cleared once, the first time v5.2
  starts. Your servers, settings, missing media and search history are kept.
- **The default port is now 8585.** If your container still has an `ASPNETCORE_URLS` variable from the old template, it
  keeps using its old port. To move to 8585, remove that variable and change your port mapping to `8585:8585`.
- **Fewer Docker settings.** Only the port, the `/config` folder, your time zone (`TZ`) and optionally `PUID`/`PGID` are
  needed. The other variables in the old template can be removed (leaving them does no harm).
- **Trackarr no longer runs as root in Docker.** It runs as user 99 / group 100 by default (Unraid's usual
  nobody:users) and fixes the ownership of your `/config` folder automatically. Set `PUID`/`PGID` if you use different
  IDs.
- **Set your time zone** with `TZ` (for example `America/New_York`) so times show in your local time instead of UTC.
- **Logs and crash reports now live in your config folder** (`logs` and `crashes`), so they survive updates.
- **Deleting a server now deletes everything that belonged to it.** Leftovers from servers deleted in earlier versions
  are cleaned up automatically.
- **The connection timeout setting now actually works.** It defaults to 30 seconds, so an offline server may take a
  little longer to be noticed. You can lower it under Settings > Servers.

## New

- **Server health and uptime.** Servers that stop responding are disabled automatically and switched back on (with a
  full sync) when they come back. A new Uptime page per server shows uptime, response times, Radarr/Sonarr's own
  health warnings, and a history of outages.
- **Network settings.** A new Settings > Network page for the port, which network address to listen on, a URL base
  (to run Trackarr at an address like `https://example.com/trackarr`), reverse proxy support, and an optional proxy for
  reaching Radarr and Sonarr.
- **HTTPS to Radarr and Sonarr.** You can now connect through a reverse proxy at a sub-path (like
  `https://example.com/radarr`), or to Radarr/Sonarr's own HTTPS with a self-signed certificate. Paste the full address
  and Trackarr fills in the details. When a connection fails, it now tells you why.
- **Protect a download.** Mark a download as protected and Trackarr will never remove it automatically.
- **Evaluate is now a preview.** It shows what Trackarr would decide for a download right now, without counting as a
  check, so clicking it can't push a download toward removal.
- **Quality profiles are put back.** When Trackarr lowers a movie's or show's quality profile to find a release, it
  now restores the original once the item downloads. If you changed the profile yourself in the meantime, it leaves
  yours alone.
- **Force Search** on the Missing page searches for an item right away.
- **Docker health check**, so Docker and Unraid can show whether Trackarr is running properly.

## Smarter Download Handling

- Downloads that are simply waiting (queued, paused, held back on purpose, or finished and waiting to be imported) are
  no longer treated as failing, so they won't be removed and blocklisted by mistake.
- If your download client is offline, Trackarr waits instead of treating the whole queue as stuck.
- TV downloads are judged per episode, so one bad episode no longer counts against the whole show.
- A download has to be missing twice in a row before Trackarr decides it disappeared.
- A download only shows "Remove" when Trackarr will actually remove it. If Passive Mode or Auto Remove is stopping it,
  you're told why.
- Removing a download yourself no longer counts as a failure.
- Downloads that recover are protected for the number of checks you set (this was accidentally measured in minutes).

## Smarter Missing Media Searches

- Trackarr searches a limited number of items per server each run (10 by default) so it doesn't flood your indexers.
- Searches go out as one request instead of one per item.
- Time between searches is now in hours (6 by default) and grows after each unsuccessful search. Items are given
  about a day and a half before the quality profile is lowered, and about a week before Trackarr stops searching.
- Items Trackarr gave up on are tried again after 30 days, since new releases show up over time.
- Passive Mode now also stops searches and quality profile changes.
- New settings for searches per run and the retry period are under Settings > Missing Media.

## Settings That Now Work As Described

- Connection timeout, Monitor Queue, Auto Remove, Recovery Protection and Bad Scan Decay now behave the way the
  settings page says.
- Turning a routine off or changing how often it runs is no longer undone by a restart.
- "Run routines on schedule" (Settings > General) applies straight away, and turning it off no longer stops the Run
  buttons.
- The cleanup interval applies as soon as you save it.
- Speed and ETA rewards were never actually used, so they've been removed from the Health Scoring page.

## Faster

- Trackarr makes far fewer calls to Radarr and Sonarr and to its own database, which matters most with large
  libraries and long queues.
- Open browser tabs refresh once per batch of changes instead of once per download.

## Fixes

- The sidebar counters no longer trigger searches in the background.
- Two Radarr or two Sonarr servers no longer mix up each other's movies and shows.
- One offline server no longer stops the queue from updating for all the others.
- Routines that partly failed now show as Failed instead of Success.
- The Servers page no longer crashes when something goes wrong, and a wrong API key now shows as not authenticated.
- Saving a server no longer undoes its latest status.
- The queue no longer shows duplicate rows or hides downloads that don't have an ID yet.
- The History page no longer crashes after a server has been deleted.
- The Routines page no longer keeps checking in the background after you leave it.
- Resetting the database works again, saving a server whose address is unreachable no longer fails, and settings
  pages correctly show unsaved changes.
- Times are shown in your time zone.
- An invalid blacklist pattern is now reported in the logs instead of being silently ignored.

## Behind the Scenes

- The Docker image is about 178 MB smaller.
- The web interface's external styles and scripts are pinned to fixed versions, so outside updates can't change how
  it looks.
- Database updates for this version run automatically the first time v5.2 starts.
