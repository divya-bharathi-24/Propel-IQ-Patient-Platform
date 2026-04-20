# Task - task_001_fe_angular_performance_optimisation

## Requirement Reference

- **User Story:** us_053 — Frontend Performance & Horizontal Scalability Baseline
- **Story Location:** `.propel/context/tasks/EP-011/us_053/us_053.md`
- **Acceptance Criteria:**
  - AC-1: Angular SPA deployed to Netlify/Vercel CDN achieves LCP <2.5s, FID <100ms, CLS <0.1 measured by Lighthouse/Core Web Vitals tooling on a standard broadband connection (NFR-012).
- **Edge Cases:**
  - Core Web Vitals degrade after a new deployment: Lighthouse CI GitHub Actions check fails the deployment; previous deployment remains live.

---

## Design References (Frontend Tasks Only)

| Reference Type         | Value    |
| ---------------------- | -------- |
| **UI Impact**          | Yes      |
| **Figma URL**          | N/A      |
| **Wireframe Status**   | N/A      |
| **Wireframe Type**     | N/A      |
| **Wireframe Path/URL** | N/A — performance optimisation task, no new screens |
| **Screen Spec**        | N/A      |
| **UXR Requirements**   | N/A      |
| **Design Tokens**      | N/A      |

---

## Applicable Technology Stack

| Layer     | Technology                  | Version |
| --------- | --------------------------- | ------- |
| Frontend  | Angular                     | 18.x    |
| Build     | Angular CLI / esbuild        | 18.x    |
| Hosting   | Netlify / Vercel CDN        | —       |
| CI        | GitHub Actions              | —       |
| Audit     | Lighthouse CI (`@lhci/cli`) | latest  |

**Note:** All code and libraries MUST be compatible with versions listed above.

---

## AI References (AI Tasks Only)

| Reference Type           | Value |
| ------------------------ | ----- |
| **AI Impact**            | No    |
| **AIR Requirements**     | N/A   |
| **AI Pattern**           | N/A   |
| **Prompt Template Path** | N/A   |
| **Guardrails Config**    | N/A   |
| **Model Provider**       | N/A   |

---

## Mobile References (Mobile Tasks Only)

| Reference Type       | Value |
| -------------------- | ----- |
| **Mobile Impact**    | No    |
| **Platform Target**  | N/A   |
| **Min OS Version**   | N/A   |
| **Mobile Framework** | N/A   |

---

## Task Overview

Apply four Angular 18 performance optimisation techniques to meet Core Web Vitals targets (LCP <2.5s, FID <100ms, CLS <0.1), and add a Lighthouse CI GitHub Actions gate that blocks deployment on regression.

**Optimisations:**

1. **Route-based lazy loading** — all feature modules (Patient, Appointment, Admin, Clinical, AI) use `loadComponent` / `loadChildren` in `app.routes.ts`. Only the shell + auth routes are eagerly loaded. This directly reduces initial bundle size (LCP impact).

2. **`@defer` blocks for non-critical UI** — heavy components (AI metrics dashboard, audit log table, document upload panel) use Angular 18's `@defer` with `on viewport` trigger. This removes them from the LCP paint path.

3. **`angular.json` production build configuration** — verify `optimization: true`, `sourceMap: false`, `budgets` set with `maximumWarning: 500kb` and `maximumError: 1mb` for initial bundle; `commonChunk: true`, `namedChunks: false`. Angular CLI 18 uses esbuild by default — no additional config needed for tree-shaking.

4. **Netlify `_headers` / Vercel `vercel.json` cache headers** — static assets (`/assets/`, `*.js`, `*.css`) served with `Cache-Control: public, max-age=31536000, immutable`; `index.html` served with `Cache-Control: no-cache` (ensures new deployments are picked up). This dramatically reduces repeat-visit LCP.

5. **Lighthouse CI** — `lighthouserc.json` config in repo root; GitHub Actions workflow step runs `lhci autorun` after build; asserts LCP ≤2500, FID ≤100, CLS ≤0.1, performance score ≥80.

---

## Dependent Tasks

- No hard dependencies — optimisations apply to the existing Angular workspace.

---

## Impacted Components

| Component | Module | Action |
| --------- | ------ | ------ |
| `app.routes.ts` (existing) | App Routing | MODIFY — convert all feature routes to `loadComponent` / `loadChildren` lazy loading |
| `AiMetricsDashboardPageComponent` (existing, US_050) | Admin | MODIFY — wrap content in `@defer (on viewport)` block |
| `AuditLogPageComponent` (existing, US_047) | Admin | MODIFY — wrap `app-audit-event-table` in `@defer (on viewport)` |
| `DocumentUploadPanelComponent` (existing) | Clinical | MODIFY — wrap in `@defer (on idle)` |
| `angular.json` (existing) | Build Config | MODIFY — verify production budgets; ensure `optimization: true` |
| `_headers` (Netlify) / `vercel.json` (new) | Hosting Config | CREATE — cache-control headers for static assets and `index.html` |
| `lighthouserc.json` (new) | CI Config | CREATE — LCP ≤2500ms, FID ≤100ms, CLS ≤0.1, performance ≥80 thresholds |
| `.github/workflows/lighthouse-ci.yml` (new) | CI | CREATE — GitHub Actions: build → `lhci autorun` → fail on threshold breach |

---

## Implementation Plan

1. **Route-based lazy loading** — update `app.routes.ts`:

   ```typescript
   export const routes: Routes = [
     {
       path: 'auth',
       loadChildren: () =>
         import('./auth/auth.routes').then(m => m.AUTH_ROUTES),
     },
     {
       path: 'patient',
       canActivate: [AuthGuard],
       loadChildren: () =>
         import('./patient/patient.routes').then(m => m.PATIENT_ROUTES),
     },
     {
       path: 'appointment',
       canActivate: [AuthGuard],
       loadChildren: () =>
         import('./appointment/appointment.routes').then(m => m.APPOINTMENT_ROUTES),
     },
     {
       path: 'admin',
       canActivate: [AuthGuard, AdminRoleGuard],
       loadChildren: () =>
         import('./admin/admin.routes').then(m => m.ADMIN_ROUTES),
     },
     {
       path: 'clinical',
       canActivate: [AuthGuard],
       loadChildren: () =>
         import('./clinical/clinical.routes').then(m => m.CLINICAL_ROUTES),
     },
     { path: '', redirectTo: 'auth/login', pathMatch: 'full' },
     { path: '**', redirectTo: 'auth/login' },
   ];
   ```

   Each feature `*.routes.ts` file uses `loadComponent` for individual page components within that module. The auth module is kept synchronous — it is the landing route and must not have an additional lazy-load round-trip on first visit.

2. **`@defer` blocks for non-critical heavy components**:

   ```html
   <!-- AiMetricsDashboardPageComponent template -->
   <div class="page-header">...</div>

   @defer (on viewport) {
     <app-circuit-breaker-status [metrics]="store.operationalMetrics()" />
     <div class="metrics-grid">
       <app-latency-panel [metrics]="store.operationalMetrics()" />
       <app-token-consumption-panel [metrics]="store.operationalMetrics()" />
       <app-error-rate-panel [metrics]="store.operationalMetrics()" />
     </div>
   } @placeholder {
     <div class="metrics-loading-placeholder" style="height: 400px;">
       <mat-spinner diameter="40" />
     </div>
   }
   ```

   The placeholder has a fixed height (`400px`) to prevent CLS — the most common source of CLS regression from deferred content.

3. **`angular.json` production build budgets** — verify/add:

   ```json
   "budgets": [
     {
       "type": "initial",
       "maximumWarning": "500kb",
       "maximumError": "1mb"
     },
     {
       "type": "anyComponentStyle",
       "maximumWarning": "4kb",
       "maximumError": "8kb"
     }
   ]
   ```

   Angular 18 CLI uses esbuild by default (`"builder": "@angular-devkit/build-angular:application"`). Confirm `optimization: true` in the production configuration — this enables: minification, tree-shaking, dead-code elimination, and CSS optimisation.

4. **Netlify `_headers` file** (place in `public/` folder, deployed alongside `index.html`):

   ```
   # Static assets — immutable cache (content-hashed filenames)
   /assets/*
     Cache-Control: public, max-age=31536000, immutable

   /*.js
     Cache-Control: public, max-age=31536000, immutable

   /*.css
     Cache-Control: public, max-age=31536000, immutable

   # HTML entry point — always revalidate for fresh deployment pickup
   /index.html
     Cache-Control: no-cache, no-store, must-revalidate
   ```

   For Vercel, equivalent in `vercel.json`:
   ```json
   {
     "headers": [
       {
         "source": "/(.*\\.(js|css|woff2|png|svg|ico))",
         "headers": [{ "key": "Cache-Control", "value": "public, max-age=31536000, immutable" }]
       },
       {
         "source": "/index.html",
         "headers": [{ "key": "Cache-Control", "value": "no-cache, no-store, must-revalidate" }]
       }
     ]
   }
   ```

5. **`lighthouserc.json`**:

   ```json
   {
     "ci": {
       "collect": {
         "url": ["http://localhost:4200"],
         "startServerCommand": "npx serve dist/propel-iq-patient-platform",
         "numberOfRuns": 3
       },
       "assert": {
         "assertions": {
           "categories:performance": ["error", { "minScore": 0.8 }],
           "largest-contentful-paint": ["error", { "maxNumericValue": 2500 }],
           "total-blocking-time": ["error", { "maxNumericValue": 100 }],
           "cumulative-layout-shift": ["error", { "maxNumericValue": 0.1 }]
         }
       },
       "upload": {
         "target": "temporary-public-storage"
       }
     }
   }
   ```

   Note: Lighthouse CI uses `total-blocking-time` (TBT) as a proxy for FID/INP in automated runs (FID requires real user interaction; TBT is the lab-measurable equivalent).

6. **`.github/workflows/lighthouse-ci.yml`**:

   ```yaml
   name: Lighthouse CI
   on:
     push:
       branches: [main, develop]
     pull_request:
       branches: [main]

   jobs:
     lighthouse:
       runs-on: ubuntu-latest
       steps:
         - uses: actions/checkout@v4
         - uses: actions/setup-node@v4
           with:
             node-version: '20'
             cache: 'npm'
         - run: npm ci
         - run: npx ng build --configuration production
         - run: npm install -g @lhci/cli
         - run: lhci autorun
           env:
             LHCI_GITHUB_APP_TOKEN: ${{ secrets.LHCI_GITHUB_APP_TOKEN }}
   ```

---

## Current Project State

```
app/
  app.routes.ts                           ← EXISTS — MODIFY
  admin/
    pages/
      ai-metrics/
        ai-metrics-dashboard-page.component.ts  ← EXISTS (US_050) — MODIFY
      audit-log/
        audit-log-page.component.ts            ← EXISTS (US_047) — MODIFY
  clinical/
    components/
      document-upload-panel.component.ts       ← EXISTS — MODIFY
angular.json                              ← EXISTS — MODIFY
public/                                   ← may exist (Netlify headers)
vercel.json                               ← may exist
```

---

## Expected Changes

| Action | File Path | Description |
| ------ | --------- | ----------- |
| MODIFY | `app/app.routes.ts` | Convert all feature routes to `loadChildren` / `loadComponent` lazy loading |
| MODIFY | `app/admin/pages/ai-metrics/ai-metrics-dashboard-page.component.ts` | Wrap metric panels in `@defer (on viewport)` with fixed-height placeholder (prevent CLS) |
| MODIFY | `app/admin/pages/audit-log/audit-log-page.component.ts` | Wrap `app-audit-event-table` in `@defer (on viewport)` with placeholder |
| MODIFY | `app/clinical/components/document-upload-panel.component.ts` | Wrap in `@defer (on idle)` |
| MODIFY | `angular.json` | Verify `optimization: true`; set `maximumWarning: 500kb`, `maximumError: 1mb` bundle budgets |
| CREATE | `public/_headers` | Netlify cache-control rules: immutable for assets, no-cache for `index.html` |
| CREATE | `vercel.json` | Vercel equivalent cache headers |
| CREATE | `lighthouserc.json` | LCP ≤2500ms, TBT ≤100ms, CLS ≤0.1, performance score ≥80 |
| CREATE | `.github/workflows/lighthouse-ci.yml` | GitHub Actions: build → `lhci autorun` → fail on threshold breach |

---

## External References

- [Angular 18 — Route-based Lazy Loading with `loadComponent`](https://angular.dev/guide/routing/lazy-loading) — `loadComponent` for standalone component lazy loading
- [Angular 18 — `@defer` Blocks](https://angular.dev/guide/defer) — `on viewport`, `on idle` triggers; `@placeholder` with fixed dimensions for CLS prevention
- [Angular CLI 18 — esbuild builder](https://angular.dev/tools/cli/build-system-migration) — `@angular-devkit/build-angular:application` uses esbuild by default
- [Lighthouse CI (`@lhci/cli`)](https://github.com/GoogleChrome/lighthouse-ci) — `lhci autorun`; `assert` configuration; GitHub status checks
- [NFR-012 (design.md)](../../../docs/design.md) — LCP <2.5s, FID <100ms, CLS <0.1 on standard broadband
- [TR-014 (design.md)](../../../docs/design.md) — Deploy to Netlify/Vercel CDN

---

## Build Commands

- Refer to [`.propel/build/`](../../../../build/) for applicable Angular build commands.

---

## Implementation Validation Strategy

- [ ] `npx ng build --configuration production` — verify no budget errors; initial bundle < 1MB
- [ ] Run `npx lhci autorun` locally against production build — LCP ≤2500ms, TBT ≤100ms, CLS ≤0.1
- [ ] Verify `app.routes.ts` — all non-auth routes use `loadChildren` or `loadComponent`; no feature component imported at the top of the file
- [ ] Verify `@defer` placeholder elements have explicit `height` set (prevents CLS)
- [ ] Verify `_headers` / `vercel.json` cache rules: `index.html` = no-cache; `.js`/`.css` = immutable

---

## Implementation Checklist

- [ ] Modify `app.routes.ts`: convert all feature module routes (`patient`, `appointment`, `admin`, `clinical`) to `loadChildren`; keep `auth` eager; verify no feature component eagerly imported in `AppComponent`
- [ ] Modify `AiMetricsDashboardPageComponent`, `AuditLogPageComponent`, `DocumentUploadPanelComponent`: wrap heavy content in `@defer` (`on viewport` or `on idle`); add `@placeholder` with fixed pixel height to prevent CLS
- [ ] Verify `angular.json` production configuration: `optimization: true`; bundle budget `maximumError: 1mb`; `sourceMap: false`
- [ ] Create `public/_headers` (Netlify) and `vercel.json` (Vercel) with immutable cache for hashed assets, no-cache for `index.html`
- [ ] Create `lighthouserc.json`: assert LCP ≤2500, TBT ≤100, CLS ≤0.1, performance ≥80 (3 runs, averaged)
- [ ] Create `.github/workflows/lighthouse-ci.yml`: build → `lhci autorun`; job fails on assertion breach; previous deployment stays live
