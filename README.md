# Trackarr

A self-hosted companion app for [Radarr](https://radarr.video/) and [Sonarr](https://sonarr.tv/) that gives you a unified view of your media automation — downloads, queue health, missing media, and what needs your attention.

> **Alpha software** — Trackarr is in early development. Expect breaking changes, incomplete features, and
> rough edges. It connects to live Radarr/Sonarr instances and can remove downloads and change quality profiles. Turn
> on **Passive Mode** (Settings > Servers) to watch without it changing anything. Use at your own risk.

## Features

**Queue monitoring** — See active downloads across all your Radarr and Sonarr instances in one place. Track download
progress, status changes, and stalled or failing items.

**Health scoring** — Every download gets a health score (0–100) based on how it's behaving. Stalls, errors and lack of
progress lower it; recoveries and steady progress raise it. Downloads that are just waiting (queued, paused, waiting to
import) aren't penalised. Configurable thresholds can flag or automatically remove unhealthy downloads, and you can
**protect** any download so it's never removed.

**Missing media tracking** — Finds movies and episodes missing from your library and searches for them at a pace your
indexers can handle: a limited number per run, longer waits after each unsuccessful search, and a fresh try later for
items it gave up on. It can drop to a fallback quality profile to find something, and restores your original profile
once the item downloads.

**Multi-server support** — Connect multiple Radarr and Sonarr instances, over HTTP or HTTPS, directly or through a
reverse proxy. Each server gets its own health checks, uptime history and settings.

**Server health and uptime** — Heartbeat checks track each server's response time and uptime, surface Radarr/Sonarr's
own health warnings, and automatically pause and resume servers that go offline.

**Background routines** — Scheduled jobs handle heartbeats, queue refreshes, health checks, missing media searches and
database cleanup. Each routine keeps its own run history and can be paused, run by hand or rescheduled.

**Download history** — Browse past download attempts with event-level detail to understand what happened and why.

## Installation

### Docker Compose (recommended)

```yaml
services:
  trackarr:
    container_name: trackarr
    image: confinedfish/trackarr:latest
    restart: unless-stopped
    ports:
      - "8585:8585"
    volumes:
      - ./docker-data/trackarr:/config
    environment:
      TZ: America/New_York   # your time zone
      PUID: 1000             # optional: user/group that owns /config (default 99:100)
      PGID: 1000
```

```bash
docker compose up -d
```

Trackarr will be available at `http://localhost:8585`. The database, settings, logs and crash reports are stored in `./docker-data/trackarr/`.

### Docker CLI

```bash
docker run -d \
  --name trackarr \
  --restart unless-stopped \
  -p 8585:8585 \
  -v /path/to/config:/config \
  -e TZ=America/New_York \
  confinedfish/trackarr:latest
```

### Unraid

1. In the Unraid Docker tab, click **Add Container**
2. Set **Repository** to `confinedfish/trackarr:latest`
3. Under **Template**, you can use the [Unraid template XML](deploy/unraid/trackarr.xml) from this repo
4. Check the port (default `8585`), appdata path (default `/mnt/user/appdata/trackarr`) and time zone
5. Click **Apply**

Unraid will automatically detect when a new image is available on Docker Hub and offer to update the container.

### Port, URL base and reverse proxies

The port, bind address, URL base (e.g. `/trackarr`) and reverse proxy settings live in **Settings > Network**, like
Radarr and Sonarr, not in Docker variables. They apply after a restart. If you change the port, change the container
side of the port mapping to match.

Behind a reverse proxy, turn on **Trust forwarded headers**. Leave **Trusted Proxies** empty to trust proxies on
private networks (the usual Docker or LAN setup), or list your proxy's IP or range.

Upgrading from an older template that set `ASPNETCORE_URLS`? It still works and overrides the port setting. Remove it
(and update the port mapping) to use the Network settings.

### Connecting to Radarr/Sonarr over HTTPS

- **Through a reverse proxy** (e.g. `https://example.com/radarr`): paste the full URL into the host field and Trackarr
  fills in HTTPS, port 443 and the URL base.
- **Radarr/Sonarr's own SSL** (Settings > General > Use SSL, port 9898 by default): turn on **Use SSL**, set the port,
  and turn on **Skip certificate validation**, since that certificate is self-signed.

If a connection fails, the error now says why (certificate problem, connection refused, host not found, timeout).

### Updating

A new image is published to Docker Hub only when there's a release. To update:

- **Docker Compose:** `docker compose pull && docker compose up -d`
- **Docker CLI:** `docker pull confinedfish/trackarr:latest` and recreate the container
- **Unraid:** Click the update notification in the Docker tab

`latest` is always the newest release. To stay on one major version, use its number instead (for example
`confinedfish/trackarr:6`), or pin an exact version such as `6.0.1`. Release notes are on the
[Releases](https://github.com/alexzeigler/Trackarr/releases) page and in Trackarr under System > Releases.

### First-time setup

1. Open Trackarr in your browser and create a login. Every page needs it from then on.
2. Go to **Servers** and add your Radarr/Sonarr instances (you'll need the host, port, and API key for each)
3. Background routines will start syncing queue and missing media data automatically

**Locked out?** Set the environment variable `TRACKARR_RESET_AUTH=true` and restart the container. The login is
removed (servers, settings and history are kept) and the next visit shows the setup page again. Remove the variable
straight away, or every restart removes the login.

**No login at home?** Settings > Security > **Local Network Access** skips the login for visitors on your local
networks, like "Disabled for local addresses" in Radarr and Sonarr. Only turn it on when Trackarr's port isn't
reachable from the internet. Behind a reverse proxy, also turn on **Trust forwarded headers** (Settings > Network).
Until you do, every visit through the proxy needs the login, because Trackarr can't tell where it came from. The page
shows the address Trackarr sees for your browser and whether that counts as local.

## Configuration

Trackarr stores its settings in the database and exposes them through the UI. Key configuration areas:

| Category | Examples |
|---|---|
| **General** | App name, routine scheduling, default server type |
| **Network** | Port, bind address, URL base, reverse proxy trust, outgoing proxy |
| **Server defaults** | Queue monitoring, auto-remove, Passive Mode, missing media search, connection timeout |
| **Health policy** | Starting score, penalty/reward weights, removal thresholds, grace periods, recovery protection |
| **Missing policy** | Search cooldown, searches per run, failed searches before a fallback profile, episode cascade, abandon and retry |
| **Display** | Date/time format, table page sizes, chart visibility, table density |

Each server can also override the defaults (timeout, queue monitoring, auto-remove, missing search) and has its own
fallback quality profile, URL base and HTTPS options.

## Development

**Requirements:** [.NET 10.0 SDK](https://dotnet.microsoft.com/download)

```bash
git clone https://github.com/alexzeigler/Trackarr.git
cd Trackarr
dotnet run --project Trackarr/Trackarr.csproj
```

The development launch profile starts the app at `http://localhost:5264` (it sets `ASPNETCORE_URLS`, which overrides
the port in Settings > Network). Without it, Trackarr listens on port 8585. The database is created automatically on
first run, in `Trackarr.Data/` during development.

**Running tests:**

```bash
dotnet test
```

**Branches and releases:** work goes into `develop` through pull requests, and releases are version tags on
`master`. See [docs/RELEASING.md](docs/RELEASING.md).

**Mock server:** the maintainer tests against real Radarr/Sonarr data with the Trackarr MockServer, which lives in a
separate private repository. It forwards reads to the live servers and intercepts searches, removals and other writes,
so Trackarr can be pointed at it without changing anything.

### Project structure

```
Trackarr/              Web app: Blazor Server UI, startup, hosting and network settings
Trackarr.Core/         Domain models: media, health, queue, routines, settings
Trackarr.Data/         EF Core DbContext and migrations (SQLite)
Trackarr.Services/     Business logic: Radarr/Sonarr API client, health scoring, missing search
Trackarr.Routines/     Background jobs (heartbeat, queue, evaluation, missing media, cleanup)
Trackarr.Tests/        Unit and integration tests (xUnit)
deploy/unraid/         Unraid container template
```

### Tech stack

- .NET 10 / ASP.NET Core / Blazor Server
- SQLite via Entity Framework Core
- Tabler and Bootstrap for the UI, ApexCharts for charts
- xUnit for testing

## Contributing

Trackarr is not yet set up for outside contributions. Once the first alpha builds are more stable, bug reports, testing
feedback, and setup experience reports will be the most useful.

## Built with AI

Trackarr is developed with heavy use of AI. A large share of the code — including most of the v5.2 reliability fixes,
the network and HTTPS work, the cleanup, and many of the tests — was written by Claude, an AI model made by Anthropic,
working in Claude Code. The maintainer decides what gets built, directs and reviews the changes, tests them against real
Radarr and Sonarr setups, and makes every release.

That shapes how much you should trust it:

- AI-written code can contain mistakes that look correct. There's an automated test suite (around 570 tests), but tests
  only cover what someone thought to check.
- Trackarr can remove downloads, blocklist releases and change quality profiles on your Radarr and Sonarr. Start with
  **Passive Mode** on and watch what it would do before letting it act.
- If something looks wrong, please open an issue. Reports from real setups are what catch the problems tests miss.

## Disclaimer

Trackarr is an independent project, not affiliated with Radarr, Sonarr, Anthropic, or any download client.
