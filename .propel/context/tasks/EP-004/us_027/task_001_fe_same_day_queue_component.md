# Task - TASK_001

## Requirement Reference

- **User Story**: US_027 — Same-Day Queue View & Arrived Status Marking
- **Story Location**: `.propel/context/tasks/EP-004/us_027/us_027.md`
- **Acceptance Criteria**:
  - AC-1: Given I navigate to the same-day queue view, When the page loads, Then all appointments for the current calendar day are displayed with patient name, appointment time, booking type (Self-Booked / Walk-In), and current arrival status (Waiting / Arrived / Cancelled)
  - AC-3: Given a new walk-in is added after the page has loaded, When the queue refreshes (polling), Then the new entry appears in the queue within 10 seconds without requiring a full page reload
- **Edge Cases**:
  - Queue has 0 appointments for today: empty state shown with "No appointments scheduled for today" message
  - Staff accidentally marks wrong patient as arrived: "Undo Arrived" button shown on `Arrived` rows (same-session day only); dispatches revert action to set status back to `Waiting`

## Design References (Frontend Tasks Only)

| Reference Type | Value |
|----------------|-------|
| **UI Impact** | Yes |
| **Figma URL** | N/A |
| **Wireframe Status** | PENDING |
| **Wireframe Type** | N/A |
| **Wireframe Path/URL** | TODO: Upload to `.propel/context/wireframes/Hi-Fi/wireframe-SCR-XXX-queue-view.[html\|png\|jpg]` or provide external URL |
| **Screen Spec** | N/A (figma_spec.md not yet generated) |
| **UXR Requirements** | N/A (figma_spec.md not yet generated) |
| **Design Tokens** | N/A (designsystem.md not yet generated) |

### **CRITICAL: Wireframe Implementation Requirement**

**Wireframe Status = PENDING:** When wireframe becomes available, implementation MUST:

- Match layout, spacing, typography, and colors from the wireframe
- Implement all states: Loaded (queue rows), Loading (skeleton), Empty (0 appointments), Error (API failure)
- Implement all row states: Waiting (default), Arrived (success chip), Cancelled (muted/strikethrough)
- Validate implementation against wireframe at breakpoints: 375px, 768px, 1440px
- Run `/analyze-ux` after implementation to verify pixel-perfect alignment

## Applicable Technology Stack

| Layer | Technology | Version |
|-------|------------|---------|
| Frontend | Angular | 18.x |
| Frontend State | NgRx Signals | 18.x |
| Backend | ASP.NET Core Web API | .net 10 |
| Database | PostgreSQL | 16+ |
| Library | Angular Router | 18.x |
| Library | Angular `HttpClient` + `interval()` (RxJS) | 18.x |
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

Implement the `SameDayQueueComponent` — an Angular 18 standalone, `ChangeDetectionStrategy.OnPush` Staff-facing dashboard page that displays all same-day appointments in a tabular queue view with live polling.

**Queue table** renders one row per appointment with columns: patient name, appointment time (`timeSlotStart`), booking type badge (`Self-Booked` / `Walk-In`), and arrival status chip (`Waiting` / `Arrived` / `Cancelled`). Rows are ordered by `timeSlotStart ASC`.

**Polling** — `interval(10_000)` (RxJS) merged with a manual `refresh$` `Subject` drives repeated calls to `QueueService.getQueue()` (`GET /api/queue/today`). Uses `takeUntilDestroyed()` to prevent memory leaks. The `isLoading` signal is set to `true` only on the initial load, not on background polls (avoiding flicker — AC-3).

**"Mark as Arrived" action** — Each row with `status = Waiting` shows a "Mark as Arrived" button. On click, dispatches `QueueService.markArrived(appointmentId)` (`PATCH /api/queue/{appointmentId}/arrived`). On success: optimistic update of the local `queueItems` signal; triggers a poll refresh for server confirmation.

**"Undo Arrived" action** — Each row with `status = Arrived` (arrived `today`) shows an "Undo Arrived" button. Dispatches `QueueService.revertArrived(appointmentId)` (`PATCH /api/queue/{appointmentId}/revert-arrived`). Restricted visually to the current calendar day (client-side `arrivalDate === today` check; server enforces the same).

**Empty state** — `@empty` block with message "No appointments scheduled for today" and a secondary CTA link to walk-in booking.

**WCAG 2.2 AA** — `aria-label` on action buttons (e.g., `"Mark John Smith as arrived"`); `role="status"` on status chips; `aria-live="polite"` on the queue table container for screen reader announcements of row updates.

**Route guard** — `/staff/queue` is protected by `staffGuard` (checks `role === 'Staff' || role === 'Admin'` from JWT claims).

## Dependent Tasks

- **US_027 / TASK_002** — `GET /api/queue/today`, `PATCH /api/queue/{id}/arrived`, and `PATCH /api/queue/{id}/revert-arrived` backend endpoints must be implemented before end-to-end integration.
- **US_011 / TASK_001** — `AuthInterceptor` must attach Bearer token to all `HttpClient` calls.
- **US_026 tasks** — Walk-in booking creates `QueueEntry` records that appear in this view.

## Impacted Components

| Component | Status | Location |
|-----------|--------|----------|
| `SameDayQueueComponent` | NEW | `app/features/staff/queue/same-day-queue.component.ts` |
| `QueueRowComponent` | NEW | `app/features/staff/queue/queue-row/queue-row.component.ts` |
| `QueueStatusChipComponent` | NEW | `app/shared/components/queue-status-chip/queue-status-chip.component.ts` |
| `BookingTypeBadgeComponent` | NEW | `app/shared/components/booking-type-badge/booking-type-badge.component.ts` |
| `QueueService` | NEW | `app/features/staff/queue/queue.service.ts` |
| `QueueModels` (TypeScript interfaces) | NEW | `app/features/staff/queue/queue.models.ts` |
| `AppRoutingModule` | MODIFY | Add `/staff/queue` route with `staffGuard` and `title: 'Same-Day Queue'` |

## Implementation Plan

1. **TypeScript models** (`queue.models.ts`):

   ```typescript
   export type ArrivalStatus = 'Waiting' | 'Arrived' | 'Cancelled';
   export type BookingType = 'SelfBooked' | 'WalkIn';

   export interface QueueItem {
     appointmentId: string;
     patientName: string;
     timeSlotStart: string;   // "HH:mm"
     bookingType: BookingType;
     arrivalStatus: ArrivalStatus;
     arrivalTimestamp: string | null;  // ISO UTC, null if not arrived
   }
   ```

2. **`QueueService`**:

   ```typescript
   @Injectable({ providedIn: 'root' })
   export class QueueService {
     private readonly http = inject(HttpClient);

     getQueue(): Observable<QueueItem[]> {
       return this.http.get<QueueItem[]>('/api/queue/today');
     }

     markArrived(appointmentId: string): Observable<void> {
       return this.http.patch<void>(`/api/queue/${appointmentId}/arrived`, {});
     }

     revertArrived(appointmentId: string): Observable<void> {
       return this.http.patch<void>(`/api/queue/${appointmentId}/revert-arrived`, {});
     }
   }
   ```

3. **`SameDayQueueComponent`** — signals + polling:

   ```typescript
   @Component({
     standalone: true,
     selector: 'app-same-day-queue',
     changeDetection: ChangeDetectionStrategy.OnPush,
     imports: [QueueRowComponent, ...],
     template: `...`
   })
   export class SameDayQueueComponent {
     private readonly queueService = inject(QueueService);
     private readonly destroyRef = inject(DestroyRef);

     queueItems = signal<QueueItem[]>([]);
     isLoading = signal(true);
     errorMessage = signal<string | null>(null);

     private readonly refresh$ = new Subject<void>();

     constructor() {
       // Initial load + polling every 10s
       merge(of(null), interval(10_000), this.refresh$).pipe(
         switchMap(() =>
           this.queueService.getQueue().pipe(
             catchError(() => {
               this.errorMessage.set('Unable to refresh queue. Retrying...');
               return EMPTY;
             })
           )
         ),
         takeUntilDestroyed(this.destroyRef)
       ).subscribe(items => {
         this.queueItems.set(items);
         this.isLoading.set(false);
         this.errorMessage.set(null);
       });
     }

     markArrived(appointmentId: string): void {
       // Optimistic update
       this.queueItems.update(items =>
         items.map(i => i.appointmentId === appointmentId
           ? { ...i, arrivalStatus: 'Arrived', arrivalTimestamp: new Date().toISOString() }
           : i
         )
       );
       this.queueService.markArrived(appointmentId).pipe(
         takeUntilDestroyed(this.destroyRef)
       ).subscribe({
         error: () => this.refresh$.next() // Rollback via fresh poll on error
       });
     }

     revertArrived(appointmentId: string): void {
       this.queueItems.update(items =>
         items.map(i => i.appointmentId === appointmentId
           ? { ...i, arrivalStatus: 'Waiting', arrivalTimestamp: null }
           : i
         )
       );
       this.queueService.revertArrived(appointmentId).pipe(
         takeUntilDestroyed(this.destroyRef)
       ).subscribe({
         error: () => this.refresh$.next()
       });
     }
   }
   ```

4. **Template** — `@if`/`@for`/`@empty` control flow:

   ```html
   <section aria-label="Same-Day Queue" aria-live="polite">
     @if (isLoading()) {
       <!-- skeleton rows -->
     } @else if (errorMessage()) {
       <p role="alert">{{ errorMessage() }}</p>
     } @else {
       <table>
         <thead>...</thead>
         <tbody>
           @for (item of queueItems(); track item.appointmentId) {
             <app-queue-row [item]="item"
               (markArrived)="markArrived($event)"
               (revertArrived)="revertArrived($event)" />
           } @empty {
             <tr><td colspan="5">No appointments scheduled for today</td></tr>
           }
         </tbody>
       </table>
     }
   </section>
   ```

5. **Route registration**:

   ```typescript
   { path: 'staff/queue', component: SameDayQueueComponent,
     canActivate: [staffGuard], title: 'Same-Day Queue' }
   ```

   `staffGuard` checks `user.role === 'Staff' || user.role === 'Admin'` from the decoded JWT; redirects to `/403` otherwise.

## Current Project State

```
app/
├── features/
│   ├── auth/           (US_011 — completed)
│   ├── patient/        (US_016 — completed)
│   ├── booking/        (US_019 — completed)
│   └── staff/          ← NEW (this task + US_026)
│       └── queue/
│           ├── same-day-queue.component.ts
│           ├── queue.service.ts
│           ├── queue.models.ts
│           └── queue-row/
│               └── queue-row.component.ts
├── shared/
│   └── components/
│       ├── queue-status-chip/
│       │   └── queue-status-chip.component.ts    ← NEW
│       └── booking-type-badge/
│           └── booking-type-badge.component.ts   ← NEW
```

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | `app/features/staff/queue/queue.models.ts` | TypeScript interfaces: `QueueItem`, `ArrivalStatus`, `BookingType` |
| CREATE | `app/features/staff/queue/queue.service.ts` | `QueueService`: `getQueue()`, `markArrived()`, `revertArrived()` HTTP calls |
| CREATE | `app/features/staff/queue/same-day-queue.component.ts` | Root queue page: `interval(10_000)` polling, `signal()` state, optimistic updates, `@for`/`@empty` |
| CREATE | `app/features/staff/queue/queue-row/queue-row.component.ts` | Presentational row: action buttons with `aria-label`, status chip, booking type badge |
| CREATE | `app/shared/components/queue-status-chip/queue-status-chip.component.ts` | Status chip: Waiting (grey), Arrived (green), Cancelled (muted) |
| CREATE | `app/shared/components/booking-type-badge/booking-type-badge.component.ts` | Booking type badge: Self-Booked (blue), Walk-In (amber) |
| MODIFY | `app/app.routes.ts` | Add `/staff/queue` route with `staffGuard` and `title: 'Same-Day Queue'` |

## External References

- [Angular `interval()` + `takeUntilDestroyed`](https://angular.dev/guide/signals/rxjs-interop)
- [RxJS `merge()` + `switchMap()` for polling](https://rxjs.dev/api/index/function/merge)
- [WCAG 2.2 — 4.1.3 Status Messages (`aria-live="polite"`)](https://www.w3.org/TR/WCAG22/#status-messages)
- [FR-026 — Same-day queue view for staff](spec.md#FR-026)
- [FR-027 — Staff-only Arrived marking](spec.md#FR-027)

## Build Commands

- Refer to: `.propel/build/frontend-build.md`

## Implementation Validation Strategy

- [ ] Unit tests pass: `SameDayQueueComponent` renders queue rows for mocked `QueueItem[]` response
- [ ] Unit tests pass: `markArrived()` optimistically updates `queueItems` signal before API response
- [ ] Polling verified: `interval(10_000)` triggers a second `getQueue()` call after 10 seconds in test
- [ ] Empty state rendered when API returns `[]`
- [ ] `staffGuard` blocks Patient-role user from accessing `/staff/queue` (redirects to `/403`)
- [ ] **[UI Tasks]** Visual comparison against wireframe at 375px, 768px, 1440px (when wireframe is available)
- [ ] **[UI Tasks]** Run `/analyze-ux` to validate wireframe alignment (when wireframe is available)

## Implementation Checklist

- [ ] Create `SameDayQueueComponent` (standalone, `OnPush`): `merge(of(null), interval(10_000), refresh$)` polling pipeline; `isLoading` signal set only on initial load (not on polls); `takeUntilDestroyed()` prevents memory leaks; `@for`/`@empty` template control flow; `aria-live="polite"` on container
- [ ] Implement `QueueService.getQueue()` (`GET /api/queue/today`), `markArrived()` (`PATCH /api/queue/{id}/arrived`), `revertArrived()` (`PATCH /api/queue/{id}/revert-arrived`) via `HttpClient`; all calls carry Bearer token via `AuthInterceptor` (US_011)
- [ ] `markArrived()` and `revertArrived()` optimistic update pattern: update `queueItems` signal immediately; on API error call `refresh$.next()` to restore server state via next poll
- [ ] `QueueRowComponent`: show "Mark as Arrived" button only when `status = Waiting`; show "Undo Arrived" button only when `status = Arrived` AND `arrivalDate === today` (client-side guard); `aria-label="Mark {patientName} as arrived"` on action button (WCAG 2.2 AA)
- [ ] Add `staffGuard` function (`role === 'Staff' || role === 'Admin'`); register `/staff/queue` route with `staffGuard` and `title: 'Same-Day Queue'` in `app.routes.ts`
- [ ] Create shared `QueueStatusChipComponent` (Waiting/Arrived/Cancelled CSS classes) and `BookingTypeBadgeComponent` (Self-Booked/Walk-In CSS classes); both standalone with `OnPush`
