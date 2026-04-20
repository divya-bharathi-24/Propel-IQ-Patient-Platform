# Task - task_001_fe_preferred_slot_designation_ui

## Requirement Reference

- **User Story:** us_023 — Preferred Slot Designation & Waitlist FIFO Enrollment
- **Story Location:** `.propel/context/tasks/EP-003-II/us_023/us_023.md`
- **Acceptance Criteria:**
  - AC-1: In the booking workflow, the Patient can designate one unavailable slot as their preferred slot; on submit a WaitlistEntry is created with all required fields
  - AC-3: On the Patient dashboard, an active WaitlistEntry shows a "Preferred Slot Waitlisted" indicator with the preferred date and time
  - AC-4: Patient can remove the preferred slot designation; on confirm, WaitlistEntry status is set to `Expired` and the indicator is removed from the dashboard
- **Edge Cases:**
  - Patient selects a preferred slot that is actually available at booking time: the FE fetches slot availability before displaying the unavailable slot list; if a selected "preferred" slot turns out to be available, the UI prompts "This slot is now available — would you like to book it directly?" and routes to the slot picker with that slot pre-selected
  - Patient cancels their current appointment while on the waitlist: handled by US_020 cancel flow (no additional FE action needed in US_023)

---

## Design References (Frontend Tasks Only)

| Reference Type         | Value                                                                                                                                        |
| ---------------------- | -------------------------------------------------------------------------------------------------------------------------------------------- |
| **UI Impact**          | Yes                                                                                                                                          |
| **Figma URL**          | N/A                                                                                                                                          |
| **Wireframe Status**   | PENDING                                                                                                                                      |
| **Wireframe Type**     | N/A                                                                                                                                          |
| **Wireframe Path/URL** | TODO: Upload to `.propel/context/wireframes/Hi-Fi/wireframe-SCR-XXX-preferred-slot.[html\|png\|jpg]` or provide external URL |
| **Screen Spec**        | N/A (figma_spec.md not yet generated)                                                                                                        |
| **UXR Requirements**   | N/A (figma_spec.md not yet generated)                                                                                                        |
| **Design Tokens**      | N/A (designsystem.md not yet generated)                                                                                                      |

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

Implement two UI surfaces for the preferred slot / waitlist feature:

**1 — Preferred Slot Step in the Booking Wizard** (extends US_019 `BookingWizardComponent`): After the patient selects an available slot, an optional step offers a date/time picker showing only unavailable slots for the same specialty. The patient can pick one as their "preferred slot" or skip. On skip, `preferredDate` and `preferredTimeSlot` are not sent to the booking command. On select, the chosen slot's date and start time are attached to the booking payload. If the selected preferred slot turns out to be available (edge case), a prompt directs the patient to book it directly.

**2 — Dashboard Waitlist Indicator** (extends the Patient appointment dashboard): When a patient has an `Active` WaitlistEntry linked to a booking, the appointment card shows a `<mat-chip>` badge "Preferred Slot Waitlisted" with the preferred date and time. A "Remove Preference" action expands to a confirmation; on confirm, `PATCH /api/waitlist/{id}/cancel` is called and the chip is removed.

---

## Dependent Tasks

- **EP-003-I/us_018 task_001_fe_slot_picker** — `SlotPickerComponent` is reused; the unavailable-slot variant must render greyed-out/unavailable slots as selectable items for designation
- **EP-003-II/us_023 task_002_be_waitlist_enrollment_api** — `GET /api/waitlist/me` and `PATCH /api/waitlist/{id}/cancel` endpoints must be deployed before integration testing
- **EP-003-I/us_019 task_001_fe_booking_wizard_component** — `BookingWizardComponent` must be extended with the optional preferred-slot step

---

## Impacted Components

| Status | Component / Module | Project |
| ------ | ------------------- | ------- |
| CREATE | `PreferredSlotStepComponent` | Angular Frontend (`app/features/appointments/components/preferred-slot-step/`) |
| CREATE | `WaitlistService` | Angular Frontend (`app/features/appointments/services/`) |
| CREATE | `WaitlistStore` (NgRx Signals) | Angular Frontend (`app/features/appointments/state/`) |
| MODIFY | `BookingWizardComponent` (US_019) | Add optional preferred-slot step; pass `preferredDate`/`preferredTimeSlot` to booking payload |
| MODIFY | `UpcomingAppointmentCardComponent` (US_019/US_020) | Add "Preferred Slot Waitlisted" chip and "Remove Preference" action |

---

## Implementation Plan

1. **`WaitlistService`** — two methods:
   - `getMyWaitlistEntries()` → `GET /api/waitlist/me` → returns `WaitlistEntryDto[]` (for dashboard indicator)
   - `cancelPreference(waitlistId: string)` → `PATCH /api/waitlist/{id}/cancel` → 200 on success

2. **`WaitlistStore`** (NgRx Signals `signalStore`):
   - State: `entries: WaitlistEntryDto[]`, `loadingState: 'idle' | 'loading' | 'loaded' | 'error'`, `cancelState: 'idle' | 'cancelling' | 'cancelled'`
   - Methods: `loadEntries()`, `cancelPreference(waitlistId)`, `clearCancelState()`

3. **`PreferredSlotStepComponent`** (optional step in wizard):
   - `@Input() specialtyId: string`; `@Output() slotDesignated = new EventEmitter<{ preferredDate: string; preferredTimeSlot: string } | null>()`
   - Renders a date picker for `preferredDate`; on date select calls `SlotAvailabilityService.getAvailableSlots()` to load all slots for that date; renders unavailable slots (`isAvailable = false`) as selectable radio buttons — these are the only valid choices for preferred designation
   - Available slots are hidden (not shown in this step — patient is explicitly picking an unavailable slot)
   - **Edge-case guard**: if all returned slots for a date are available (no `isAvailable = false` slots), show info banner "All slots on this date are available — you can book one directly" with a "Book This Date" button that routes back to the slot picker
   - **Skip button**: `"Skip — I don't have a preference"` emits `slotDesignated.emit(null)`
   - On unavailable slot selection: emit `slotDesignated.emit({ preferredDate, preferredTimeSlot })`

4. **`BookingWizardComponent`** modifications (US_019):
   - Add Step 2b (optional, after slot selection, before intake mode): embed `PreferredSlotStepComponent`
   - On `slotDesignated` event: store `preferredDate` and `preferredTimeSlot` in wizard state signal; pass to booking command payload
   - If patient skips: `preferredDate` and `preferredTimeSlot` remain null; booking proceeds without WaitlistEntry

5. **`UpcomingAppointmentCardComponent`** modifications (US_019/US_020):
   - `ngOnInit`: call `WaitlistStore.loadEntries()` on first render; match entries by `currentAppointmentId`
   - If a matching `Active` WaitlistEntry found: render `<mat-chip color="accent">Preferred Slot Waitlisted</mat-chip>` showing `entry.preferredDate` and `entry.preferredTimeSlot`
   - "Remove Preference" `<button mat-button>` opens inline confirmation; on confirm calls `WaitlistStore.cancelPreference(entry.id)`; on success: chip disappears, `MatSnackBar` "Waitlist preference removed"

6. **ARIA / accessibility**: `mat-chip` has `aria-label="Waitlisted for {date} at {time}"`; confirmation interaction uses `aria-live="polite"` region for screen readers.

---

## Current Project State

```
Propel-IQ-Patient-Platform/
├── .propel/
├── .github/
└── (no app/ scaffold yet — greenfield Angular project)
```

> Update with actual `app/` tree after scaffold is complete, referencing US_018 and US_019 components.

---

## Expected Changes

| Action | File Path | Description |
| ------ | --------- | ----------- |
| CREATE | `app/features/appointments/components/preferred-slot-step/preferred-slot-step.component.ts` | Optional wizard step: unavailable slot picker, skip option, available-slot edge-case banner |
| CREATE | `app/features/appointments/components/preferred-slot-step/preferred-slot-step.component.html` | Template: date picker, unavailable slot list as radio buttons, skip button, edge-case banner |
| CREATE | `app/features/appointments/services/waitlist.service.ts` | `getMyWaitlistEntries()` and `cancelPreference(id)` HTTP methods |
| CREATE | `app/features/appointments/state/waitlist.store.ts` | NgRx Signals store: entries, loadingState, cancelState |
| CREATE | `app/features/appointments/models/waitlist.models.ts` | Interfaces: `WaitlistEntryDto`, `CancelPreferenceResponseDto` |
| MODIFY | `app/features/appointments/components/booking-wizard/booking-wizard.component.ts` | Add Step 2b (preferred slot); pass `preferredDate`/`preferredTimeSlot` to booking payload signal |
| MODIFY | `app/features/appointments/components/booking-wizard/booking-wizard.component.html` | Add `<app-preferred-slot-step>` wizard step with skip affordance |
| MODIFY | `app/features/appointments/components/upcoming-appointment-card/upcoming-appointment-card.component.ts` | Load waitlist entries; show "Preferred Slot Waitlisted" chip + remove preference action |
| MODIFY | `app/features/appointments/components/upcoming-appointment-card/upcoming-appointment-card.component.html` | Add `<mat-chip>` + remove-preference confirmation UI |

---

## External References

- [Angular 18 — MatStepper (optional step)](https://material.angular.io/components/stepper/overview)
- [Angular 18 — MatChip / MatChipSet](https://material.angular.io/components/chips/overview)
- [NgRx Signals — signalStore](https://ngrx.io/guide/signals/signal-store)
- [Angular Material — MatRadioButton (unavailable slot selection)](https://material.angular.io/components/radio/overview)
- [Angular Material — MatSnackBar](https://material.angular.io/components/snack-bar/overview)
- [Angular HttpClient — PATCH request](https://angular.dev/guide/http/making-requests)
- [WCAG 2.2 AA — Status Messages (aria-live for chip updates)](https://www.w3.org/WAI/WCAG22/quickref/#status-messages)
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

- [ ] Preferred slot step renders only unavailable slots for the selected date; available slots are not shown
- [ ] Skip button emits `null` and booking wizard proceeds without preferred slot fields in payload
- [ ] When all slots on the preferred date are available: edge-case banner shown; no unavailable slot list rendered
- [ ] Selecting an unavailable slot emits `{ preferredDate, preferredTimeSlot }` to the booking wizard
- [ ] "Preferred Slot Waitlisted" chip appears on appointment card when an Active WaitlistEntry exists for that appointment
- [ ] "Remove Preference" confirmation triggers `PATCH /api/waitlist/{id}/cancel`; on success chip removed and snackbar shown
- [ ] `WaitlistStore.loadEntries()` called on appointment card init; state updates reactively
- [ ] `mat-chip` has correct `aria-label` with date and time values
- [ ] **[UI Tasks]** Visual comparison against wireframe at 375px, 768px, 1440px (when wireframe becomes AVAILABLE)
- [ ] **[UI Tasks]** Run `/analyze-ux` to validate wireframe alignment (when wireframe becomes AVAILABLE)

---

## Implementation Checklist

- [ ] Create `WaitlistService`: `getMyWaitlistEntries()` → `GET /api/waitlist/me`; `cancelPreference(id)` → `PATCH /api/waitlist/{id}/cancel`
- [ ] Create `WaitlistStore` (NgRx Signals): `entries`, `loadingState`, `cancelState`; methods `loadEntries()`, `cancelPreference()`, `clearCancelState()`
- [ ] Build `PreferredSlotStepComponent`: load slots for selected date, show only `isAvailable = false` as radio options, skip button, edge-case banner when no unavailable slots
- [ ] Extend `BookingWizardComponent` step 2b: embed `PreferredSlotStepComponent`; store `preferredDate`/`preferredTimeSlot` in wizard signals; include in booking payload
- [ ] Modify `UpcomingAppointmentCardComponent`: on init load `WaitlistStore.entries()`; match by `currentAppointmentId`; show `mat-chip` "Preferred Slot Waitlisted" with date/time; add "Remove Preference" inline confirmation
- [ ] On cancel-preference success: remove chip from view via `WaitlistStore.cancelPreference()`; show `MatSnackBar` "Waitlist preference removed"
- [ ] Add `aria-label` to chip and `aria-live="polite"` to status region for accessibility
- [ ] **[UI Tasks - MANDATORY]** Reference wireframe from Design References table during implementation (when AVAILABLE)
- [ ] **[UI Tasks - MANDATORY]** Validate UI matches wireframe before marking task complete (when AVAILABLE)
