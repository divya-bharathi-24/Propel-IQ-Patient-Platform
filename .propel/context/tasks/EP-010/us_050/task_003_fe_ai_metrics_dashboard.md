# Task - task_003_fe_ai_metrics_dashboard

## Requirement Reference

- **User Story:** us_050 — AI Operational Controls — Circuit Breaker, Token Budget & Model Swap
- **Story Location:** `.propel/context/tasks/EP-010/us_050/us_050.md`
- **Acceptance Criteria:**
  - AC-4: Operator views AI metrics dashboard showing token consumption per request, p95 latency (target ≤30s), error rates, circuit breaker trips, and confidence score distributions — all updated within 60 seconds (AIR-O04).
  - AC-3 (operator UI): Operator can change AI model version from the settings panel; change takes effect within 5 minutes.
- **Edge Cases:**
  - p95 latency panel shows "Insufficient Data" label (not 0 or null) when the backend returns `null` p95LatencyMs.
  - Circuit breaker status shows a prominent "OPEN — Manual Review Required" banner with elapsed open time when `cbOpen = true`, not just a table row.

---

## Design References (Frontend Tasks Only)

| Reference Type         | Value    |
| ---------------------- | -------- |
| **UI Impact**          | Yes      |
| **Figma URL**          | N/A      |
| **Wireframe Status**   | PENDING  |
| **Wireframe Type**     | N/A      |
| **Wireframe Path/URL** | TODO: Provide wireframe for AI Metrics Dashboard page (SCR-AI-METRICS) |
| **Screen Spec**        | N/A      |
| **UXR Requirements**   | N/A      |
| **Design Tokens**      | Angular Material 18.x design tokens (colors, typography, spacing from existing `designsystem.md`) |

> **Wireframe Status: PENDING** — Implement using Angular Material cards and standard admin panel layout consistent with existing admin pages (AuditLogPage, UserManagementPage). Validate against wireframe when provided.

---

## Applicable Technology Stack

| Layer     | Technology                          | Version |
| --------- | ----------------------------------- | ------- |
| Frontend  | Angular                             | 18.x    |
| State     | NgRx Signals (`signalStore`)        | 18.x    |
| UI        | Angular Material                    | 18.x    |
| HTTP      | Angular HttpClient                  | 18.x    |
| Routing   | Angular Router                      | 18.x    |

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

Build the **AI Metrics Dashboard** page in the Angular admin panel. The page auto-refreshes every 60 seconds via an RxJS `interval` stream and displays five metric panels:

1. **Circuit Breaker Status** — prominent banner when open (`cbOpen = true`); "CLOSED — Healthy" badge otherwise; trip count last 24h.
2. **p95 Latency** — progress-bar style indicator with a 30-second target line; "Insufficient Data" fallback label.
3. **Token Consumption** — average prompt tokens, average response tokens, combined per request.
4. **Error Rate** — percentage display with colour-coded threshold (green < 2%, amber 2–5%, red > 5%).
5. **Model Version & Config** — read-only display of current active model version + inline form to update it (calls POST /api/admin/ai-config/model-version).

All data flows through `AiMetricsDashboardStore` (NgRx Signals) with a `loadOperationalMetrics()` action. The existing `AuditLogPageComponent` pattern is used as the structural template for the page.

---

## Dependent Tasks

- `EP-010/us_050/task_002_be_ai_operational_metrics_api.md` — `GET /api/admin/ai-metrics/operational` and `POST /api/admin/ai-config/model-version` must exist.

---

## Impacted Components

| Component | Module | Action |
| --------- | ------ | ------ |
| `AiMetricsDashboardPageComponent` (new) | Admin / AI Metrics | CREATE — page shell: auto-refresh every 60 seconds; route guard `AdminRoleGuard` |
| `CircuitBreakerStatusComponent` (new) | Admin / AI Metrics | CREATE — banner panel: `cbOpen` signal drives open/closed display; elapsed open time formatted |
| `LatencyPanelComponent` (new) | Admin / AI Metrics | CREATE — `mat-progress-bar` (value= p95/30000 * 100); "Insufficient Data" label when null |
| `TokenConsumptionPanelComponent` (new) | Admin / AI Metrics | CREATE — shows avgPromptTokens, avgResponseTokens, combined total |
| `ErrorRatePanelComponent` (new) | Admin / AI Metrics | CREATE — percentage with colour-coded chip (green/amber/red thresholds) |
| `ModelVersionPanelComponent` (new) | Admin / AI Metrics | CREATE — current model version display + reactive form to submit version change |
| `AiMetricsDashboardStore` (new) | Admin / AI Metrics | CREATE — NgRx Signals `signalStore`: `operationalMetrics` signal, `loadOperationalMetrics()`, `updateModelVersion()`, `isLoading`, `error`, `lastRefreshed` |
| `AiMetricsDashboardService` (new) | Admin / AI Metrics | CREATE — `HttpClient` calls: `getOperationalMetrics()` → GET /api/admin/ai-metrics/operational; `updateModelVersion(version)` → POST /api/admin/ai-config/model-version |
| `admin.routes.ts` (existing) | Admin Routing | MODIFY — add `/admin/ai-metrics` route with `AiMetricsDashboardPageComponent` + `AdminRoleGuard` |

---

## Implementation Plan

1. **`AiMetricsDashboardService`** — HTTP client wrapper:

   ```typescript
   @Injectable({ providedIn: 'root' })
   export class AiMetricsDashboardService {
     private readonly http = inject(HttpClient);
     private readonly baseUrl = '/api/admin/ai-metrics';

     getOperationalMetrics(): Observable<AiOperationalMetricsSummary> {
       return this.http.get<AiOperationalMetricsSummary>(`${this.baseUrl}/operational`);
     }

     updateModelVersion(modelVersion: string): Observable<void> {
       return this.http.post<void>('/api/admin/ai-config/model-version', { modelVersion });
     }
   }
   ```

2. **`AiMetricsDashboardStore`** — NgRx Signals store:

   ```typescript
   export const AiMetricsDashboardStore = signalStore(
     { providedIn: 'root' },
     withState<AiMetricsDashboardState>({
       operationalMetrics: null as AiOperationalMetricsSummary | null,
       isLoading: false,
       error: null as string | null,
       lastRefreshed: null as Date | null,
     }),
     withMethods((store, svc = inject(AiMetricsDashboardService)) => ({
       loadOperationalMetrics: rxMethod<void>(
         pipe(
           tap(() => patchState(store, { isLoading: true, error: null })),
           switchMap(() =>
             svc.getOperationalMetrics().pipe(
               tapResponse({
                 next: m => patchState(store, { operationalMetrics: m, isLoading: false, lastRefreshed: new Date() }),
                 error: (e: Error) => patchState(store, { isLoading: false, error: e.message }),
               })
             )
           )
         )
       ),
       updateModelVersion: rxMethod<string>(
         pipe(
           switchMap(version =>
             svc.updateModelVersion(version).pipe(
               tapResponse({
                 next: () => {
                   // Reload metrics after model version update
                   patchState(store, { isLoading: true });
                   svc.getOperationalMetrics().subscribe(m =>
                     patchState(store, { operationalMetrics: m, isLoading: false })
                   );
                 },
                 error: (e: Error) => patchState(store, { error: e.message }),
               })
             )
           )
         )
       ),
     }))
   );
   ```

3. **`AiMetricsDashboardPageComponent`** — page shell with 60-second auto-refresh:

   ```typescript
   @Component({
     selector: 'app-ai-metrics-dashboard',
     standalone: true,
     template: `
       <div class="page-header">
         <h1>AI Operational Metrics</h1>
         <span class="last-refreshed">Last updated: {{ store.lastRefreshed() | date:'HH:mm:ss' }}</span>
       </div>

       <app-circuit-breaker-status [metrics]="store.operationalMetrics()" />

       <div class="metrics-grid">
         <app-latency-panel [metrics]="store.operationalMetrics()" />
         <app-token-consumption-panel [metrics]="store.operationalMetrics()" />
         <app-error-rate-panel [metrics]="store.operationalMetrics()" />
       </div>

       <app-model-version-panel
         [metrics]="store.operationalMetrics()"
         (modelVersionChange)="store.updateModelVersion($event)" />
     `,
   })
   export class AiMetricsDashboardPageComponent implements OnInit, OnDestroy {
     protected readonly store = inject(AiMetricsDashboardStore);
     private refreshSub?: Subscription;

     ngOnInit(): void {
       this.store.loadOperationalMetrics();
       // Auto-refresh every 60 seconds
       this.refreshSub = interval(60_000).subscribe(() => this.store.loadOperationalMetrics());
     }

     ngOnDestroy(): void {
       this.refreshSub?.unsubscribe();
     }
   }
   ```

4. **`CircuitBreakerStatusComponent`** — prominent alert banner:

   ```typescript
   // Template:
   // <div *ngIf="metrics?.cbOpen" class="cb-open-banner mat-elevation-z2">
   //   <mat-icon color="warn">warning</mat-icon>
   //   <span>CIRCUIT BREAKER OPEN — AI provider unavailable. Manual review required.</span>
   //   <span class="trip-count">{{ metrics?.cbTrips24h }} trip(s) in last 24h</span>
   // </div>
   // <div *ngIf="!metrics?.cbOpen" class="cb-closed-badge">
   //   <mat-icon color="accent">check_circle</mat-icon> CLOSED — Healthy
   // </div>
   ```

5. **`LatencyPanelComponent`** — p95 latency with target line at 30s:

   ```typescript
   // Computed: p95Percent = metrics ? Math.min((metrics.p95LatencyMs / 30_000) * 100, 100) : 0
   // Template:
   // <mat-card>
   //   <mat-card-title>p95 Latency</mat-card-title>
   //   <ng-container *ngIf="metrics?.p95LatencyMs !== null; else noData">
   //     <span>{{ metrics.p95LatencyMs | number:'1.0-0' }}ms</span>
   //     <mat-progress-bar [value]="p95Percent" [color]="metrics.p95LatencyMs > 30000 ? 'warn' : 'primary'" />
   //     <small>Target: ≤30s</small>
   //   </ng-container>
   //   <ng-template #noData><span class="muted">Insufficient Data</span></ng-template>
   // </mat-card>
   ```

6. **`ModelVersionPanelComponent`** — model version read + update form:

   ```typescript
   // Shows: current active model version (from metrics.currentModelVersion)
   // Inline reactive form with mat-select for version dropdown (populated from hardcoded allowedVersions
   // or from API — use hardcoded for Phase 1)
   // On submit: emits (modelVersionChange) output
   // Shows success snackbar: "Model version updated — effective within 5 minutes"
   ```

7. **`admin.routes.ts` addition**:

   ```typescript
   {
     path: 'ai-metrics',
     component: AiMetricsDashboardPageComponent,
     canActivate: [AdminRoleGuard],
     title: 'AI Metrics Dashboard',
   }
   ```

---

## Current Project State

```
app/
  admin/
    pages/
      audit-log/
        audit-log-page.component.ts     ← EXISTS — reference pattern
      user-management/
        user-management-page.component.ts ← EXISTS
    services/
      admin-user.service.ts             ← EXISTS
    stores/
      admin-user.store.ts               ← EXISTS
    admin.routes.ts                     ← EXISTS — MODIFY
```

---

## Expected Changes

| Action | File Path | Description |
| ------ | --------- | ----------- |
| CREATE | `app/admin/pages/ai-metrics/ai-metrics-dashboard-page.component.ts` | Page shell; 60-second interval auto-refresh; injects `AiMetricsDashboardStore` |
| CREATE | `app/admin/pages/ai-metrics/components/circuit-breaker-status.component.ts` | Open/closed banner; `cbOpen` drives `mat-card` colour and warning icon |
| CREATE | `app/admin/pages/ai-metrics/components/latency-panel.component.ts` | `mat-progress-bar`; `p95LatencyMs` null → "Insufficient Data"; target line at 30s |
| CREATE | `app/admin/pages/ai-metrics/components/token-consumption-panel.component.ts` | `mat-card` showing avg prompt + response tokens |
| CREATE | `app/admin/pages/ai-metrics/components/error-rate-panel.component.ts` | Error rate percentage with colour-coded `mat-chip` |
| CREATE | `app/admin/pages/ai-metrics/components/model-version-panel.component.ts` | Read-only model version display + `mat-select` update form; `(modelVersionChange)` output |
| CREATE | `app/admin/stores/ai-metrics-dashboard.store.ts` | NgRx Signals store: `operationalMetrics` signal, `loadOperationalMetrics()`, `updateModelVersion()` |
| CREATE | `app/admin/services/ai-metrics-dashboard.service.ts` | `HttpClient` wrapper for GET operational metrics + POST model version |
| CREATE | `app/admin/models/ai-operational-metrics-summary.model.ts` | TS interface matching `AiOperationalMetricsSummaryResponse` BE DTO |
| MODIFY | `app/admin/admin.routes.ts` | Add `/admin/ai-metrics` route with `AdminRoleGuard` |

---

## External References

- [Angular 18 — Standalone Components](https://angular.dev/guide/components/importing) — standalone component imports pattern
- [NgRx Signals 18 — signalStore + rxMethod](https://ngrx.io/guide/signals/signal-store) — `withMethods`, `rxMethod`, `tapResponse` for HTTP state management
- [Angular Material 18 — mat-progress-bar](https://material.angular.io/components/progress-bar/overview) — `[value]` input (0–100) for latency gauge
- [Angular Material 18 — mat-card](https://material.angular.io/components/card/overview) — metric panel cards
- [RxJS — interval](https://rxjs.dev/api/index/function/interval) — 60-second auto-refresh polling
- [AIR-O04 (design.md)](../../../docs/design.md) — Metrics updated within 60 seconds; token consumption, p95 latency, error rates, CB trips, confidence distributions

---

## Build Commands

- Refer to [`.propel/build/`](../../../../build/) for applicable Angular build and test commands.

---

## Implementation Validation Strategy

- [ ] Auto-refresh: `AiMetricsDashboardStore.loadOperationalMetrics()` called on page init and every 60 seconds via `interval`
- [ ] Circuit breaker open: banner with "OPEN — Manual Review Required" is visible when `cbOpen = true`
- [ ] Circuit breaker closed: green "CLOSED — Healthy" badge shown when `cbOpen = false`
- [ ] p95 latency panel shows "Insufficient Data" text (not a zero value) when API returns `null` p95LatencyMs
- [ ] p95 latency exceeding 30,000ms turns the `mat-progress-bar` `color="warn"` (red)
- [ ] Error rate panel shows amber chip for 2–5% and red chip for >5%
- [ ] Model version form submits correctly; success snackbar "effective within 5 minutes" shown
- [ ] Route `/admin/ai-metrics` requires Admin role — `AdminRoleGuard` redirects non-Admin users

---

## Implementation Checklist

- [ ] Create `AiOperationalMetricsSummary` TypeScript interface: `cbOpen`, `cbTrips24h`, `p95LatencyMs` (nullable), `avgPromptTokens`, `avgResponseTokens`, `errorRate`, `currentModelVersion`, `status`
- [ ] Create `AiMetricsDashboardService`: `getOperationalMetrics()` and `updateModelVersion(version)` HTTP methods
- [ ] Create `AiMetricsDashboardStore`: `signalStore` with `operationalMetrics`, `isLoading`, `error`, `lastRefreshed`; `loadOperationalMetrics` rxMethod; `updateModelVersion` rxMethod
- [ ] Create `AiMetricsDashboardPageComponent`: inject store; call `loadOperationalMetrics()` on `ngOnInit`; set up 60-second `interval` subscription; unsubscribe on `ngOnDestroy`
- [ ] Create `CircuitBreakerStatusComponent`: `cbOpen` → prominent `mat-card` warn banner with trip count; `cbOpen = false` → green check badge
- [ ] Create `LatencyPanelComponent`: `mat-progress-bar` with target line; null guard → "Insufficient Data" `<ng-template>`
- [ ] Create `ErrorRatePanelComponent`: colour-coded `mat-chip` based on error rate thresholds (< 2% green, 2–5% amber, > 5% red)
- [ ] Create `ModelVersionPanelComponent`: current version display + `mat-select` form; submit calls `(modelVersionChange)` output; success snackbar
- [ ] Modify `admin.routes.ts`: add `/admin/ai-metrics` with `AiMetricsDashboardPageComponent` and `AdminRoleGuard`
