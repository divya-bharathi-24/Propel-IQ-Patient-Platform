# Task - task_001_fe_angular_workspace_setup

## Requirement Reference

- User Story: [us_001]
- Story Location: [.propel/context/tasks/EP-TECH/us_001/us_001.md]
- Acceptance Criteria:
  - AC1: Given I clone the repository, When I run `npm install && ng serve`, Then the Angular 18 dev server starts without errors and displays a bootstrapped shell application.
  - AC2: Given the Angular workspace is initialized, When I inspect the project structure, Then standalone components are the default, Angular Signals are enabled, and NgRx Signals store is scaffolded with at least one feature slice.
  - AC3: Given the workspace is set up, When I run `ng build --configuration production`, Then the production build completes with LCP-optimized bundle output and no TypeScript compile errors.
  - AC4: Given the workspace is configured, When I run `ng lint`, Then ESLint rules pass with zero errors across all scaffold files.
- Edge Case:
  - Incompatible Node.js version: Document minimum Node.js version in README; enforce with `engines` field in `package.json` (min Node 20 LTS for Angular 18).
  - Multiple developers sharing the dev server port: Port 4200 is the default; document overriding with `ng serve --port <N>` or `angular.json` `port` option.

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

| Layer          | Technology        | Version |
|----------------|-------------------|---------|
| Frontend       | Angular           | 18.x    |
| Frontend State | NgRx Signals      | 18.x    |
| Testing — E2E  | Playwright        | 1.x     |
| CI/CD          | GitHub Actions    | —       |

**Note**: All code and libraries MUST be compatible with versions above.

## AI References (AI Tasks Only)

| Reference Type       | Value |
|----------------------|-------|
| **AI Impact**        | No    |
| **AIR Requirements** | N/A   |
| **AI Pattern**       | N/A   |
| **Prompt Template Path** | N/A |
| **Guardrails Config**| N/A   |
| **Model Provider**   | N/A   |

## Mobile References (Mobile Tasks Only)

| Reference Type     | Value |
|--------------------|-------|
| **Mobile Impact**  | No    |
| **Platform Target**| N/A   |
| **Min OS Version** | N/A   |
| **Mobile Framework**| N/A  |

## Task Overview

Initialize the Angular 18 Single-Page Application (SPA) workspace for the Unified Patient Access & Clinical Intelligence Platform. The workspace must use standalone components as the default, enable Angular Signals for reactive state, scaffold an NgRx Signals store with at least one feature slice, configure ESLint with zero-error rules, enforce a minimum Node.js engine version, and produce an LCP-optimized production bundle (`ng build --configuration production`). This task is the foundational frontend artifact that all patient-facing and staff-facing UI feature stories depend on. It aligns with TR-001 (Angular 18 with standalone components and signals) and TR-002 (.net 10 modular architecture for the backend, to be integrated later).

## Dependent Tasks

- None — This is the first task in EP-TECH; it is the foundational enabler for all subsequent frontend tasks.

## Impacted Components

| Component / Module        | Action    | Notes                                          |
|---------------------------|-----------|------------------------------------------------|
| `app/` (workspace root)   | CREATE    | Angular 18 CLI-generated workspace             |
| `app/angular.json`        | CREATE    | Workspace configuration; port, build budgets   |
| `app/package.json`        | CREATE    | Dependencies, scripts, `engines` Node constraint |
| `app/tsconfig.json`       | CREATE    | Root TypeScript configuration                  |
| `app/tsconfig.app.json`   | CREATE    | App-specific strict TypeScript config          |
| `app/.eslintrc.json`      | CREATE    | ESLint rules for Angular 18 (or `eslint.config.mjs`) |
| `app/src/main.ts`         | CREATE    | Application bootstrap with `bootstrapApplication()` |
| `app/src/app/app.config.ts` | CREATE  | `ApplicationConfig` with `provideRouter`, `provideHttpClient`, `provideStore()` |
| `app/src/app/app.component.ts` | CREATE | Root standalone `AppComponent` with `RouterOutlet` |
| `app/src/app/store/app.store.ts` | CREATE | NgRx Signals feature slice — `app` feature (e.g., loading flag) |
| `README.md`               | MODIFY    | Add "Getting Started" section with Node.js minimum version, `npm install && ng serve` instructions, and port override documentation |

## Implementation Plan

1. **Scaffold Angular 18 workspace** using Angular CLI (`ng new`) with `--standalone` flag (default in Angular 18), `--routing`, `--style=scss`, and `--strict` TypeScript settings. This satisfies AC1 and AC2 (standalone default, signals available).

2. **Verify Angular Signals availability** — Angular Signals (`signal()`, `computed()`, `effect()`) are built into Angular 17+ with no additional package required. Confirm that `@angular/core` version resolves to `18.x` in `package.json`. No extra configuration is needed.

3. **Install and configure NgRx Signals store** — Add `@ngrx/signals` (version `18.x`) via `npm install @ngrx/signals`. Scaffold a minimal feature slice `AppStore` using `signalStore()` in `app/src/app/store/app.store.ts` with at least one state property (e.g., `isLoading: boolean`) and corresponding `patchState` updater. Provide the store in `app.config.ts` using `withState()`. This satisfies AC2.

4. **Configure ESLint** — Run `ng add @angular-eslint/schematics` to install and configure `@angular-eslint` rules. Verify `ng lint` produces zero errors on the scaffold. This satisfies AC4.

5. **Enforce Node.js engine constraint** — Add `"engines": { "node": ">=20.0.0" }` to `package.json`. Document the minimum version in the `README.md` "Prerequisites" section. This satisfies the incompatible Node.js edge case.

6. **Optimize production build for LCP** — In `angular.json` under `configurations.production`:
   - Set `optimization: true` (default).
   - Set `sourceMap: false`.
   - Set `namedChunks: false`, `extractLicenses: true`, `outputHashing: 'all'`.
   - Configure build budgets: `maximumWarning: "500kb"`, `maximumError: "1mb"` for the initial bundle.
   - Set `buildOptimizer: true` (default in Angular 18).
   - Enable `@angular/build` (esbuild-based) builder for faster, smaller bundles: use `@angular-devkit/build-angular:application` builder.
   This satisfies AC3 (LCP-optimized bundle, no TypeScript errors).

7. **Document port configuration** — In `angular.json` under `serve.options`, set `"port": 4200`. Add a note in `README.md` describing how to override: `ng serve --port 4201`. This satisfies the multi-developer port conflict edge case.

8. **Validate all ACs** — Run the four acceptance criteria commands in sequence: `npm install`, `ng serve` (check browser at http://localhost:4200), `ng build --configuration production`, `ng lint`. Confirm all pass with zero errors.

## Current Project State

```
Propel-IQ-Patient-Platform/
├── .github/
│   ├── instructions/
│   └── prompts/
├── .propel/
│   ├── context/
│   │   ├── docs/
│   │   │   ├── design.md
│   │   │   ├── spec.md
│   │   │   ├── epics.md
│   │   │   └── models.md
│   │   └── tasks/
│   │       └── EP-TECH/
│   │           └── us_001/
│   │               ├── us_001.md
│   │               └── task_001_fe_angular_workspace_setup.md  ← THIS TASK
│   ├── prompts/
│   ├── rules/
│   └── templates/
├── BRD Unified Patient Acces.md
└── README.md
```

*The `app/` Angular workspace folder does not yet exist. It will be created as part of this task.*

## Expected Changes

| Action  | File Path                                             | Description                                              |
|---------|-------------------------------------------------------|----------------------------------------------------------|
| CREATE  | `app/`                                                | Angular 18 SPA workspace root                            |
| CREATE  | `app/angular.json`                                    | Workspace config: builder, build budgets, port 4200, production optimizations |
| CREATE  | `app/package.json`                                    | NPM dependencies: `@angular/core@18.x`, `@ngrx/signals@18.x`, `@angular-eslint`; `engines.node >=20.0.0`; scripts: `start`, `build`, `lint`, `test` |
| CREATE  | `app/tsconfig.json`                                   | Root strict TypeScript config (`strict: true`, `strictTemplates: true`) |
| CREATE  | `app/tsconfig.app.json`                               | App-specific TypeScript config extending root            |
| CREATE  | `app/.eslintrc.json` or `app/eslint.config.mjs`       | Angular ESLint rules; zero warnings on scaffold          |
| CREATE  | `app/src/main.ts`                                     | `bootstrapApplication(AppComponent, appConfig)` entrypoint |
| CREATE  | `app/src/app/app.config.ts`                           | `ApplicationConfig` wiring: `provideRouter(routes)`, `provideHttpClient()`, `withComponentInputBinding()` |
| CREATE  | `app/src/app/app.component.ts`                        | Root standalone `AppComponent` with `<router-outlet>` template |
| CREATE  | `app/src/app/app.routes.ts`                           | Root route array (empty shell, ready for feature routes) |
| CREATE  | `app/src/app/store/app.store.ts`                      | `AppStore` using `signalStore()` with `isLoading: boolean` state and `setLoading` updater |
| MODIFY  | `README.md`                                           | Add "Prerequisites" (Node ≥20), "Getting Started" (`npm install && ng serve`), and "Port Configuration" sections |

## External References

- [Angular 18 Standalone Components Guide](https://angular.dev/guide/components/importing)
- [Angular Signals Overview](https://angular.dev/guide/signals)
- [NgRx Signals Store — Getting Started](https://ngrx.io/guide/signals/signal-store)
- [Angular 18 Release Notes](https://blog.angular.io/angular-v18-is-now-available-e79d5ac0affe)
- [Angular ESLint Setup](https://github.com/angular-eslint/angular-eslint#readme)
- [Angular CLI — Application Builder (esbuild)](https://angular.dev/tools/cli/build)
- [Core Web Vitals — LCP Optimization](https://web.dev/lcp/)
- [NFR-012: LCP < 2.5s, FID < 100ms, CLS < 0.1 — design.md#performance]
- [TR-001: Angular 18 with standalone components and signals — design.md#technical-requirements]

## Build Commands

```bash
# Install dependencies
npm install

# Start development server (default port 4200)
ng serve

# Start on a custom port
ng serve --port 4201

# Production build (LCP-optimized)
ng build --configuration production

# Lint all files
ng lint

# Run unit tests
ng test
```

## Implementation Validation Strategy

- [ ] `npm install` completes with zero `npm ERR!` errors and no critical vulnerability alerts
- [ ] `ng serve` starts at http://localhost:4200 and the browser displays the bootstrapped Angular shell application with no console errors
- [ ] `src/app/store/app.store.ts` exports `AppStore` using `signalStore()` with at least one state property and can be injected into `AppComponent`
- [ ] `ng build --configuration production` exits with code 0 and outputs a production bundle under `dist/`; no TypeScript compile errors; initial bundle size < 1 MB (budget configured in `angular.json`)
- [ ] `ng lint` exits with code 0 and reports zero errors across all scaffold `.ts` and `.html` files
- [ ] `package.json` `engines.node` field is set to `">=20.0.0"`
- [ ] Integration tests pass (if applicable)
- [ ] **[UI Tasks]** Visual comparison against wireframe completed at 375px, 768px, 1440px — N/A (no UI screens in this task)

## Implementation Checklist

- [ ] Run `ng new propel-iq-patient-platform --standalone --routing --style=scss --strict` inside the `app/` directory (or scaffold equivalent `app/` folder structure)
- [ ] Install NgRx Signals: `npm install @ngrx/signals@18` and verify `@ngrx/signals` appears in `package.json` dependencies at version `18.x`
- [ ] Create `app/src/app/store/app.store.ts` — define `AppStore` with `signalStore()`, `withState<AppState>({ isLoading: false })`, and `withMethods()` providing `setLoading(loading: boolean)` updater
- [ ] Add `provideStore()` / NgRx Signals provider in `app.config.ts`; verify `AppStore` is injectable in `AppComponent`
- [ ] Run `ng add @angular-eslint/schematics` and confirm `.eslintrc.json` (or `eslint.config.mjs`) is generated; run `ng lint` and fix any auto-fixable issues until zero errors remain
- [ ] Add `"engines": { "node": ">=20.0.0" }` to `package.json` and update `README.md` with the minimum Node.js version and port override instructions
- [ ] Update `angular.json` production configuration: set `sourceMap: false`, `namedChunks: false`, `outputHashing: 'all'`, `buildOptimizer: true`, and configure budget thresholds (`maximumWarning: "500kb"`, `maximumError: "1mb"`)
- [ ] Run all four AC commands (`npm install`, `ng serve`, `ng build --configuration production`, `ng lint`) and confirm all exit with code 0 and zero errors
