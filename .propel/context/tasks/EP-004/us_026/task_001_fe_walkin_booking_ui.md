# Task - task_001_fe_walkin_booking_ui

## Requirement Reference

- **User Story:** us_026 — Staff Walk-In Booking with Optional Patient Account Creation
- **Story Location:** `.propel/context/tasks/EP-004/us_026/us_026.md`
- **Acceptance Criteria:**
  - AC-1: Staff can search for existing patients by name or date of birth and link the walk-in to a matching Patient record
  - AC-2: When no match is found, Staff can open a quick-create form (name, contact number, email) to create a Patient record and link it to the new Appointment
  - AC-3: When no match is found and Staff skips account creation, an Appointment is created with an anonymous visit ID and no `patientId`; the visit appears in the same-day queue
  - AC-4: Patient-role users cannot access the walk-in booking interface (route guard enforces Staff role)
- **Edge Cases:**
  - Duplicate email during quick-create: backend returns HTTP 409 with `existingPatientId`; UI shows "A patient with this email already exists" with a "Link to existing patient" action
  - Fully booked time slot: backend returns an indication that the slot is full; UI confirms the walk-in will be added to the same-day queue without a time slot

---

## Design References (Frontend Tasks Only)

| Reference Type         | Value                                                                                                                                        |
| ---------------------- | -------------------------------------------------------------------------------------------------------------------------------------------- |
| **UI Impact**          | Yes                                                                                                                                          |
| **Figma URL**          | N/A                                                                                                                                          |
| **Wireframe Status**   | PENDING                                                                                                                                      |
| **Wireframe Type**     | N/A                                                                                                                                          |
| **Wireframe Path/URL** | TODO: Upload to `.propel/context/wireframes/Hi-Fi/wireframe-SCR-XXX-walkin-booking.[html\|png\|jpg]` or provide external URL |
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

Implement the Staff-exclusive walk-in booking interface as a lazy-loaded feature module at `/staff/walkin`. The interface is a three-path wizard:

**Path A — Existing Patient:** Staff searches by name or date of birth → selects a match → confirms walk-in booking linked to that Patient record.

**Path B — New Patient:** No match found → Staff opens quick-create inline form (name, contact, email) → on submit, Patient created and walk-in linked. Duplicate email from backend (HTTP 409) prompts "Link to existing patient" instead.

**Path C — Anonymous:** No match found → Staff skips account creation → walk-in confirmed with anonymous visit ID; shown in same-day queue.

In all paths, if the target time slot is fully booked, a confirmation prompt informs Staff the walk-in will be queued without a time slot assignment.

Route is guarded by `StaffGuard` (Staff role only — AC-4).

---

## Dependent Tasks

- **EP-004/us_026 task_002_be_walkin_booking_api** — patient search (`GET /api/staff/patients/search`) and walk-in command (`POST /api/staff/walkin`) must be deployed before integration testing
- **US_011 (EP-001)** — JWT `AuthGuard` and role-based `StaffGuard` must be active

---

## Impacted Components

| Status | Component / Module | Project |
| ------ | ------------------- | ------- |
| CREATE | `WalkInBookingComponent` | Angular Frontend (`app/features/staff/components/walkin-booking/`) |
| CREATE | `PatientSearchComponent` | Angular Frontend (`app/features/staff/components/patient-search/`) — reusable search widget |
| CREATE | `QuickCreatePatientFormComponent` | Angular Frontend (`app/features/staff/components/quick-create-patient/`) |
| CREATE | `WalkInService` | Angular Frontend (`app/features/staff/services/`) |
| CREATE | `WalkInStore` (NgRx Signals) | Angular Frontend (`app/features/staff/state/`) |
| CREATE | `StaffModule` + `StaffRoutingModule` | Angular Frontend (`app/features/staff/`) — lazy-loaded at `/staff` |
| CREATE | `StaffGuard` (CanActivateFn) | Angular Frontend (`app/core/guards/`) — allows Staff role only |
| MODIFY | `AppRoutingModule` | Add lazy-loaded `/staff` route guarded by `StaffGuard` |

---

## Implementation Plan

1. **`StaffGuard`** (`CanActivateFn`):
   - Reads role from JWT claims via `AuthService.currentUserRole()`; if role `!= 'Staff'` → `router.navigate(['/access-denied'])`; return `false`

2. **`WalkInService`** — three methods:
   - `searchPatients(query: string)` → `GET /api/staff/patients/search?query={query}` → `PatientSearchResultDto[]`
   - `createWalkIn(payload: WalkInBookingDto)` → `POST /api/staff/walkin` → `WalkInResponseDto`
   - `linkToExisting(appointmentId: string, patientId: string)` — utility for duplicate-email edge case (calls walk-in again with `patientId` pre-filled)

3. **`WalkInStore`** (NgRx Signals `signalStore`):
   - State: `searchResults: PatientSearchResultDto[]`, `selectedPatient: PatientSearchResultDto | null`, `actionState: 'idle' | 'searching' | 'submitting' | 'success' | 'error'`, `duplicatePatient: PatientSearchResultDto | null`, `slotFullWarning: boolean`
   - Methods: `searchPatients(query)`, `selectPatient(p)`, `submitWalkIn(dto)`, `clearDuplicate()`, `clearState()`

4. **`PatientSearchComponent`**:
   - `<mat-form-field>` with live search input (debounced 400ms `valueChanges`); calls `WalkInStore.searchPatients()`
   - Renders results as a `<mat-selection-list>` showing name, DOB, email
   - "No results found" state: shows two CTA buttons — "Create New Patient" and "Continue as Anonymous"
   - `@Output() patientSelected = new EventEmitter<PatientSearchResultDto>()`
   - `@Output() createNewRequested = new EventEmitter<void>()`
   - `@Output() anonymousRequested = new EventEmitter<void>()`

5. **`QuickCreatePatientFormComponent`**:
   - Reactive form: `name` (required, max 200 chars), `contactNumber` (optional, E.164 validator from US_015), `email` (required, email format)
   - On submit: `WalkInStore.submitWalkIn({ mode: 'create', name, contactNumber, email })`
   - On HTTP 409 (duplicate email): `WalkInStore` sets `duplicatePatient`; component shows `<mat-card class="duplicate-warning">` "Patient [name] already exists with this email" + "Link to [name]" button → calls `submitWalkIn({ mode: 'link', patientId: duplicatePatient.id })`
   - Back button → returns to `PatientSearchComponent`

6. **`WalkInBookingComponent`** (parent wizard):
   - Step 1: `PatientSearchComponent` — route based on output events:
     - `patientSelected` → Step 3 (confirm with linked patient)
     - `createNewRequested` → Step 2 (quick-create form)
     - `anonymousRequested` → Step 3 (confirm anonymous)
   - Step 2: `QuickCreatePatientFormComponent`
   - Step 3: Confirmation screen showing appointment summary; if `slotFullWarning` is true: info banner "This slot is fully booked — the walk-in will be added to the same-day queue" before final confirm button
   - On successful submit: navigate to `/staff/queue` (same-day queue) with `MatSnackBar` "Walk-in registered"
   - On anonymous confirm: `submitWalkIn({ mode: 'anonymous' })`

7. **Slot-full handling**: backend returns a field `queuedOnly: true` in the response when slot is full; FE shows informational banner (not an error) before confirmation; patient confirms and proceeds.

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

| Action | File Path | Description |
| ------ | --------- | ----------- |
| CREATE | `app/features/staff/staff.module.ts` | Lazy-loaded Staff feature module |
| CREATE | `app/features/staff/staff-routing.module.ts` | Staff routes: `/staff/walkin`, `/staff/queue` |
| CREATE | `app/features/staff/components/walkin-booking/walkin-booking.component.ts` | Parent wizard: orchestrates search → create/anonymous → confirm steps |
| CREATE | `app/features/staff/components/walkin-booking/walkin-booking.component.html` | Wizard template: step routing, slot-full banner, confirm button |
| CREATE | `app/features/staff/components/patient-search/patient-search.component.ts` | Debounced search input, results list, no-results CTAs |
| CREATE | `app/features/staff/components/patient-search/patient-search.component.html` | Template: search field, mat-selection-list, no-results state |
| CREATE | `app/features/staff/components/quick-create-patient/quick-create-patient-form.component.ts` | Quick patient form: name, contact, email; 409 duplicate handling |
| CREATE | `app/features/staff/components/quick-create-patient/quick-create-patient-form.component.html` | Form template: fields, duplicate-email warning card, back button |
| CREATE | `app/features/staff/services/walkin.service.ts` | `searchPatients()`, `createWalkIn()` HTTP methods |
| CREATE | `app/features/staff/state/walkin.store.ts` | NgRx Signals store: searchResults, selectedPatient, actionState, duplicatePatient, slotFullWarning |
| CREATE | `app/features/staff/models/walkin.models.ts` | Interfaces: `WalkInBookingDto`, `WalkInResponseDto`, `PatientSearchResultDto` |
| CREATE | `app/core/guards/staff.guard.ts` | `StaffGuard` CanActivateFn: Staff role check, redirect on failure |
| MODIFY | `app/app-routing.module.ts` | Add lazy-loaded `/staff` route guarded by `StaffGuard` |

---

## External References

- [Angular 18 — CanActivateFn (functional route guard)](https://angular.dev/guide/routing/common-router-tasks#preventing-unauthorized-access)
- [Angular Material — MatSelectionList (patient search results)](https://material.angular.io/components/list/overview)
- [Angular 18 — debounceTime + valueChanges (live search)](https://angular.dev/guide/forms/reactive-forms#listening-to-changes)
- [NgRx Signals — signalStore](https://ngrx.io/guide/signals/signal-store)
- [Angular Material — MatSnackBar](https://material.angular.io/components/snack-bar/overview)
- [Angular Material — MatCard (duplicate warning)](https://material.angular.io/components/card/overview)
- [WCAG 2.2 AA — Error Identification (duplicate email warning)](https://www.w3.org/WAI/WCAG22/quickref/#error-identification)
- [OWASP A01 — Broken Access Control (Staff-only route guard)](https://owasp.org/Top10/A01_2021-Broken_Access_Control/)

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

- [ ] `/staff/walkin` route is inaccessible to Patient-role JWT (redirects to `/access-denied`)
- [ ] Patient search debounces 400ms and shows results by name or DOB match
- [ ] "No results found" state shows both "Create New Patient" and "Continue as Anonymous" CTAs
- [ ] Quick-create form validates name (required), email (required + format), contactNumber (optional, E.164)
- [ ] HTTP 409 from quick-create shows duplicate-email warning card with "Link to existing patient" action
- [ ] Anonymous path creates walk-in with no patient link; confirmation screen shows anonymous visit ID
- [ ] Slot-full info banner shown before confirm button when backend returns `queuedOnly: true`
- [ ] On successful walk-in: navigates to `/staff/queue` with "Walk-in registered" snackbar
- [ ] **[UI Tasks]** Visual comparison against wireframe at 375px, 768px, 1440px (when wireframe becomes AVAILABLE)
- [ ] **[UI Tasks]** Run `/analyze-ux` to validate wireframe alignment (when wireframe becomes AVAILABLE)

---

## Implementation Checklist

- [ ] Create `StaffGuard` (`CanActivateFn`): allow only Staff role; redirect non-Staff to `/access-denied`
- [ ] Create `WalkInService`: `searchPatients(query)` → `GET /api/staff/patients/search`; `createWalkIn(dto)` → `POST /api/staff/walkin`
- [ ] Create `WalkInStore` (NgRx Signals): `searchResults`, `selectedPatient`, `actionState`, `duplicatePatient`, `slotFullWarning`; methods `searchPatients()`, `submitWalkIn()`, `clearDuplicate()`
- [ ] Build `PatientSearchComponent`: debounced search input (400ms), `mat-selection-list` results, no-results state with "Create New" and "Anonymous" output events
- [ ] Build `QuickCreatePatientFormComponent`: name/email/contact reactive form; on 409 → show `duplicatePatient` warning with "Link to existing" button
- [ ] Build `WalkInBookingComponent` wizard: route between search → create/anonymous → confirm steps; slot-full banner when `slotFullWarning = true`
- [ ] Add `StaffModule` lazy-loaded at `/staff` in `AppRoutingModule` guarded by `StaffGuard`
- [ ] **[UI Tasks - MANDATORY]** Reference wireframe from Design References table during implementation (when AVAILABLE)
- [ ] **[UI Tasks - MANDATORY]** Validate UI matches wireframe before marking task complete (when AVAILABLE)
