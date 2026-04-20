# Task - TASK_001

## Requirement Reference

- **User Story**: US_032 — High-Risk Appointment Flag with Recommended Interventions
- **Story Location**: `.propel/context/tasks/EP-006/us_032/us_032.md`
- **Acceptance Criteria**:
  - AC-1: Given an appointment risk score is classified as High, When the Staff appointment list loads, Then a prominent "High-Risk" flag banner is displayed on the appointment card with a list of recommended interventions (e.g., "Send additional reminder", "Request callback").
  - AC-2: Given a High-risk flag is displayed, When I explicitly click "Accept" on a recommended intervention, Then the intervention is marked as accepted with my staff ID and timestamp, and the relevant action is triggered.
  - AC-3: Given a High-risk flag is displayed, When I click "Dismiss", Then the flag is acknowledged with my staff ID and a dismissal reason (optional), and the flag no longer appears as a pending action for that appointment.
  - AC-4: Given a High-risk appointment flag is unacknowledged, When I view the Staff dashboard, Then unacknowledged High-risk flags are prominently surfaced in a "Requires Attention" section sorted by appointment time.
- **Edge Cases**:
  - Score drops to Medium before acknowledgment: The banner is automatically cleared on the next appointment list load (server no longer returns it as pending); history entry retained for audit.
  - Not logged in when flag is generated: Flag remains in "Requires Attention" until any Staff member acknowledges it on next login.

## Design References (Frontend Tasks Only)

| Reference Type | Value |
|----------------|-------|
| **UI Impact** | Yes |
| **Figma URL** | N/A |
| **Wireframe Status** | PENDING |
| **Wireframe Type** | N/A |
| **Wireframe Path/URL** | TODO: Upload to `.propel/context/wireframes/Hi-Fi/wireframe-SCR-XXX-high-risk-flag.[html\|png\|jpg]` or provide external URL |
| **Screen Spec** | N/A (figma_spec.md not yet generated) |
| **UXR Requirements** | N/A (figma_spec.md not yet generated) |
| **Design Tokens** | N/A (designsystem.md not yet generated) |

### **CRITICAL: Wireframe Implementation Requirement**

**Wireframe Status = PENDING:** When wireframe becomes available, implementation MUST:

- Match layout of the "High-Risk" banner (red background, warning icon, intervention list) from the wireframe
- Implement all states: Default (no high-risk), Flag Pending (banner visible with intervention rows), Intervention Accepted (row muted/checked), Intervention Dismissed (row removed or strikethrough)
- Implement "Requires Attention" section: visible when count > 0, hidden/empty-state when 0
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
| Library | Angular `HttpClient` | 18.x |
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

Implement two Angular 18 standalone, `ChangeDetectionStrategy.OnPush` components that surface high-risk appointment flags to Staff:

**`HighRiskFlagBannerComponent`** — embedded inside each appointment card on the Staff appointment management interface. Rendered only when `appointment.riskSeverity === 'High'` and there are pending interventions. Displays:
- A prominent red "⚠ High-Risk" label (WCAG `role="status"`, `aria-label="High-risk appointment flag"`)
- A list of pending `RiskIntervention` rows. Each row shows the intervention label ("Send additional reminder" / "Request callback") and two action buttons:
  - **Accept** (`aria-label="Accept: Send additional reminder"`): calls `RiskFlagService.acceptIntervention(interventionId)` → `PATCH /api/risk/interventions/{id}/accept`; optimistic update removes the row from `pendingInterventions` signal on success; rolls back via `refresh$.next()` on error.
  - **Dismiss** (`aria-label="Dismiss: Send additional reminder"`): toggles a dismissal-reason text input (optional, max 500 chars); on confirm, calls `RiskFlagService.dismissIntervention(interventionId, reason)` → `PATCH /api/risk/interventions/{id}/dismiss`; optimistic update removes the row.

When all interventions for an appointment are acknowledged, the banner component sets `allAcknowledged = computed(() => pendingInterventions().length === 0)` and renders an empty/success state ("All interventions acknowledged").

**`RequiresAttentionSectionComponent`** — placed prominently at the top of the Staff dashboard (`/staff/dashboard`) above the main appointment list. On component init, calls `RiskFlagService.getRequiresAttention()` (`GET /api/risk/requires-attention`) to load all unacknowledged High-risk appointments. Results are sorted by `appointmentTime ASC`. Each item in the list renders the patient name, appointment time, and a button linking to the full appointment card where the intervention banner is shown.

**`@empty` block**: renders "No appointments require attention" when the `requiresAttentionItems` signal is empty.

**Route guard**: both components are only accessible under routes with `staffGuard` (role `'Staff' || 'Admin'`).

**WCAG 2.2 AA**: `role="status"` on the risk banner; `aria-live="polite"` on the requires-attention count; `aria-label` on each action button per intervention row.

## Dependent Tasks

- **US_032 / TASK_002** — `GET /api/risk/requires-attention`, `GET /api/risk/{appointmentId}/interventions`, `PATCH /api/risk/interventions/{id}/accept`, `PATCH /api/risk/interventions/{id}/dismiss` endpoints must be implemented before end-to-end integration.
- **US_031 (EP-006)** — `NoShowRisk` record with severity classification (score > 0.66 = High) must be created by the risk scoring engine before flags are raised.
- **US_032 / TASK_002** — `NoShowRiskAssessedEvent` handler must generate `RiskIntervention` rows automatically; the FE fetches them via API.
- **US_011 / TASK_001** — `AuthInterceptor` must attach Bearer token to all `HttpClient` calls.

## Impacted Components

| Component | Status | Location |
|-----------|--------|----------|
| `HighRiskFlagBannerComponent` | NEW | `app/features/staff/risk-flag/high-risk-flag-banner/high-risk-flag-banner.component.ts` |
| `InterventionRowComponent` | NEW | `app/features/staff/risk-flag/intervention-row/intervention-row.component.ts` |
| `RequiresAttentionSectionComponent` | NEW | `app/features/staff/risk-flag/requires-attention/requires-attention-section.component.ts` |
| `RiskFlagService` | NEW | `app/features/staff/risk-flag/risk-flag.service.ts` |
| `RiskFlagModels` (TypeScript interfaces) | NEW | `app/features/staff/risk-flag/risk-flag.models.ts` |
| `StaffDashboardComponent` | MODIFY | Add `<app-requires-attention-section>` above the main appointment list |
| `AppointmentCardComponent` (Staff) | MODIFY | Add `<app-high-risk-flag-banner>` rendered conditionally when `riskSeverity === 'High'` |

## Implementation Plan

1. **TypeScript models** (`risk-flag.models.ts`):

   ```typescript
   export type RiskSeverity = 'Low' | 'Medium' | 'High';
   export type InterventionType = 'AdditionalReminder' | 'CallbackRequest';
   export type InterventionStatus = 'Pending' | 'Accepted' | 'Dismissed' | 'AutoCleared';

   export interface RiskIntervention {
     interventionId: string;
     type: InterventionType;
     label: string;         // e.g. "Send additional reminder"
     status: InterventionStatus;
   }

   export interface RequiresAttentionItem {
     appointmentId: string;
     patientName: string;
     appointmentTime: string;    // ISO UTC
     riskScore: number;
     pendingInterventionCount: number;
   }
   ```

2. **`RiskFlagService`**:

   ```typescript
   @Injectable({ providedIn: 'root' })
   export class RiskFlagService {
     private readonly http = inject(HttpClient);

     getRequiresAttention(): Observable<RequiresAttentionItem[]> {
       return this.http.get<RequiresAttentionItem[]>('/api/risk/requires-attention');
     }

     getInterventions(appointmentId: string): Observable<RiskIntervention[]> {
       return this.http.get<RiskIntervention[]>(`/api/risk/${appointmentId}/interventions`);
     }

     acceptIntervention(interventionId: string): Observable<void> {
       return this.http.patch<void>(`/api/risk/interventions/${interventionId}/accept`, {});
     }

     dismissIntervention(interventionId: string, reason: string | null): Observable<void> {
       return this.http.patch<void>(
         `/api/risk/interventions/${interventionId}/dismiss`,
         { reason }
       );
     }
   }
   ```

3. **`HighRiskFlagBannerComponent`** — signal-based, inputs from parent appointment card:

   ```typescript
   @Component({
     standalone: true,
     selector: 'app-high-risk-flag-banner',
     changeDetection: ChangeDetectionStrategy.OnPush,
     imports: [InterventionRowComponent, ...],
   })
   export class HighRiskFlagBannerComponent {
     private readonly riskFlagService = inject(RiskFlagService);
     private readonly destroyRef = inject(DestroyRef);

     @Input({ required: true }) appointmentId!: string;

     pendingInterventions = signal<RiskIntervention[]>([]);
     isLoading = signal(true);
     private readonly refresh$ = new Subject<void>();

     allAcknowledged = computed(() => this.pendingInterventions().length === 0);

     constructor() {
       merge(of(null), this.refresh$).pipe(
         switchMap(() => this.riskFlagService.getInterventions(this.appointmentId)),
         takeUntilDestroyed(this.destroyRef)
       ).subscribe(items => {
         this.pendingInterventions.set(items.filter(i => i.status === 'Pending'));
         this.isLoading.set(false);
       });
     }

     accept(interventionId: string): void {
       this.pendingInterventions.update(items => items.filter(i => i.interventionId !== interventionId));
       this.riskFlagService.acceptIntervention(interventionId).pipe(
         takeUntilDestroyed(this.destroyRef)
       ).subscribe({ error: () => this.refresh$.next() });
     }

     dismiss(interventionId: string, reason: string | null): void {
       this.pendingInterventions.update(items => items.filter(i => i.interventionId !== interventionId));
       this.riskFlagService.dismissIntervention(interventionId, reason).pipe(
         takeUntilDestroyed(this.destroyRef)
       ).subscribe({ error: () => this.refresh$.next() });
     }
   }
   ```

4. **Template** — WCAG roles:

   ```html
   @if (!isLoading() && !allAcknowledged()) {
     <div class="high-risk-banner" role="status" aria-label="High-risk appointment flag">
       <span class="risk-label">⚠ High-Risk</span>
       @for (intervention of pendingInterventions(); track intervention.interventionId) {
         <app-intervention-row
           [intervention]="intervention"
           (accepted)="accept($event)"
           (dismissed)="dismiss($event.id, $event.reason)" />
       } @empty {
         <span class="acknowledged">All interventions acknowledged</span>
       }
     </div>
   }
   ```

5. **`RequiresAttentionSectionComponent`**:

   ```typescript
   requiresAttentionItems = signal<RequiresAttentionItem[]>([]);
   isLoading = signal(true);

   constructor() {
     this.riskFlagService.getRequiresAttention().pipe(
       takeUntilDestroyed(this.destroyRef)
     ).subscribe(items => {
       this.requiresAttentionItems.set(
         [...items].sort((a, b) =>
           new Date(a.appointmentTime).getTime() - new Date(b.appointmentTime).getTime()
         )
       );
       this.isLoading.set(false);
     });
   }
   ```

## Current Project State

```
app/
├── features/
│   ├── auth/             (US_011 — completed)
│   ├── booking/          (US_019 — completed)
│   ├── staff/
│   │   ├── queue/        (US_027 — completed)
│   │   └── risk-flag/    ← NEW (this task)
│   │       ├── risk-flag.service.ts
│   │       ├── risk-flag.models.ts
│   │       ├── high-risk-flag-banner/
│   │       │   └── high-risk-flag-banner.component.ts
│   │       ├── intervention-row/
│   │       │   └── intervention-row.component.ts
│   │       └── requires-attention/
│   │           └── requires-attention-section.component.ts
```

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | `app/features/staff/risk-flag/risk-flag.models.ts` | TypeScript interfaces: `RiskIntervention`, `RequiresAttentionItem`, `RiskSeverity`, `InterventionType`, `InterventionStatus` |
| CREATE | `app/features/staff/risk-flag/risk-flag.service.ts` | `RiskFlagService`: `getRequiresAttention()`, `getInterventions()`, `acceptIntervention()`, `dismissIntervention()` |
| CREATE | `app/features/staff/risk-flag/high-risk-flag-banner/high-risk-flag-banner.component.ts` | Standalone banner: `signal<RiskIntervention[]>`, optimistic accept/dismiss, `allAcknowledged` computed, `takeUntilDestroyed()`, WCAG `role="status"` |
| CREATE | `app/features/staff/risk-flag/intervention-row/intervention-row.component.ts` | Presentational row: label, Accept button, Dismiss button (with optional reason text input), `aria-label` per button |
| CREATE | `app/features/staff/risk-flag/requires-attention/requires-attention-section.component.ts` | Staff dashboard section: load `getRequiresAttention()` on init, sort by appointment time ASC, `@empty` block |
| MODIFY | `app/features/staff/dashboard/staff-dashboard.component.ts` | Add `<app-requires-attention-section>` at top of template, above appointment list |
| MODIFY | `app/features/staff/appointments/appointment-card/appointment-card.component.ts` | Add `<app-high-risk-flag-banner>` rendered when `appointment.riskSeverity === 'High'` |

## External References

- [Angular Signals — `signal()`, `computed()`, `input()`](https://angular.dev/guide/signals)
- [Angular `takeUntilDestroyed()` — memory leak prevention](https://angular.dev/api/core/rxjs-interop/takeUntilDestroyed)
- [WCAG 2.2 — 4.1.3 Status Messages (`role="status"`, `aria-live="polite"`)](https://www.w3.org/TR/WCAG22/#status-messages)
- [FR-030 — High-risk flagging with Staff accept/dismiss (spec.md#FR-030)](spec.md#FR-030)

## Build Commands

- Refer to: `.propel/build/frontend-build.md`

## Implementation Validation Strategy

- [ ] Unit tests pass: `HighRiskFlagBannerComponent` renders intervention rows when `pendingInterventions().length > 0`
- [ ] Unit tests pass: `accept()` optimistically removes the intervention from `pendingInterventions` signal before API response
- [ ] Unit tests pass: `allAcknowledged` computed is `true` when `pendingInterventions().length === 0`
- [ ] `@empty` block rendered in `RequiresAttentionSectionComponent` when API returns empty list
- [ ] "Dismiss" button triggers optional reason text input before calling `dismissIntervention()`
- [ ] `staffGuard` blocks Patient-role users from accessing both components
- [ ] **[UI Tasks]** Visual comparison against wireframe at 375px, 768px, 1440px (when wireframe is available)
- [ ] **[UI Tasks]** Run `/analyze-ux` to validate wireframe alignment (when wireframe is available)

## Implementation Checklist

- [ ] Create `HighRiskFlagBannerComponent` (standalone, `OnPush`): `@Input appointmentId`; loads `getInterventions(appointmentId)` on init via `merge(of(null), refresh$).pipe(switchMap(...), takeUntilDestroyed())`; `pendingInterventions` signal filtered to `status === 'Pending'`; `allAcknowledged = computed(() => pendingInterventions().length === 0)`; `@for`/`@empty` control flow; `role="status"` + `aria-label` (WCAG 2.2)
- [ ] Optimistic accept/dismiss: immediately filter intervention from `pendingInterventions` signal; on API error call `refresh$.next()` to restore server state; `takeUntilDestroyed()` on action subscriptions
- [ ] `InterventionRowComponent`: shows intervention label, Accept button (`aria-label="Accept: {label}"`), Dismiss button with toggleable reason `<textarea>` (max 500 chars, optional); emits `(accepted): string` and `(dismissed): { id, reason }` outputs
- [ ] Create `RequiresAttentionSectionComponent`: loads `getRequiresAttention()` on init; client-side sort by `appointmentTime ASC`; `@for`/`@empty` (renders "No appointments require attention"); `aria-live="polite"` on item count; `staffGuard` protected route
- [ ] Modify `StaffDashboardComponent` to render `<app-requires-attention-section>` prominently above main appointment list (AC-4)
- [ ] Modify `AppointmentCardComponent` to render `<app-high-risk-flag-banner [appointmentId]="...">` conditionally when `appointment.riskSeverity === 'High'` (AC-1)
