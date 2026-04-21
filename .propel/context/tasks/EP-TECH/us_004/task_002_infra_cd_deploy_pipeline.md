# Task - TASK_002

## Requirement Reference

- User Story: [us_004] (extracted from input)
- Story Location: [.propel/context/tasks/EP-TECH/us_004/us_004.md]
- Acceptance Criteria:
  - **AC-2**: Given all gates pass on a merged PR, When the deploy job runs, Then the Angular SPA deploys to Netlify/Vercel and the .NET API Docker image deploys to Railway within 10 minutes of merge.
  - **AC-3**: Given the pipeline is configured, When I inspect the workflow YAML, Then secrets (API keys, deploy tokens) are stored as GitHub repository secrets and never appear in plain text in logs.
- Edge Case:
  - What happens if the Railway deployment fails mid-deploy? — Pipeline marks the deploy step as failed; previous deployment remains live (Railway atomic deploy); failure details are logged to the workflow run summary.
  - How are database migration failures handled in CI? — A dedicated migration smoke test (`dotnet ef database update --dry-run`) runs as a pre-deploy check step; failure blocks traffic cutover.

## Design References (Frontend Tasks Only)

| Reference Type         | Value |
| ---------------------- | ----- |
| **UI Impact**          | No    |
| **Figma URL**          | N/A   |
| **Wireframe Status**   | N/A   |
| **Wireframe Type**     | N/A   |
| **Wireframe Path/URL** | N/A   |
| **Screen Spec**        | N/A   |
| **UXR Requirements**   | N/A   |
| **Design Tokens**      | N/A   |

## Applicable Technology Stack

| Layer        | Technology            | Version |
| ------------ | --------------------- | ------- |
| Frontend     | Angular               | 18.x    |
| Backend      | ASP.NET Core Web API  | .net 10 |
| CI/CD        | GitHub Actions        | —       |
| Hosting (FE) | Netlify / Vercel      | —       |
| Hosting (BE) | Railway               | —       |
| Container    | Docker                | 24.x    |
| ORM/Migrate  | Entity Framework Core | 9.x     |
| AI/ML        | N/A                   | N/A     |
| Mobile       | N/A                   | N/A     |

**Note**: All code, and libraries, MUST be compatible with versions above.

## AI References (AI Tasks Only)

| Reference Type           | Value |
| ------------------------ | ----- |
| **AI Impact**            | No    |
| **AIR Requirements**     | N/A   |
| **AI Pattern**           | N/A   |
| **Prompt Template Path** | N/A   |
| **Guardrails Config**    | N/A   |
| **Model Provider**       | N/A   |

## Mobile References (Mobile Tasks Only)

| Reference Type       | Value |
| -------------------- | ----- |
| **Mobile Impact**    | No    |
| **Platform Target**  | N/A   |
| **Min OS Version**   | N/A   |
| **Mobile Framework** | N/A   |

## Task Overview

Implement the GitHub Actions CD workflow that triggers on every push to `main` (i.e., after a merged PR). The pipeline:

1. Builds the Angular 18 SPA production bundle and deploys it to Netlify (or Vercel).
2. Builds the .net 10 API Docker image, pushes it to a container registry (GitHub Container Registry — free), and deploys to Railway via the Railway CLI.
3. Enforces a 10-minute total deploy time constraint.
4. Stores all deploy tokens and API keys exclusively as GitHub repository secrets (`${{ secrets.* }}`), ensuring they never appear in workflow logs.
5. Includes a database migration dry-run check before traffic cutover to prevent broken schema deployments.

This task covers only the **merge-to-main trigger** workflow (`cd.yml`). Pull-request quality gates are handled in `task_001_infra_ci_pr_gate_workflow.md`.

## Dependent Tasks

- **task_001_infra_ci_pr_gate_workflow.md** — CI gates must pass before any merge, ensuring only verified code reaches `main`.
- US_001 — Angular workspace with `ng build` script must exist.
- US_002 — .NET solution with a `Dockerfile` in the `server/` directory must exist.

## Impacted Components

| Component                  | Action    | Notes                                                                 |
| -------------------------- | --------- | --------------------------------------------------------------------- |
| `.github/workflows/cd.yml` | CREATE    | New GitHub Actions CD workflow — merge-to-main deploy pipeline        |
| `server/Dockerfile`        | VERIFY    | Must exist and produce a runnable .net 10 API image; CREATE if absent |
| `.github/workflows/ci.yml` | REFERENCE | Confirms quality gates from task_001; no modification required        |

## Implementation Plan

1. **Create `.github/workflows/cd.yml`** — Define workflow triggered on `push` to `main` branch only (not triggered by other branches or tags). Add 10-minute `timeout-minutes: 10` at workflow level.
2. **Frontend Deploy Job** (`deploy-frontend`) — Checkout repo, set up Node.js 20, run `npm ci`, build Angular production bundle (`npx ng build --configuration production`), deploy `dist/` to Netlify using `nwtgck/actions-netlify@v3` action with `NETLIFY_AUTH_TOKEN` and `NETLIFY_SITE_ID` from GitHub secrets. Alternatively, deploy to Vercel using `amondnet/vercel-action@v25` with `VERCEL_TOKEN`, `VERCEL_ORG_ID`, `VERCEL_PROJECT_ID` from GitHub secrets.
3. **Backend Build & Push Docker Image** (`build-push-docker`) — Checkout repo, log in to GitHub Container Registry (`ghcr.io`) using `docker/login-action@v3` with `GITHUB_TOKEN` (automatic, no additional secret needed). Build Docker image tagged with `ghcr.io/${{ github.repository }}/api:latest` and the commit SHA. Push both tags.
4. **Backend Deploy to Railway** (`deploy-backend`) — Depends on `deploy-frontend` and `build-push-docker`; install Railway CLI (`npm i -g @railway/cli`), run `railway up --service api` using `RAILWAY_TOKEN` from GitHub secrets; Railway pulls the latest Docker image and redeploys atomically.
5. **Database Migration Smoke Check** — Within `deploy-backend` job, before signalling deploy success, run `dotnet ef database update --dry-run --project server/src/Infrastructure` to validate migration script integrity against the staging database. Use `DATABASE_URL` from GitHub secrets. Failure blocks the job.
6. **Deploy Time Enforcement** — Set `timeout-minutes: 10` on the entire workflow to hard-fail the pipeline if the total deploy exceeds 10 minutes (AC-2 SLA).
7. **Secrets Hygiene** — All sensitive values (`NETLIFY_AUTH_TOKEN`, `NETLIFY_SITE_ID`, `RAILWAY_TOKEN`, `DATABASE_URL`) are referenced exclusively via `${{ secrets.* }}` syntax. Add `::add-mask::` to any intermediate variable that derives from a secret, ensuring no plain-text leakage in logs.
8. **Deploy Summary Output** — Use `$GITHUB_STEP_SUMMARY` to append a Markdown deploy summary (commit SHA, deploy timestamps, frontend URL, backend health endpoint) to the workflow run summary.

## Current Project State

```
Propel-IQ-Patient-Platform/
├── .github/
│   └── workflows/
│       └── ci.yml               # Created in task_001
├── app/                         # Angular 18 workspace
│   ├── angular.json
│   ├── package.json
│   └── dist/                    # Generated by ng build
├── server/                      # .net 10 solution
│   ├── PropelIQ.sln
│   ├── Dockerfile               # VERIFY exists; CREATE if absent
│   └── src/
│       └── Infrastructure/      # EF Core migrations project
└── docker-compose.yml
```

_Update this tree during execution based on completed dependent tasks._

## Expected Changes

| Action             | File Path                  | Description                                                |
| ------------------ | -------------------------- | ---------------------------------------------------------- |
| CREATE             | `.github/workflows/cd.yml` | GitHub Actions CD workflow — merge-to-main deploy pipeline |
| CREATE (if absent) | `server/Dockerfile`        | Multi-stage .net 10 Docker image for Railway deployment    |

### Secrets to Register in GitHub Repository Settings

> Register these under **Settings → Secrets and variables → Actions → Repository secrets** before running the workflow. Never commit values.

| Secret Name          | Purpose                                                       |
| -------------------- | ------------------------------------------------------------- |
| `NETLIFY_AUTH_TOKEN` | Netlify personal access token for CLI deploy                  |
| `NETLIFY_SITE_ID`    | Netlify site ID (or Vercel equivalents below)                 |
| `VERCEL_TOKEN`       | Vercel deployment token (if Vercel is preferred over Netlify) |
| `VERCEL_ORG_ID`      | Vercel organization ID                                        |
| `VERCEL_PROJECT_ID`  | Vercel project ID                                             |
| `RAILWAY_TOKEN`      | Railway API token for `railway up` CLI                        |
| `DATABASE_URL`       | Neon PostgreSQL connection string (staging)                   |

## External References

- [GitHub Actions — `push` event trigger](https://docs.github.com/en/actions/writing-workflows/choosing-when-your-workflow-runs/events-that-trigger-workflows#push)
- [GitHub Actions — `timeout-minutes`](https://docs.github.com/en/actions/writing-workflows/workflow-syntax-for-github-actions#jobsjob_idtimeout-minutes)
- [GitHub Actions — Encrypted secrets](https://docs.github.com/en/actions/security-for-github-actions/security-guides/using-secrets-in-github-actions)
- [GitHub Actions — `GITHUB_STEP_SUMMARY`](https://docs.github.com/en/actions/writing-workflows/choosing-what-your-workflow-does/workflow-commands-for-github-actions#adding-a-job-summary)
- [GitHub Container Registry — Publishing Docker images](https://docs.github.com/en/packages/working-with-a-github-packages-registry/working-with-the-container-registry)
- [`docker/login-action@v3`](https://github.com/docker/login-action)
- [`docker/build-push-action@v5`](https://github.com/docker/build-push-action)
- [Netlify — GitHub Actions deployment (`nwtgck/actions-netlify`)](https://github.com/nwtgck/actions-netlify)
- [Vercel — GitHub Actions deployment (`amondnet/vercel-action`)](https://github.com/amondnet/vercel-action)
- [Railway CLI — Deploy with `railway up`](https://docs.railway.app/guides/cli)
- [Entity Framework Core — `dotnet ef database update --dry-run`](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/applying#idempotent-sql-scripts)
- [OWASP — Secrets Management Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/Secrets_Management_Cheat_Sheet.html)

## Build Commands

```bash
# Angular — production build (run locally to verify before push)
cd app && npm ci && npx ng build --configuration production

# Docker — build and tag API image locally
cd server && docker build -t propeliq-api:local .

# Docker — run API container locally for smoke test
docker run --rm -p 5000:8080 propeliq-api:local

# EF Core — migration dry-run (requires DATABASE_URL env var)
cd server && dotnet ef database update --dry-run \
  --project src/Infrastructure \
  --startup-project src/PropelIQ.Api

# Railway CLI — deploy (requires RAILWAY_TOKEN env var)
railway up --service api
```

## Implementation Validation Strategy

- [x] Angular production bundle builds successfully (`ng build --configuration production` exits 0)
- [x] Docker image builds without errors (`docker build` exits 0 locally)
- [x] `cd.yml` workflow triggers on push to `main`; does NOT trigger on feature branch pushes
- [x] Total workflow duration is under 10 minutes (enforce via `timeout-minutes: 10`)
- [ ] Frontend is accessible at Netlify/Vercel URL after deploy job completes
- [ ] Backend health endpoint responds at Railway URL after deploy job completes
- [x] No secret values appear in plain text in workflow run logs (inspect via GitHub Actions UI)
- [x] EF migration dry-run step blocks deploy if migration script is invalid

## Implementation Checklist

- [x] Create `.github/workflows/cd.yml` with `push` trigger on `main` and `timeout-minutes: 10`
- [x] Add `deploy-frontend` job: `checkout` → Node 20 setup → `npm ci` → `ng build --configuration production` → Netlify/Vercel deploy action using `${{ secrets.NETLIFY_AUTH_TOKEN }}` / `${{ secrets.NETLIFY_SITE_ID }}`
- [x] Add `build-push-docker` job: `checkout` → `docker/login-action@v3` (GHCR, `GITHUB_TOKEN`) → `docker/build-push-action@v5` (tag: `ghcr.io/${{ github.repository }}/api:${{ github.sha }}` and `:latest`)
- [x] Add `deploy-backend` job (needs `deploy-frontend`, `build-push-docker`): install Railway CLI → run EF migration dry-run (`DATABASE_URL` from secrets) → `railway up --service api` (`RAILWAY_TOKEN` from secrets)
- [x] Verify `server/Dockerfile` exists; if absent, create multi-stage .net 10 Dockerfile (`sdk:9.0` build → `aspnet:9.0` runtime)
- [x] Register all required secrets in GitHub repository Settings → Actions secrets (document list in PR description)
- [x] Append deploy summary to `$GITHUB_STEP_SUMMARY` with commit SHA, deploy time, frontend URL, backend health URL
- [x] Confirm no secret values leak in logs — inspect workflow run log for `NETLIFY_AUTH_TOKEN`, `RAILWAY_TOKEN`, `DATABASE_URL` substrings
