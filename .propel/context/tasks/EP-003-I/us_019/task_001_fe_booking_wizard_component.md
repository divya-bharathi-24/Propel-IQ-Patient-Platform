# Task - TASK_001

## Requirement Reference

- **User Story**: US_019 — End-to-End Single-Session Appointment Booking Workflow
- **Story Location**: `.propel/context/tasks/EP-003-I/us_019/us_019.md`
- **Acceptance Criteria**:
  - AC-1: Given I have selected an available slot and proceed through the booking wizard (slot → intake mode → insurance → confirm), Then all four steps are completable in a single browser session without being redirected away
  - AC-4: Given my booking is confirmed, When I view the confirmation screen, Then I see the appointment date, time, provider specialty, reference number, and a "Add to Calendar" option
- **Edge Cases**:
  - Browser closed at intake mode selection step: slot hold expires after 5-minute TTL (no orphaned booking); FE places hold when advancing from Step 1 via `POST /api/appointments/hold-slot`
  - Insurance pre-check service temporarily unavailable: Step 3 displays insurance status badge as "Check Pending"; booking proceeds without blocking (AC-4 confirmation still shown)

## Design References (Frontend Tasks Only)

| Reference Type | Value |
|----------------|-------|
| **UI Impact** | Yes |
| **Figma URL** | N/A |
| **Wireframe Status** | PENDING |
| **Wireframe Type** | N/A |
| **Wireframe Path/URL** | TODO: Upload to `.propel/context/wireframes/Hi-Fi/wireframe-SCR-XXX-booking-wizard.[html\|png\|jpg]` or provide external URL |
| **Screen Spec** | N/A (figma_spec.md not yet generated) |
| **UXR Requirements** | N/A (figma_spec.md not yet generated) |
| **Design Tokens** | N/A (designsystem.md not yet generated) |

### **CRITICAL: Wireframe Implementation Requirement**

**Wireframe Status = PENDING:** When wireframe becomes available, implementation MUST:

- Match layout, spacing, typography, and colors from the wireframe
- Implement all states: Default (step active), Loading (API call in flight), Success (booking confirmed), Error (409 slot conflict), Insurance result badge states (Verified/Not Recognized/Incomplete/Check Pending)
- Validate implementation against wireframe at breakpoints: 375px, 768px, 1440px
- Run `/analyze-ux` after implementation to verify pixel-perfect alignment

## Applicable Technology Stack

| Layer | Technology | Version |
|-------|------------|---------|
| Frontend | Angular | 18.x |
| Frontend State | NgRx Signals | 18.x |
| Backend | ASP.NET Core Web API | .NET 9 |
| Database | PostgreSQL | 16+ |
| Cache | Upstash Redis (serverless) | — |
| Library | Angular Router | 18.x |
| Library | Angular Reactive Forms | 18.x |
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

Implement `BookingWizardComponent` — an Angular 18 standalone, `ChangeDetectionStrategy.OnPush` multi-step wizard that walks an authenticated patient through four sequential steps in a single browser session without full-page navigation:

1. **Step 1 — Slot Selection**: Displays available appointment slots fetched from `GET /api/appointments/slots` (US_018 context). On slot selection, calls `POST /api/appointments/hold-slot` (TASK_002) to place a 5-minute Redis hold, then advances to Step 2.
2. **Step 2 — Intake Mode**: Presents two radio options — "AI-Assisted" and "Manual" — for the patient to select their preferred intake mode. Advances to Step 3.
3. **Step 3 — Insurance Pre-Check**: Reactive `FormGroup` collects optional `insurerName` and `memberId` fields. A "Skip" button nulls both fields. An `insuranceStatus` badge (signal-driven) reflects the result returned by the booking API: Verified / Not Recognized / Incomplete / Check Pending.
4. **Step 4 — Confirmation Screen**: Shows appointment date, time slot, provider specialty, reference number (first 8 chars of appointment UUID), and an "Add to Calendar" button that generates a `.ics` data URI download. A "Back to Dashboard" `routerLink` navigates to `/dashboard`.

A `bookingWizardStore` (NgRx Signals store) holds cross-step state (`selectedSlot`, `intakeMode`, `insuranceInfo`, `step`, `isSubmitting`, `bookingResult`). The `BookingService` calls `POST /api/appointments/book` on Step 4 confirmation. A 409 response resets the wizard to Step 1 with an inline alert: "Slot no longer available. Please select another." All API calls carry the Bearer token via `AuthInterceptor` (US_011). WCAG 2.2 AA: `aria-label` on step indicators, `role="status"` on the insurance badge, `aria-live="polite"` on the error banner.

## Dependent Tasks

- **US_019 / TASK_002** — `POST /api/appointments/book` and `POST /api/appointments/hold-slot` backend endpoints must be implemented before end-to-end integration.
- **US_019 / TASK_003** — `insurance_validations` table migration must be applied so the booking endpoint can write insurance results.
- **US_018 / TASK_001** — `SlotAvailabilityService` and `GET /api/appointments/slots` must be available for Step 1 slot data.
- **US_011 / TASK_001** — `AuthInterceptor` and `authGuard` must exist to protect the booking route and attach the JWT.

## Impacted Components

| Component | Status | Location |
|-----------|--------|----------|
| `BookingWizardComponent` | NEW | `app/features/booking/wizard/booking-wizard.component.ts` |
| `SlotSelectionStepComponent` | NEW | `app/features/booking/wizard/steps/slot-selection-step.component.ts` |
| `IntakeModeStepComponent` | NEW | `app/features/booking/wizard/steps/intake-mode-step.component.ts` |
| `InsuranceStepComponent` | NEW | `app/features/booking/wizard/steps/insurance-step.component.ts` |
| `BookingConfirmationStepComponent` | NEW | `app/features/booking/wizard/steps/booking-confirmation-step.component.ts` |
| `bookingWizardStore` | NEW | `app/features/booking/wizard/booking-wizard.store.ts` |
| `BookingService` | NEW | `app/features/booking/booking.service.ts` |
| `BookingModels` (TypeScript interfaces) | NEW | `app/features/booking/booking.models.ts` |
| `AppRoutingModule` | MODIFY | Add `/appointments/book` route with `authGuard` and `title: 'Book Appointment'` |

## Implementation Plan

1. **TypeScript models** (`booking.models.ts`):

   ```typescript
   export type IntakeMode = 'AiAssisted' | 'Manual';
   export type InsuranceStatus = 'Verified' | 'NotRecognized' | 'Incomplete' | 'CheckPending';

   export interface AvailableSlot {
     slotId: string;
     specialtyId: string;
     specialtyName: string;
     date: string;          // ISO 8601 (e.g., "2026-05-10")
     timeSlotStart: string; // "HH:mm"
     timeSlotEnd: string;   // "HH:mm"
   }

   export interface InsuranceInfo {
     insurerName: string | null;
     memberId: string | null;
   }

   export interface CreateBookingRequest {
     slotId: string;
     specialtyId: string;
     intakeMode: IntakeMode;
     insuranceName: string | null;
     insuranceId: string | null;
     preferredSlotId: string | null;
   }

   export interface BookingResult {
     appointmentId: string;
     referenceNumber: string; // First 8 chars of appointmentId (uppercase)
     date: string;
     timeSlotStart: string;
     specialtyName: string;
     insuranceStatus: InsuranceStatus;
   }
   ```

2. **`bookingWizardStore`** (NgRx Signals):

   ```typescript
   export const bookingWizardStore = signalStore(
     withState<BookingWizardState>({
       step: 1,
       selectedSlot: null,
       intakeMode: null,
       insuranceInfo: { insurerName: null, memberId: null },
       isSubmitting: false,
       bookingResult: null,
       errorMessage: null,
     }),
     withMethods((store, bookingService = inject(BookingService)) => ({
       selectSlot: (slot: AvailableSlot) => patchState(store, { selectedSlot: slot, step: 2 }),
       setIntakeMode: (mode: IntakeMode) => patchState(store, { intakeMode: mode, step: 3 }),
       setInsuranceInfo: (info: InsuranceInfo) => patchState(store, { insuranceInfo: info }),
       async confirmBooking(): Promise<void> { /* handled in component */ },
       resetWizard: () => patchState(store, { step: 1, selectedSlot: null, intakeMode: null, bookingResult: null, errorMessage: null }),
     }))
   );
   ```

3. **`BookingWizardComponent`** — OnPush, standalone, uses `@if`/`@for` control flow. Renders active step panel controlled by `store.step()` signal. Contains error alert banner (`@if(store.errorMessage())`) with `aria-live="polite"`.

4. **`SlotSelectionStepComponent`** — Calls `SlotAvailabilityService.getAvailableSlots()` via `toSignal()`. Renders slot cards with `@for`; "No slots available" `@empty` block. On slot card click: calls `BookingService.holdSlot(slot)` (PATCH request), then dispatches `store.selectSlot(slot)`.

5. **`InsuranceStepComponent`** — `ReactiveFormsModule` with optional fields. "Skip" button: `patchValue({ insurerName: null, memberId: null })`, advance to Step 4. Insurance status badge rendered via `@if` once `bookingResult` is available (post-confirm); status mapped to CSS class: `verified` (green), `not-recognized` (amber), `incomplete` (amber), `check-pending` (grey).

6. **`BookingConfirmationStepComponent`** — Reads `store.bookingResult()`. "Add to Calendar" button generates `.ics` content:

   ```
   BEGIN:VCALENDAR
   BEGIN:VEVENT
   SUMMARY:Appointment - {{specialtyName}}
   DTSTART:{{date}}T{{timeSlotStart}}00Z
   DTEND:{{date}}T{{timeSlotEnd}}00Z
   DESCRIPTION:Reference: {{referenceNumber}}
   END:VEVENT
   END:VCALENDAR
   ```
   Creates a `data:text/calendar;charset=utf-8,` URI and triggers download via `<a>` click simulation.

7. **`BookingService.confirmBooking()`** — POST `/api/appointments/book`, returns `BookingResult`. On 409: `patchState(store, { errorMessage: 'Slot no longer available. Please select another.', step: 1 })`. On success: `patchState(store, { bookingResult: response, step: 4 })`.

8. **Routing** — Add to app routes:
   ```typescript
   { path: 'appointments/book', component: BookingWizardComponent, canActivate: [authGuard], title: 'Book Appointment' }
   ```

## Current Project State

```
app/
├── features/
│   ├── auth/           (US_011 — completed)
│   ├── patient/
│   │   └── dashboard/  (US_016 — completed)
│   └── booking/        ← NEW (this task)
│       ├── wizard/
│       │   ├── booking-wizard.component.ts
│       │   ├── booking-wizard.store.ts
│       │   └── steps/
│       │       ├── slot-selection-step.component.ts
│       │       ├── intake-mode-step.component.ts
│       │       ├── insurance-step.component.ts
│       │       └── booking-confirmation-step.component.ts
│       ├── booking.service.ts
│       └── booking.models.ts
```

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | `app/features/booking/booking.models.ts` | TypeScript interfaces: `AvailableSlot`, `CreateBookingRequest`, `BookingResult`, `InsuranceStatus`, `IntakeMode` |
| CREATE | `app/features/booking/booking.service.ts` | `BookingService` — calls `POST /api/appointments/book` and `POST /api/appointments/hold-slot` |
| CREATE | `app/features/booking/wizard/booking-wizard.store.ts` | NgRx Signals store: `bookingWizardStore` with step, slot, intakeMode, insuranceInfo, result, error state |
| CREATE | `app/features/booking/wizard/booking-wizard.component.ts` | Root wizard shell: step switcher, error banner, WCAG aria attributes |
| CREATE | `app/features/booking/wizard/steps/slot-selection-step.component.ts` | Step 1: slot cards, hold-slot call on selection |
| CREATE | `app/features/booking/wizard/steps/intake-mode-step.component.ts` | Step 2: AI-Assisted / Manual radio picker |
| CREATE | `app/features/booking/wizard/steps/insurance-step.component.ts` | Step 3: optional insurance form, status badge, Skip button |
| CREATE | `app/features/booking/wizard/steps/booking-confirmation-step.component.ts` | Step 4: confirmation details, ICS calendar download, dashboard link |
| MODIFY | `app/app.routes.ts` | Add `/appointments/book` route with `authGuard` and `title: 'Book Appointment'` |

## External References

- [Angular Signals — NgRx Signal Store](https://ngrx.io/guide/signals/signal-store)
- [Angular `toSignal` + `takeUntilDestroyed`](https://angular.dev/guide/signals/rxjs-interop)
- [iCalendar RFC 5545](https://datatracker.ietf.org/doc/html/rfc5545) — `.ics` file format
- [WCAG 2.2 — 4.1.3 Status Messages](https://www.w3.org/TR/WCAG22/#status-messages) — `aria-live="polite"` for insurance badge and error banner

## Build Commands

- Refer to: `.propel/build/frontend-build.md`

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] Integration tests pass (mock `BookingService` with 200 and 409 responses)
- [ ] **[UI Tasks]** Visual comparison against wireframe completed at 375px, 768px, 1440px (when wireframe is available)
- [ ] **[UI Tasks]** Run `/analyze-ux` to validate wireframe alignment (when wireframe is available)
- [ ] 409 conflict scenario tested: wizard resets to Step 1 with correct error message
- [ ] "Add to Calendar" generates valid `.ics` data URI with correct appointment fields
- [ ] `authGuard` blocks unauthenticated access to `/appointments/book`

## Implementation Checklist

- [ ] Create `BookingWizardComponent` (standalone, `ChangeDetectionStrategy.OnPush`, `@if`/`@for`) with 4 named step panels; step transitions guarded by signal-based validity checks on `bookingWizardStore.selectedSlot()` and `bookingWizardStore.intakeMode()`
- [ ] Implement `bookingWizardStore` (NgRx Signals): `step`, `selectedSlot`, `intakeMode`, `insuranceInfo`, `isSubmitting`, `bookingResult`, `errorMessage` signals; `resetWizard()` method
- [ ] Step 1 — integrate `SlotAvailabilityService` (US_018 context) via `toSignal()`; on slot card select, call `BookingService.holdSlot(slot)` then `store.selectSlot(slot)` to advance to Step 2
- [ ] Step 2 — Intake Mode picker: `AI-Assisted` and `Manual` radio options with `aria-label`; advance to Step 3 on "Next" via `store.setIntakeMode()`
- [ ] Step 3 — Insurance form: optional `ReactiveFormsModule` `FormGroup`; "Skip" button nulls both fields; `insuranceStatus` badge driven by `store.bookingResult()?.insuranceStatus` signal after confirmation; `role="status"` + `aria-label` on badge
- [ ] Step 4 — Confirmation screen: display `referenceNumber`, `date`, `timeSlotStart`, `specialtyName`; "Add to Calendar" generates `.ics` data URI download via anchor click simulation; `routerLink="/dashboard"` for "Back to Dashboard"
- [ ] Submit handler in `BookingWizardComponent`: call `BookingService.confirmBooking()`; on 409 set `errorMessage` and reset `step = 1`; on 200 set `bookingResult` and advance `step = 4`; `isSubmitting` signal gates duplicate submissions
- [ ] Register `/appointments/book` route with `authGuard` and `title: 'Book Appointment'`; apply WCAG 2.2 AA `aria-label` on step indicator breadcrumbs and `aria-live="polite"` on error banner
