# Task - task_001_fe_slot_picker

## Requirement Reference

- **User Story:** us_018 — Real-Time Slot Availability with Redis Cache
- **Story Location:** `.propel/context/tasks/EP-003-I/us_018/us_018.md`
- **Acceptance Criteria:**
  - AC-1: When available slots are fetched, the list reflects confirmed and pending bookings with a cache staleness of ≤5 seconds from the last slot state change
  - AC-2: When a booking or cancellation occurs, the Redis cache is invalidated and the next request returns the updated slot list within 5 seconds
  - AC-3: When Redis is unavailable, the system falls back to a direct PostgreSQL query and returns the slot list successfully (transparency from the user's perspective — the slot picker renders normally)
  - AC-4: Fully booked days are greyed out with a "No slots available" label and a prompt to join the waitlist
- **Edge Cases:**
  - Two patients simultaneously select the same slot → on the second patient's booking attempt, the backend returns a conflict error; the FE dismisses the selected slot, refreshes the slot list, and shows "Slot no longer available — please choose another"
  - Stale cache shows a slot as available; optimistic locking rejects the booking at commit time → same conflict UX as above

---

## Design References (Frontend Tasks Only)

| Reference Type         | Value                                                                                                                     |
| ---------------------- | ------------------------------------------------------------------------------------------------------------------------- |
| **UI Impact**          | Yes                                                                                                                       |
| **Figma URL**          | N/A                                                                                                                       |
| **Wireframe Status**   | PENDING                                                                                                                   |
| **Wireframe Type**     | N/A                                                                                                                       |
| **Wireframe Path/URL** | TODO: Upload to `.propel/context/wireframes/Hi-Fi/wireframe-SCR-XXX-slot-picker.[html\|png\|jpg]` or provide external URL |
| **Screen Spec**        | N/A (figma_spec.md not yet generated)                                                                                     |
| **UXR Requirements**   | N/A (figma_spec.md not yet generated)                                                                                     |
| **Design Tokens**      | N/A (designsystem.md not yet generated)                                                                                   |

> **Wireframe Status:** PENDING — implement following Angular Material and WCAG 2.2 AA guidelines. Run `/analyze-ux` once wireframe is provided.

---

## Applicable Technology Stack

| Layer            | Technology         | Version |
| ---------------- | ------------------ | ------- |
| Frontend         | Angular            | 18.x    |
| Frontend State   | NgRx Signals       | 18.x    |
| Frontend Routing | Angular Router     | 18.x    |
| HTTP Client      | Angular HttpClient | 18.x    |
| UI Components    | Angular Material   | 18.x    |
| Testing — Unit   | Jest               | Latest  |
| Testing — E2E    | Playwright         | 1.x     |
| AI/ML            | N/A                | N/A     |
| Mobile           | N/A                | N/A     |

> All code and libraries MUST be compatible with versions above.

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

Implement the slot picker UI step in the appointment booking flow. The component presents an interactive date-and-time selector that fetches available slots from `GET /api/appointments/slots?specialtyId={id}&date={date}`. Slot state is managed via NgRx Signals. The component handles three visual states per day: **Available** (selectable slots shown), **Fully Booked** (day greyed out, "No slots available" label, "Join Waitlist" CTA), and **Loading** (skeleton placeholder). On slot selection, the user advances to the booking confirmation step. On a concurrency conflict response from the booking step, the picker refreshes and shows "Slot no longer available — please choose another."

---

## Dependent Tasks

- **task_002_be_slot_availability_api** (EP-003-I/us_018) — `GET /api/appointments/slots` must be deployed before integration testing
- **US_011** (EP-001) — JWT `AuthGuard` must be active; slot picker is accessible only to authenticated Patients

---

## Impacted Components

| Status | Component / Module                     | Project                                                                        |
| ------ | -------------------------------------- | ------------------------------------------------------------------------------ |
| CREATE | `SlotPickerComponent`                  | Angular Frontend (`app/features/appointments/`)                                |
| CREATE | `SlotAvailabilityService`              | Angular Frontend (`app/features/appointments/services/`)                       |
| CREATE | `SlotAvailabilityStore` (NgRx Signals) | Angular Frontend (`app/features/appointments/state/`)                          |
| MODIFY | `AppointmentsModule` routing           | Add slot picker step to booking route                                          |
| MODIFY | `AppRoutingModule`                     | Ensure appointments lazy-loaded route is guarded by `AuthGuard` (Patient role) |

---

## Implementation Plan

1. **`SlotAvailabilityService`** — one method:
   - `getAvailableSlots(specialtyId: string, date: string)` → `GET /api/appointments/slots?specialtyId={specialtyId}&date={date}` → returns `SlotAvailabilityResponseDto` (array of `SlotDto` with `timeSlotStart`, `timeSlotEnd`, `isAvailable`, `specialtyId`, `date`)
   - No caching on the Angular side — cache freshness is guaranteed by the backend (≤5s Redis TTL per NFR-020). The service always issues a fresh HTTP GET.

2. **`SlotAvailabilityStore`** (NgRx Signals `signalStore`):
   - State: `slots: SlotDto[]`, `selectedSlot: SlotDto | null`, `loadingState: 'idle' | 'loading' | 'loaded' | 'error'`, `conflictMessage: string | null`
   - Actions (methods): `loadSlots(specialtyId, date)`, `selectSlot(slot)`, `clearConflict()`, `setConflict(message)`
   - `loadSlots` calls `SlotAvailabilityService.getAvailableSlots()`, sets loading state, populates `slots`, handles HTTP errors

3. **`SlotPickerComponent`**:
   - `@Input() specialtyId: string` — passed by the parent booking wizard
   - Date picker (`mat-datepicker`) for selecting a date; on date change → call `store.loadSlots(specialtyId, selectedDate)`
   - Slot grid: render one `<button mat-stroked-button>` per slot returned by the store
   - **Available slot button**: clickable, shows time range (e.g., `09:00 – 09:30`); on click → `store.selectSlot(slot)`
   - **Unavailable slot button**: `[disabled]="true"` with `aria-disabled="true"` — shown greyed with strikethrough time; never rendered for fully booked day (see below)
   - **Fully booked day**: when all slots for the selected date have `isAvailable = false`, hide the slot grid entirely and show:
     - `<p class="no-slots-label">No slots available</p>` (greyed-out)
     - `<button mat-flat-button color="primary" (click)="onJoinWaitlist()">Join Waitlist</button>`
   - **Loading state**: `<mat-spinner>` or Angular Material skeleton placeholder while `loadingState === 'loading'`
   - **Conflict banner**: when `store.conflictMessage()` is non-null, show `<mat-card class="conflict-warning">{{ store.conflictMessage() }}</mat-card>` above the slot grid; auto-dismiss on new slot selection
   - On `onJoinWaitlist()`: emit `@Output() joinWaitlistRequested = new EventEmitter<{specialtyId, date}>()` (handled by parent booking wizard)
   - Accessibility: `role="grid"` on the slot grid container, `aria-label` on each slot button, `aria-pressed` for selected state

4. **Conflict handling from parent booking wizard** (caller responsibility, documented here for context):
   - When the booking step receives HTTP 409 (slot no longer available), it calls `store.setConflict("Slot no longer available — please choose another")` and `store.loadSlots(specialtyId, date)` to refresh

5. **Security (OWASP A01)**: `AuthGuard` (Patient role) on the appointments feature route; no PHI displayed in the slot picker; `specialtyId` and `date` are validated as query params before dispatch.

---

## Current Project State

```
Propel-IQ-Patient-Platform/
├── .propel/
├── .github/
└── (no app/ scaffold yet — greenfield Angular project)
```

> Update with actual `app/` tree after scaffold is complete.

---

## Expected Changes

| Action | File Path                                                                     | Description                                                                 |
| ------ | ----------------------------------------------------------------------------- | --------------------------------------------------------------------------- |
| CREATE | `app/features/appointments/appointments.module.ts`                            | Lazy-loaded Appointments feature module                                     |
| CREATE | `app/features/appointments/appointments-routing.module.ts`                    | Booking wizard routes including slot picker step                            |
| CREATE | `app/features/appointments/components/slot-picker/slot-picker.component.ts`   | Slot picker with date selector, slot grid, booked-day handling              |
| CREATE | `app/features/appointments/components/slot-picker/slot-picker.component.html` | Template: loading skeleton, slot grid, fully-booked state, conflict banner  |
| CREATE | `app/features/appointments/components/slot-picker/slot-picker.component.scss` | Greyed-out slot and fully-booked day styles                                 |
| CREATE | `app/features/appointments/services/slot-availability.service.ts`             | `getAvailableSlots(specialtyId, date)` HTTP service                         |
| CREATE | `app/features/appointments/state/slot-availability.store.ts`                  | NgRx Signals store: slots, selectedSlot, loadingState, conflictMessage      |
| CREATE | `app/features/appointments/models/slot.models.ts`                             | Interfaces: `SlotDto`, `SlotAvailabilityResponseDto`                        |
| MODIFY | `app/app-routing.module.ts`                                                   | Add lazy-loaded `/appointments` route guarded by `AuthGuard` (Patient role) |

---

## External References

- [Angular 18 — NgRx Signals signalStore](https://ngrx.io/guide/signals/signal-store)
- [Angular Material — Date Picker](https://material.angular.io/components/datepicker/overview)
- [Angular Material — Button states (disabled)](https://material.angular.io/components/button/overview)
- [Angular Material — Progress Spinner](https://material.angular.io/components/progress-spinner/overview)
- [Angular HttpClient — GET with query params](https://angular.dev/guide/http/making-requests)
- [WCAG 2.2 AA — Non-text Contrast (greyed-out elements must meet 3:1)](https://www.w3.org/WAI/WCAG22/quickref/#non-text-contrast)
- [WCAG 2.2 AA — Name, Role, Value (aria-pressed, aria-disabled)](https://www.w3.org/WAI/WCAG22/quickref/#name-role-value)
- [OWASP A01 — Broken Access Control](https://owasp.org/Top10/A01_2021-Broken_Access_Control/)

---

## Build Commands

```bash
# Install dependencies
npm install

# Serve development build
ng serve

# Build production
ng build --configuration production

# Run unit tests
ng test

# Run E2E tests
npx playwright test
```

---

## Implementation Validation Strategy

- [x] Slot grid renders available slots as enabled buttons and unavailable slots as disabled with `aria-disabled="true"`
- [x] When all slots for a day are booked: slot grid is hidden; "No slots available" label is visible; "Join Waitlist" CTA is rendered
- [x] "Join Waitlist" click emits `joinWaitlistRequested` output event with `{specialtyId, date}`
- [x] Loading state shows `<mat-spinner>` while `loadingState === 'loading'`
- [x] Conflict banner appears when `store.conflictMessage()` is non-null; disappears on new slot selection
- [x] `SlotAvailabilityService` issues fresh GET on every `loadSlots()` call (no client-side caching)
- [x] `AuthGuard` blocks unauthenticated access to `/appointments` (redirects to login)
- [x] Greyed-out slots meet WCAG 2.2 AA non-text contrast ratio ≥ 3:1
- [ ] **[UI Tasks]** Visual comparison against wireframe at 375px, 768px, 1440px (when wireframe becomes AVAILABLE)
- [ ] **[UI Tasks]** Run `/analyze-ux` to validate wireframe alignment (when wireframe becomes AVAILABLE)

---

## Implementation Checklist

- [x] Create `SlotAvailabilityService` with `getAvailableSlots(specialtyId, date)` returning `SlotDto[]`
- [x] Create `SlotAvailabilityStore` (NgRx Signals): state shape `{ slots, selectedSlot, loadingState, conflictMessage }`; methods `loadSlots()`, `selectSlot()`, `setConflict()`, `clearConflict()`
- [x] Build `SlotPickerComponent`: date picker triggers `loadSlots()`; slot grid renders per-slot buttons; selected slot emits to parent
- [x] Implement fully-booked-day state: hide slot grid, show "No slots available" + "Join Waitlist" button when all slots `isAvailable = false`
- [x] Implement conflict banner: show message from `store.conflictMessage()`, auto-clear on slot re-selection
- [x] Add ARIA attributes: `role="grid"` on slot container, `aria-label` per slot button, `aria-pressed` for selected slot, `aria-disabled` for unavailable slots
- [x] Apply `AuthGuard` (Patient role) to lazy-loaded `/appointments` route
- [ ] **[UI Tasks - MANDATORY]** Reference wireframe from Design References table during implementation (when AVAILABLE)
- [ ] **[UI Tasks - MANDATORY]** Validate UI matches wireframe before marking task complete (when AVAILABLE)
