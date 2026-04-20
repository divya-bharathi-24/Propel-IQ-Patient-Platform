# Task - TASK_001

## Requirement Reference

- **User Story**: US_016 — Patient Dashboard Aggregation
- **Story Location**: `.propel/context/tasks/EP-002/us_016/us_016.md`
- **Acceptance Criteria**:
  - AC-1: Given authenticated Patient navigates to dashboard, Then see upcoming appointments list with date, time, specialty, and status (Booked/Arrived/Completed/Cancelled)
  - AC-2: Given a pending intake form exists for a booked appointment, Then show "Complete Intake" call-to-action linked to the correct appointment
  - AC-3: Given previously uploaded clinical documents exist, Then show document upload history with file names, upload dates, and processing statuses (Pending/Processing/Completed/Failed)
  - AC-4: Given 360° view has been verified by staff, Then show "360° View Ready" indicator; otherwise show "Pending Staff Verification"
- **Edge Cases**:
  - No upcoming appointments: show empty state with "Book Appointment" CTA navigating to `/appointments/book`
  - Document with `processingStatus = Failed`: show "Processing Failed" with a "Retry Upload" action link

## Design References (Frontend Tasks Only)

| Reference Type | Value |
|----------------|-------|
| **UI Impact** | Yes |
| **Figma URL** | N/A |
| **Wireframe Status** | PENDING |
| **Wireframe Type** | N/A |
| **Wireframe Path/URL** | TODO: Upload to `.propel/context/wireframes/Hi-Fi/wireframe-SCR-XXX-patient-dashboard.[html\|png\|jpg]` or provide external URL |
| **Screen Spec** | N/A (figma_spec.md not yet generated) |
| **UXR Requirements** | N/A (figma_spec.md not yet generated) |
| **Design Tokens** | N/A (designsystem.md not yet generated) |

### **CRITICAL: Wireframe Implementation Requirement**

**Wireframe Status = PENDING:** When wireframe becomes available, implementation MUST:

- Match layout, spacing, typography, and colors from the wireframe
- Implement all states: Default (loaded), Loading (skeleton), Empty (no appointments), Error (API failure), Validation
- Validate implementation against wireframe at breakpoints: 375px, 768px, 1440px
- Run `/analyze-ux` after implementation to verify pixel-perfect alignment

## Applicable Technology Stack

| Layer | Technology | Version |
|-------|------------|---------|
| Frontend | Angular | 18.x |
| Frontend State | NgRx Signals | 18.x |
| Backend | ASP.NET Core Web API | .NET 9 |
| Database | PostgreSQL | 16+ |
| Library | Angular Router | 18.x |
| AI/ML | N/A | N/A |
| Vector Store | N/A | N/A |
| AI Gateway | N/A | N/A |
| Mobile | N/A | N/A |

**Note**: All code and libraries MUST be compatible with versions above.

## AI References (AI Tasks Only)

| Reference Type | Value |
|----------------|-------|
| **AI Impact** | No |
| **AIR Requirements** | N/A |
| **AI Pattern** | N/A |
| **Prompt Template Path** | N/A |
| **Guardrails Config** | N/A |
| **Model Provider** | N/A |

## Mobile References (Mobile Tasks Only)

| Reference Type | Value |
|----------------|-------|
| **Mobile Impact** | No |
| **Platform Target** | N/A |
| **Min OS Version** | N/A |
| **Mobile Framework** | N/A |

## Task Overview

Implement the `PatientDashboardComponent` in Angular 18 that aggregates four distinct healthcare data panels for the authenticated patient in a single page load. The component calls `GET /api/patient/dashboard` once (single-call aggregation), receives the `PatientDashboardDto`, and renders four sections:

1. **Upcoming Appointments panel** — sortable list with date/time, specialty, and status badge; empty state with "Book Appointment" CTA
2. **Pending Intake panel** — lists appointments with no completed intake record; each item has a "Complete Intake" button routing to `/intake/{appointmentId}`
3. **Document Upload History panel** — scrollable list of clinical document uploads with file name, upload date, and processing status chip; "Processing Failed" rows include a "Retry" action
4. **360° View indicator** — a status badge component: "360° View Ready" (success state) when `viewVerified = true`; "Pending Staff Verification" (warning state) when `false`

All API calls use the `AuthInterceptor` (from US_011) to attach the Bearer token. Loading states use Angular skeleton placeholder blocks. Error states display a retry banner. Data is held in NgRx `signal()`-based local state (no global store needed for a single-page view of this complexity).

## Dependent Tasks

- **US_016 / TASK_002** — `GET /api/patient/dashboard` backend endpoint must be implemented and running before end-to-end integration.
- **US_016 / TASK_003** — `view_verified_at` column on `patients` must be migrated before the backend can serve the `viewVerified` flag.
- **US_011 / TASK_001** — `AuthInterceptor` and `AuthGuard` must exist to protect the dashboard route and attach JWT.
- **US_015** — Patient profile route must exist so the dashboard navigation shell is consistent.

## Impacted Components

| Component | Status | Location |
|-----------|--------|----------|
| `PatientDashboardComponent` | NEW | `app/features/patient/dashboard/` |
| `AppointmentStatusBadgeComponent` | NEW | `app/shared/components/appointment-status-badge/` |
| `DocumentStatusChipComponent` | NEW | `app/shared/components/document-status-chip/` |
| `ViewReadinessIndicatorComponent` | NEW | `app/shared/components/view-readiness-indicator/` |
| `PatientDashboardService` | NEW | `app/features/patient/dashboard/patient-dashboard.service.ts` |
| `PatientDashboardDto` (TypeScript interface) | NEW | `app/features/patient/dashboard/patient-dashboard.model.ts` |
| `AppRoutingModule` | MODIFY | Add `/dashboard` child route under patient feature, guarded by `authGuard` |

## Implementation Plan

1. **TypeScript models** (`patient-dashboard.model.ts`):

   ```typescript
   export interface UpcomingAppointmentItem {
     id: string;
     date: string;            // ISO 8601 date string
     timeSlotStart: string;
     specialty: string;
     status: 'Booked' | 'Arrived' | 'Completed' | 'Cancelled';
     hasPendingIntake: boolean;
   }

   export interface DocumentHistoryItem {
     id: string;
     fileName: string;
     uploadedAt: string;      // ISO 8601 datetime
     processingStatus: 'Pending' | 'Processing' | 'Completed' | 'Failed';
   }

   export interface PatientDashboardDto {
     upcomingAppointments: UpcomingAppointmentItem[];
     documents: DocumentHistoryItem[];
     viewVerified: boolean;
   }
   ```

2. **`PatientDashboardService`**: Call `GET /api/patient/dashboard` and return `Observable<PatientDashboardDto>`. Use Angular `HttpClient`. No caching — fresh data on each page navigation. Handle HTTP errors: catch and re-throw structured `DashboardLoadError`.

3. **`PatientDashboardComponent`** (standalone, `OnPush` change detection):
   - Inject `PatientDashboardService`. Use `toSignal()` to convert the Observable to a Signal for template binding.
   - Use a `loadingState` Signal (`'idle' | 'loading' | 'success' | 'error'`) to drive skeleton/error views.
   - On `ngOnInit`, dispatch the load; on destroy, unsubscribe via `takeUntilDestroyed`.
   - Derive `pendingIntakeItems` as a `computed()` Signal filtering `upcomingAppointments` where `hasPendingIntake === true`.
   - Template structure:
     ```
     <section aria-label="Upcoming Appointments"> ... </section>
     <section aria-label="Pending Intake">        ... </section>
     <section aria-label="Document Upload History"> ... </section>
     <section aria-label="360° View Status">      ... </section>
     ```

4. **`AppointmentStatusBadgeComponent`**: Accept `@Input() status` string, emit a CSS class (`status-booked`, `status-arrived`, `status-completed`, `status-cancelled`) and an ARIA label (`aria-label="Appointment status: Booked"`). No business logic — presentational only.

5. **`DocumentStatusChipComponent`**: Accept `@Input() processingStatus`. Render colour-coded chip. When `processingStatus === 'Failed'`, emit a `retryClicked` `@Output()` EventEmitter so the parent can navigate to the upload page.

6. **`ViewReadinessIndicatorComponent`**: Accept `@Input() verified: boolean`. Show two states:
   - `verified = true`: green badge "360° View Ready" with a checkmark icon (`aria-label="360 degree view is ready"`)
   - `verified = false`: amber badge "Pending Staff Verification" (`aria-label="360 degree view is pending staff verification"`)

7. **Empty state handling**: In the template, use `@if (pendingIntakeItems().length === 0)` to show the "No pending intake items" placeholder. Use `@if (dashboard()?.upcomingAppointments.length === 0)` to show "No upcoming appointments" with a `[routerLink]="['/appointments/book']"` button. WCAG 2.2 AA: all empty-state CTAs must have descriptive `aria-label`.

8. **Route registration** in `AppRoutingModule`:

   ```typescript
   {
     path: 'dashboard',
     component: PatientDashboardComponent,
     canActivate: [authGuard],
     title: 'My Dashboard — Propel IQ'
   }
   ```

## Current Project State

```
app/
├── features/
│   └── patient/
│       └── dashboard/               ← NEW
├── shared/
│   └── components/                  ← NEW shared presentational components
└── app-routing.module.ts            ← MODIFY
```

> Greenfield Angular 18 project. All paths are target locations per project scaffold convention.

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | `app/features/patient/dashboard/patient-dashboard.component.ts` | Standalone Angular component; loads dashboard data via signal; drives all 4 sections |
| CREATE | `app/features/patient/dashboard/patient-dashboard.component.html` | Template with 4 `<section>` panels, skeleton loaders, empty states, error banner |
| CREATE | `app/features/patient/dashboard/patient-dashboard.component.scss` | Dashboard layout styles; responsive grid for panels at 375/768/1440px |
| CREATE | `app/features/patient/dashboard/patient-dashboard.service.ts` | `GET /api/patient/dashboard` HttpClient call; error handling |
| CREATE | `app/features/patient/dashboard/patient-dashboard.model.ts` | TypeScript interfaces: `PatientDashboardDto`, `UpcomingAppointmentItem`, `DocumentHistoryItem` |
| CREATE | `app/shared/components/appointment-status-badge/appointment-status-badge.component.ts` | Presentational badge with `status` input and ARIA label |
| CREATE | `app/shared/components/document-status-chip/document-status-chip.component.ts` | Presentational chip with `processingStatus` input and `retryClicked` output |
| CREATE | `app/shared/components/view-readiness-indicator/view-readiness-indicator.component.ts` | Verified/Pending status indicator with `verified` boolean input |
| MODIFY | `app/app-routing.module.ts` | Add `/dashboard` route with `authGuard` and page `title` |

## External References

- [Angular 18 — Signals & `toSignal()`](https://angular.dev/guide/signals/rxjs-interop) — Converting Observables to Signals; `computed()` for derived state
- [Angular 18 — `@if` / `@for` template control flow](https://angular.dev/guide/templates/control-flow) — New built-in `@if`, `@for`, `@empty` block syntax (Angular 17+)
- [Angular 18 — `takeUntilDestroyed`](https://angular.dev/api/core/rxjs-interop/takeUntilDestroyed) — Declarative subscription cleanup without manual `unsubscribe`
- [Angular 18 — OnPush Change Detection](https://angular.dev/best-practices/skipping-component-rendering) — `ChangeDetectionStrategy.OnPush` with Signals for performance
- [WCAG 2.2 AA — Status Messages (4.1.3)](https://www.w3.org/WAI/WCAG22/Understanding/status-messages.html) — Accessible status messages for loading/error/empty states
- [WCAG 2.2 AA — Name, Role, Value (4.1.2)](https://www.w3.org/WAI/WCAG22/Understanding/name-role-value.html) — `aria-label` for custom badge and chip components
- [Angular Router — Route Titles](https://angular.dev/guide/routing/router-reference#setting-the-page-title) — `title` property on route config for `<title>` tag management
- [NgRx Signals 18.x](https://ngrx.io/guide/signals) — `signal()`, `computed()`, `effect()` for local reactive component state

## Build Commands

```bash
# Generate dashboard feature components
ng generate component features/patient/dashboard --standalone --change-detection OnPush
ng generate service features/patient/dashboard/patient-dashboard
ng generate interface features/patient/dashboard/patient-dashboard model

# Generate shared presentational components
ng generate component shared/components/appointment-status-badge --standalone
ng generate component shared/components/document-status-chip --standalone
ng generate component shared/components/view-readiness-indicator --standalone

# Serve locally
ng serve --port 4200

# Build for production (verify bundle size)
ng build --configuration production --stats-json
```

## Implementation Validation Strategy

- [ ] Unit tests pass (to be planned separately via `plan-unit-test` workflow)
- [ ] **[UI Tasks]** Visual comparison against wireframe completed at 375px, 768px, 1440px (when wireframe is available)
- [ ] **[UI Tasks]** Run `/analyze-ux` to validate wireframe alignment (when wireframe is available)
- [ ] Dashboard loads and displays all 4 sections when authenticated as a Patient with test data
- [ ] Upcoming appointments section shows correct status badges for each status value (Booked/Arrived/Completed/Cancelled)
- [ ] Pending intake section: appointments with `hasPendingIntake = true` appear; "Complete Intake" button navigates to `/intake/{appointmentId}`
- [ ] Empty state renders correctly when `upcomingAppointments = []` — "Book Appointment" CTA is present and navigable
- [ ] Document with `processingStatus = 'Failed'` shows "Retry Upload" link (does not throw JavaScript error)
- [ ] `viewVerified = true` renders green "360° View Ready" badge; `viewVerified = false` renders amber "Pending Staff Verification" badge
- [ ] Loading skeleton displays during API call; disappears on success or error
- [ ] Unauthenticated access to `/dashboard` redirects to `/login` (AuthGuard active)
- [ ] All badge/chip/indicator components pass accessibility check: correct `aria-label` attributes verified via browser DevTools accessibility tree

## Implementation Checklist

- [ ] Create `PatientDashboardDto`, `UpcomingAppointmentItem`, and `DocumentHistoryItem` TypeScript interfaces in `patient-dashboard.model.ts`
- [ ] Implement `PatientDashboardService` with `GET /api/patient/dashboard` call and structured error mapping
- [ ] Implement `PatientDashboardComponent` with `loadingState` signal, `toSignal()` integration, `computed()` pending intake filter, and `takeUntilDestroyed` cleanup
- [ ] Implement all 4 template sections with `@if`/`@for` and `@empty` blocks; include skeleton loaders and error retry banner
- [ ] Create `AppointmentStatusBadgeComponent` (presentational, `aria-label` for each status value)
- [ ] Create `DocumentStatusChipComponent` (presentational, `retryClicked` output for `Failed` status)
- [ ] Create `ViewReadinessIndicatorComponent` (verified/pending states with ARIA labels)
- [ ] Register `/dashboard` route with `authGuard` and `title` in `AppRoutingModule`
