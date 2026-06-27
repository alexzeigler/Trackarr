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

## Quick start

### Docker Compose (recommended)

Create a `compose.yaml`:

```yaml
services:
  trackarr:
    container_name: trackarr
    image: trackarr:local
    build:
      context: .
      dockerfile: Trackarr/Dockerfile
    restart: unless-stopped
    ports:
      - "5264:8080"
    volumes:
      - ./docker-data/trackarr:/config
    environment:
      ASPNETCORE_ENVIRONMENT: Production
      DOTNET_ENVIRONMENT: Production
      ASPNETCORE_URLS: http://+:8080
      ConnectionStrings__DefaultConnection: "Data Source=/config/trackarr.db"
      DataProtection__KeysPath: "/config/keys"
```

```bash
docker compose up -d
```

Trackarr will be available at `http://localhost:5264`. The database and configuration are persisted to `./docker-data/trackarr/`.

### First-time setup

1. Open Trackarr in your browser
2. Go to **Servers** and add your Radarr/Sonarr instances (you'll need the host, port, and API key for each)
3. Background routines will start syncing queue and missing media data automatically

## Configuration

Trackarr stores its settings in the database and exposes them through the UI. Key configuration areas:

| Category | Examples |
|---|---|
| **General** | App name, auto-start routines, default server type |
| **Server defaults** | Queue monitoring, auto-remove failed downloads, missing media search, request timeout |
| **Health policy** | Starting score, penalty/reward weights, removal thresholds, grace periods |
| **Missing policy** | Search cooldown, max failed searches, cascade limit, abandonment threshold |
| **Display** | Date/time format, table page sizes, chart visibility, table density |

## Development

**Requirements:** [.NET 10.0 SDK](https://dotnet.microsoft.com/download)

```bash
git clone https://github.com/your-username/Trackarr.git
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
