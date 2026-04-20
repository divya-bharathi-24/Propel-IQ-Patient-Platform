# Task - task_001_infra_docker_compose_local_dev

## Requirement Reference

- User Story: [us_003]
- Story Location: [.propel/context/tasks/EP-TECH/us_003/us_003.md]
- Acceptance Criteria:
  - AC1: Given Docker Desktop is running, When I execute `docker compose up`, Then all four services (Angular dev server, .net 10 API, PostgreSQL 16 with pgvector, Redis local emulator) start within 2 minutes without manual intervention.
  - AC2: Given the Docker Compose environment is running, When the backend initializes, Then it applies pending EF Core migrations automatically and seeds the Specialty reference table.
  - AC3: Given the local stack is up, When I navigate to the Swagger UI URL, Then the API gateway is reachable and all health check endpoints return HTTP 200.
  - AC4: Given the stack is running, When I stop a single service (e.g., Redis), Then the remaining services continue running and the API returns a graceful degradation response for Redis-dependent endpoints.
- Edge Case:
  - PostgreSQL starts before migrations are ready: `depends_on` with `condition: service_healthy` on the PostgreSQL service health check ensures the backend only starts after the DB accepts connections; the migration runner retries with backoff.
  - Environment variable / secrets handling: `.env.example` is committed to source control with placeholder values; `.env` (containing real values) is listed in `.gitignore` and never committed.

## Design References (Frontend Tasks Only)

| Reference Type        | Value |
|-----------------------|-------|
| **UI Impact**         | No    |
| **Figma URL**         | N/A   |
| **Wireframe Status**  | N/A   |
| **Wireframe Type**    | N/A   |
| **Wireframe Path/URL**| N/A   |
| **Screen Spec**       | N/A   |
| **UXR Requirements**  | N/A   |
| **Design Tokens**     | N/A   |

## Applicable Technology Stack

| Layer                | Technology                             | Version    |
|----------------------|----------------------------------------|------------|
| Containerization     | Docker / Docker Compose                | 24.x       |
| Frontend             | Angular (dev server in container)      | 18.x       |
| Backend              | ASP.NET Core Web API                   | .net 10     |
| ORM                  | Entity Framework Core                  | 9.x        |
| Database             | PostgreSQL + pgvector extension        | 16+        |
| Cache                | Redis (local; mirrors Upstash interface) | 7.x (Alpine) |

**Note**: All code and libraries MUST be compatible with versions above.

## AI References (AI Tasks Only)

| Reference Type        | Value |
|-----------------------|-------|
| **AI Impact**         | No    |
| **AIR Requirements**  | N/A   |
| **AI Pattern**        | N/A   |
| **Prompt Template Path** | N/A |
| **Guardrails Config** | N/A   |
| **Model Provider**    | N/A   |

## Mobile References (Mobile Tasks Only)

| Reference Type      | Value |
|---------------------|-------|
| **Mobile Impact**   | No    |
| **Platform Target** | N/A   |
| **Min OS Version**  | N/A   |
| **Mobile Framework**| N/A   |

## Task Overview

Create the Docker Compose local development environment that orchestrates all four platform services — Angular 18 dev server (`app/`), .net 10 ASP.NET Core API (`server/`), PostgreSQL 16 with the `pgvector` extension, and a local Redis container that mirrors the Upstash Redis interface — with a single `docker compose up` command. The compose file uses `depends_on` with `condition: service_healthy` to sequence startup correctly (PostgreSQL ready before backend migrations run). The backend container entrypoint runs `dotnet ef database update` automatically and seeds the Specialty reference table before serving HTTP traffic. An `/healthz` endpoint is exposed on the API gateway confirming reachability (AC3). Graceful degradation for Redis-dependent endpoints is implemented in the .net 10 backend: if Redis is unreachable, affected endpoints return HTTP 200 with an `X-Degraded: redis` response header and a non-cached fallback response, so remaining services are unaffected (AC4 / NFR-018). All secrets are injected via a `.env` file (`.env.example` committed; `.env` git-ignored). This task satisfies TR-022 (Docker containers for local dev) and NFR-016 (service isolation enabling horizontal scaling readiness).

## Dependent Tasks

- [us_002/task_001_be_dotnet_solution_scaffold.md] — .net 10 solution (`server/`) must exist before this task creates the backend Dockerfile and targets the solution for `dotnet build` and `dotnet ef database update`.
- [us_001/task_001_fe_angular_workspace_setup.md] — Angular 18 workspace (`app/`) must exist before this task creates the frontend Dockerfile and targets `ng serve`.

## Impacted Components

| Component / Module                        | Action | Notes                                                                     |
|-------------------------------------------|--------|---------------------------------------------------------------------------|
| `docker-compose.yml`                      | CREATE | Orchestrates all 4 services; named volumes; bridge network; env_file      |
| `docker-compose.override.yml`             | CREATE | Dev-only overrides (hot-reload volume mounts, debug ports)                |
| `.env.example`                            | CREATE | Template with all required env var keys and placeholder values            |
| `.gitignore`                              | MODIFY | Add `.env` entry to prevent secrets from being committed                  |
| `app/Dockerfile`                          | CREATE | Multi-stage: Node 20 LTS base; `npm install`; `ng serve --host 0.0.0.0`  |
| `server/Dockerfile`                       | CREATE | Multi-stage: `mcr.microsoft.com/dotnet/sdk:9.0` build stage; `aspnet:9.0` runtime stage |
| `server/docker-entrypoint.sh`             | CREATE | Waits for DB health, runs `dotnet ef database update`, then `dotnet` start |
| `server/Propel.Api.Gateway/Endpoints/HealthCheckEndpoint.cs` | CREATE | Minimal API `/healthz` endpoint returning HTTP 200 with service status     |
| `server/Propel.Api.Gateway/Program.cs`   | MODIFY | Register `/healthz` endpoint; configure Redis resilience (try/catch + `X-Degraded` header) |
| `server/Propel.Api.Gateway/Data/SeedData.cs` | CREATE | Seeds Specialty reference table on first run if empty                     |

## Implementation Plan

1. **Create `docker-compose.yml`** — Define four services on a shared bridge network `propel-net`:
   - **`frontend`**: builds from `app/Dockerfile`; maps port `4200:4200`; mounts `./app:/app` (hot-reload); `depends_on: [backend]`.
   - **`backend`**: builds from `server/Dockerfile`; maps port `5000:5000`, `5001:5001` (HTTPS); `env_file: .env`; `depends_on: postgres: condition: service_healthy`; entrypoint: `./docker-entrypoint.sh`.
   - **`postgres`**: image `pgvector/pgvector:pg16`; maps port `5432:5432`; named volume `pgdata`; `env_file: .env`; healthcheck: `pg_isready -U ${POSTGRES_USER} -d ${POSTGRES_DB}` with `interval: 5s, retries: 10, start_period: 10s`.
   - **`redis`**: image `redis:7-alpine`; maps port `6379:6379`; named volume `redisdata`; healthcheck: `redis-cli ping` with `interval: 5s, retries: 5`. Upstash Redis SDK is configured in the backend to point to `redis://redis:6379` when `REDIS_USE_LOCAL=true`; production uses the Upstash TLS URL.
   This satisfies AC1.

2. **Create `app/Dockerfile`** — Use `node:20-alpine` as base image. Set `WORKDIR /app`. Copy `package.json` and `package-lock.json` first (layer cache optimization). Run `npm ci`. Copy remaining source. Expose port `4200`. CMD: `npx ng serve --host 0.0.0.0 --poll 1000`. The `--poll` flag enables hot-reload inside the container without inotify issues on Windows Docker Desktop.

3. **Create `server/Dockerfile`** — Multi-stage build. **Build stage** (`mcr.microsoft.com/dotnet/sdk:9.0`): copy `.sln` and all `*.csproj` files; restore (`dotnet restore`); copy full source; publish (`dotnet publish Propel.Api.Gateway -c Release -o /app/publish`). **Runtime stage** (`mcr.microsoft.com/dotnet/aspnet:9.0`): copy from build stage `/app/publish`; copy `docker-entrypoint.sh`; `chmod +x`; expose ports `5000` and `5001`; ENTRYPOINT `["./docker-entrypoint.sh"]`. SDK tools (`dotnet-ef`) are installed in the build stage only, preventing leakage into the runtime image.

4. **Create `server/docker-entrypoint.sh`** — The script: (a) waits for the PostgreSQL connection using a loop (`pg_isready` or `dotnet-ef` connectivity check with max 30 retries, 2-second sleep); (b) runs `dotnet ef database update --project Propel.Api.Gateway` to apply any pending EF Core migrations; (c) calls a one-time seed check (handled inside `Program.cs` startup); (d) executes `dotnet Propel.Api.Gateway.dll`. This satisfies AC2 (edge case: DB-ready health check before migrations run).

5. **Implement Specialty reference table seed** — In `server/Propel.Api.Gateway/Data/SeedData.cs`, add a static `SeedSpecialtiesAsync(AppDbContext db)` method that checks `await db.Specialties.AnyAsync()`; if empty, bulk-inserts a predefined list of medical specialties (e.g., General Practice, Cardiology, Orthopaedics, Neurology, Paediatrics). Call this from `Program.cs` after `app.MapControllers()` and before `app.Run()`: `await SeedData.SeedSpecialtiesAsync(scope.ServiceProvider.GetRequiredService<AppDbContext>())`. Add the `Specialty` entity and `DbSet<Specialty>` to `AppDbContext` to support this seed. This satisfies AC2.

6. **Create `/healthz` endpoint** — In `server/Propel.Api.Gateway/Endpoints/HealthCheckEndpoint.cs`, define a minimal API endpoint `app.MapGet("/healthz", ...)` that checks DB connectivity (`db.Database.CanConnectAsync()`) and Redis (`redisCache.PingAsync()`). Returns HTTP 200 with `{ "status": "healthy", "db": "ok", "redis": "ok|degraded" }`. If Redis is unreachable, returns HTTP 200 with `redis: "degraded"` rather than HTTP 500. This satisfies AC3.

7. **Implement Redis graceful degradation in backend** — In `Program.cs`, configure the Redis connection with `IConnectionMultiplexer` wrapped in a resilience policy: all Redis operations use `try { ... } catch (RedisConnectionException) { logger.LogWarning("Redis unavailable - serving uncached"); return fallback; }`. Add response header `X-Degraded: redis` when Redis is bypassed. Register the `IConnectionMultiplexer` as a singleton with `abortConnect=false` so the application starts even if Redis is down. This satisfies AC4 and NFR-018.

8. **Create `.env.example` and update `.gitignore`** — Create `.env.example` at the repo root with all required variables: `POSTGRES_USER`, `POSTGRES_PASSWORD`, `POSTGRES_DB`, `POSTGRES_CONNECTION_STRING`, `REDIS_USE_LOCAL`, `REDIS_CONNECTION_STRING`, `JWT_SECRET`, `JWT_ISSUER`, `JWT_AUDIENCE`, `ASPNETCORE_ENVIRONMENT`, `ASPNETCORE_URLS`. Add `.env` to `.gitignore`. Document the setup procedure in `README.md`: "Copy `.env.example` to `.env`, fill in values, then run `docker compose up`." This satisfies the secrets edge case.

## Current Project State

```
Propel-IQ-Patient-Platform/
├── .github/
├── .propel/
│   └── context/
│       └── tasks/
│           └── EP-TECH/
│               ├── us_001/
│               │   ├── us_001.md
│               │   └── task_001_fe_angular_workspace_setup.md  ✓
│               ├── us_002/
│               │   ├── us_002.md
│               │   └── task_001_be_dotnet_solution_scaffold.md  ✓
│               └── us_003/
│                   ├── us_003.md
│                   └── task_001_infra_docker_compose_local_dev.md  ← THIS TASK
├── app/                  ← Angular 18 SPA (us_001)
├── server/               ← .net 10 solution (us_002)
├── BRD Unified Patient Acces.md
└── README.md
```

*`docker-compose.yml`, Dockerfiles, `.env.example`, and the entrypoint script do not yet exist. They will be created as part of this task.*

## Expected Changes

| Action | File Path                                                                 | Description                                                                      |
|--------|---------------------------------------------------------------------------|----------------------------------------------------------------------------------|
| CREATE | `docker-compose.yml`                                                      | Orchestrates frontend, backend, postgres (pgvector), redis; health checks; named volumes |
| CREATE | `docker-compose.override.yml`                                             | Dev-only volume mounts for hot-reload; debug port overrides                      |
| CREATE | `.env.example`                                                            | Template env vars: PostgreSQL, Redis, JWT, ASPNETCORE settings                   |
| MODIFY | `.gitignore`                                                              | Add `.env` to prevent secrets commit                                             |
| CREATE | `app/Dockerfile`                                                          | Node 20 Alpine; `npm ci`; `ng serve --host 0.0.0.0 --poll 1000`                  |
| CREATE | `server/Dockerfile`                                                       | Multi-stage: SDK 9.0 build → ASP.net 10.0 runtime; `dotnet publish` release build |
| CREATE | `server/docker-entrypoint.sh`                                             | Wait-for-DB loop; `dotnet ef database update`; seed trigger; `dotnet` start       |
| CREATE | `server/Propel.Api.Gateway/Endpoints/HealthCheckEndpoint.cs`             | `/healthz` minimal API; DB + Redis connectivity check; HTTP 200 with degraded state |
| CREATE | `server/Propel.Api.Gateway/Data/SeedData.cs`                             | `SeedSpecialtiesAsync` — inserts Specialty rows if table is empty                |
| MODIFY | `server/Propel.Api.Gateway/Program.cs`                                   | Register `/healthz`; register Redis with `abortConnect=false`; add `X-Degraded` header logic; call `SeedData` on startup |
| MODIFY | `README.md`                                                               | Add "Local Development" section: prerequisites (Docker Desktop), `.env` setup, `docker compose up` instructions |

## External References

- [Docker Compose v2 — Official Reference](https://docs.docker.com/compose/compose-file/)
- [Docker Compose `depends_on` with health checks](https://docs.docker.com/compose/compose-file/05-services/#depends_on)
- [pgvector Docker image — `pgvector/pgvector:pg16`](https://hub.docker.com/r/pgvector/pgvector)
- [Redis 7 Alpine Docker image](https://hub.docker.com/_/redis)
- [.net 10 Multi-stage Dockerfile best practices](https://learn.microsoft.com/en-us/dotnet/core/docker/build-container)
- [EF Core CLI — `dotnet ef database update`](https://learn.microsoft.com/en-us/ef/core/cli/dotnet#dotnet-ef-database-update)
- [ASP.NET Core Health Checks](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/health-checks?view=aspnetcore-9.0)
- [StackExchange.Redis — `abortConnect=false` for graceful degradation](https://stackexchange.github.io/StackExchange.Redis/Configuration.html)
- [Upstash Redis — Local development with standard Redis](https://upstash.com/docs/redis/howto/connectwithredisclients)
- [TR-022: Docker containers for local dev — design.md#technical-requirements]
- [NFR-016: Horizontal scaling readiness — design.md#scalability]
- [NFR-018: Graceful degradation for external services — design.md#availability]

## Build Commands

```bash
# Copy env template and fill in values
cp .env.example .env

# Start all services (detached)
docker compose up -d

# Start all services with live logs
docker compose up

# Rebuild images after code changes
docker compose up --build

# Stop all services (preserve volumes)
docker compose stop

# Stop and remove containers + volumes (clean slate)
docker compose down -v

# View logs for a specific service
docker compose logs -f backend

# Test graceful degradation (stop Redis only)
docker compose stop redis
# Then call a Redis-dependent endpoint and verify X-Degraded: redis header
```

## Implementation Validation Strategy

- [ ] `docker compose up` from the repo root completes within 2 minutes and all four service containers reach `running` state with no `Exit` codes (AC1)
- [ ] `docker compose logs backend` shows "Migrations applied successfully" and "Specialties seeded: N rows" messages on first boot (AC2)
- [ ] `docker compose ps` shows `postgres` service health as `healthy` before the `backend` service starts (AC2 edge case — `depends_on` ordering)
- [ ] `curl http://localhost:5000/healthz` returns HTTP 200 with `{ "status": "healthy", "db": "ok", "redis": "ok" }` (AC3)
- [ ] After `docker compose stop redis`, `curl http://localhost:5000/healthz` returns HTTP 200 with `redis: "degraded"` and the frontend and backend remain reachable (AC4)
- [ ] Redis-dependent endpoint responses include `X-Degraded: redis` header when Redis is stopped (AC4 / NFR-018)
- [ ] `.env` file is listed in `.gitignore` and `git status` does not show it as tracked (secrets edge case)
- [ ] `docker compose down -v && docker compose up` (fresh start) re-applies migrations and re-seeds without errors

## Implementation Checklist

- [ ] Create `docker-compose.yml` with four services (`frontend`, `backend`, `postgres`, `redis`), bridge network `propel-net`, named volumes `pgdata` and `redisdata`, and health checks on `postgres` and `redis` services; wire `depends_on: postgres: condition: service_healthy` for `backend`
- [ ] Create `app/Dockerfile`: `FROM node:20-alpine`; `WORKDIR /app`; `COPY package*.json ./`; `RUN npm ci`; `COPY . .`; `EXPOSE 4200`; `CMD ["npx","ng","serve","--host","0.0.0.0","--poll","1000"]`
- [ ] Create `server/Dockerfile` multi-stage build: build stage (`dotnet/sdk:9.0`) restores, publishes `Propel.Api.Gateway`; runtime stage (`dotnet/aspnet:9.0`) copies publish output and `docker-entrypoint.sh`; install `dotnet-ef` tool in build stage; expose 5000/5001
- [ ] Create `server/docker-entrypoint.sh`: wait-for-postgres loop (max 30 retries, 2s sleep); run `dotnet ef database update`; exec `dotnet Propel.Api.Gateway.dll`; make executable (`chmod +x`)
- [ ] Add `Specialty` entity and `DbSet<Specialty>` to `AppDbContext`; create `SeedData.SeedSpecialtiesAsync` seeding ≥5 Specialty rows if table is empty; call from `Program.cs` startup after migrations
- [ ] Implement `/healthz` minimal API endpoint checking `db.Database.CanConnectAsync()` and Redis `PING`; return HTTP 200 JSON with `db` and `redis` status fields; never return 5xx for Redis failure (graceful degradation)
- [ ] Configure `IConnectionMultiplexer` with `abortConnect=false` in `Program.cs`; wrap all Redis read/write calls in `try/catch (RedisConnectionException)`; append `X-Degraded: redis` response header on fallback path
- [ ] Create `.env.example` with all required keys (PostgreSQL, Redis, JWT, ASPNETCORE vars); add `.env` to `.gitignore`; update `README.md` with "Local Development" setup instructions
