# Task - task_001_fe_cancel_reschedule_ui

## Requirement Reference

- **User Story:** us_020 — Appointment Cancellation & Rescheduling
- **Story Location:** `.propel/context/tasks/EP-003-I/us_020/us_020.md`
- **Acceptance Criteria:**
  - AC-1: When Patient clicks "Cancel Appointment" and confirms, the Appointment status is updated to `Cancelled`, the slot is released, and a success confirmation is shown
  - AC-2: On cancellation confirmed, any active reminders are suppressed and the CalendarSync event is deleted (backend responsibility; FE shows confirmation)
  - AC-3: When Patient chooses to reschedule, selects a new slot, and confirms, the original appointment is cancelled and a new booking is created; FE shows PDF confirmation notice ("Confirmation email sent")
  - AC-4: WaitlistEntry cancellation is handled by the backend on appointment cancel; FE shows no additional prompt
- **Edge Cases:**
  - Past appointment: "Cancel" and "Reschedule" buttons are hidden (not just disabled) for appointments whose date is in the past — validated client-side to avoid unnecessary API calls
  - Reschedule 409 Conflict: when the backend returns HTTP 409 (new slot taken), FE navigates the user back to the slot picker step and shows an inline banner "Slot no longer available — please choose another"

---

## Design References (Frontend Tasks Only)

| Reference Type         | Value                                                                                                                                                    |
| ---------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------- |
| **UI Impact**          | Yes                                                                                                                                                      |
| **Figma URL**          | N/A                                                                                                                                                      |
| **Wireframe Status**   | PENDING                                                                                                                                                  |
| **Wireframe Type**     | N/A                                                                                                                                                      |
| **Wireframe Path/URL** | TODO: Upload to `.propel/context/wireframes/Hi-Fi/wireframe-SCR-XXX-appointment-cancel.[html\|png\|jpg]` or provide external URL |
| **Screen Spec**        | N/A (figma_spec.md not yet generated)                                                                                                                    |
| **UXR Requirements**   | N/A (figma_spec.md not yet generated)                                                                                                                    |
| **Design Tokens**      | N/A (designsystem.md not yet generated)                                                                                                                  |

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

Implement the cancellation and rescheduling UI within the Patient appointments section. The `UpcomingAppointmentCardComponent` (or list row) exposes **"Cancel"** and **"Reschedule"** action buttons for future appointments only. Clicking **Cancel** opens a `MatDialog` confirmation. Clicking **Reschedule** launches a lightweight two-step wizard that reuses `SlotPickerComponent` (from US_018 task_001) for slot selection, then routes to a new-booking confirmation screen. All state is managed via NgRx Signals. The feature integrates with two backend commands (`POST /api/appointments/{id}/cancel` and `POST /api/appointments/{id}/reschedule`).

---

## Dependent Tasks

- **EP-003-I/us_018 task_001_fe_slot_picker** — `SlotPickerComponent` must be implemented before the reschedule wizard can reuse it
- **EP-003-I/us_020 task_002_be_cancel_appointment_command** — Cancel endpoint must be deployed before integration testing
- **EP-003-I/us_020 task_003_be_reschedule_appointment_command** — Reschedule endpoint must be deployed before integration testing
- **US_011 (EP-001)** — `AuthGuard` must be active; actions accessible to authenticated Patients only

---

## Impacted Components

| Status | Component / Module | Project |
| ------ | ------------------- | ------- |
| CREATE | `CancelConfirmationDialogComponent` | Angular Frontend (`app/features/appointments/components/`) |
| CREATE | `RescheduleWizardComponent` | Angular Frontend (`app/features/appointments/components/`) |
| CREATE | `AppointmentManagementService` | Angular Frontend (`app/features/appointments/services/`) |
| CREATE | `AppointmentManagementStore` (NgRx Signals) | Angular Frontend (`app/features/appointments/state/`) |
| MODIFY | `UpcomingAppointmentCardComponent` (or list component from US_019) | Add Cancel / Reschedule action buttons; hide for past appointments |
| MODIFY | `AppointmentsModule` routing | Add `/appointments/:id/reschedule` route |

---

## Implementation Plan

1. **`AppointmentManagementService`** — two methods:
   - `cancelAppointment(appointmentId: string)` → `POST /api/appointments/{id}/cancel` → 200 on success, 400 on past-appointment validation error
   - `rescheduleAppointment(appointmentId: string, newSlot: RescheduleRequestDto)` → `POST /api/appointments/{id}/reschedule` → 200 on success, 409 on slot conflict

2. **`AppointmentManagementStore`** (NgRx Signals `signalStore`):
   - State: `actionState: 'idle' | 'cancelling' | 'rescheduling' | 'success' | 'error'`, `errorMessage: string | null`, `conflictMessage: string | null`
   - Methods: `cancelAppointment(id)`, `rescheduleAppointment(id, newSlot)`, `clearMessages()`

3. **`UpcomingAppointmentCardComponent`** modifications:
   - Add `isFuture` computed property: `new Date(appointment.date) > new Date()` — bind to `*ngIf` / `@if` guard on action buttons
   - **Cancel button**: `<button mat-stroked-button color="warn" (click)="openCancelDialog(appointment.id)">Cancel Appointment</button>`  — only visible when `isFuture`
   - **Reschedule button**: `<button mat-stroked-button (click)="onReschedule(appointment.id)">Reschedule</button>` — only visible when `isFuture`

4. **`CancelConfirmationDialogComponent`** (opened via `MatDialog.open()`):
   - Displays: "Are you sure you want to cancel your appointment on {date} at {time}? This action cannot be undone."
   - Two buttons: "Keep Appointment" (dialog close, no action) and "Confirm Cancellation" (calls `store.cancelAppointment(id)`)
   - On success: close dialog, show `MatSnackBar` "Appointment cancelled. Confirmation email will be suppressed."
   - On 400 (past appointment from server): show inline `<mat-error>` "Cannot cancel a past appointment"
   - Loading state: disable "Confirm Cancellation" button while `actionState === 'cancelling'`

5. **`RescheduleWizardComponent`** (`/appointments/:id/reschedule` route):
   - Step 1: Embeds `SlotPickerComponent` (`@Input() specialtyId` from original appointment); on slot selected advances to step 2
   - Step 2: Confirmation screen showing old appointment details → new slot details; "Confirm Reschedule" button
   - On confirm: calls `store.rescheduleAppointment(originalId, { newSlotDate, newSlotStart, newSlotEnd, specialtyId })`
   - On 200: navigate to `/appointments` with `MatSnackBar` "Appointment rescheduled. Confirmation email sent."
   - On 409 (slot taken): navigate back to Step 1 and `slotPickerStore.setConflict("Slot no longer available — please choose another")` (reusing store from US_018)
   - On cancel wizard (back button): navigate to `/appointments` without mutation

6. **Security (OWASP A01)**: appointment IDs are resolved from route params — the backend validates ownership (patient can only cancel/reschedule their own appointments). FE does not expose other patients' appointment IDs.

---

## Current Project State

```
Propel-IQ-Patient-Platform/
├── .propel/
├── .github/
└── (no app/ scaffold yet — greenfield Angular project)
```

> Update with actual `app/` tree after scaffold is complete, referencing US_018 and US_019 completed components.

---

## Expected Changes

| Action | File Path | Description |
| ------ | --------- | ----------- |
| CREATE | `app/features/appointments/components/cancel-dialog/cancel-confirmation-dialog.component.ts` | Cancel confirmation MatDialog: confirm/dismiss, loading state, error display |
| CREATE | `app/features/appointments/components/cancel-dialog/cancel-confirmation-dialog.component.html` | Dialog template: appointment summary, confirm/cancel buttons |
| CREATE | `app/features/appointments/components/reschedule-wizard/reschedule-wizard.component.ts` | Two-step reschedule wizard: SlotPicker step + confirmation step |
| CREATE | `app/features/appointments/components/reschedule-wizard/reschedule-wizard.component.html` | Wizard template: stepper, slot picker, confirm button, 409 conflict banner |
| CREATE | `app/features/appointments/services/appointment-management.service.ts` | `cancelAppointment()`, `rescheduleAppointment()` HTTP calls |
| CREATE | `app/features/appointments/state/appointment-management.store.ts` | NgRx Signals store: actionState, errorMessage, conflictMessage |
| CREATE | `app/features/appointments/models/appointment-management.models.ts` | Interfaces: `RescheduleRequestDto`, `CancelResponseDto` |
| MODIFY | `app/features/appointments/components/upcoming-appointment-card/upcoming-appointment-card.component.ts` | Add `isFuture` guard, Cancel/Reschedule action buttons |
| MODIFY | `app/features/appointments/appointments-routing.module.ts` | Add `/appointments/:id/reschedule` route |

---

## External References

- [Angular 18 — MatDialog (open, afterClosed)](https://material.angular.io/components/dialog/overview)
- [Angular 18 — MatStepper (linear stepper for wizard)](https://material.angular.io/components/stepper/overview)
- [Angular Material — MatSnackBar](https://material.angular.io/components/snack-bar/overview)
- [NgRx Signals — signalStore](https://ngrx.io/guide/signals/signal-store)
- [Angular Router — paramMap (route params)](https://angular.dev/guide/routing/router-reference#activated-route)
- [OWASP A01 — Broken Access Control](https://owasp.org/Top10/A01_2021-Broken_Access_Control/)
- [WCAG 2.2 AA — Error Identification (aria-live for snackbar)](https://www.w3.org/WAI/WCAG22/quickref/#error-identification)

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

- [ ] "Cancel" and "Reschedule" buttons visible only for future appointments; hidden for past appointments
- [ ] Cancel dialog opens on button click; "Keep Appointment" closes without API call; "Confirm Cancellation" calls `POST /api/appointments/{id}/cancel`
- [ ] On cancel success: `MatSnackBar` "Appointment cancelled" shown; appointment removed or status updated in the list
- [ ] On cancel 400 (past appointment): inline `mat-error` shown inside dialog without closing it
- [ ] Reschedule wizard Step 1: `SlotPickerComponent` renders correctly; Step 2: shows old vs new slot summary
- [ ] On reschedule success: navigates to `/appointments` with snackbar "Appointment rescheduled. Confirmation email sent."
- [ ] On reschedule 409: wizard returns to Step 1 with conflict banner "Slot no longer available — please choose another"
- [ ] "Confirm Cancellation" button disabled during `actionState === 'cancelling'`
- [ ] **[UI Tasks]** Visual comparison against wireframe at 375px, 768px, 1440px (when wireframe becomes AVAILABLE)
- [ ] **[UI Tasks]** Run `/analyze-ux` to validate wireframe alignment (when wireframe becomes AVAILABLE)

---

## Implementation Checklist

- [ ] Create `AppointmentManagementService`: `cancelAppointment(id)` → `POST .../cancel`; `rescheduleAppointment(id, dto)` → `POST .../reschedule`
- [ ] Create `AppointmentManagementStore` (NgRx Signals): `actionState`, `errorMessage`, `conflictMessage`; methods `cancelAppointment()`, `rescheduleAppointment()`, `clearMessages()`
- [ ] Modify `UpcomingAppointmentCardComponent`: add `isFuture` guard (`appointment.date > today`); show Cancel/Reschedule buttons only when `isFuture`
- [ ] Build `CancelConfirmationDialogComponent` (MatDialog): confirmation text, "Keep" / "Confirm Cancellation" buttons, loading state, inline `mat-error` on 400
- [ ] Build `RescheduleWizardComponent` (MatStepper): Step 1 embeds `SlotPickerComponent`; Step 2 shows slot summary + "Confirm Reschedule"; handles 409 by resetting to Step 1 with conflict banner
- [ ] Add `/appointments/:id/reschedule` route to `AppointmentsRoutingModule` guarded by `AuthGuard` (Patient role)
- [ ] On reschedule 409: call `slotAvailabilityStore.setConflict(...)` and navigate back to stepper Step 1
- [ ] **[UI Tasks - MANDATORY]** Reference wireframe from Design References table during implementation (when AVAILABLE)
- [ ] **[UI Tasks - MANDATORY]** Validate UI matches wireframe before marking task complete (when AVAILABLE)
