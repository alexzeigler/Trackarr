# Trackarr

A self-hosted companion app for [Radarr](https://radarr.video/) and [Sonarr](https://sonarr.tv/) that gives you a unified view of your media automation — downloads, queue health, missing media, and what needs your attention.

> **Alpha software** — Trackarr is in early development (v5.x-alpha). Expect breaking changes, incomplete features, and rough edges. It connects to live Radarr/Sonarr instances and there is no read-only mode yet. Use at your own risk.

## Features

**Queue monitoring** — See active downloads across all your Radarr and Sonarr instances in one place. Track download progress, status changes, and stalled or failing items.

**Health scoring** — Every download gets a health score (0–100) based on its lifecycle. Stalls, errors, and lack of progress reduce the score; recoveries and steady progress improve it. Configurable thresholds can automatically flag or remove unhealthy downloads.

**Missing media tracking** — Automatically detects movies and episodes that are missing from your library. Tracks search state, manages cooldowns between searches, and abandons items that repeatedly fail to find results.

**Multi-server support** — Connect multiple Radarr and Sonarr instances. Each server gets independent health checks, queue monitoring, and configuration.

**Background routines** — Scheduled jobs handle heartbeat checks, queue refreshes, health evaluations, missing media detection, and database cleanup. Each routine tracks its own run history and can be individually configured.

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

Images are automatically built and pushed to Docker Hub on every release. To update:

- **Docker Compose:** `docker compose pull && docker compose up -d`
- **Docker CLI:** `docker pull confinedfish/trackarr:latest` and recreate the container
- **Unraid:** Click the update notification in the Docker tab

### First-time setup

1. Open Trackarr in your browser
2. Go to **Servers** and add your Radarr/Sonarr instances (you'll need the host, port, and API key for each)
3. Background routines will start syncing queue and missing media data automatically

## Configuration

Trackarr stores its settings in the database and exposes them through the UI. Key configuration areas:

| Category | Examples |
|---|---|
| **General** | App name, routine scheduling, default server type |
| **Network** | Port, bind address, URL base, reverse proxy trust, outgoing proxy |
| **Server defaults** | Queue monitoring, auto-remove failed downloads, missing media search, request timeout |
| **Health policy** | Starting score, penalty/reward weights, removal thresholds, grace periods |
| **Missing policy** | Search cooldown, max failed searches, cascade limit, abandonment threshold |
| **Display** | Date/time format, table page sizes, chart visibility, table density |

## Development

**Requirements:** [.NET 10.0 SDK](https://dotnet.microsoft.com/download)

```bash
git clone https://github.com/alexzeigler/Trackarr.git
cd Trackarr
dotnet run --project Trackarr/Trackarr.csproj
```

The app starts at `http://localhost:5264`. The database is created automatically on first run.

**Running tests:**

```bash
dotnet test
```

### Project structure

```
Trackarr/              Web app — Blazor Server UI, startup, middleware
Trackarr.Core/         Domain models — media, health, queue, settings
Trackarr.Data/         EF Core DbContext and migrations (SQLite)
Trackarr.Services/     Business logic — API clients, health scoring, search
Trackarr.Routines/     Background job implementations
Trackarr.Client/       Blazor WebAssembly client components
Trackarr.Tests/        Unit and integration tests (xUnit)
```

### Tech stack

- .NET 10 / ASP.NET Core / Blazor Server
- SQLite via Entity Framework Core
- Blazor Bootstrap / Bootstrap
- xUnit for testing

## Contributing

Trackarr is not yet set up for outside contributions. Once the first alpha builds are more stable, bug reports, testing feedback, and setup experience reports will be the most useful.

## Disclaimer

Trackarr is an independent project, not affiliated with Radarr, Sonarr, or any download client.
