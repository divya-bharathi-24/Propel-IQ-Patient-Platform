# Task - TASK_001

## Requirement Reference

- User Story: [us_004] (extracted from input)
- Story Location: [.propel/context/tasks/EP-TECH/us_004/us_004.md]
- Acceptance Criteria:
  - **AC-1**: Given a pull request is opened against the main branch, When the pipeline triggers, Then it runs Angular lint, .NET build, xUnit unit tests, and Playwright E2E smoke tests — failing the PR if any gate fails.
  - **AC-4**: Given the E2E test gate runs, When Playwright tests execute, Then all scaffold smoke tests pass and a test report artifact is uploaded to the workflow run.
- Edge Case:
  - What happens if the Railway deployment fails mid-deploy? — Not in scope for this task (handled in task_002).
  - How are database migration failures handled in CI? — A dedicated migration smoke test runs after deploy; not in scope for this task.

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

| Layer          | Technology     | Version |
| -------------- | -------------- | ------- |
| Frontend       | Angular        | 18.x    |
| Backend        | ASP.NET Core   | .net 10 |
| CI/CD          | GitHub Actions | —       |
| Testing — Unit | xUnit + Moq    | 2.x     |
| Testing — E2E  | Playwright     | 1.x     |
| AI/ML          | N/A            | N/A     |
| Vector Store   | N/A            | N/A     |
| AI Gateway     | N/A            | N/A     |
| Mobile         | N/A            | N/A     |

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

Implement the GitHub Actions CI workflow that runs on every pull request opened or synchronized against the `main` branch. The workflow enforces four sequential quality gates: Angular lint, Angular build, .NET build + xUnit unit tests, and Playwright E2E smoke tests. Any failing gate blocks PR merge. On completion, the Playwright HTML test report is uploaded as a GitHub Actions artifact for inspection.

This task covers only the **pull-request trigger** workflow (`ci.yml`). Deployment concerns are handled in `task_002_infra_cd_deploy_pipeline.md`.

## Dependent Tasks

- US_001 — Angular workspace must exist (ng lint and ng build targets must be defined)
- US_002 — .NET solution must exist (`dotnet build` and `dotnet test` must succeed locally)

## Impacted Components

| Component                       | Action | Notes                                                                        |
| ------------------------------- | ------ | ---------------------------------------------------------------------------- |
| `.github/workflows/ci.yml`      | CREATE | New GitHub Actions CI workflow file                                          |
| `playwright.config.ts` (root)   | MODIFY | Ensure `reporter: 'html'` and output dir `playwright-report/` are configured |
| `package.json` / `angular.json` | VERIFY | `lint` and `build` scripts must match workflow invocation                    |
| `PropelIQ.sln` (or equivalent)  | VERIFY | Solution-level `dotnet test` executes all xUnit test projects                |

## Implementation Plan

1. **Create `.github/workflows/ci.yml`** — Define workflow triggered on `pull_request` events targeting `main` (types: `opened`, `synchronize`, `reopened`).
2. **Angular Lint Job** (`lint-angular`) — Checkout repository, set up Node.js 20, restore npm dependencies (`npm ci`), run `npx ng lint` (exit code must be 0).
3. **Angular Build Job** (`build-angular`) — Depends on `lint-angular`; run `npx ng build --configuration production` to validate production bundle compilation.
4. **Backend Build & Unit Test Job** (`build-test-dotnet`) — Checkout repository, set up .net 10 SDK, run `dotnet restore` then `dotnet build --configuration Release --no-restore`, then `dotnet test --no-build --verbosity normal` for all xUnit test projects.
5. **Playwright E2E Smoke Test Job** (`e2e-playwright`) — Depends on `build-angular` and `build-test-dotnet`; set up Node.js 20, run `npm ci`, install Playwright browsers (`npx playwright install --with-deps chromium`), spin up the Angular dev server, execute `npx playwright test --project=chromium` for smoke tests only.
6. **Upload Playwright Test Report** — Within the `e2e-playwright` job, use `actions/upload-artifact@v4` to upload `playwright-report/` directory as artifact `playwright-report` with retention of 7 days. Run step with `if: always()` to ensure upload even on test failure.
7. **Job Failure Propagation** — Verify that any single failing job causes the overall PR check status to fail, blocking merge (GitHub default behaviour; confirm branch protection rule requires all jobs).
8. **Concurrency Control** — Add `concurrency` group using `${{ github.ref }}` with `cancel-in-progress: true` to cancel duplicate in-flight runs on the same PR branch.

## Current Project State

```
Propel-IQ-Patient-Platform/
├── .github/
│   └── workflows/               # To be created
├── app/                         # Angular 18 workspace (from US_001)
│   ├── angular.json
│   ├── package.json
│   └── src/
├── server/                      # .net 10 solution (from US_002)
│   ├── PropelIQ.sln
│   └── src/
├── docker-compose.yml           # Local dev orchestration
└── playwright.config.ts         # E2E config (from US_003)
```

_Update this tree during execution based on completed dependent tasks._

## Expected Changes

| Action | File Path                  | Description                                                                 |
| ------ | -------------------------- | --------------------------------------------------------------------------- |
| CREATE | `.github/workflows/ci.yml` | GitHub Actions workflow — PR quality gate pipeline                          |
| MODIFY | `playwright.config.ts`     | Ensure `reporter: ['html']` and `outputFolder: 'playwright-report'` are set |

## External References

- [GitHub Actions — Workflow syntax for GitHub Actions](https://docs.github.com/en/actions/writing-workflows/workflow-syntax-for-github-actions)
- [GitHub Actions — `upload-artifact@v4`](https://github.com/actions/upload-artifact)
- [GitHub Actions — `setup-node@v4`](https://github.com/actions/setup-node)
- [GitHub Actions — `setup-dotnet@v4`](https://github.com/actions/setup-dotnet)
- [Playwright — CI Configuration (GitHub Actions)](https://playwright.dev/docs/ci-intro)
- [Playwright — HTML Reporter](https://playwright.dev/docs/test-reporters#html-reporter)
- [Angular CLI — `ng lint` reference](https://angular.dev/tools/cli/lint)
- [.NET CLI — `dotnet test`](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-test)
- [xUnit — Getting started with .NET](https://xunit.net/docs/getting-started/netcore/cmdline)

## Build Commands

```bash
# Angular — lint gate
cd app && npm ci && npx ng lint

# Angular — build gate
cd app && npx ng build --configuration production

# .NET — build gate
cd server && dotnet restore && dotnet build --configuration Release --no-restore

# .NET — unit test gate
cd server && dotnet test --no-build --verbosity normal

# Playwright — E2E smoke tests (after Angular dev server is up)
cd app && npx playwright install --with-deps chromium
cd app && npx playwright test --project=chromium
```

## Implementation Validation Strategy

- [ ] Unit tests pass (`dotnet test` exits 0 locally)
- [ ] Angular lint passes (`ng lint` exits 0 locally)
- [ ] Angular production build succeeds (`ng build --configuration production` exits 0)
- [ ] Playwright scaffold smoke tests pass locally
- [ ] CI workflow triggers automatically on PR opened/synchronize against `main`
- [ ] Any deliberately broken job (e.g., lint error introduced) causes the PR check status to fail
- [ ] Playwright HTML report artifact appears in the Actions run summary after E2E job completes
- [ ] Concurrency cancellation confirmed — opening two PRs from same branch cancels the earlier run

## Implementation Checklist

- [x] Create `.github/workflows/ci.yml` with `pull_request` trigger targeting `main`
- [x] Add `lint-angular` job: `checkout` → `setup-node@v4 (node 20)` → `npm ci` → `npx ng lint`
- [x] Add `build-angular` job (needs `lint-angular`): `checkout` → `setup-node@v4` → `npm ci` → `npx ng build --configuration production`
- [x] Add `build-test-dotnet` job: `checkout` → `setup-dotnet@v4 (.net 10)` → `dotnet restore` → `dotnet build --no-restore` → `dotnet test --no-build`
- [x] Add `e2e-playwright` job (needs `build-angular`, `build-test-dotnet`): Node setup → `npm ci` → `npx playwright install --with-deps chromium` → start Angular dev server → run `npx playwright test --project=chromium`
- [x] Add `upload-artifact@v4` step in `e2e-playwright` job: path `playwright-report/`, name `playwright-report`, `if: always()`, retention-days 7
- [x] Add `concurrency` block at workflow level: group `ci-${{ github.ref }}`, `cancel-in-progress: true`
- [x] Verify `playwright.config.ts` has `reporter: [['html', { outputFolder: 'playwright-report' }]]`
