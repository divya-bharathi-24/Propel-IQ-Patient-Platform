# Task - TASK_002

## Requirement Reference

- User Story: [us_005] (extracted from input)
- Story Location: [.propel/context/tasks/EP-TECH/us_005/us_005.md]
- Acceptance Criteria:
  - **AC-2**: Given the Railway project is configured, When the .NET 9 Docker container is deployed, Then the API is reachable at the configured domain over TLS 1.2+ and returns HTTP 200 on the `/health` endpoint.
- Edge Case:
  - What happens if Railway free-tier limits are reached? — Document monitoring alert at 80% resource usage and migration path to Railway Hobby/Pro tier in `railway.toml` comments and README. Pipeline marks deploy step as failed; previous deployment remains live (Railway atomic deploys).

## Design References (Frontend Tasks Only)

| Reference Type       | Value |
| -------------------- | ----- |
| **UI Impact**        | No    |
| **Figma URL**        | N/A   |
| **Wireframe Status** | N/A   |
| **Wireframe Type**   | N/A   |
| **Wireframe Path/URL** | N/A |
| **Screen Spec**      | N/A   |
| **UXR Requirements** | N/A   |
| **Design Tokens**    | N/A   |

## Applicable Technology Stack

| Layer          | Technology             | Version |
| -------------- | ---------------------- | ------- |
| Backend        | ASP.NET Core Web API   | .NET 9  |
| Hosting (BE)   | Railway                | —       |
| Container      | Docker                 | 24.x    |
| CI/CD          | GitHub Actions         | —       |
| Registry       | GitHub Container Registry (GHCR) | — |
| AI/ML          | N/A                    | N/A     |
| Mobile         | N/A                    | N/A     |

**Note**: All code, and libraries, MUST be compatible with versions above.

## AI References (AI Tasks Only)

| Reference Type       | Value |
| -------------------- | ----- |
| **AI Impact**        | No    |
| **AIR Requirements** | N/A   |
| **AI Pattern**       | N/A   |
| **Prompt Template Path** | N/A |
| **Guardrails Config**| N/A   |
| **Model Provider**   | N/A   |

## Mobile References (Mobile Tasks Only)

| Reference Type      | Value |
| ------------------- | ----- |
| **Mobile Impact**   | No    |
| **Platform Target** | N/A   |
| **Min OS Version**  | N/A   |
| **Mobile Framework**| N/A   |

## Task Overview

Configure the Railway project to host the .NET 9 API as a stateless Docker container over HTTPS (TLS 1.2+ enforced by Railway's edge layer). This includes creating a multi-stage `Dockerfile` for the .NET solution, a `railway.toml` service manifest, and implementing the ASP.NET Core Health Checks `/health` endpoint. The endpoint is used by Railway's health-gate to confirm liveness after each deploy and by the CD pipeline smoke test from US_004 task_002.

This task covers only the **Railway backend hosting** configuration. Netlify frontend deployment is handled in `task_001_infra_netlify_frontend_deploy.md`. Environment variable management and CORS are handled in `task_003_infra_env_vars_cors_policy.md`.

## Dependent Tasks

- US_002 — .NET 9 solution must exist with a compilable `server/src/PropelIQ.Api` entry project
- US_004 task_002 — CD pipeline builds and pushes Docker image to GHCR and calls `railway up`; Railway service must be pre-configured before the secret `RAILWAY_TOKEN` is registered

## Impacted Components

| Component | Action | Notes |
| --------- | ------ | ----- |
| `server/Dockerfile` | CREATE | Multi-stage .NET 9 Docker image (build → runtime), non-root user |
| `railway.toml` (repository root) | CREATE | Railway service manifest: service name, start command, port, health check path, restart policy |
| `server/src/PropelIQ.Api/Program.cs` | MODIFY | Register ASP.NET Core Health Checks middleware and map `/health` endpoint |
| `.dockerignore` (server/) | CREATE | Exclude `bin/`, `obj/`, `.git/`, local secrets from Docker build context |

## Implementation Plan

1. **Create `server/Dockerfile` — multi-stage build** — Stage 1 (`build`): use `mcr.microsoft.com/dotnet/sdk:9.0` as build image; copy solution files; run `dotnet restore` then `dotnet publish -c Release -o /app/publish --no-restore`. Stage 2 (`runtime`): use `mcr.microsoft.com/dotnet/aspnet:9.0` as runtime image; set `WORKDIR /app`; copy from build stage; create and switch to non-root user `appuser` (UID 1001) to satisfy OWASP A05 (Security Misconfiguration — avoid running as root); set `ENTRYPOINT ["dotnet", "PropelIQ.Api.dll"]`.

2. **Set `ASPNETCORE_URLS` and port in Dockerfile** — Add `ENV ASPNETCORE_URLS=http://+:8080` so the container binds on port 8080. Railway terminates TLS 1.2+ at its edge and proxies to the container over plain HTTP internally. Expose `EXPOSE 8080`.

3. **Create `server/.dockerignore`** — Exclude `bin/`, `obj/`, `.git/`, `**/*.user`, `**/*.suo`, `appsettings.Development.json`, `**/.env` to minimise build context and prevent local secrets from entering the image.

4. **Register ASP.NET Core Health Checks** — In `Program.cs`, add `builder.Services.AddHealthChecks()` and `app.MapHealthChecks("/health")`. The endpoint returns `HTTP 200 Healthy` with a JSON body `{"status":"Healthy"}`. No authentication required on `/health` (it is a liveness probe, not a data endpoint).

5. **Create `railway.toml` — service manifest** — Define service `api` with `startCommand = "dotnet PropelIQ.Api.dll"`, `port = 8080`, `healthcheckPath = "/health"`, `healthcheckTimeout = 30`, `restartPolicyType = "ON_FAILURE"`, `restartPolicyMaxRetries = 3`. Set `numReplicas = 1` (free tier limit).

6. **Configure Railway project via Railway Dashboard** — Create Railway project, add a service named `api`, link it to the GitHub repository. Set deploy source to GHCR image (`ghcr.io/<owner>/propeliq-api:latest`) pulled by the CD pipeline. Record `RAILWAY_TOKEN` from Railway Dashboard → Settings → Tokens and register as GitHub secret (used by CD pipeline).

7. **Validate TLS 1.2+ enforcement** — Railway's edge proxy provides TLS termination by default using certificates managed by Railway. Verify via `curl --tlsv1.2 https://<railway-domain>/health` returns `HTTP 200`. Document this in `railway.toml` comments: "TLS 1.2+ terminated at Railway edge; container binds HTTP on 8080 only."

8. **Add free-tier resource monitoring alert** — Document in `railway.toml` comments: alert threshold at 80% of free-tier monthly usage cap ($5 credit). Add migration note: "Upgrade to Railway Hobby ($5/mo) or Pro tier when credit usage exceeds 80% to maintain NFR-003 (99.9% uptime)."

## Current Project State

```
Propel-IQ-Patient-Platform/
├── app/                         # Angular 18 workspace (from US_001)
├── server/                      # .NET 9 solution (from US_002)
│   ├── PropelIQ.sln
│   ├── Dockerfile               # To be created
│   ├── .dockerignore            # To be created
│   └── src/
│       └── PropelIQ.Api/
│           └── Program.cs       # To be modified
├── .github/
│   └── workflows/
│       ├── ci.yml               # From US_004 task_001
│       └── cd.yml               # From US_004 task_002
├── netlify.toml                 # From us_005 task_001
└── railway.toml                 # To be created
```

_Update this tree during execution based on completed dependent tasks._

## Expected Changes

| Action | File Path | Description |
| ------ | --------- | ----------- |
| CREATE | `server/Dockerfile` | Multi-stage .NET 9 Docker image; non-root user; port 8080 |
| CREATE | `server/.dockerignore` | Exclude build artifacts, local secrets, and IDE files from Docker context |
| CREATE | `railway.toml` | Railway service manifest: port, health check, restart policy |
| MODIFY | `server/src/PropelIQ.Api/Program.cs` | Register `AddHealthChecks()` and map `/health` endpoint |

### Reference: `server/Dockerfile`

```dockerfile
# Stage 1 — Build
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY ["PropelIQ.sln", "."]
COPY ["src/PropelIQ.Api/PropelIQ.Api.csproj", "src/PropelIQ.Api/"]
# Add other project .csproj copies as needed
RUN dotnet restore "PropelIQ.sln"
COPY . .
RUN dotnet publish "src/PropelIQ.Api/PropelIQ.Api.csproj" \
    -c Release -o /app/publish --no-restore

# Stage 2 — Runtime
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app
# Non-root user (OWASP A05 — avoid running as root)
RUN adduser --disabled-password --gecos "" --uid 1001 appuser
COPY --from=build /app/publish .
USER appuser
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "PropelIQ.Api.dll"]
```

### Reference: `railway.toml`

```toml
[build]
  builder = "DOCKERFILE"
  dockerfilePath = "server/Dockerfile"

[deploy]
  startCommand         = "dotnet PropelIQ.Api.dll"
  port                 = 8080
  healthcheckPath      = "/health"
  healthcheckTimeout   = 30
  restartPolicyType    = "ON_FAILURE"
  restartPolicyMaxRetries = 3
  numReplicas          = 1

# TLS NOTE: TLS 1.2+ is terminated at Railway's edge proxy.
# The container binds plain HTTP on port 8080 only.
# NFR-005 (TLS 1.2+) is satisfied by Railway's managed edge certificate.

# FREE-TIER ALERT: Monitor Railway usage dashboard.
# Alert threshold: 80% of $5/month free credit.
# Migration path: Upgrade to Railway Hobby ($5/mo) or Pro to maintain NFR-003 (99.9% uptime).
```

### Reference: `/health` endpoint in `Program.cs`

```csharp
// Register health checks
builder.Services.AddHealthChecks();

// ...

// Map health check endpoint — no auth required (liveness probe only)
app.MapHealthChecks("/health");
```

## External References

- [Railway — Deploy with Docker](https://docs.railway.app/guides/dockerfiles)
- [Railway — `railway.toml` configuration reference](https://docs.railway.app/reference/config-as-code)
- [Railway — Health checks configuration](https://docs.railway.app/guides/healthchecks-and-restarts)
- [Railway — TLS and custom domains](https://docs.railway.app/guides/public-networking)
- [Railway CLI — `railway up` deploy command](https://docs.railway.app/guides/cli)
- [ASP.NET Core — Health checks middleware](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/health-checks)
- [Docker — Multi-stage builds](https://docs.docker.com/build/building/multi-stage/)
- [Microsoft — .NET 9 Docker images (`mcr.microsoft.com/dotnet`)](https://hub.docker.com/_/microsoft-dotnet)
- [OWASP — Docker Security Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/Docker_Security_Cheat_Sheet.html)

## Build Commands

```bash
# Docker — build image locally (from server/ directory)
cd server
docker build -t propeliq-api:local .

# Docker — run container locally and verify /health
docker run --rm -p 8080:8080 propeliq-api:local
curl http://localhost:8080/health
# Expected: HTTP 200 {"status":"Healthy"}

# .NET — publish locally to verify
cd server
dotnet publish src/PropelIQ.Api/PropelIQ.Api.csproj -c Release -o ./publish

# Railway CLI — link service and deploy (requires RAILWAY_TOKEN env var)
npm install -g @railway/cli
railway login --token $RAILWAY_TOKEN
railway link
railway up --service api

# Verify TLS 1.2+ on deployed Railway domain
curl --tlsv1.2 -I https://<railway-domain>/health
# Expected: HTTP/2 200
```

## Implementation Validation Strategy

- [ ] `docker build` succeeds locally with no errors
- [ ] Container runs as non-root user (`docker inspect` confirms `User: appuser`)
- [ ] `curl http://localhost:8080/health` returns `HTTP 200` with body `{"status":"Healthy"}`
- [ ] `railway up` deploys container; Railway dashboard shows service `Healthy`
- [ ] `curl --tlsv1.2 https://<railway-domain>/health` returns `HTTP 200` (TLS 1.2+ enforced)
- [ ] Previous deployment remains live if a new deploy fails (Railway atomic deploy confirmed)
- [ ] `.dockerignore` excludes `appsettings.Development.json` (verified by inspecting image contents)

## Implementation Checklist

- [ ] Create `server/Dockerfile` — multi-stage build (sdk:9.0 → aspnet:9.0), non-root user `appuser` (UID 1001), `ASPNETCORE_URLS=http://+:8080`, `EXPOSE 8080`
- [ ] Create `server/.dockerignore` — exclude `bin/`, `obj/`, `.git/`, `appsettings.Development.json`, `**/.env`
- [ ] Modify `server/src/PropelIQ.Api/Program.cs` — add `builder.Services.AddHealthChecks()` and `app.MapHealthChecks("/health")`
- [ ] Create `railway.toml` at repository root — service manifest with port 8080, `healthcheckPath = "/health"`, restart policy, TLS note, free-tier monitoring comment
- [ ] Create Railway project via Railway Dashboard; link GitHub repo; configure deploy source as GHCR image
- [ ] Generate `RAILWAY_TOKEN` from Railway Dashboard → Settings → Tokens; register as GitHub secret
- [ ] Build and run Docker image locally; confirm `/health` returns `HTTP 200` as non-root user
- [ ] Deploy to Railway via `railway up`; confirm health check passes and TLS endpoint responds
